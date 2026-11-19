using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEdit.Tests
{
    /// <summary>
    /// NameConverter(漢数字→算用数字、数字の0埋め)のテスト。ファイルを触らない純粋な文字列変換。
    /// WinForms版(CS_Form/FFEdit)とWPF版(Essentials/FFEdit)で同じ内容にしてある。
    /// </summary>
    [TestClass]
    public class NameConverterTests
    {
        [DataTestMethod]
        [DataRow("第十二話.mp4", "第12話.mp4")]
        [DataRow("第十話", "第10話")]
        [DataRow("第二十話", "第20話")]
        [DataRow("第一話", "第1話")]
        [DataRow("二〇二四年", "2024年")]
        [DataRow("〇〇七", "007")]
        [DataRow("百二十三", "123")]
        [DataRow("千九百八十四年", "1984年")]
        [DataRow("二千二十四", "2024")]
        [DataRow("三万五千", "35000")]
        [DataRow("一億二千万", "120000000")]
        [DataRow("二〇万", "200000")]
        [DataRow("十一", "11")]
        [DataRow("零", "0")]
        [DataRow("第三章_第十五節", "第3章_第15節")]
        [DataRow("abc", "abc")]
        [DataRow("", "")]
        public void ConvertKanjiNumeralsは漢数字を算用数字に置き換える(string input, string expected)
        {
            Assert.AreEqual(expected, NameConverter.ConvertKanjiNumerals(input));
        }

        [DataTestMethod]
        [DataRow("千葉旅行", "千葉旅行")]
        [DataRow("百合の花", "百合の花")]
        [DataRow("万歳", "万歳")]
        public void ConvertKanjiNumeralsは単独の百千万などは熟語とみなして残す(string input, string expected)
        {
            Assert.AreEqual(expected, NameConverter.ConvertKanjiNumerals(input));
        }

        [TestMethod]
        public void ConvertKanjiNumeralsはnullをそのまま返す()
        {
            Assert.IsNull(NameConverter.ConvertKanjiNumerals(null));
        }

        [TestMethod]
        public void ConvertKanjiNumeralsは桁あふれする並びを元のまま残す()
        {
            // 99999999兆はlongの上限(約922京)を超えるので変換しない
            string input = "九九九九九九九九兆";
            Assert.AreEqual(input, NameConverter.ConvertKanjiNumerals(input));
        }

        [DataTestMethod]
        [DataRow("AAA1BBB", 1, 2, "AAA01BBB")]
        [DataRow("AAA1BBB2", 1, 3, "AAA001BBB2")]
        [DataRow("AAA1BBB2", 2, 3, "AAA1BBB002")]
        [DataRow("AAA1BBB2", -1, 2, "AAA1BBB02")]
        [DataRow("AAA1BBB2", -2, 2, "AAA01BBB2")]
        [DataRow("A1B2C3", 0, 2, "A01B02C03")]
        [DataRow("AAA123BBB", 1, 2, "AAA123BBB")]
        [DataRow("AAA12BBB", 1, 5, "AAA00012BBB")]
        [DataRow("AAA1BBB", 2, 2, "AAA1BBB")]
        [DataRow("AAA1BBB", -2, 2, "AAA1BBB")]
        [DataRow("ABC", 1, 2, "ABC")]
        [DataRow("AAA1BBB", 1, 0, "AAA1BBB")]
        [DataRow("", 1, 2, "")]
        public void PadNumberは指定した番目の数字を0埋めする(string input, int position, int digits, string expected)
        {
            Assert.AreEqual(expected, NameConverter.PadNumber(input, position, digits));
        }

        [TestMethod]
        public void PadNumberは全角数字を対象にしない()
        {
            Assert.AreEqual("第１話", NameConverter.PadNumber("第１話", 1, 3));
        }
    }
}
