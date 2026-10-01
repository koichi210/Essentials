using System.Windows;
using System.Windows.Controls;

namespace EventRecorderForWpf
{
    // 右クリックメニュー「MOUSE_UP時間を一括変更」から開く、待機時間(ms)を1つ入力するだけの小さなダイアログ
    // (WinForms版MouseUpWaitBulkChangeFormのWPF版。WinForms版と同じくコードだけで画面を組み立てている)
    internal class MouseUpWaitBulkChangeWindow : DialogWindowBase
    {
        private readonly TextBox txtWaitMs;

        public int WaitMs { get; private set; }

        public MouseUpWaitBulkChangeWindow()
        {
            Title = "MOUSE_UP時間を一括変更";
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            Grid root = new Grid { Width = 380, Height = 110 };

            TextBlock label = new TextBlock
            {
                Text = "LEFT_UP、RIGHT_UP直前のWAIT時間を一括変更します(ms)",
                Margin = new Thickness(10, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            txtWaitMs = new TextBox
            {
                Text = "100",
                Width = 100,
                Margin = new Thickness(10, 38, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Button btnOk = new Button
            {
                Content = "OK",
                Width = 85,
                Height = 23,
                Margin = new Thickness(285, 70, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsDefault = true,
            };
            btnOk.Click += BtnOk_Click;

            root.Children.Add(label);
            root.Children.Add(txtWaitMs);
            root.Children.Add(btnOk);
            Content = root;

            Loaded += (s, e) =>
            {
                txtWaitMs.Focus();
                txtWaitMs.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            int wait;
            if (!int.TryParse(txtWaitMs.Text, out wait) || wait < 0)
            {
                MessageBox.Show(
                    this,
                    "0以上の整数を入力してね",
                    "MOUSE_UP時間を一括変更",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            WaitMs = wait;
            DialogResult = true;
        }
    }
}
