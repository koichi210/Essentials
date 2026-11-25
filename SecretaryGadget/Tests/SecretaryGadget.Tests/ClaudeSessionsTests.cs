using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>Claude Code の会話の状態(実行中／処理完了)と、呼んだときの吹き出しの一覧。</summary>
    [TestClass]
    public class ClaudeSessionsTests
    {
        private static readonly DateTime _now = new DateTime(2026, 11, 25, 22, 0, 0);

        private static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

        private static string Live(string id, int pid, string status, DateTime since, string cwd = @"D:\GitHub\Sample", string name = "sample-01")
        {
            return $"{{\"pid\":{pid},\"sessionId\":\"{id}\",\"status\":\"{status}\",\"statusUpdatedAt\":{Ms(since)},\"cwd\":{System.Text.Json.JsonSerializer.Serialize(cwd)},\"name\":\"{name}\"}}";
        }

        [TestMethod]
        public void ParseLive_StatusRules()
        {
            Func<int, bool> alive = pid => pid != 99;
            ClaudeSession? s = ClaudeSessions.ParseLive(Live("a", 1, "idle", _now.AddMinutes(-5)), _now, alive);
            Assert.IsNotNull(s);
            Assert.AreEqual("a", s!.SessionId);
            Assert.AreEqual("Sample", s.Project);
            Assert.AreEqual("sample-01", s.Title);
            Assert.IsFalse(s.Busy);
            // busy は実行中
            Assert.IsTrue(ClaudeSessions.ParseLive(Live("b", 1, "busy", _now.AddMinutes(-5)), _now, alive)!.Busy);
            // 終わったプロセスは出さない
            Assert.IsNull(ClaudeSessions.ParseLive(Live("c", 99, "idle", _now.AddMinutes(-5)), _now, alive));
            // 長く放っておいた idle も、承認待ちなど busy 以外も処理完了
            Assert.IsFalse(ClaudeSessions.ParseLive(Live("d", 1, "idle", _now.AddHours(-3)), _now, alive)!.Busy);
            Assert.IsFalse(ClaudeSessions.ParseLive(Live("e", 1, "waiting", _now.AddHours(-2)), _now, alive)!.Busy);
        }

        [TestMethod]
        public void TitleFromTail_PrefersCustomThenLastAiTitle()
        {
            string ai = "{\"type\":\"ai-title\",\"aiTitle\":\"古い題名\"}\n{\"type\":\"user\",\"message\":{\"content\":\"ai-title という文字\"}}\n{\"type\":\"ai-title\",\"aiTitle\":\"新しい題名\"}\n";
            Assert.AreEqual("新しい題名", ClaudeSessions.TitleFromTail("途中から始まる壊れた行\"}\n" + ai));
            Assert.AreEqual("自分で付けた題名", ClaudeSessions.TitleFromTail("{\"type\":\"custom-title\",\"customTitle\":\"自分で付けた題名\"}\n" + ai));
            Assert.IsNull(ClaudeSessions.TitleFromTail("{\"type\":\"user\"}\n"));
        }

        [TestMethod]
        public void ReadSessions_ReadsSessionsAndTitles_DoneFirstThenRunning()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sg-claude-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "sessions"));
                Directory.CreateDirectory(Path.Combine(dir, "projects", "D--GitHub"));
                File.WriteAllText(Path.Combine(dir, "sessions", "100.json"), Live("s1", 100, "idle", _now.AddMinutes(-2)));
                File.WriteAllText(Path.Combine(dir, "sessions", "200.json"), Live("s2", 200, "idle", _now.AddMinutes(-20)));
                File.WriteAllText(Path.Combine(dir, "sessions", "300.json"), Live("s3", 300, "busy", _now.AddMinutes(-1)));
                File.WriteAllText(Path.Combine(dir, "sessions", "400.json"), "{書き込み途中");
                File.WriteAllText(Path.Combine(dir, "sessions", "note.json"), Live("x", 1, "idle", _now));
                File.WriteAllText(Path.Combine(dir, "projects", "D--GitHub", "s2.jsonl"), "{\"type\":\"ai-title\",\"aiTitle\":\"箱庭の改修\"}\n");
                var list = ClaudeSessions.ReadSessions(dir, _now, _ => true);
                CollectionAssert.AreEqual(new[] { "s2", "s1", "s3" }, list.Select(s => s.SessionId).ToArray());
                Assert.IsTrue(list[2].Busy);
                Assert.AreEqual("箱庭の改修", list[0].Title);
                Assert.AreEqual("sample-01", list[1].Title);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void SessionStatusMenu_SeparatorHeaderAndRows()
        {
            var none = NoticeTexts.SessionStatusMenu(Array.Empty<ClaudeSession>());
            CollectionAssert.AreEqual(new[] { BubbleMenuKind.Separator, BubbleMenuKind.Header, BubbleMenuKind.Info }, none.Select(r => r.Kind).ToArray());
            Assert.AreEqual("セッションの状態", none[1].Label);
            Assert.AreEqual("・いまはありません", none[2].Label);
            Assert.IsTrue(none.All(r => r.Run == null));

            var many = Enumerable.Range(1, 10).Select(i => new ClaudeSession($"id{i}", i == 2 ? " " : $"会話{i}", "P", _now, i == 1)).ToList();
            var rows = NoticeTexts.SessionStatusMenu(many);
            Assert.AreEqual(2 + NoticeTexts.SessionStatusMax + 1, rows.Count);
            Assert.AreEqual("・【実行中】会話1", rows[2].Label);
            Assert.AreEqual("・【処理完了】(無題)", rows[3].Label);
            // 赤くするのは【実行中】だけ
            Assert.AreEqual("【実行中】", rows[2].Alert);
            Assert.IsNull(rows[3].Alert);
            Assert.AreEqual("ほか 2 件", rows[^1].Label);
        }
    }
}
