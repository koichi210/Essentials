using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Launcher.Models;
using Launcher.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Launcher.Tests
{
    /// <summary>画面の「魔法陣 → セクション(タグ) → 項目」が MagicLauncher と同じ並びになるか</summary>
    [TestClass]
    public class ViewBuilderTests
    {
        private static List<CircleModel> BuildMessy()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "messy.json"));
            return ViewBuilder.Build((JsonObject)JsonNode.Parse(json), key => true);
        }

        [TestMethod]
        public void Circles_FirstCircleThenCirclesOrder()
        {
            List<CircleModel> circles = BuildMessy();

            CollectionAssert.AreEqual(new[] { "Main", "Games" }, circles.Select(c => c.Name).ToArray());
            Assert.IsTrue(circles[0].IsFirst);
        }

        [TestMethod]
        public void Sections_FollowTagOrder_BlankCircleGoesToFirst_UntaggedLast()
        {
            List<CircleModel> circles = BuildMessy();

            // 数字だけのタグ名「1」は JavaScript と同じく先頭。circle が空欄・無しのタグは最初の魔法陣
            CollectionAssert.AreEqual(new[] { "1", "ツール", "Web", "色なし", "タグなし" }, circles[0].Sections.Select(s => s.Name).ToArray());
            // 項目の無いタグ(未使用)も、項目を足せるようにセクションとして出す
            CollectionAssert.AreEqual(new[] { "ゲーム", "未使用" }, circles[1].Sections.Select(s => s.Name).ToArray());
            Assert.IsTrue(circles[0].Sections.Last().IsUntagged);
            Assert.AreEqual(ViewBuilder.UntaggedKey, circles[0].Sections.Last().Key);
        }

        [TestMethod]
        public void Items_GoUnderTheirFirstTag_InvalidItemsAreSkipped()
        {
            List<CircleModel> circles = BuildMessy();
            Dictionary<string, SectionModel> sections = circles.SelectMany(c => c.Sections).ToDictionary(s => s.Name);

            CollectionAssert.AreEqual(new[] { "メモ帳" }, sections["ツール"].Items.Select(i => i.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "example.com" }, sections["Web"].Items.Select(i => i.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "Foo" }, sections["色なし"].Items.Select(i => i.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "数値" }, sections["1"].Items.Select(i => i.Title).ToArray());
            CollectionAssert.AreEqual(new[] { "タグなし" }, sections["タグなし"].Items.Select(i => i.Title).ToArray());
            Assert.AreEqual(0, sections["未使用"].Items.Count);
            Assert.AreEqual(6, sections.Values.Sum(s => s.Items.Count), "パスの無い項目・文字列の項目は出さない");
        }

        [TestMethod]
        public void Items_PathQuotesAreStripped_AndEmojiFallsBackForUrls()
        {
            Dictionary<string, SectionModel> sections = BuildMessy().SelectMany(c => c.Sections).ToDictionary(s => s.Name);

            Assert.AreEqual("C:\\Program Files\\Foo\\", sections["色なし"].Items[0].Path);
            Assert.AreEqual("🌐🎮", sections["Web"].Items[0].Emoji);
            Assert.AreEqual("🔗 重複id", sections["ゲーム"].Items[0].DisplayText);
            Assert.AreEqual("📝 メモ帳", sections["ツール"].Items[0].DisplayText);
        }

        [TestMethod]
        public void Expanded_ComesFromTheCallback()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "messy.json"));

            List<CircleModel> circles = ViewBuilder.Build((JsonObject)JsonNode.Parse(json), key => key != "ツール");

            Assert.IsFalse(circles[0].Sections.First(s => s.Name == "ツール").IsExpanded);
            Assert.IsTrue(circles[0].Sections.First(s => s.Name == "Web").IsExpanded);
        }

        [DataTestMethod]
        [DataRow("https://example.com/a?b=1", "example.com")]
        [DataRow("steam://rungameid/123", "rungameid")]
        [DataRow("C:\\Users\\me\\Work\\", "Work")]
        [DataRow("\"D:\\a\\b.txt\"", "b.txt")]
        [DataRow("onenote:", "onenote:")]
        public void DerivedName_MatchesMagicLauncher(string path, string expected)
        {
            Assert.AreEqual(expected, ViewBuilder.DerivedName(path));
        }
    }
}
