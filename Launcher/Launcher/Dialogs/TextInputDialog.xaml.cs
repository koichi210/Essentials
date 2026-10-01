using System.Windows;

namespace Launcher.Dialogs
{
    /// <summary>
    /// 1行テキスト入力用の簡易ダイアログ（セクション名の追加・変更に使用）
    /// </summary>
    public partial class TextInputDialog : Window
    {
        /// <summary>入力結果</summary>
        public string InputText => InputTextBox.Text;

        public TextInputDialog(string message, string defaultText = "")
        {
            InitializeComponent();
            MessageText.Text = message;
            InputTextBox.Text = defaultText;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                MessageBox.Show("名前を入力してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
