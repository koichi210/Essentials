using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// ToyingDataLogic（「データ加工」タブの全角→半角変換のロジック）のテスト。旧ToyingDataのLogicTestsを移植したもの。WinForms版のLogicTestsをそのまま流用している。
    /// </summary>
    [TestClass]
    public class ToyingDataLogicTests
    {
        [TestMethod]
        public void TryGetRegexPattern_何も選ばれていなければfalse()
        {
            string dummy;
            bool result = ToyingDataLogic.TryGetRegexPattern(false, false, false, false, out dummy);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryGetRegexPattern_数字のみ選ぶとその範囲だけ含む()
        {
            string result;
            ToyingDataLogic.TryGetRegexPattern(true, false, false, false, out result);

            Assert.AreEqual("[０-９]", result);
        }

        [TestMethod]
        public void TryGetRegexPattern_複数選ぶと結合される()
        {
            string result;
            bool success = ToyingDataLogic.TryGetRegexPattern(true, true, false, true, out result);

            Assert.IsTrue(success);
            Assert.AreEqual("[０-９Ａ-Ｚ　]", result);
        }

        [TestMethod]
        public void ApplyWide2Narrow_全角数字を半角にする()
        {
            string regexPattern;
            ToyingDataLogic.TryGetRegexPattern(true, false, false, false, out regexPattern);

            string[] result = ToyingDataLogic.ApplyWide2Narrow(new[] { "１２３", "abc" }, regexPattern);

            Assert.AreEqual("123", result[0]);
            Assert.AreEqual("abc", result[1], "対象外の文字はそのまま");
        }

        [TestMethod]
        public void ApplyWide2Narrow_対象文字が無ければ変化しない()
        {
            string regexPattern;
            ToyingDataLogic.TryGetRegexPattern(true, false, false, false, out regexPattern);

            string[] result = ToyingDataLogic.ApplyWide2Narrow(new[] { "あいう" }, regexPattern);

            Assert.AreEqual("あいう", result[0]);
        }
    }
}
