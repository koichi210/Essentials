using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace HeifToPng
{
    public partial class MainWindow : Window
    {
        private static readonly int[] _parallelChoices = { 1, 2, 4, 8, 16 };

        // 設定(settings.json)の置き場。実データは既定で%LOCALAPPDATA%\HeifToPng\にあり、
        // exe直下にはその場所を示す案内板ファイル(DataFolder.txt)だけを置く([[_Common/UserDataLocation.cs]])。
        // システムメニューの「データ保存先を変更」で差し替わるので readonly にしない
        private const string _appName = "HeifToPng";
        private string _userDataFolder = UserDataLocation.GetUserDataFolder(_appName);
        private string SettingsPath => Path.Combine(_userDataFolder, AppSettings.FileName);
        private readonly List<string> _sources = new List<string>();
        private CancellationTokenSource _cts;
        private bool _isConverting;
        // 変換中に閉じようとしたので、中止が終わったら閉じる
        private bool _closeRequested;

        public MainWindow()
        {
            InitializeComponent();

            ParallelCombo.ItemsSource = _parallelChoices;
            ApplySettings(AppSettings.Load(SettingsPath));
            UpdateControlState();

            WpfDataFolderMenu.Attach(this, ChangeDataFolder);
        }

        private void ApplySettings(AppSettings settings)
        {
            _sources.Clear();
            AddSources(settings.Sources);
            SubfolderCheck.IsChecked = settings.IncludeSubfolders;
            OverwriteCheck.IsChecked = settings.Overwrite;
            TimestampCheck.IsChecked = settings.KeepTimestamps;
            ParallelCombo.SelectedItem = _parallelChoices.Contains(settings.MaxParallelism) ? settings.MaxParallelism : 4;
        }

        private AppSettings CollectSettings()
        {
            return new AppSettings
            {
                Sources = _sources.ToList(),
                IncludeSubfolders = SubfolderCheck.IsChecked == true,
                Overwrite = OverwriteCheck.IsChecked == true,
                KeepTimestamps = TimestampCheck.IsChecked == true,
                MaxParallelism = ParallelCombo.SelectedItem is int parallelism ? parallelism : 4,
            };
        }

        // *******************************************************************************
        // データ保存先フォルダの変更(システムメニューの「データ保存先を変更」から呼ばれる)

        private void ChangeDataFolder()
        {
            if (_isConverting)
            {
                MessageBox.Show(this, "変換中は保存先を変更できません。終わってからやり直してください。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder, MoveSettingFile, newFolder =>
            {
                _userDataFolder = newFolder;
                ApplySettings(AppSettings.Load(SettingsPath));
                UpdateControlState();
            });
        }

        // 設定ファイルは1つだけなので、全プロファイルの引っ越しではなくこの1ファイルだけを移す
        private void MoveSettingFile(string oldFolder, string newFolder)
        {
            string oldPath = Path.Combine(oldFolder, AppSettings.FileName);
            string newPath = Path.Combine(newFolder, AppSettings.FileName);
            if (!File.Exists(oldPath) || File.Exists(newPath))
            {
                return;
            }

            MessageBoxResult moveResult = MessageBox.Show(this,
                "既存の設定ファイルを新しい保存先に移動しますか？" + Environment.NewLine + Environment.NewLine
                    + "移動元: " + oldPath + Environment.NewLine
                    + "移動先: " + newPath,
                _appName + " - 設定ファイルの引っ越し",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (moveResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                File.Move(oldPath, newPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "移動に失敗したよ: " + ex.Message,
                    _appName + " - 設定ファイルの引っ越し", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // *******************************************************************************
        // 変換

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sources.Count == 0)
            {
                MessageBox.Show(this, "変換元のフォルダかファイルを追加してください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                AddFolderButton.Focus();
                return;
            }

            AppSettings settings = CollectSettings();
            var options = new ConvertOptions
            {
                Sources = settings.Sources,
                IncludeSubfolders = settings.IncludeSubfolders,
                Overwrite = settings.Overwrite,
                KeepTimestamps = settings.KeepTimestamps,
                MaxParallelism = settings.MaxParallelism,
            };

            _cts = new CancellationTokenSource();
            _isConverting = true;
            UpdateControlState();
            WorkProgressBar.Value = 0;
            WorkProgressBar.IsIndeterminate = true;
            StatusText.Text = "HEIFファイルを探しています…";
            AddLog(string.Format("{0:HH:mm:ss} 変換開始(変換元 {1} 件)", DateTime.Now, options.Sources.Count));
            var progress = new Progress<ConvertProgress>(ShowProgress);
            try
            {
                CancellationToken token = _cts.Token;
                ConvertResult result = await Task.Run(() => HeifConverter.Run(options, progress, token));
                _isConverting = false;
                ShowResult(result);
            }
            finally
            {
                _isConverting = false;
                _cts.Dispose();
                _cts = null;
                UpdateControlState();
                if (_closeRequested)
                {
                    Close();
                }
            }
        }

        private void ShowProgress(ConvertProgress progress)
        {
            // 変換が終わった後に、間引かれて遅れて届いた通知で表示を上書きしない
            if (!_isConverting)
            {
                return;
            }
            if (progress.Enumerating)
            {
                WorkProgressBar.IsIndeterminate = true;
                StatusText.Text = string.Format("HEIFファイルを探しています… {0:N0} 件  {1}", progress.Total, progress.CurrentPath);
                return;
            }

            WorkProgressBar.IsIndeterminate = false;
            double ratio = progress.Total == 0 ? 1 : (double)progress.Done / progress.Total;
            WorkProgressBar.Value = ratio * WorkProgressBar.Maximum;
            StatusText.Text = string.Format("変換中… {0:N0} / {1:N0}  {2}", progress.Done, progress.Total, progress.CurrentPath);
        }

        private void ShowResult(ConvertResult result)
        {
            WorkProgressBar.IsIndeterminate = false;
            if (result.DecoderMissing)
            {
                const string message = "このPCではHEIF画像を読めません。Microsoft Store から「HEIF画像拡張機能」と「HEVCビデオ拡張機能」を入れてから、もう一度変換してください。";
                AddLog(string.Format("{0:HH:mm:ss} 中止: HEIFを読むための拡張機能が入っていません", DateTime.Now));
                StatusText.Text = message;
                if (!_closeRequested)
                {
                    MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }
            if (!result.Cancelled)
            {
                WorkProgressBar.Value = WorkProgressBar.Maximum;
            }

            foreach (string failure in result.Failures)
            {
                AddLog("  失敗: " + failure);
            }
            string summary = string.Format("{0}: 対象 {1:N0} 件 / 変換 {2:N0} 件 / スキップ(PNGあり) {3:N0} 件 / 失敗 {4:N0} 件",
                result.Cancelled ? "中止" : "完了", result.Total, result.Converted, result.Skipped, result.Failures.Count);
            AddLog(string.Format("{0:HH:mm:ss} {1}", DateTime.Now, summary));
            StatusText.Text = summary;

            // 閉じる途中なので、結果のダイアログは出さない
            if (_closeRequested)
            {
                return;
            }
            if (result.Total == 0 && !result.Cancelled && result.Failures.Count == 0)
            {
                MessageBox.Show(this, "HEIFファイル(" + string.Join(" / ", HeifConverter.Extensions) + ")が見つかりませんでした。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result.Failures.Count > 0)
            {
                MessageBox.Show(this, summary + Environment.NewLine + Environment.NewLine + "失敗したファイルはログに出しています。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            StatusText.Text = "中止しています…(変換中のファイルが終わるまでお待ちください)";
        }

        private void UpdateControlState()
        {
            bool busy = _isConverting;
            SourceList.IsEnabled = !busy;
            AddFolderButton.IsEnabled = !busy;
            AddFileButton.IsEnabled = !busy;
            RemoveSourceButton.IsEnabled = !busy;
            ClearSourceButton.IsEnabled = !busy;
            SubfolderCheck.IsEnabled = !busy;
            OverwriteCheck.IsEnabled = !busy;
            TimestampCheck.IsEnabled = !busy;
            ParallelCombo.IsEnabled = !busy;
            ConvertButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
        }

        // *******************************************************************************
        // 変換元のリスト

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "HEIF画像を変換するフォルダを選んでください";
                dialog.ShowNewFolderButton = false;
                string last = _sources.LastOrDefault();
                string lastFolder = last != null && File.Exists(last) ? Path.GetDirectoryName(last) : last;
                if (lastFolder != null && Directory.Exists(lastFolder))
                {
                    dialog.SelectedPath = lastFolder;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AddSources(new[] { dialog.SelectedPath });
                }
            }
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "変換するHEIF画像を選んでください(複数可)",
                Filter = "HEIF画像 (" + string.Join(";", HeifConverter.Extensions.Select(x => "*" + x)) + ")|"
                    + string.Join(";", HeifConverter.Extensions.Select(x => "*" + x)) + "|すべてのファイル (*.*)|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog(this) == true)
            {
                AddSources(dialog.FileNames);
            }
        }

        // リストの末尾に足す(同じパスは足さない)
        private void AddSources(IEnumerable<string> paths)
        {
            foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                string normalized;
                try
                {
                    normalized = Path.GetFullPath(path).TrimEnd('\\');
                    // 「D:\」が「D:」になるとカレントフォルダの意味になってしまうので戻す
                    if (normalized.EndsWith(":"))
                    {
                        normalized += "\\";
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }
                if (!_sources.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    _sources.Add(normalized);
                }
            }
            RefreshSourceList();
        }

        private void RefreshSourceList()
        {
            SourceList.ItemsSource = null;
            SourceList.ItemsSource = _sources.ToList();
        }

        private void RemoveSourceButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedSources();
        }

        private void SourceList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedSources();
                e.Handled = true;
            }
        }

        private void RemoveSelectedSources()
        {
            foreach (string path in SourceList.SelectedItems.Cast<string>().ToList())
            {
                _sources.Remove(path);
            }
            RefreshSourceList();
        }

        private void ClearSourceButton_Click(object sender, RoutedEventArgs e)
        {
            _sources.Clear();
            RefreshSourceList();
        }

        // *******************************************************************************
        // ログ

        private void AddLog(string line)
        {
            LogList.Items.Add(line);
            LogList.ScrollIntoView(line);
        }

        private void LogList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedLog();
                e.Handled = true;
            }
        }

        private void CopyLogMenu_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedLog();
        }

        private void CopySelectedLog()
        {
            // 選んだ順ではなく画面の並び順でコピーする
            List<string> lines = LogList.Items.Cast<string>().Where(item => LogList.SelectedItems.Contains(item)).ToList();
            if (lines.Count > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
            }
        }

        private void ClearLogMenu_Click(object sender, RoutedEventArgs e)
        {
            LogList.Items.Clear();
        }

        // *******************************************************************************
        // ウィンドウ

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && SourceList.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // フォルダもファイルもそのまま変換元に足す(複数可)。HEIF以外のファイルは変換時に無視される
        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Handled = true;
            if (paths == null || !SourceList.IsEnabled)
            {
                return;
            }
            AddSources(paths);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isConverting)
            {
                MessageBoxResult answer = MessageBox.Show(this, "変換中です。中止して閉じますか？(変換済みのPNGは残ります)",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                // 今すぐ閉じると変換中のスレッドが書きかけの一時ファイル(.png.tmp)を残して終わるので、
                // 中止を待ってから閉じ直す(ConvertButton_Click の finally)
                e.Cancel = true;
                if (answer == MessageBoxResult.Yes)
                {
                    _closeRequested = true;
                    CancelButton_Click(this, null);
                }
                return;
            }

            try
            {
                CollectSettings().Save(SettingsPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 設定が保存できなくても終了は妨げない
            }
        }
    }
}
