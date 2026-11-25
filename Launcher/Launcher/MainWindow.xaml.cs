using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Launcher.Dialogs;
using Launcher.Models;
using Launcher.Services;
using Microsoft.Win32;

namespace Launcher
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// 設定ファイルは MagicLauncher(魔法陣ランチャー)と共通。画面は「読む → 組み立てる」だけで、
    /// 変更は毎回 ConfigService がディスクの内容を読み直して差分だけ当てる(魔法陣側の変更を消さない)。
    /// </summary>
    public partial class MainWindow : Window
    {
        // 魔法陣ランチャーや手での編集を取り込む間隔(MagicLauncher の画面と同じ 10 秒)
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

        private readonly ConfigService _configService = new ConfigService();
        private readonly ViewStateService _viewState = new ViewStateService();
        private readonly DispatcherTimer _pollTimer;

        private ConfigSnapshot _snapshot;
        private string _loadedVersion;
        private List<CircleModel> _circles = new List<CircleModel>();

        public MainWindow()
        {
            InitializeComponent();

            LoadConfig();

            _pollTimer = new DispatcherTimer { Interval = _pollInterval };
            _pollTimer.Tick += (s, e) => ReloadIfChanged();
            _pollTimer.Start();
            Activated += (s, e) => ReloadIfChanged();
        }

        // ランチャー項目を起動
        private void LaunchItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not ItemModel item)
            {
                return;
            }

            try
            {
                LaunchService.Launch(item.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"起動に失敗しました。\n\nリンク先: {item.Path}\n\n{ex.Message}", "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 魔法陣にセクション(タグ)を追加
        private void AddSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not CircleModel circle)
            {
                return;
            }

            var dialog = new TextInputDialog($"「{circle.Name}」に追加するセクション名を入力してください") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            RunChange(() => _configService.AddTag(dialog.InputText, circle.Name));
        }

        // セクション名を変更
        private void RenameSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not SectionModel section || section.IsUntagged)
            {
                return;
            }

            var dialog = new TextInputDialog("セクション名を入力してください", section.Name) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string newName = dialog.InputText.Trim();
            RunChange(() =>
            {
                _configService.RenameTag(section.Name, newName);
                _viewState.Rename(section.Key, newName);
            });
        }

        // セクションを削除
        private void DeleteSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not SectionModel section || section.IsUntagged)
            {
                return;
            }

            int moving = section.Items.Count(i => i.Tags.Count > 1);
            int deleting = section.Items.Count - moving;
            string detail = section.Items.Count == 0
                ? "（中に項目はありません）"
                : $"（中の項目 {section.Items.Count} 件のうち、{deleting} 件は削除されます" + (moving > 0 ? $"。ほかのタグも付いている {moving} 件は、そのタグのセクションへ移ります）" : "）");
            var result = MessageBox.Show(
                $"セクション「{section.Name}」を削除します。\n{detail}\n魔法陣ランチャーからも消えます。よろしいですか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            RunChange(() => _configService.DeleteTag(section.Name));
        }

        // セクションに項目を追加
        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not SectionModel section)
            {
                return;
            }

            var dialog = new ItemEditDialog { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            RunChange(() => _configService.AddItem(section.IsUntagged ? null : section.Name, dialog.ItemTitle, dialog.ItemPath));
        }

        // 項目を編集
        private void EditItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not ItemModel item)
            {
                return;
            }

            var dialog = new ItemEditDialog(item.Name, item.Path) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            RunChange(() => _configService.UpdateItem(item.Id, dialog.ItemTitle, dialog.ItemPath));
        }

        // 項目を削除
        private void DeleteItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not ItemModel item)
            {
                return;
            }

            var result = MessageBox.Show($"項目「{item.Title}」を削除してよろしいですか？\n（魔法陣ランチャーからも消えます）", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            RunChange(() => _configService.DeleteItem(item.Id));
        }

        // 設定ファイルをテキストエディタで開く（手動編集したい場合用）
        private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
        {
            string path = _configService.GetLocation().FullPath;
            try
            {
                if (!File.Exists(path))
                {
                    // まだ無ければ空の設定ファイルを作ってから開く
                    _configService.Mutate(data => 0);
                    LoadConfig();
                }
                LaunchService.Launch(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定ファイルを開けませんでした。\n\n{path}\n\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 設定ファイルを再読み込み（他で編集した場合用）
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        // 使う設定ファイルを選び直す(MagicLauncher の「設定ファイルの場所」も同じ値になる)
        private void ChangeConfigPathButton_Click(object sender, RoutedEventArgs e)
        {
            string current = _configService.GetLocation().FullPath;
            string currentDir = Path.GetDirectoryName(current);
            var dialog = new SaveFileDialog
            {
                Title = "共通の設定ファイル(launcher.json)を選んでください",
                Filter = "JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                FileName = Path.GetFileName(current),
                InitialDirectory = Directory.Exists(currentDir) ? currentDir : "",
                OverwritePrompt = false,
                CheckPathExists = true,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string selected = Path.GetFullPath(dialog.FileName);
            if (string.Equals(selected, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                _configService.SetConfigPath(selected);
            }
            catch (Exception ex) when (ex is ConfigException || ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show($"設定ファイルの場所を変更できませんでした。\n\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadConfig();

            string note = File.Exists(selected) ? "" : "\n（まだファイルが無いので、最初に登録したときに作られるよ）";
            MessageBox.Show($"設定ファイルの場所を変更したよ。魔法陣ランチャーも同じファイルを使うよ。\n\n{selected}{note}",
                "設定ファイルの場所の変更", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>変更を設定ファイルに書き、読み直して画面に反映する</summary>
        private void RunChange(Action change)
        {
            try
            {
                change();
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(ex.Message, "保存できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 保存先(ネットワークドライブ・クラウド同期フォルダ等)に書けないときも、アプリごと落ちないようにする
                MessageBox.Show($"設定を保存できませんでした。\n\n{_configService.GetLocation().FullPath}\n\n{ex.Message}",
                    "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            LoadConfig();
        }

        // ほかの所(魔法陣ランチャー・手での編集・設定ファイルの場所の変更)で変わっていたら読み直す
        private void ReloadIfChanged()
        {
            if (_configService.GetVersionToken() != _loadedVersion)
            {
                LoadConfig();
            }
        }

        /// <summary>設定を読み込み直し、画面(魔法陣・セクション一覧・設定ファイルの場所)に反映する</summary>
        private void LoadConfig()
        {
            // 読めなかった(手で編集している途中の書き間違い等)ときは警告の帯を出し、
            // 読めるまで書き込みは ConfigService が拒否する。画面は最後に読めた内容のまま
            _loadedVersion = _configService.GetVersionToken();
            _snapshot = _configService.Load();
            if (_snapshot.Error == null)
            {
                foreach (SectionModel section in _circles.SelectMany(c => c.Sections))
                {
                    section.PropertyChanged -= Section_PropertyChanged;
                }
                _circles = ViewBuilder.Build(_snapshot.Data, _viewState.IsExpanded);
                foreach (SectionModel section in _circles.SelectMany(c => c.Sections))
                {
                    section.PropertyChanged += Section_PropertyChanged;
                }
                CirclesItemsControl.ItemsSource = _circles;
                ErrorBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text = $"⚠ 設定ファイルを読めません（直してから「再読み込み」してください。読めるまで変更は保存しません）\n{_snapshot.Error}";
                ErrorBanner.Visibility = Visibility.Visible;
            }

            ConfigLocation location = _snapshot.Location;
            ConfigPathText.Text = $"設定ファイル: {location.FullPath}" + (location.IsDefault ? "（既定の場所。魔法陣ランチャーとはまだ共有していません）" : "");
            ConfigPathText.ToolTip = ConfigPathText.Text;
        }

        // セクションの開閉はこの PC の画面の状態として覚える(共有の設定ファイルには書かない)
        private void Section_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SectionModel.IsExpanded) && sender is SectionModel section)
            {
                _viewState.SetExpanded(section.Key, section.IsExpanded);
            }
        }
    }
}
