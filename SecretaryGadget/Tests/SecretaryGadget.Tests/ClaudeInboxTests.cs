using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>Claude Code のフックが claude-inbox に置いた知らせの読み取りと、吹き出しの文面。</summary>
    [TestClass]
    public class ClaudeInboxTests
    {
        private const string _id = "11111111-2222-3333-4444-555555555555";

        private static string Stop(string message = "できました") =>
            $"{{\"session_id\":\"{_id}\",\"transcript_path\":\"x\",\"cwd\":\"D:\\\\GitHub\\\\Sample\",\"hook_event_name\":\"Stop\",\"stop_hook_active\":false,\"last_assistant_message\":{System.Text.Json.JsonSerializer.Serialize(message)}}}";

        private static string Notification(string type, string message) =>
            $"{{\"session_id\":\"{_id}\",\"cwd\":\"D:\\\\GitHub\",\"hook_event_name\":\"Notification\",\"message\":\"{message}\",\"notification_type\":\"{type}\"}}";

        [TestMethod]
        public void Parse_StopAndNotification()
        {
            ClaudeHookEvent? stop = ClaudeInbox.Parse(Stop());
            Assert.IsNotNull(stop);
            Assert.AreEqual("Stop", stop!.Event);
            Assert.AreEqual(_id, stop.SessionId);
            Assert.AreEqual(@"D:\GitHub\Sample", stop.Cwd);
            Assert.AreEqual("できました", stop.Message);

            ClaudeHookEvent? n = ClaudeInbox.Parse(Notification("permission_prompt", "Claude needs your permission to use Bash"));
            Assert.AreEqual("permission_prompt", n!.NotificationType);
            Assert.AreEqual("Claude needs your permission to use Bash", n.Message);

            // 知らない種類・会話 ID が変・壊れた JSON は読まない(ID はファイル名に使うので GUID だけ)
            Assert.IsNull(ClaudeInbox.Parse(Stop().Replace("\"Stop\"", "\"PreToolUse\"")));
            Assert.IsNull(ClaudeInbox.Parse(Stop().Replace(_id, @"..\..\x")));
            Assert.IsNull(ClaudeInbox.Parse("{壊れた"));
            Assert.IsNull(ClaudeInbox.Parse("[]"));
        }

        [TestMethod]
        public void Drain_ReadsAndDeletes_SkipsWritingAndDropsOld()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sg-inbox-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                DateTime now = DateTime.Now;
                File.WriteAllText(Path.Combine(dir, "2.json"), Stop("2つめ"));
                File.WriteAllText(Path.Combine(dir, "1.json"), Stop("1つめ"));
                File.SetLastWriteTime(Path.Combine(dir, "1.json"), now.AddSeconds(-5));
                File.SetLastWriteTime(Path.Combine(dir, "2.json"), now.AddSeconds(-1));
                File.WriteAllText(Path.Combine(dir, "old.json"), Stop("古い"));
                File.SetLastWriteTime(Path.Combine(dir, "old.json"), now.AddMinutes(-11));
                File.WriteAllText(Path.Combine(dir, "writing.tmp"), "{書いている途中");
                File.WriteAllText(Path.Combine(dir, "dead.tmp"), "{途中で死んだ");
                File.SetLastWriteTime(Path.Combine(dir, "dead.tmp"), now.AddMinutes(-11));
                File.WriteAllText(Path.Combine(dir, "bad.json"), "{壊れた");

                var events = ClaudeInbox.Drain(dir, now);
                CollectionAssert.AreEqual(new[] { "1つめ", "2つめ" }, events.Select(e => e.Message).ToArray());
                // 書いている途中の .tmp だけ残る
                CollectionAssert.AreEqual(new[] { "writing.tmp" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
                Assert.AreEqual(0, ClaudeInbox.Drain(Path.Combine(dir, "無い"), now).Count);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ClaudeNotice_Texts()
        {
            Notice stop = NoticeTexts.Claude(ClaudeInbox.Parse(Stop("一行目\n\n  二行目"))!, "箱庭の改修")!;
            Assert.AreEqual(NoticeKind.Claude, stop.Kind);
            Assert.AreEqual("Claude Code の返事が届きました", stop.Headline);
            Assert.AreEqual("会話：箱庭の改修", stop.Line1);
            Assert.AreEqual("一行目 二行目", stop.Line2);
            Assert.AreEqual("claude:" + _id, stop.Key);
            Assert.IsFalse(stop.IsCalled);

            Assert.AreEqual("会話：(無題)", NoticeTexts.Claude(ClaudeInbox.Parse(Stop(""))!, " ")!.Line1);
            Assert.IsNull(NoticeTexts.Claude(ClaudeInbox.Parse(Stop(""))!, "t")!.Line2);

            Notice ask = NoticeTexts.Claude(ClaudeInbox.Parse(Notification("permission_prompt", "Claude needs your permission to use Bash"))!, "t")!;
            Assert.AreEqual("Claude Code が確認を待っています", ask.Headline);
            Assert.AreEqual("Bash の許可を待っています", ask.Line2);
            Assert.AreEqual("質問があります", NoticeTexts.Claude(ClaudeInbox.Parse(Notification("elicitation_dialog", "質問があります"))!, "t")!.Line2);

            // 1分放っておいたときの idle_prompt は Stop と同じことなので出さない。ログイン成功なども出さない
            Assert.IsNull(NoticeTexts.Claude(ClaudeInbox.Parse(Notification("idle_prompt", "Claude is waiting for your input"))!, "t"));
            Assert.IsNull(NoticeTexts.Claude(ClaudeInbox.Parse(Notification("auth_success", "ok"))!, "t"));
        }

        [TestMethod]
        public void Snippet_CutsLongTextByCharacters()
        {
            Assert.IsNull(NoticeTexts.Snippet(" \n "));
            string longText = string.Concat(Enumerable.Repeat("あ🎉", 50));
            string cut = NoticeTexts.Snippet(longText)!;
            Assert.IsTrue(cut.EndsWith("…"));
            Assert.AreEqual(NoticeTexts.ClaudeSnippetLength, cut.TrimEnd('…').EnumerateRunes().Count());
        }

        [TestMethod]
        public void Pile_CountsClaude()
        {
            Assert.AreEqual("メール 1 件・Teams 0 件・Claude Code 3 件のお知らせがあります。", NoticeTexts.Pile(1, 0, 0, 3).Line1);
            Assert.AreEqual("受信トレイ・Teams・Claude Code をご確認ください。", NoticeTexts.Pile(1, 0, 0, 3).Line2);
        }

        [TestMethod]
        public void TitleFor_FallsBackToFolderName()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sg-title-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "projects", "D--GitHub"));
                File.WriteAllText(Path.Combine(dir, "projects", "D--GitHub", _id + ".jsonl"), "{\"type\":\"ai-title\",\"aiTitle\":\"箱庭の改修\"}\n");
                Assert.AreEqual("箱庭の改修", ClaudeSessions.TitleFor(_id, @"D:\GitHub", dir));
                Assert.AreEqual("Sample", ClaudeSessions.TitleFor("99999999-2222-3333-4444-555555555555", @"D:\GitHub\Sample\", dir));
                Assert.AreEqual(string.Empty, ClaudeSessions.TitleFor("99999999-2222-3333-4444-555555555555", string.Empty, dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
