using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MailerForWpf.Tests
{
    /// <summary>
    /// Logic（Form1.cs から切り出した、メール件名・本文のプレースホルダ置換ロジック）
    /// のテスト。抽出前と挙動が変わっていないことを、抽出後のコードに対して確認する。
    /// </summary>
    [TestClass]
    public class LogicTests
    {
        // ------------------------------------------------------------------
        // ReplaceDay
        // ------------------------------------------------------------------

        [TestMethod]
        public void ReplaceDay_年あり指定でyyyy_M_d形式に置換する()
        {
            var dt = new DateTime(2024, 1, 5);

            string result = Logic.ReplaceDay(dt, "date=%%KEY%%", "%%KEY%%");

            Assert.AreEqual("date=2024/1/5", result);
        }

        [TestMethod]
        public void ReplaceDay_年無し指定でM_d形式に置換する()
        {
            var dt = new DateTime(2024, 1, 5);

            string result = Logic.ReplaceDay(dt, "date=%%KEY%%", "%%KEY%%", false);

            Assert.AreEqual("date=1/5", result);
        }

        [TestMethod]
        public void ReplaceDay_キーが無ければ変化しない()
        {
            var dt = new DateTime(2024, 1, 5);

            string result = Logic.ReplaceDay(dt, "no key here", "%%KEY%%");

            Assert.AreEqual("no key here", result);
        }

        // ------------------------------------------------------------------
        // GetDayOfWeek
        // ------------------------------------------------------------------

        // dt.ToString("ddd"/"dddd") は実行環境のカレントカルチャに依存する
        // （このマシンは日本語ロケールなので "月"/"月曜日" になる）。特定言語を
        // ハードコードせず、同じ書式指定子で計算した値と突き合わせて検証する。

        [TestMethod]
        public void ReplaceDayOfWeek_小文字は3文字略称になる()
        {
            var monday = new DateTime(2024, 1, 1); // 2024/1/1は月曜日

            string result = Logic.ReplaceDayOfWeek("%%dayofweek%%", monday);

            Assert.AreEqual(monday.ToString("ddd"), result);
        }

        [TestMethod]
        public void ReplaceDayOfWeek_大文字は完全な曜日名になる()
        {
            var monday = new DateTime(2024, 1, 1);

            string result = Logic.ReplaceDayOfWeek("%%DAYOFWEEK%%", monday);

            Assert.AreEqual(monday.ToString("dddd"), result);
        }

        // ------------------------------------------------------------------
        // GetUsersDay
        // ------------------------------------------------------------------

        [TestMethod]
        public void ReplaceUsersDay_大文字小文字それぞれ置換される()
        {
            var dt = new DateTime(2024, 3, 10);

            string result = Logic.ReplaceUsersDay("%%USERSDAY%% / %%usersday%%", dt);

            Assert.AreEqual("2024/3/10 / 3/10", result);
        }

        // ------------------------------------------------------------------
        // GetDateText（DateTime.Now基準なので、実行時刻から期待値を動的に求める）
        // ------------------------------------------------------------------

        [TestMethod]
        public void ReplaceRelativeDays_todayとTODAYが現在日付に置換される()
        {
            DateTime before = DateTime.Now;
            string result = Logic.ReplaceRelativeDays("%%today%% / %%TODAY%%");
            DateTime after = DateTime.Now;

            string expectedShort = string.Format("{0}/{1}", before.Month, before.Day);
            string expectedLong = string.Format("{0}/{1}/{2}", before.Year, before.Month, before.Day);

            // 日付をまたぐ瞬間のテスト実行を考慮し、before/afterのどちらかと一致すればよい
            bool matchesBefore = result == expectedShort + " / " + expectedLong;
            bool matchesAfter = result == string.Format("{0}/{1}", after.Month, after.Day) + " / " +
                                          string.Format("{0}/{1}/{2}", after.Year, after.Month, after.Day);
            Assert.IsTrue(matchesBefore || matchesAfter, "実行時刻に基づく日付になっているはず: " + result);
        }

        [TestMethod]
        public void ReplaceRelativeDays_tomorrowは翌日になる()
        {
            DateTime tomorrow = DateTime.Now.AddDays(1);
            string expected = string.Format("{0}/{1}", tomorrow.Month, tomorrow.Day);

            string result = Logic.ReplaceRelativeDays("%%tomorrow%%");

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void ReplaceRelativeDays_置換対象が無ければそのまま()
        {
            string result = Logic.ReplaceRelativeDays("no placeholder here");

            Assert.AreEqual("no placeholder here", result);
        }

        // ------------------------------------------------------------------
        // GetReplaceDay（3種類の置換を組み合わせる）
        // ------------------------------------------------------------------

        [TestMethod]
        public void ReplaceDatePlaceholders_ユーザー日付とtodayとdayofweekを同時に置換できる()
        {
            var userDate = new DateTime(2024, 6, 15); // 2024/6/15は土曜日

            string result = Logic.ReplaceDatePlaceholders("%%usersday%% (%%dayofweek%%)", userDate);

            Assert.AreEqual("6/15 (" + userDate.ToString("ddd") + ")", result);
        }

        // ------------------------------------------------------------------
        // GetLoopList
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetDayOffsetList_件数分の連番リストを作る()
        {
            List<int> result = Logic.GetDayOffsetList(3, false);

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result);
        }

        [TestMethod]
        public void GetDayOffsetList_reverse指定で降順になる()
        {
            List<int> result = Logic.GetDayOffsetList(3, true);

            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, result);
        }

        [TestMethod]
        public void GetDayOffsetList_0件なら空リスト()
        {
            List<int> result = Logic.GetDayOffsetList(0, false);

            Assert.AreEqual(0, result.Count);
        }
    }
}
