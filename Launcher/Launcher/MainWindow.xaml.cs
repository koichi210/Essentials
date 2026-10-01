using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Launcher.Dialogs;
using Launcher.Models;
using Launcher.Services;

namespace Launcher
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ConfigService _configService = new ConfigService();
        private LauncherConfig _config;

        public ObservableCollection<SectionModel> Sections => _config.Sections;

        public MainWindow()
        {
            InitializeComponent();

            LoadConfig();
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

        // セクションを末尾に追加
        private void AddSectionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextInputDialog("セクション名を入力してください") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            Sections.Add(new SectionModel { Name = dialog.InputText, IsExpanded = true });
            SaveConfig();
        }

        // セクション名を変更
        private void RenameSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not SectionModel section)
            {
                return;
            }

            var dialog = new TextInputDialog("セクション名を入力してください", section.Name) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            section.Name = dialog.InputText;
            SaveConfig();
        }

        // セクションを削除
        private void DeleteSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not SectionModel section)
            {
                return;
            }

            var result = MessageBox.Show(
                $"セクション「{section.Name}」を削除します。\n（中の項目も全て削除されます）よろしいですか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            Sections.Remove(section);
            SaveConfig();
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

            section.Items.Add(new ItemModel { Title = dialog.ItemTitle, Path = dialog.ItemPath });
            SaveConfig();
        }

        // 項目を編集
        private void EditItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not ItemModel item)
            {
                return;
            }

            var dialog = new ItemEditDialog(item.Title, item.Path) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            item.Title = dialog.ItemTitle;
            item.Path = dialog.ItemPath;
            SaveConfig();
        }

        // 項目を削除
        private void DeleteItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not ItemModel item)
            {
                return;
            }

            var owningSection = Sections.FirstOrDefault(s => s.Items.Contains(item));
            if (owningSection == null)
            {
                return;
            }

            var result = MessageBox.Show($"項目「{item.Title}」を削除してよろしいですか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            owningSection.Items.Remove(item);
            SaveConfig();
        }

        // 設定ファイルをテキストエディタで開く（手動編集したい場合用）
        private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();

            try
            {
                LaunchService.Launch(_configService.ConfigFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定ファイルを開けませんでした。\n\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 設定ファイルを再読み込み（他で編集した場合用）
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        // 保存先フォルダを選び直す
        private void ChangeDataFolderButton_Click(object sender, RoutedEventArgs e)
        {
            bool changed;
            string message;
            try
            {
                changed = _configService.ChangeDataFolder(out message);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存先フォルダの変更に失敗しました。\n\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!changed)
            {
                return;
            }

            // 新しい保存先の内容を読み込み直す（既存のconfig.jsonがあればそちらを使う）
            LoadConfig();

            MessageBox.Show(message, "保存先フォルダの変更", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>設定を読み込み直し、画面(セクション一覧・保存先表示)に反映する</summary>
        private void LoadConfig()
        {
            _config = _configService.Load();
            SectionsItemsControl.ItemsSource = Sections;
            UpdateDataFolderText();
        }

        private void UpdateDataFolderText()
        {
            DataFolderText.Text = $"保存先: {_configService.ConfigFilePath}";
        }

        private void SaveConfig()
        {
            _configService.Save(_config);
        }
    }
}
