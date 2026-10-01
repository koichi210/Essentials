using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEdit.Tests
{
    /// <summary>
    /// TimeText（WinForms版のDateTimePicker Format=Time / ShowUpDown の代わりの時刻入力処理）のテスト。
    /// </summary>
    [TestClass]
    public class TimeTextTests
    {
        [TestMethod]
        public void Format_H_mm_ss形式になる()
        {
            Assert.AreEqual("9:05:03", TimeText.Format(new TimeSpan(9, 5, 3)));
            Assert.AreEqual("23:59:59", TimeText.Format(new TimeSpan(23, 59, 59)));
        }

        [TestMethod]
        public void TryParse_時分秒と時分を読める()
        {
            TimeSpan time;
            Assert.IsTrue(TimeText.TryParse("12:34:56", out time));
            Assert.AreEqual(new TimeSpan(12, 34, 56), time);

            Assert.IsTrue(TimeText.TryParse(" 7:08 ", out time));
            Assert.AreEqual(new TimeSpan(7, 8, 0), time);
        }

        [TestMethod]
        public void TryParse_不正な値はfalse()
        {
            TimeSpan time;
            Assert.IsFalse(TimeText.TryParse("25:00:00", out time));
            Assert.IsFalse(TimeText.TryParse("abc", out time));
            Assert.IsFalse(TimeText.TryParse("", out time));
            Assert.IsFalse(TimeText.TryParse(null, out time));
        }

        [TestMethod]
        public void GetFieldIndex_カーソル位置で時分秒を判定する()
        {
            Assert.AreEqual(0, TimeText.GetFieldIndex("12:34:56", 0));
            Assert.AreEqual(0, TimeText.GetFieldIndex("12:34:56", 2));
            Assert.AreEqual(1, TimeText.GetFieldIndex("12:34:56", 3));
            Assert.AreEqual(2, TimeText.GetFieldIndex("12:34:56", 8));
        }

        [TestMethod]
        public void Increment_その欄の中で一周し桁上がりしない()
        {
            Assert.AreEqual(new TimeSpan(0, 59, 59), TimeText.Increment(new TimeSpan(23, 59, 59), 0, 1));
            Assert.AreEqual(new TimeSpan(23, 0, 59), TimeText.Increment(new TimeSpan(23, 59, 59), 1, 1));
            Assert.AreEqual(new TimeSpan(23, 59, 0), TimeText.Increment(new TimeSpan(23, 59, 59), 2, 1));
            Assert.AreEqual(new TimeSpan(10, 0, 59), TimeText.Increment(new TimeSpan(10, 0, 0), 2, -1));
        }
    }
}
