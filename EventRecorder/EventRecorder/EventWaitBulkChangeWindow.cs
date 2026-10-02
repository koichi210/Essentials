using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EventRecorder
{
    // 右クリックメニュー「WAIT時間を一括変更」から開く、対象イベント名と待機時間(ms)を入力するダイアログ。
    // 対象イベントは「直前がWAIT_MS行であるイベント」で、そのWAIT_MS行の待機時間をまとめて変更する
    // (WinForms版EventWaitBulkChangeFormのWPF版。WinForms版と同じくコードだけで画面を組み立てている)
    internal class EventWaitBulkChangeWindow : DialogWindowBase
    {
        private readonly TextBox txtEventName;
        private readonly TextBox txtWaitMs;

        public String EventName { get; private set; }
        public int WaitMs { get; private set; }

        // 変更対象に指定できるイベント名(WAIT_MS自身とUNKNOWNは対象外)。大文字小文字は区別せず、正規の名前へ揃える
        internal static Boolean TryNormalizeEventName(String text, out String eventName)
        {
            eventName = null;
            String trimmed = (text ?? String.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            foreach (String name in Enum.GetNames(typeof(GlobalHook.MouseHook.Stroke))
                .Concat(Enum.GetNames(typeof(GlobalHook.KeyboardHook.Stroke))))
            {
                if (name == "UNKNOWN")
                {
                    continue;
                }

                if (String.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    eventName = name;
                    return true;
                }
            }

            return false;
        }

        public EventWaitBulkChangeWindow(String initialEventName)
        {
            Title = "WAIT時間を一括変更";
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            Grid root = new Grid { Width = 380, Height = 150 };

            TextBlock label = new TextBlock
            {
                Text = "対象イベント(このイベント直前のWAIT_MS行が対象)",
                Margin = new Thickness(10, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            txtEventName = new TextBox
            {
                Text = initialEventName,
                Width = 200,
                Margin = new Thickness(10, 34, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            TextBlock waitLabel = new TextBlock
            {
                Text = "変更後のWAIT時間(ms)",
                Margin = new Thickness(10, 68, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            txtWaitMs = new TextBox
            {
                Text = "100",
                Width = 100,
                Margin = new Thickness(10, 90, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Button btnOk = new Button
            {
                Content = "OK",
                Width = 85,
                Height = 23,
                Margin = new Thickness(285, 110, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsDefault = true,
            };
            btnOk.Click += BtnOk_Click;

            root.Children.Add(label);
            root.Children.Add(txtEventName);
            root.Children.Add(waitLabel);
            root.Children.Add(txtWaitMs);
            root.Children.Add(btnOk);
            Content = root;

            Loaded += (s, e) =>
            {
                // 初期値のイベント名が入っていれば待機時間から、無ければイベント名から入力してもらう
                TextBox target = txtEventName.Text.Length > 0 ? txtWaitMs : txtEventName;
                target.Focus();
                target.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            String eventName;
            if (!TryNormalizeEventName(txtEventName.Text, out eventName))
            {
                MessageBox.Show(
                    this,
                    "イベント名が正しくないよ(例: LEFT_UP, RIGHT_DOWN, KEY_DOWN, KEY_UP)",
                    "WAIT時間を一括変更",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                txtEventName.Focus();
                return;
            }

            int wait;
            if (!int.TryParse(txtWaitMs.Text, out wait) || wait < 0)
            {
                MessageBox.Show(
                    this,
                    "0以上の整数を入力してね",
                    "WAIT時間を一括変更",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EventName = eventName;
            WaitMs = wait;
            DialogResult = true;
        }
    }
}
