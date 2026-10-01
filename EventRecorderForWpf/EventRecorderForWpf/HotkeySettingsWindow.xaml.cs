using System;
using System.Windows;
using System.Windows.Input;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorderForWpf
{
    // 記録/再生のホットキーをユーザーが変更するための設定画面(WinForms版HotkeySettingsFormのWPF版)。
    // ウィンドウ左上のシステムメニューから開く。「保存」ボタンを押すまでは呼び出し元(MainWindow)には一切反映されない
    // (RecordHotkey/PlayHotkeyは、DialogResult=trueで閉じた時だけ呼び出し元が読み取って使う)。
    // ホットキーの表現はWinForms版と同じSystem.Windows.Forms.Keys(修飾キーのフラグ込み)
    public partial class HotkeySettingsWindow : DialogWindowBase
    {
        public Keys RecordHotkey { get; private set; }
        public Keys PlayHotkey { get; private set; }

        public HotkeySettingsWindow(Keys currentRecordHotkey, Keys currentPlayHotkey)
        {
            InitializeComponent();

            RecordHotkey = currentRecordHotkey;
            PlayHotkey = currentPlayHotkey;
            textBox_Record.Text = HotkeyFormatter.Format(RecordHotkey);
            textBox_Play.Text = HotkeyFormatter.Format(PlayHotkey);
        }

        private void textBox_Record_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Keys captured;
            if (!TryCaptureHotkey(e, out captured))
            {
                return;
            }

            RecordHotkey = captured;
            textBox_Record.Text = HotkeyFormatter.Format(RecordHotkey);
        }

        private void textBox_Play_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Keys captured;
            if (!TryCaptureHotkey(e, out captured))
            {
                return;
            }

            PlayHotkey = captured;
            textBox_Play.Text = HotkeyFormatter.Format(PlayHotkey);
        }

        // テキストボックスへの通常の文字入力・キャレット移動は起こさせず、押されたキーの
        // 組み合わせをそのままホットキー候補として取り出す。Shift/Ctrl/Alt/Winキー単独の
        // 押下はホットキーとして成立しないので無視し、実キーが押されるまで待つ
        private static Boolean TryCaptureHotkey(KeyEventArgs e, out Keys captured)
        {
            captured = Keys.None;

            Key key = ActualKey(e);
            ModifierKeys modifiers = Keyboard.Modifiers;

            // WinForms版では、Tab(フォーカス移動)・Enter(AcceptButton=保存)はテキストボックスのKeyDownまで
            // 届かなかった(ダイアログキーとして先に処理される)ので、同じくここでは横取りしない。
            // Escape(閉じる)はDialogWindowBaseが先に処理する
            if (key == Key.Tab)
            {
                return false;
            }
            if (key == Key.Enter && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None)
            {
                return false;
            }

            e.Handled = true;

            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin)
            {
                return false;
            }

            captured = ToFormsKeys(key, modifiers);
            return captured != Keys.None;
        }

        // Alt同時押し(Key.System)・IME処理中(Key.ImeProcessed)の場合は、実際に押されたキーを取り出す
        private static Key ActualKey(KeyEventArgs e)
        {
            if (e.Key == Key.System)
            {
                return e.SystemKey;
            }
            if (e.Key == Key.ImeProcessed)
            {
                return e.ImeProcessedKey;
            }
            if (e.Key == Key.DeadCharProcessed)
            {
                return e.DeadCharProcessedKey;
            }
            return e.Key;
        }

        // WPFのKey+修飾キーを、WinForms版のKeyData(例: Ctrl+Sなら Keys.Control | Keys.S)と同じ形に変換する
        internal static Keys ToFormsKeys(Key key, ModifierKeys modifiers)
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
            {
                return Keys.None;
            }

            Keys result = (Keys)virtualKey;
            if ((modifiers & ModifierKeys.Control) != 0)
            {
                result |= Keys.Control;
            }
            if ((modifiers & ModifierKeys.Alt) != 0)
            {
                result |= Keys.Alt;
            }
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                result |= Keys.Shift;
            }
            return result;
        }

        private void button_Reset_Click(object sender, RoutedEventArgs e)
        {
            RecordHotkey = HotkeyDefaults.Record;
            PlayHotkey = HotkeyDefaults.Play;
            textBox_Record.Text = HotkeyFormatter.Format(RecordHotkey);
            textBox_Play.Text = HotkeyFormatter.Format(PlayHotkey);
        }

        private void button_Save_Click(object sender, RoutedEventArgs e)
        {
            if (RecordHotkey == PlayHotkey)
            {
                MessageBox.Show(
                    this,
                    "レコードと再生に同じキーは割り当てられないよ。どちらかを変えてね",
                    "EventRecorder - キーバインド設定",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
