using System.Windows;
using Microsoft.Win32;

namespace Launcher.Dialogs
{
    /// <summary>
    /// 項目（タイトル＋リンク先）の追加・編集ダイアログ
    /// </summary>
    public partial class ItemEditDialog : Window
    {
        public string ItemTitle => TitleTextBox.Text;
        public string ItemPath => PathTextBox.Text;

        public ItemEditDialog(string title = "", string path = "")
        {
            InitializeComponent();
            TitleTextBox.Text = title;
            PathTextBox.Text = path;
            TitleTextBox.Focus();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "起動するファイルを選択",
                Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
            };

            if (dialog.ShowDialog(this) == true)
            {
                PathTextBox.Text = dialog.FileName;

                if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
                {
                    TitleTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(PathTextBox.Text))
            {
                MessageBox.Show("リンク先を入力してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
