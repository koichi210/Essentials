using System;
using System.Collections.Generic;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorderForWpf
{
    // ホットキーの既定値。設定画面([[HotkeySettingsWindow]])の「初期値に戻す」ボタンと、
    // MainWindow側の初期値の両方から同じ値を参照する(WinForms版と同じ値)
    public static class HotkeyDefaults
    {
        // 記録開始/停止。ボタンクリックだとクリック自体のマウスイベントが記録に
        // 混ざってしまうため、キー操作で完結できるようにしている
        public const Keys Record = Keys.F1;

        // 再生開始/停止。IME変換キーはアプリのテキスト入力とほぼ衝突しないため既定に選んでいる
        public const Keys Play = Keys.IMEConvert;
    }

    // ホットキーの組み合わせ(Keys)を「Ctrl+Shift+F2」のような表示用文字列に変換する(WinForms版と同じ)
    public static class HotkeyFormatter
    {
        public static String Format(Keys hotkey)
        {
            if (hotkey == Keys.None)
            {
                return String.Empty;
            }

            List<String> parts = new List<String>();
            if ((hotkey & Keys.Control) == Keys.Control)
            {
                parts.Add("Ctrl");
            }
            if ((hotkey & Keys.Alt) == Keys.Alt)
            {
                parts.Add("Alt");
            }
            if ((hotkey & Keys.Shift) == Keys.Shift)
            {
                parts.Add("Shift");
            }

            parts.Add(FormatKeyCode(hotkey & Keys.KeyCode));

            return String.Join("+", parts);
        }

        // Keys.ToString()の表記が分かりにくいものだけ、日本語の見出しに置き換える
        private static String FormatKeyCode(Keys keyCode)
        {
            switch (keyCode)
            {
                case Keys.IMEConvert:
                    return "変換";
                case Keys.IMENonconvert:
                    return "無変換";
                default:
                    return keyCode.ToString();
            }
        }
    }
}
