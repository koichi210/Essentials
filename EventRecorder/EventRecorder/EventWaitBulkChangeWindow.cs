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
        private readonly TextBox _txtEventName;
        private readonly TextBox _txtWaitMs;

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

            eventName = Enum.GetNames(typeof(GlobalHook.MouseHook.Stroke))
                .Concat(Enum.GetNames(typeof(GlobalHook.KeyboardHook.Stroke)))
                .FirstOrDefault(name => name != "UNKNOWN" && String.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
            return eventName != null;
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

            _txtEventName = new TextBox
            {
                Text = initialEventName,
                Width = 200,
                Margin = new Thickness(10, 34, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Event列の名前(例: LEFT_DOWN、KEY_DOWN)。大文字/小文字は区別しない。直前がWAIT_MS行でない行は変更されない",
            };

            TextBlock waitLabel = new TextBlock
            {
                Text = "変更後のWAIT時間(ms)",
                Margin = new Thickness(10, 68, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            _txtWaitMs = new TextBox
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
            root.Children.Add(_txtEventName);
            root.Children.Add(waitLabel);
            root.Children.Add(_txtWaitMs);
            root.Children.Add(btnOk);
            Content = root;

            Loaded += (s, e) =>
            {
                // 初期値のイベント名が入っていれば待機時間から、無ければイベント名から入力してもらう
                TextBox target = _txtEventName.Text.Length > 0 ? _txtWaitMs : _txtEventName;
                target.Focus();
                target.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            String eventName;
            if (!TryNormalizeEventName(_txtEventName.Text, out eventName))
            {
                ShowWarning("イベント名が正しくないよ(例: LEFT_UP, RIGHT_DOWN, KEY_DOWN, KEY_UP)");
                _txtEventName.Focus();
                return;
            }

            int wait;
            if (!int.TryParse(_txtWaitMs.Text, out wait) || wait < 0)
            {
                ShowWarning("0以上の整数を入力してね");
                return;
            }

            EventName = eventName;
            WaitMs = wait;
            DialogResult = true;
        }

        private void ShowWarning(String message)
        {
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
