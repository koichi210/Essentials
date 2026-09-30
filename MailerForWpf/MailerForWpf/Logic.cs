using System;
using System.Collections.Generic;

namespace MailerForWpf
{
    /// <summary>
    /// もともと Form1.cs に private メソッドとして埋め込まれていた、メール件名・本文の
    /// プレースホルダ置換ロジックを、テストできる形に切り出したもの。
    ///
    /// コードは元のファイルにあったものをそのまま移しただけで、中身の書き換えはしていない。
    /// </summary>
    internal static class Logic
    {
        /// <summary>件名・本文の中の %%usersday%% / %%today%% / %%dayofweek%% 等をすべて置換する。</summary>
        public static String GetReplaceDay(String SrcText, DateTime UserDate)
        {
            var NewText = GetUsersDay(SrcText, UserDate);
            NewText = GetDateText(NewText);
            NewText = GetDayOfWeek(NewText, UserDate);
            return NewText;
        }

        public static String GetDayOfWeek(String SrcText, DateTime dt)
        {
            String DestText = SrcText.Replace("%%dayofweek%%", dt.ToString("ddd"));
            DestText = DestText.Replace("%%DAYOFWEEK%%", dt.ToString("dddd"));
            return DestText;
        }

        public static String GetUsersDay(String SrcText, DateTime UserDate)
        {
            String DestText = "";
            DestText = ReplaceDay(UserDate, SrcText, "%%USERSDAY%%");
            DestText = ReplaceDay(UserDate, DestText, "%%usersday%%", false);
            return DestText;
        }

        /// <summary>DateTime.Now を基準に %%today%% / %%tomorrow%% / %%weekend%% を置換する。</summary>
        public static String GetDateText(String SrcText)
        {
            DateTime today = DateTime.Now;
            String DestText = "";
            DestText = ReplaceDay(today, SrcText, "%%TODAY%%");
            DestText = ReplaceDay(today, DestText, "%%today%%", false);

            var tomorrow = today.AddDays(1);
            DestText = ReplaceDay(tomorrow, DestText, "%%TOMORROW%%");
            DestText = ReplaceDay(tomorrow, DestText, "%%tomorrow%%", false);

            DateTime friday = today.AddDays(today.DayOfWeek == DayOfWeek.Friday ? 0 : 5 - (int)today.DayOfWeek);
            DestText = ReplaceDay(friday, DestText, "%%WEEKEND%%");
            DestText = ReplaceDay(friday, DestText, "%%weekend%%", false);
            return DestText;
        }

        public static String ReplaceDay(DateTime dt, String SrcText, String KeyName, bool IsYear = true)
        {
            String DateString = "";
            if (IsYear)
            {
                DateString += dt.Year.ToString() + "/";
            }
            DateString += dt.Month.ToString() + "/";
            DateString += dt.Day.ToString();

            return SrcText.Replace(KeyName, DateString);
        }

        /// <summary>メール作成する日数分のオフセット一覧を作る。reverse指定で降順にする。</summary>
        public static List<int> GetLoopList(int createNum, bool reverse)
        {
            var offsetDay = new List<int>();
            for (var i = 0; i < createNum; i++)
            {
                offsetDay.Add(i);
            }
            if (reverse)
            {
                offsetDay.Sort((x, y) => y - x);
            }
            return offsetDay;
        }
    }
}
