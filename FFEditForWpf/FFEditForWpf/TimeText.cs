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

        public static String Format(TimeSpan Time)
        {
            return new DateTime(Time.Ticks).ToString(DisplayFormat, CultureInfo.InvariantCulture);
        }

        // 時刻文字列を時刻(0:00:00～23:59:59)にする。形式が不正ならfalse
        public static Boolean TryParse(String Text, out TimeSpan Time)
        {
            DateTime parsed;
            if (DateTime.TryParseExact((Text ?? "").Trim(), ParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                Time = parsed.TimeOfDay;
                return true;
            }
            Time = TimeSpan.Zero;
            return false;
        }

        // カーソル位置(CaretIndex)より前にある':'の数で、どの欄か(0=時, 1=分, 2=秒)を決める
        public static int GetFieldIndex(String Text, int CaretIndex)
        {
            int field = 0;
            for (int i = 0; i < CaretIndex && i < Text.Length; i++)
            {
                if (Text[i] == ':')
                {
                    field++;
                }
            }
            return Math.Min(field, 2);
        }

        // 指定した欄だけをDelta分増減する(桁上がりせず、その欄の中で一周する)
        public static TimeSpan Increment(TimeSpan Time, int FieldIndex, int Delta)
        {
            int hours = Time.Hours;
            int minutes = Time.Minutes;
            int seconds = Time.Seconds;
            switch (FieldIndex)
            {
                case 0:
                    hours = Wrap(hours + Delta, 24);
                    break;
                case 1:
                    minutes = Wrap(minutes + Delta, 60);
                    break;
                default:
                    seconds = Wrap(seconds + Delta, 60);
                    break;
            }
            return new TimeSpan(hours, minutes, seconds);
        }

        private static int Wrap(int Value, int Range)
        {
            return ((Value % Range) + Range) % Range;
        }
    }
}
