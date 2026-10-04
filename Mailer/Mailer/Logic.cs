using System;
using System.Collections.Generic;

namespace Mailer
{
    /// <summary>
    /// もともと Form1.cs に private メソッドとして埋め込まれていた、メール件名・本文の
    /// プレースホルダ置換ロジックを、テストできる形に切り出したもの。
    /// </summary>
    internal static class Logic
    {
        /// <summary>件名・本文の中の %%usersday%% / %%today%% / %%dayofweek%% 等をすべて置換する。</summary>
        public static String ReplaceDatePlaceholders(String srcText, DateTime userDate)
        {
            var newText = ReplaceUsersDay(srcText, userDate);
            newText = ReplaceRelativeDays(newText);
            newText = ReplaceDayOfWeek(newText, userDate);
            return newText;
        }

        public static String ReplaceDayOfWeek(String srcText, DateTime dt)
        {
            String destText = srcText.Replace("%%dayofweek%%", dt.ToString("ddd"));
            destText = destText.Replace("%%DAYOFWEEK%%", dt.ToString("dddd"));
            return destText;
        }

        public static String ReplaceUsersDay(String srcText, DateTime userDate)
        {
            String destText = ReplaceDay(userDate, srcText, "%%USERSDAY%%");
            destText = ReplaceDay(userDate, destText, "%%usersday%%", false);
            return destText;
        }

        /// <summary>DateTime.Now を基準に %%today%% / %%tomorrow%% / %%weekend%% を置換する。</summary>
        public static String ReplaceRelativeDays(String srcText)
        {
            DateTime today = DateTime.Now;
            String destText = ReplaceDay(today, srcText, "%%TODAY%%");
            destText = ReplaceDay(today, destText, "%%today%%", false);

            var tomorrow = today.AddDays(1);
            destText = ReplaceDay(tomorrow, destText, "%%TOMORROW%%");
            destText = ReplaceDay(tomorrow, destText, "%%tomorrow%%", false);

            DateTime friday = today.AddDays(today.DayOfWeek == DayOfWeek.Friday ? 0 : 5 - (int)today.DayOfWeek);
            destText = ReplaceDay(friday, destText, "%%WEEKEND%%");
            destText = ReplaceDay(friday, destText, "%%weekend%%", false);
            return destText;
        }

        public static String ReplaceDay(DateTime dt, String srcText, String keyName, bool withYear = true)
        {
            String dateString = withYear
                ? dt.Year + "/" + dt.Month + "/" + dt.Day
                : dt.Month + "/" + dt.Day;

            return srcText.Replace(keyName, dateString);
        }

        /// <summary>メール作成する日数分のオフセット一覧を作る。reverse指定で降順にする。</summary>
        public static List<int> GetDayOffsetList(int createNum, bool reverse)
        {
            var offsets = new List<int>(Math.Max(createNum, 0));
            for (var i = 0; i < createNum; i++)
            {
                offsets.Add(reverse ? createNum - 1 - i : i);
            }
            return offsets;
        }
    }
}
