// WinForms版のDateTimePicker(Format=Time, ShowUpDown=true)の代わりに使う、時刻入力欄の処理。
// WPFには時刻入力コントロールが無いため、TextBox(H:mm:ss)＋上下ボタン/↑↓キーで同じ操作ができるようにした。
// ↑↓で増減するのは、カーソルがある欄(時/分/秒)だけ。WinForms版と同じく桁上がりはせず、その欄の中で一周する。
using System;
using System.Globalization;

namespace FFEditForWpf
{
    internal static class TimeText
    {
        public const String DisplayFormat = "H:mm:ss";

        private static readonly String[] ParseFormats = { "H:mm:ss", "H:m:s", "H:mm", "H:m" };

        public static String Format(TimeSpan time)
        {
            return new DateTime(time.Ticks).ToString(DisplayFormat, CultureInfo.InvariantCulture);
        }

        // 時刻文字列を時刻(0:00:00～23:59:59)にする。形式が不正ならfalse
        public static Boolean TryParse(String text, out TimeSpan time)
        {
            DateTime parsed;
            if (DateTime.TryParseExact((text ?? "").Trim(), ParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }
            time = TimeSpan.Zero;
            return false;
        }

        // カーソル位置(caretIndex)より前にある':'の数で、どの欄か(0=時, 1=分, 2=秒)を決める
        public static int GetFieldIndex(String text, int caretIndex)
        {
            int field = 0;
            for (int i = 0; i < caretIndex && i < text.Length; i++)
            {
                if (text[i] == ':')
                {
                    field++;
                }
            }
            return Math.Min(field, 2);
        }

        // 指定した欄だけをdelta分増減する(桁上がりせず、その欄の中で一周する)
        public static TimeSpan Increment(TimeSpan time, int fieldIndex, int delta)
        {
            int hours = time.Hours;
            int minutes = time.Minutes;
            int seconds = time.Seconds;
            switch (fieldIndex)
            {
                case 0:
                    hours = Wrap(hours + delta, 24);
                    break;
                case 1:
                    minutes = Wrap(minutes + delta, 60);
                    break;
                default:
                    seconds = Wrap(seconds + delta, 60);
                    break;
            }
            return new TimeSpan(hours, minutes, seconds);
        }

        private static int Wrap(int value, int range)
        {
            return ((value % range) + range) % range;
        }
    }
}
