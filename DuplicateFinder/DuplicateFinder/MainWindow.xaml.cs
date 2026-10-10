using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DuplicateFinder
{
    public partial class MainWindow : Window
    {
        private static readonly int[] _parallelChoices = { 1, 2, 4, 8, 16, 32 };
        private static readonly KeyValuePair<KeepRule, string>[] _keepRuleChoices =
        {
            new KeyValuePair<KeepRule, string>(KeepRule.RootOrder, "リストの上の対象フォルダにあるものを残す"),
            new KeyValuePair<KeepRule, string>(KeepRule.Oldest, "更新日時が古いものを残す"),
            new KeyValuePair<KeepRule, string>(KeepRule.Newest, "更新日時が新しいものを残す"),
            new KeyValuePair<KeepRule, string>(KeepRule.ShortestPath, "パスが短いものを残す"),
            new KeyValuePair<KeepRule, string>(KeepRule.LongestPath, "パスが長いものを残す"),
        };
        // エラー一覧のダイアログに出す最大件数(残りは件数だけ出す)
        private const int _maxErrorLines = 30;

        private readonly string _settingsPath = AppSettings.DefaultFilePath;
        // 対象フォルダ(並び順は自動選択「リストの上の対象フォルダにあるものを残す」の優先順)
        private readonly List<string> _rootFolders = new List<string>();
        private List<FileRow> _rows = new List<FileRow>();
        private CancellationTokenSource _cts;
        private bool _isScanning;
        private bool _isDeleting;
        private int _markedCount;
        private long _markedBytes;

        public MainWindow()
        {
            InitializeComponent();

            ParallelCombo.ItemsSource = _parallelChoices;
            KeepRuleCombo.ItemsSource = _keepRuleChoices;

            AppSettings settings = AppSettings.Load(_settingsPath);
            AddFolders(settings.RootFolders);
            CrossRootCheck.IsChecked = settings.CrossRootOnly;
            MinSizeBox.Text = settings.MinSizeMb;
            ExtensionsBox.Text = settings.Extensions;
            SkipHiddenCheck.IsChecked = settings.SkipHiddenAndSystem;
            ParallelCombo.SelectedItem = _parallelChoices.Contains(settings.MaxParallelism) ? settings.MaxParallelism : 1;
            RecycleCheck.IsChecked = settings.UseRecycleBin;
            KeepRuleCombo.SelectedValue = settings.KeepRule;
            if (KeepRuleCombo.SelectedIndex < 0)
            {
                KeepRuleCombo.SelectedIndex = 0;
            }

            UpdateMarkedSummary();
            UpdateControlState();
        }

        // *******************************************************************************
        // 検索

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanOptions options = ReadScanOptions();
            if (options == null)
            {
                return;
            }

            SetRows(new List<FileRow>());
            _cts = new CancellationTokenSource();
            _isScanning = true;
            UpdateControlState();
            StatusText.Text = "ファイル一覧を作成中…";
            var progress = new Progress<ScanProgress>(ShowScanProgress);
            try
            {
                CancellationToken token = _cts.Token;
                ScanResult result = await Task.Run(() => DuplicateScanner.Scan(options, progress, token));
                _isScanning = false;
                ShowScanResult(result);
            }
            finally
            {
                _isScanning = false;
                _cts.Dispose();
                _cts = null;
                UpdateControlState();
            }
        }

        // 入力欄から検索条件を作る。入力に問題があればメッセージを出してnull
        private ScanOptions ReadScanOptions()
        {
            if (_rootFolders.Count == 0)
            {
                MessageBox.Show(this, "対象フォルダを追加してください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                AddFolderButton.Focus();
                return null;
            }
            List<string> missing = _rootFolders.Where(f => !Directory.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show(this, "見つからない対象フォルダがあります。\n\n" + string.Join("\n", missing), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            bool crossRootOnly = CrossRootCheck.IsChecked == true;
            if (crossRootOnly && _rootFolders.Count < 2)
            {
                MessageBox.Show(this, "「別々の対象フォルダにまたがる重複だけ」は、対象フォルダを2つ以上追加したときに使えます。",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            double minSizeMb;
            string minSizeText = MinSizeBox.Text.Trim();
            if (minSizeText.Length == 0)
            {
                minSizeMb = 0;
            }
            else if (!double.TryParse(minSizeText, out minSizeMb) || minSizeMb < 0)
            {
                MessageBox.Show(this, "最小サイズは0以上の数値(MB)で入力してください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                MinSizeBox.Focus();
                return null;
            }

            return new ScanOptions
            {
                RootFolders = _rootFolders.ToList(),
                CrossRootOnly = crossRootOnly,
                MinSizeBytes = (long)(minSizeMb * 1024 * 1024),
                Extensions = ScanOptions.ParseExtensions(ExtensionsBox.Text),
                SkipHiddenAndSystem = SkipHiddenCheck.IsChecked == true,
                MaxParallelism = ParallelCombo.SelectedItem is int parallelism ? parallelism : 1,
            };
        }

        private void ShowScanProgress(ScanProgress progress)
        {
            // 検索が終わった後に、間引かれて遅れて届いた通知で表示を上書きしない
            if (!_isScanning)
            {
                return;
            }
            if (progress.Phase == ScanPhase.Enumerating)
            {
                WorkProgressBar.IsIndeterminate = true;
                StatusText.Text = string.Format("ファイル一覧を作成中… {0:N0} 件  {1}", progress.FilesFound, progress.CurrentPath);
                return;
            }

            WorkProgressBar.IsIndeterminate = false;
            double ratio = progress.TotalBytes == 0 ? 1 : (double)progress.ProcessedBytes / progress.TotalBytes;
            WorkProgressBar.Value = ratio * WorkProgressBar.Maximum;
            StatusText.Text = string.Format("比較中… {0:0.0}%  ({1} / {2})  {3}",
                ratio * 100, SizeFormatter.Format(progress.ProcessedBytes), SizeFormatter.Format(progress.TotalBytes), progress.CurrentPath);
        }

        private void ShowScanResult(ScanResult result)
        {
            var rows = new List<FileRow>();
            for (int i = 0; i < result.Groups.Count; i++)
            {
                rows.AddRange(result.Groups[i].Files.Select(f => new FileRow(f, i + 1)));
            }
            SetRows(rows);

            WorkProgressBar.IsIndeterminate = false;
            var status = new StringBuilder();
            if (!result.Cancelled)
            {
                WorkProgressBar.Value = WorkProgressBar.Maximum;
                status.Append("完了: ");
            }
            else if (!result.ReachedComparing)
            {
                // ファイル一覧の作成中に中止したので、比較は1件もしていない
                WorkProgressBar.Value = 0;
                StatusText.Text = "検索を中止しました(ファイル一覧の作成中だったため、結果はありません)。";
                return;
            }
            else
            {
                double ratio = result.TotalBytes == 0 ? 1 : (double)result.ProcessedBytes / result.TotalBytes;
                WorkProgressBar.Value = ratio * WorkProgressBar.Maximum;
                status.AppendFormat("中止: 全体の {0:0.0}% まで比較した時点で、重複と確定した分だけ表示しています / ", ratio * 100);
            }
            status.AppendFormat("{0:N0} ファイル中、重複 {1:N0} グループ・{2:N0} 件 / 1件ずつ残すと {3} 空きます (所要 {4}、読み込み {5})",
                result.ScannedFileCount, result.Groups.Count, rows.Count,
                SizeFormatter.Format(result.Groups.Sum(g => g.WastedBytes)),
                FormatElapsed(result.Elapsed), SizeFormatter.Format(result.BytesRead));
            if (result.SkippedCloudFileCount > 0)
            {
                status.AppendFormat("  ※未ダウンロードのクラウドファイル {0:N0} 件は対象外", result.SkippedCloudFileCount);
            }
            if (result.Errors.Count > 0)
            {
                status.AppendFormat("  ※読めなかったもの {0:N0} 件", result.Errors.Count);
            }
            StatusText.Text = status.ToString();

            if (result.Errors.Count > 0)
            {
                ShowErrorList("読めなかったファイル・フォルダがありました(これらは比較の対象外です)。", result.Errors);
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                return string.Format("{0}分{1:00}秒", (int)elapsed.TotalMinutes, elapsed.Seconds);
            }
            return string.Format("{0:0.0}秒", elapsed.TotalSeconds);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        // *******************************************************************************
        // 一覧

        private void SetRows(List<FileRow> rows)
        {
            foreach (FileRow row in _rows)
            {
                row.PropertyChanged -= Row_PropertyChanged;
            }
            _rows = rows;
            _markedCount = 0;
            _markedBytes = 0;
            foreach (FileRow row in _rows)
            {
                row.PropertyChanged += Row_PropertyChanged;
                if (row.IsMarked)
                {
                    _markedCount++;
                    _markedBytes += row.Entry.Length;
                }
            }

            // 見出しには件数が入るので、作り直すたびに付け直す
            foreach (IGrouping<int, FileRow> group in _rows.GroupBy(r => r.GroupNumber))
            {
                FileRow first = group.First();
                int count = group.Count();
                string header = string.Format("#{0}   {1} 件 × {2}   (1件残すと {3} 空きます)",
                    group.Key, count, first.SizeText, SizeFormatter.Format(first.Entry.Length * (count - 1)));
                if (_rootFolders.Count >= 2)
                {
                    header += "   [対象 " + string.Join("・", group.Select(r => r.RootNumber).Distinct().OrderBy(n => n)) + "]";
                }
                foreach (FileRow row in group)
                {
                    row.GroupHeader = header;
                }
            }

            var view = new ListCollectionView(_rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileRow.GroupHeader)));
            ResultGrid.ItemsSource = view;
            UpdateMarkedSummary();
            UpdateControlState();
        }

        // 1万件にチェックを付けても重くならないよう、合計は差分で更新する
        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FileRow.IsMarked))
            {
                return;
            }
            var row = (FileRow)sender;
            int sign = row.IsMarked ? 1 : -1;
            _markedCount += sign;
            _markedBytes += sign * row.Entry.Length;
            UpdateMarkedSummary();
        }

        private void UpdateMarkedSummary()
        {
            MarkedSummaryText.Text = _markedCount == 0
                ? ""
                : string.Format("削除チェック: {0:N0} 件 / {1}", _markedCount, SizeFormatter.Format(_markedBytes));
        }

        private List<FileRow> SelectedRows()
        {
            return ResultGrid.SelectedItems.OfType<FileRow>().ToList();
        }

        private FileRow CurrentRow()
        {
            return ResultGrid.SelectedItem as FileRow;
        }

        private void ApplyRuleButton_Click(object sender, RoutedEventArgs e)
        {
            RowMarker.ApplyKeepRule(_rows, (KeepRule)KeepRuleCombo.SelectedValue);
        }

        private void ClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            RowMarker.ClearAll(_rows);
        }

        private void ResultGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
            {
                return;
            }
            ToggleMarks(SelectedRows());
            e.Handled = true;
        }

        private void ToggleMarkMenu_Click(object sender, RoutedEventArgs e)
        {
            ToggleMarks(SelectedRows());
        }

        // 選んだ行がすべてチェック済みなら外し、そうでなければ全部に付ける
        private static void ToggleMarks(List<FileRow> rows)
        {
            bool mark = !rows.All(r => r.IsMarked);
            foreach (FileRow row in rows)
            {
                row.IsMarked = mark;
            }
        }

        private void KeepThisMenu_Click(object sender, RoutedEventArgs e)
        {
            FileRow row = CurrentRow();
            if (row != null)
            {
                RowMarker.KeepOnly(_rows, row);
            }
        }

        private void KeepFolderMenu_Click(object sender, RoutedEventArgs e)
        {
            FileRow row = CurrentRow();
            if (row == null)
            {
                return;
            }
            int count = RowMarker.KeepUnderFolder(_rows, row.Folder);
            StatusText.Text = string.Format("「{0}」以下のファイルを残すように、{1:N0} グループのチェックを付け直しました。", row.Folder, count);
        }

        private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 見出しやスクロールバーのダブルクリックは無視する
            var row = ItemsControl.ContainerFromElement(ResultGrid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row?.Item is FileRow fileRow)
            {
                OpenFile(fileRow.Path);
            }
        }

        private void OpenMenu_Click(object sender, RoutedEventArgs e)
        {
            FileRow row = CurrentRow();
            if (row != null)
            {
                OpenFile(row.Path);
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception || ex is FileNotFoundException)
            {
                MessageBox.Show(this, "開けませんでした。\n" + path + "\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowInExplorerMenu_Click(object sender, RoutedEventArgs e)
        {
            FileRow row = CurrentRow();
            if (row != null)
            {
                Process.Start("explorer.exe", "/select,\"" + row.Path + "\"");
            }
        }

        private void CopyPathMenu_Click(object sender, RoutedEventArgs e)
        {
            List<FileRow> rows = SelectedRows();
            if (rows.Count > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(r => r.Path)));
            }
        }

        // *******************************************************************************
        // 削除

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            List<FileRow> targets = _rows.Where(r => r.IsMarked).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "削除するファイルにチェックを付けてください。\n(「自動選択」→「適用」で、各グループ1件を残して他にまとめてチェックを付けられます)",
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<int> fullyMarked = RowMarker.FindFullyMarkedGroups(_rows);
            if (fullyMarked.Count > 0)
            {
                MessageBox.Show(this, "すべてのファイルにチェックが付いているグループがあります。\n各グループで最低1件はチェックを外して残してください。\n\n"
                    + string.Join(", ", fullyMarked.Take(20).Select(n => "#" + n)) + (fullyMarked.Count > 20 ? " …" : ""),
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool toRecycleBin = RecycleCheck.IsChecked == true;
            string how = toRecycleBin ? "ごみ箱へ移動" : "完全に削除(元に戻せません)";
            string question = string.Format("{0:N0} 件 ({1}) を{2}します。よろしいですか？",
                targets.Count, SizeFormatter.Format(targets.Sum(r => r.Entry.Length)), how);
            if (MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            _isDeleting = true;
            _cts = new CancellationTokenSource();
            UpdateControlState();
            WorkProgressBar.IsIndeterminate = false;
            var failures = new List<string>();
            var deleted = new HashSet<FileRow>();
            try
            {
                await DeleteRows(targets, toRecycleBin, deleted, failures, _cts.Token);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _isDeleting = false;
                UpdateControlState();
            }

            // 消したファイルを一覧から外し、1件だけになったグループ(もう重複ではない)も一覧から外す
            List<FileRow> remaining = _rows.Where(r => !deleted.Contains(r)).ToList();
            HashSet<int> stillDuplicated = new HashSet<int>(remaining.GroupBy(r => r.GroupNumber).Where(g => g.Count() >= 2).Select(g => g.Key));
            SetRows(remaining.Where(r => stillDuplicated.Contains(r.GroupNumber)).ToList());

            StatusText.Text = string.Format("{0:N0} 件を{1}しました。{2}", deleted.Count, toRecycleBin ? "ごみ箱へ移動" : "削除",
                failures.Count > 0 ? string.Format("({0:N0} 件はできませんでした)", failures.Count) : "");
            if (failures.Count > 0)
            {
                ShowErrorList("削除できなかったファイルがあります。", failures);
            }
        }

        private async Task DeleteRows(List<FileRow> targets, bool toRecycleBin, HashSet<FileRow> deleted, List<string> failures, CancellationToken token)
        {
            Dictionary<int, List<FileRow>> groups = _rows.GroupBy(r => r.GroupNumber).ToDictionary(g => g.Key, g => g.ToList());
            IntPtr owner = new WindowInteropHelper(this).Handle;
            for (int i = 0; i < targets.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    failures.Add(string.Format("中止したため、残り {0:N0} 件は削除していません", targets.Count - i));
                    break;
                }
                FileRow row = targets[i];
                string reason = CheckBeforeDelete(row, groups[row.GroupNumber], deleted)
                    ?? FileDeleter.Delete(row.Path, toRecycleBin, owner);
                if (reason == null)
                {
                    deleted.Add(row);
                }
                else
                {
                    failures.Add(row.Path + " : " + reason);
                }

                WorkProgressBar.Value = (i + 1) * WorkProgressBar.Maximum / targets.Count;
                StatusText.Text = string.Format("削除中… {0:N0} / {1:N0}  {2}", i + 1, targets.Count, row.Path);
                // ごみ箱への移動はUIスレッドで行う(シェルの確認ダイアログが出ることがあるため)。合間に画面を更新させる
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }

        // 検索した後にファイルが変わっていないか、残す側が確かに残っているかを、消す直前に確かめる
        private static string CheckBeforeDelete(FileRow target, List<FileRow> group, HashSet<FileRow> deleted)
        {
            if (!IsUnchanged(target.Entry))
            {
                return "検索した後に変更・移動されたため削除しませんでした";
            }
            bool keeperExists = group.Any(r => !r.IsMarked && !deleted.Contains(r) && IsUnchanged(r.Entry));
            return keeperExists ? null : "残す側のファイルが見つからない(または変更された)ため削除しませんでした";
        }

        private static bool IsUnchanged(FileEntry entry)
        {
            var info = new FileInfo(entry.Path);
            return info.Exists && info.Length == entry.Length && info.LastWriteTime == entry.LastWriteTime;
        }

        // *******************************************************************************
        // 共通

        private void ShowErrorList(string title, List<string> errors)
        {
            string text = title + "\n\n" + string.Join("\n", errors.Take(_maxErrorLines));
            if (errors.Count > _maxErrorLines)
            {
                text += string.Format("\n… ほか {0:N0} 件", errors.Count - _maxErrorLines);
            }
            MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void UpdateControlState()
        {
            bool busy = _isScanning || _isDeleting;
            bool hasRows = _rows.Count > 0;
            FolderList.IsEnabled = !busy;
            AddFolderButton.IsEnabled = !busy;
            RemoveFolderButton.IsEnabled = !busy;
            MoveUpButton.IsEnabled = !busy;
            MoveDownButton.IsEnabled = !busy;
            CrossRootCheck.IsEnabled = !busy;
            ScanButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            MinSizeBox.IsEnabled = !busy;
            ExtensionsBox.IsEnabled = !busy;
            SkipHiddenCheck.IsEnabled = !busy;
            ParallelCombo.IsEnabled = !busy;
            // ルールは検索前に選んでおけるよう、一覧が空でも触れるようにしておく
            KeepRuleCombo.IsEnabled = !busy;
            ApplyRuleButton.IsEnabled = !busy && hasRows;
            ClearMarksButton.IsEnabled = !busy && hasRows;
            RecycleCheck.IsEnabled = !busy;
            DeleteButton.IsEnabled = !busy && hasRows;
            ResultGrid.IsEnabled = !busy;
        }

        // *******************************************************************************
        // 対象フォルダのリスト

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "重複ファイルを探すフォルダを選んでください(サブフォルダも対象です)";
                dialog.ShowNewFolderButton = false;
                string last = _rootFolders.LastOrDefault();
                if (last != null && Directory.Exists(last))
                {
                    dialog.SelectedPath = last;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AddFolders(new[] { dialog.SelectedPath });
                }
            }
        }

        // リストの末尾に足す(同じフォルダは足さない)
        private void AddFolders(IEnumerable<string> folders)
        {
            var added = new List<int>();
            foreach (string folder in folders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                string normalized;
                try
                {
                    normalized = DuplicateScanner.NormalizeFolder(folder);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }
                if (_rootFolders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                _rootFolders.Add(normalized);
                added.Add(_rootFolders.Count - 1);
            }
            RefreshFolderList(added);
        }

        private List<int> SelectedFolderIndexes()
        {
            return FolderList.SelectedItems.Cast<object>().Select(item => FolderList.Items.IndexOf(item)).OrderBy(i => i).ToList();
        }

        private void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedFolders();
        }

        private void FolderList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedFolders();
                e.Handled = true;
            }
        }

        private void RemoveSelectedFolders()
        {
            List<int> indexes = SelectedFolderIndexes();
            for (int i = indexes.Count - 1; i >= 0; i--)
            {
                _rootFolders.RemoveAt(indexes[i]);
            }
            RefreshFolderList(new int[0]);
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedFolders(-1);
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedFolders(1);
        }

        // 選んだフォルダを1つ上(-1)・下(+1)へ。端にぶつかるときは動かさない
        private void MoveSelectedFolders(int direction)
        {
            List<int> indexes = SelectedFolderIndexes();
            if (indexes.Count == 0 || indexes[0] + direction < 0 || indexes[indexes.Count - 1] + direction >= _rootFolders.Count)
            {
                return;
            }
            IEnumerable<int> order = direction < 0 ? indexes : indexes.AsEnumerable().Reverse();
            foreach (int index in order)
            {
                string folder = _rootFolders[index];
                _rootFolders.RemoveAt(index);
                _rootFolders.Insert(index + direction, folder);
            }
            RefreshFolderList(indexes.Select(i => i + direction));
        }

        // 番号付きで表示し直す(番号は一覧の「対象」列と対応)
        private void RefreshFolderList(IEnumerable<int> selectIndexes)
        {
            FolderList.ItemsSource = _rootFolders.Select((folder, i) => (i + 1) + ".  " + folder).ToList();
            foreach (int index in selectIndexes)
            {
                FolderList.SelectedItems.Add(FolderList.Items[index]);
            }
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && FolderList.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // フォルダを落とせばそのフォルダ、ファイルを落とせばそのファイルがあるフォルダを対象に追加する(複数可)
        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Handled = true;
            if (paths == null || !FolderList.IsEnabled)
            {
                return;
            }
            AddFolders(paths.Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)));
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isDeleting)
            {
                MessageBox.Show(this, "削除中は閉じられません。「中止」を押してから閉じてください。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();

            var settings = new AppSettings
            {
                RootFolders = _rootFolders.ToList(),
                CrossRootOnly = CrossRootCheck.IsChecked == true,
                MinSizeMb = MinSizeBox.Text.Trim(),
                Extensions = ExtensionsBox.Text.Trim(),
                SkipHiddenAndSystem = SkipHiddenCheck.IsChecked == true,
                MaxParallelism = ParallelCombo.SelectedItem is int parallelism ? parallelism : 1,
                UseRecycleBin = RecycleCheck.IsChecked == true,
                KeepRule = KeepRuleCombo.SelectedValue is KeepRule rule ? rule : KeepRule.RootOrder,
            };
            try
            {
                settings.Save(_settingsPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 設定が保存できなくても終了は妨げない
            }
        }
    }
}
