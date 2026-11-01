using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    [TestClass]
    public class TimerTests
    {
        private static readonly DateTime _now = new DateTime(2026, 11, 1, 20, 15, 40);

        [DataTestMethod]
        [DataRow("22:30", 2026, 11, 1, 22, 30, "22:30")]
        [DataRow("22時30分", 2026, 11, 1, 22, 30, "22:30")]
        [DataRow("22時", 2026, 11, 1, 22, 0, "22:00")]
        [DataRow("９:05", 2026, 11, 2, 9, 5, "09:05")]
        [DataRow("20:15", 2026, 11, 2, 20, 15, "20:15")]
        [DataRow(" ２２：３０ ", 2026, 11, 1, 22, 30, "22:30")]
        public void TryParse_Clock(string input, int y, int mo, int d, int h, int mi, string label)
        {
            Assert.IsTrue(TimerInput.TryParse(input, _now, out TimerEntry? e));
            Assert.AreEqual(new DateTime(y, mo, d, h, mi, 0), e!.Due);
            Assert.AreEqual(label, e.Label);
        }

        [DataTestMethod]
        [DataRow("15分後", 20, 30, "15分後")]
        [DataRow("15分", 20, 30, "15分後")]
        [DataRow("1時間後", 21, 15, "1時間後")]
        [DataRow("1時間10分後", 21, 25, "1時間10分後")]
        [DataRow("１時間１０分後", 21, 25, "1時間10分後")]
        [DataRow("90分後", 21, 45, "90分後")]
        public void TryParse_Relative(string input, int h, int mi, string label)
        {
            Assert.IsTrue(TimerInput.TryParse(input, _now, out TimerEntry? e));
            Assert.AreEqual(new DateTime(2026, 11, 1, h, mi, 0), e!.Due);
            Assert.AreEqual(label, e.Label);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("後")]
        [DataRow("abc")]
        [DataRow("25:00")]
        [DataRow("12:60")]
        [DataRow("0分後")]
        [DataRow("25時間後")]
        [DataRow("99999999999分後")]
        public void TryParse_Invalid(string input)
        {
            Assert.IsFalse(TimerInput.TryParse(input, _now, out _));
        }

        [TestMethod]
        public void FromClock_PastTimeGoesToTomorrow_AndRangeChecked()
        {
            Assert.AreEqual(new DateTime(2026, 11, 2, 0, 0, 0), TimerInput.FromClock(0, 0, _now)!.Due);
            Assert.IsNull(TimerInput.FromClock(24, 0, _now));
            Assert.IsNull(TimerInput.FromClock(0, 60, _now));
        }

        [TestMethod]
        public void FromRelative_ZeroOrTooLongIsNull()
        {
            Assert.IsNull(TimerInput.FromRelative(0, 0, _now));
            Assert.IsNull(TimerInput.FromRelative(24, 1, _now));
            Assert.AreEqual(new DateTime(2026, 11, 1, 22, 15, 0), TimerInput.FromRelative(2, 0, _now)!.Due);
        }

        [TestMethod]
        public void Keeper_TakeDue_ReturnsOnlyExpiredAndPersists()
        {
            string path = Path.Combine(Path.GetTempPath(), "secgad-timers-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var keeper = new TimerKeeper(path);
                keeper.Add(new TimerEntry(_now.AddMinutes(10), "10分後"));
                keeper.Add(new TimerEntry(_now.AddMinutes(-1), "1分前"));

                var reloaded = new TimerKeeper(path);
                reloaded.Load();
                Assert.AreEqual(2, reloaded.Items.Count);
                Assert.AreEqual("1分前", reloaded.Items[0].Label);

                var due = keeper.TakeDue(_now);
                Assert.AreEqual(1, due.Count);
                Assert.AreEqual("1分前", due[0].Label);
                Assert.AreEqual(1, keeper.Items.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
