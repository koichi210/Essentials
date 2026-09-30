using System;
using System.Windows;

namespace FFEditForWpf
{
    public partial class ErrorMsg : Window
    {
        public ErrorMsg(String msg)
        {
            InitializeComponent();
            textBox_ErrorMessage.Text = msg;
        }

        private void button_Close_Click(object sender, RoutedEventArgs e)
        {
            // ShowDialogで開いているので、DialogResultを入れれば閉じる(WinForms版のDialogResult.OK+Close相当)
            DialogResult = true;
        }
    }
}
