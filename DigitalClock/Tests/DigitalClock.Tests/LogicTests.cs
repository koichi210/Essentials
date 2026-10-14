using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DigitalClock.Tests
{
    /// <summary>
    /// Logic(時刻の "HH:mm:ss" 整形と、復元位置が画面内かの判定)のテスト。
    /// 整形のテストはWinForms版DigitalClock.Testsと同じ内容。
    /// </summary>
    [TestClass]
    public class LogicTests
    {
        [TestMethod]
        public void 時刻を2桁ゼロ埋めで整形する()
        {
            DateTime d = new DateTime(2024, 1, 1, 9, 5, 3);

            string result = Logic.FormatTime(d);

            Assert.AreEqual("09:05:03", result);
        }

        [TestMethod]
        public void 既に2桁の時刻はそのまま整形する()
        {
            DateTime d = new DateTime(2024, 1, 1, 23, 59, 59);

            string result = Logic.FormatTime(d);

            Assert.AreEqual("23:59:59", result);
        }

        [TestMethod]
        public void 深夜0時も正しく整形する()
        {
            DateTime d = new DateTime(2024, 1, 1, 0, 0, 0);

            string result = Logic.FormatTime(d);

            Assert.AreEqual("00:00:00", result);
        }

        [TestMethod]
        public void 画面内の位置は復元できる()
        {
            Assert.IsTrue(Logic.IsOnScreen(100, 100, 0, 0, 1920, 1080));
        }

        [TestMethod]
        public void 左のサブモニタ側の負の座標も画面内として扱う()
        {
            Assert.IsTrue(Logic.IsOnScreen(-1800, 50, -1920, 0, 3840, 1080));
        }

        [TestMethod]
        public void 画面の右外や下外の位置は復元しない()
        {
            Assert.IsFalse(Logic.IsOnScreen(1920, 100, 0, 0, 1920, 1080));
            Assert.IsFalse(Logic.IsOnScreen(100, 1080, 0, 0, 1920, 1080));
        }

        [TestMethod]
        public void モニタを外して左外になった位置は復元しない()
        {
            Assert.IsFalse(Logic.IsOnScreen(-1800, 50, 0, 0, 1920, 1080));
        }

        [TestMethod]
        public void NaNの位置は復元しない()
        {
            Assert.IsFalse(Logic.IsOnScreen(double.NaN, 100, 0, 0, 1920, 1080));
        }
    }
}
