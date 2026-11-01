using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>見張り役のうち、Outlook・Teams 本体が無くても確かめられる部分。</summary>
    [TestClass]
    public class WatcherLogicTests
    {
        private static readonly string[] _keywords = { "山田", "やまだ", "Yamada" };

        [TestMethod]
        public void MentionFilter_IgnoresAddresses()
        {
            // 自分のアドレスが見出しに出ても、アドレスを除いてから判定する
            Assert.IsFalse(MentionFilter.IsMention(new[] { "yamada.t@example.com からの通知" }, _keywords));
            Assert.IsTrue(MentionFilter.IsMention(new[] { "@YAMADA さん 確認お願いします" }, _keywords));
            Assert.IsTrue(MentionFilter.IsMention(new[] { "関係ない", "やまださんへ" }, _keywords));
            Assert.IsFalse(MentionFilter.IsMention(new[] { "関係ない" }, _keywords));
        }

        [TestMethod]
        public void MentionFilter_BlankKeywordsNeverMatch()
        {
            Assert.IsFalse(MentionFilter.IsMention(new[] { "なんでも" }, new[] { "", " " }));
        }

        [TestMethod]
        public void RemoveConcatenated_DropsLinesContainingOtherLines()
        {
            var lines = new List<string> { "佐藤 太郎", "確認お願いします", "佐藤 太郎 確認お願いします", "別件" };
            CollectionAssert.AreEqual(new[] { "佐藤 太郎", "確認お願いします", "別件" }, PopupText.RemoveConcatenated(lines));
        }

        [TestMethod]
        public void SplitIntoItems_CardsTakeContainedLines()
        {
            var cards = new List<(string Id, string Name)>
            {
                ("card-title-1", "佐藤 太郎 確認お願いします"),
                ("card-title-2", "鈴木 次郎 了解です"),
            };
            var lines = new List<string> { "佐藤 太郎", "確認お願いします", "鈴木 次郎" };
            var items = PopupText.SplitIntoItems(cards, lines);
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("card:card-title-1\n佐藤 太郎 確認お願いします", items[0].Key);
            CollectionAssert.AreEqual(new[] { "佐藤 太郎", "確認お願いします" }, items[0].Lines);
            CollectionAssert.AreEqual(new[] { "鈴木 次郎" }, items[1].Lines);
        }

        [TestMethod]
        public void SplitIntoItems_CardWithoutLinesUsesCardName_AndNoCardsIsOneItem()
        {
            var one = PopupText.SplitIntoItems(new List<(string Id, string Name)> { ("card-title-9", "名前だけ") }, new List<string>());
            CollectionAssert.AreEqual(new[] { "名前だけ" }, one[0].Lines);

            var whole = PopupText.SplitIntoItems(new List<(string Id, string Name)>(), new List<string> { "a", "b" });
            Assert.AreEqual(1, whole.Count);
            CollectionAssert.AreEqual(new[] { "a", "b" }, whole[0].Lines);
            Assert.AreEqual(0, PopupText.SplitIntoItems(new List<(string Id, string Name)>(), new List<string>()).Count);
        }

        [DataTestMethod]
        [DataRow("メンション", 0)]
        [DataRow("メンション 3 未読", 3)]
        [DataRow("メンション 12未読", 12)]
        public void ParseMentionItemName_Matches(string name, int expected)
        {
            Assert.AreEqual(expected, TeamsMentionCounter.ParseMentionItemName(name));
        }

        [DataTestMethod]
        [DataRow("チャット")]
        [DataRow("メンションと返信")]
        [DataRow(null)]
        public void ParseMentionItemName_Others(string? name)
        {
            Assert.IsNull(TeamsMentionCounter.ParseMentionItemName(name));
        }

        [TestMethod]
        public void TeamsLink_FoundAndConvertedForLForm()
        {
            string body = "参加はこちら <https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=x> です";
            string? link = OutlookWatcher.FindTeamsLinkInText(body);
            Assert.AreEqual("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=x", link);
            Assert.AreEqual("msteams:/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=x", OutlookWatcher.ToTeamsUri(link!));
        }

        [TestMethod]
        public void TeamsLink_MeetFormKeptAsHttps()
        {
            string? link = OutlookWatcher.FindTeamsLinkInText("https://teams.microsoft.com/meet/123456?p=abc");
            Assert.AreEqual("https://teams.microsoft.com/meet/123456?p=abc", OutlookWatcher.ToTeamsUri(link!));
            Assert.IsNull(OutlookWatcher.FindTeamsLinkInText("リンクなし"));
        }
    }

    [TestClass]
    public class LayoutAndOptionsTests
    {
        /// <summary>左半分が透明で、x=pixelWidth/2 から不透明な絵</summary>
        private static int[] HalfOpaque(int width, int height)
        {
            return Enumerable.Repeat(width / 2, height).ToArray();
        }

        [TestMethod]
        public void Layout_FollowsFormula()
        {
            // 絵 1024x1536、等倍 h=420 → 絵幅 280
            var r = BubbleLayout.Compute(420, 1.0, 100, 1024, 1536, HalfOpaque(1024, 1536), null);
            Assert.AreEqual(420, r.WindowHeight, 1e-9);
            Assert.AreEqual(280, r.ImageWidth, 1e-9);
            Assert.AreEqual(0, r.ImageTop, 1e-9);
            double faceY = 0.23 * 420;
            Assert.AreEqual(faceY - 36, r.BubbleTop, 1e-9);
            Assert.AreEqual(faceY, r.TailTip, 1e-9);
            // L(吹き出しの行)=0.5 → r = 0.5 − (14+6)/280。キャラの左端は 306+14+6=326 に来る
            double expected = 0.5 - 20.0 / 280;
            Assert.AreEqual(expected, r.Overlap, 1e-9);
            Assert.AreEqual(306 - 280 * expected + 280, r.WindowWidth, 1e-9);
            Assert.AreEqual(306 - 280 * expected, r.ImageLeft, 1e-9);
            Assert.AreEqual(326, r.ImageLeft + 280 * 0.5, 1e-9);
        }

        [TestMethod]
        public void Layout_TallBubbleRaisesWindowAndClampsBubbleTop()
        {
            var r = BubbleLayout.Compute(420, 0.25, 300, 1024, 1536, HalfOpaque(1024, 1536), 0.34);
            Assert.AreEqual(308, r.WindowHeight, 1e-9);
            Assert.AreEqual(308 - 105, r.ImageTop, 1e-9);
            Assert.AreEqual(4, r.BubbleTop, 1e-9);
            Assert.AreEqual(0.34, r.Overlap, 1e-9);
        }

        [TestMethod]
        public void Layout_TransparentRowsDoNotLimitOverlap()
        {
            int[] leftmost = new int[1536];
            for (int y = 0; y < leftmost.Length; y++)
            {
                // 顔の行(上から23%付近)は x=0.3、それ以外は全透明
                leftmost[y] = y > 300 && y < 420 ? 307 : 1024;
            }
            var r = BubbleLayout.Compute(420, 1.0, 100, 1024, 1536, leftmost, null);
            Assert.AreEqual(307.0 / 1024 - 20.0 / 280, r.Overlap, 1e-9);
        }

        [DataTestMethod]
        [DataRow(1.0, 60.0)]
        [DataRow(1.0, 200.0)]
        [DataRow(0.5, 120.0)]
        [DataRow(0.25, 60.0)]
        [DataRow(0.25, 300.0)]
        public void Layout_BubbleNeverOverlapsIllustration(double scale, double bubbleHeight)
        {
            // 行ごとに左端がばらつく絵(帽子のつばが左へ張り出す・肩が広い等)
            int[] leftmost = new int[1536];
            for (int y = 0; y < leftmost.Length; y++)
            {
                leftmost[y] = y < 100 ? 1024 : 200 + (int)(150 * Math.Abs(Math.Sin(y / 97.0)));
            }
            var r = BubbleLayout.Compute(420, scale, bubbleHeight, 1024, 1536, leftmost, null);
            double tailTipX = BubbleLayout.BubbleLeft + BubbleLayout.BubbleWidth - BubbleLayout.TailOverlap + BubbleLayout.TailWidth;
            double toPixel = 1536 / r.ImageHeight;
            for (double wy = r.BubbleTop; wy <= r.BubbleTop + bubbleHeight + BubbleLayout.ShadowBelow; wy += 0.5)
            {
                int py = (int)((wy - r.ImageTop) * toPixel);
                if (py < 0 || py >= 1536)
                {
                    continue;
                }
                double contentLeft = r.ImageLeft + leftmost[py] / 1024.0 * r.ImageWidth;
                Assert.IsTrue(contentLeft >= tailTipX + BubbleLayout.NoOverlapGap - 1e-6, $"y={wy}: キャラの左端 {contentLeft} がしっぽの先 {tailTipX} に近すぎる");
            }
        }

        [TestMethod]
        public void StartupOptions_ParsesArguments()
        {
            StartupOptions o = StartupOptions.Parse(new[] { "--wait-instance", "--skin", "blue", "--scale", "0.5", "--diag-no-outlook", "--diag-restart-mb", "300", "--render-preview", "a.png", "--preview-call" });
            Assert.IsTrue(o.WaitInstance);
            Assert.IsFalse(o.Exit);
            Assert.AreEqual("blue", o.Skin);
            Assert.AreEqual(0.5, o.Scale);
            Assert.IsTrue(o.HasDiag("no-outlook"));
            Assert.AreEqual(300, o.RestartMb);
            Assert.AreEqual("a.png", o.RenderPreview);
            Assert.IsTrue(o.PreviewCall);
            Assert.IsTrue(StartupOptions.Parse(new[] { "--exit" }).Exit);
        }

        [TestMethod]
        public void Footer_CombinesRemainingAndClickHint()
        {
            Assert.AreEqual("ほか 2 件　クリックで開きます", App.BuildFooter(2, true));
            Assert.AreEqual("ほか 1 件", App.BuildFooter(1, false));
            Assert.AreEqual("クリックで開きます", App.BuildFooter(0, true));
            Assert.IsNull(App.BuildFooter(0, false));
        }
    }
}
