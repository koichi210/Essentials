using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// TrimFileDataLogic（「ファイル抽出」タブ。旧TrimFileDataのLogicTestsを移植したもの。WinForms版のForm1.cs から切り出した、検索ワードリストとリファレンスデータから
    /// 該当行を抽出するロジック）のテスト。
    /// </summary>
    [TestClass]
    public class TrimFileDataLogicTests
    {
        [TestMethod]
        public void 検索ワードにヒットした行を抽出する()
        {
            string[] source = { "apple" };
            string[] refer = { "I like apple pie", "I like banana" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");

            StringAssert.Contains(result, "◆apple");
            StringAssert.Contains(result, "I like apple pie");
            Assert.IsFalse(result.Contains("banana"));
        }

        [TestMethod]
        public void SplitLinesは改行コードを問わず行に分け空行は捨てる()
        {
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, TrimFileDataLogic.SplitLines("a\r\nb\nc\r\r\nd\n"));
        }

        [TestMethod]
        public void ordinalCaseがtrueなら大文字小文字を区別する()
        {
            string[] source = { "APPLE" };
            string[] refer = { "apple pie" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: true, firstWordOnly: false, searchCommonWord: "");

            Assert.IsFalse(result.Contains("apple pie"));
        }

        [TestMethod]
        public void ordinalCaseがfalseなら大文字小文字を区別しない()
        {
            string[] source = { "APPLE" };
            string[] refer = { "apple pie" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");

            StringAssert.Contains(result, "apple pie");
        }

        [TestMethod]
        public void firstWordOnlyがtrueなら最初の一致だけ抽出する()
        {
            string[] source = { "apple" };
            string[] refer = { "apple one", "apple two", "apple three" };

            string result = TrimFileDataLogic.GetHitLines(new[] { "apple" }, refer, StringComparison.OrdinalIgnoreCase, firstWordOnly: true, searchCommonWord: "");

            StringAssert.Contains(result, "apple one");
            Assert.IsFalse(result.Contains("apple two"));
            Assert.IsFalse(result.Contains("apple three"));
        }

        [TestMethod]
        public void firstWordOnlyがfalseなら全ての一致を抽出する()
        {
            string[] refer = { "apple one", "apple two" };

            string result = TrimFileDataLogic.GetHitLines(new[] { "apple" }, refer, StringComparison.OrdinalIgnoreCase, firstWordOnly: false, searchCommonWord: "");

            StringAssert.Contains(result, "apple one");
            StringAssert.Contains(result, "apple two");
        }

        [TestMethod]
        public void searchCommonWordを含まない行は除外される()
        {
            string[] refer = { "apple + common", "apple only" };

            string result = TrimFileDataLogic.GetHitLines(new[] { "apple" }, refer, StringComparison.OrdinalIgnoreCase, firstWordOnly: false, searchCommonWord: "common");

            StringAssert.Contains(result, "apple + common");
            Assert.IsFalse(result.Contains("apple only"));
        }

        [TestMethod]
        public void searchCommonWordが空なら絞り込みしない()
        {
            string[] refer = { "apple + common", "apple only" };

            string result = TrimFileDataLogic.GetHitLines(new[] { "apple" }, refer, StringComparison.OrdinalIgnoreCase, firstWordOnly: false, searchCommonWord: "");

            StringAssert.Contains(result, "apple + common");
            StringAssert.Contains(result, "apple only");
        }

        // 検索ワードは1行に1つ(以前は行内のスペース区切りの各単語が別々の検索ワードだった)
        [TestMethod]
        public void 複数の検索ワードをまとめて処理できる()
        {
            string[] source = { "apple", "banana" };
            string[] refer = { "I have apple", "I have banana", "I have orange" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");

            StringAssert.Contains(result, "I have apple");
            StringAssert.Contains(result, "I have banana");
            Assert.IsFalse(result.Contains("orange"));
        }

        [TestMethod]
        public void 検索ワードは行の先頭から後ろから見て最初のスペースの手前までになる()
        {
            string[] source = { "apple pie 3" };
            string[] refer = { "I like apple pie", "I like apple", "pie only", "3 apples" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");

            // 見出しには元の行をそのまま出し、"apple pie"を含む行だけがヒットする(以前のように"apple"/"pie"/"3"で別々に探さない)
            StringAssert.Contains(result, "◆apple pie 3" + Environment.NewLine + "I like apple pie" + Environment.NewLine);
            Assert.IsFalse(result.Contains("I like apple" + Environment.NewLine));
            Assert.IsFalse(result.Contains("pie only"));
            Assert.IsFalse(result.Contains("3 apples"));
        }

        [TestMethod]
        public void ExtractSearchWordはスペースが無ければ行全体を返す()
        {
            Assert.AreEqual("apple", TrimFileDataLogic.ExtractSearchWord("apple"));
            Assert.AreEqual("全角　スペースは区切らない", TrimFileDataLogic.ExtractSearchWord("全角　スペースは区切らない"));
        }

        [TestMethod]
        public void ExtractSearchWordは最後のスペースの手前までを返す()
        {
            Assert.AreEqual("apple pie", TrimFileDataLogic.ExtractSearchWord("apple pie 3"));
            Assert.AreEqual("apple", TrimFileDataLogic.ExtractSearchWord("apple pie"));
            Assert.AreEqual("apple", TrimFileDataLogic.ExtractSearchWord("apple "), "行末のスペースは区切りとして落ちる");
            Assert.AreEqual("apple pie", TrimFileDataLogic.ExtractSearchWord("apple pie   3"), "区切りの手前に続く余分なスペースも落とす");
            Assert.AreEqual(" apple", TrimFileDataLogic.ExtractSearchWord(" apple pie"), "先頭は行の一番先頭から(先頭のスペースも含む)");
        }

        [TestMethod]
        public void ExtractSearchWordは取り出せなければ空文字を返し何も探さない()
        {
            Assert.AreEqual("", TrimFileDataLogic.ExtractSearchWord(" apple"));
            Assert.AreEqual("", TrimFileDataLogic.ExtractSearchWord("   "));
            Assert.AreEqual("", TrimFileDataLogic.ExtractSearchWord(""));
            Assert.AreEqual("", TrimFileDataLogic.ExtractSearchWord(null));

            // 空の検索ワードで全行がヒットしないこと
            string result = TrimFileDataLogic.GetSearchData(new[] { " apple" }, new[] { "apple pie", "banana" }, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");
            Assert.IsFalse(result.Contains("apple pie"));
            Assert.IsFalse(result.Contains("banana"));
        }

        [TestMethod]
        public void ToNarrowは全角英数字と記号とスペースを半角にする()
        {
            Assert.AreEqual("ABC xyz 0129!(#)~", TrimFileDataLogic.ToNarrow("ＡＢＣ　ｘｙｚ　０１２９！（＃）～"));
            Assert.AreEqual("あいうアイウ漢字", TrimFileDataLogic.ToNarrow("あいうアイウ漢字"), "かな・漢字は変えない");
            Assert.AreEqual("abc", TrimFileDataLogic.ToNarrow("abc"));
            Assert.AreEqual("", TrimFileDataLogic.ToNarrow(""));
            Assert.IsNull(TrimFileDataLogic.ToNarrow(null));
        }

        [TestMethod]
        public void 全角半角を区別しないなら検索ワードと検索対象の両方を半角にそろえて比べ結果は元の行を出す()
        {
            string[] source = { "ａｐｐｌｅ１", "pie" };
            string[] refer = { "I like apple1", "ＩＮ ＴＨＥ ａｐｐｌｅ１", "ｐｉｅ　ｃｈａｒｔ" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "", ignoreWidth: true);

            StringAssert.Contains(result, "◆ａｐｐｌｅ１" + Environment.NewLine + "I like apple1" + Environment.NewLine + "ＩＮ ＴＨＥ ａｐｐｌｅ１" + Environment.NewLine);
            StringAssert.Contains(result, "◆pie" + Environment.NewLine + "ｐｉｅ　ｃｈａｒｔ" + Environment.NewLine);
        }

        [TestMethod]
        public void 全角半角を区別しないがOFFなら全角と半角は別の文字として扱う()
        {
            string[] source = { "ａｐｐｌｅ" };
            string[] refer = { "I like apple", "ａｐｐｌｅ pie" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "");

            Assert.IsFalse(result.Contains("I like apple"));
            StringAssert.Contains(result, "ａｐｐｌｅ pie");
        }

        [TestMethod]
        public void 全角半角を区別しないなら共通ワードも半角にそろえ全角スペースも区切りになる()
        {
            // "ａｐｐｌｅ　メモ"は全角スペースが半角になってから区切られ、検索ワードは"apple"になる
            string[] source = { "ａｐｐｌｅ　メモ" };
            string[] refer = { "apple ERROR", "apple ok", "Ａｐｐｌｅ ＥＲＲＯＲ" };

            string result = TrimFileDataLogic.GetSearchData(source, refer, ordinalCase: false, firstWordOnly: false, searchCommonWord: "ｅｒｒｏｒ", ignoreWidth: true);

            StringAssert.Contains(result, "apple ERROR");
            StringAssert.Contains(result, "Ａｐｐｌｅ ＥＲＲＯＲ");
            Assert.IsFalse(result.Contains("apple ok"));
        }
    }
}
