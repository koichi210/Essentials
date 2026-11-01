using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    [TestClass]
    public class NoticeTextsTests
    {
        private static readonly DateTime _day = new DateTime(2026, 10, 30);

        [DataTestMethod]
        [DataRow(10, 29, "おはようございます")]
        [DataRow(10, 30, "お疲れさまです")]
        [DataRow(17, 59, "お疲れさまです")]
        [DataRow(18, 0, "遅くまでお疲れさまです")]
        [DataRow(23, 59, "遅くまでお疲れさまです")]
        public void Greeting_SwitchesAt1030And1800(int hour, int minute, string expected)
        {
            Assert.AreEqual(expected, NoticeTexts.Greeting(_day.AddHours(hour).AddMinutes(minute)));
        }

        [TestMethod]
        public void Startup_UsesGreetingAndUserName()
        {
            Notice n = NoticeTexts.Startup(_day.AddHours(22), "テストさん");
            Assert.AreEqual("遅くまでお疲れさまです、テストさん", n.Headline);
            Assert.AreEqual("秘書がお知らせを監視しています。", n.Line1);
            Assert.AreEqual("本日もよろしくお願いいたします。", n.Line2);
            Assert.AreEqual(NoticeKind.Greeting, n.Kind);
        }

        [DataTestMethod]
        [DataRow("山田 太郎 (営業部)", "山田 太郎")]
        [DataRow("山田 太郎（営業部）", "山田 太郎")]
        [DataRow("山田 太郎 (営業部) (外部)", "山田 太郎")]
        [DataRow("佐藤さんがあなたをメンションしました", "佐藤")]
        [DataRow("佐藤さんからのメッセージ", "佐藤")]
        [DataRow("鈴木 花子", "鈴木 花子")]
        public void CleanSenderName_RemovesSuffixes(string raw, string expected)
        {
            Assert.AreEqual(expected, NoticeTexts.CleanSenderName(raw));
        }

        [TestMethod]
        public void SummarizeSenders_DedupesAndCountsOthers()
        {
            var names = new[] { "A (x)", "B", "A", "C", "D" };
            // A は2件、B は1件 → 表示した2人で3件、残り 5−3=2 件
            Assert.AreEqual("A・B ほか 2 件", NoticeTexts.SummarizeSenders(names, 5));
        }

        [TestMethod]
        public void SummarizeSenders_NoOthers_OmitsHoka()
        {
            Assert.AreEqual("A", NoticeTexts.SummarizeSenders(new[] { "A", "A" }, 2));
            Assert.AreEqual(string.Empty, NoticeTexts.SummarizeSenders(Array.Empty<string>(), 3));
        }

        [TestMethod]
        public void Call_OutlookAbsent_UsesFixedLineAndNoMeetings()
        {
            DateTime now = _day.AddHours(22).AddMinutes(59);
            Notice n = NoticeTexts.Call(now, "テストさん", null, Array.Empty<string>(), 0, now.AddMinutes(-15), Array.Empty<string>(), null);
            Assert.AreEqual("遅くまでお疲れさまです、テストさん", n.Headline);
            Assert.AreEqual("メール：Outlook が起動していないようです", n.Line1);
            Assert.AreEqual("Teams のメンション未読：0 件（22:44 時点）", n.Line2);
            Assert.IsNull(n.Line3);
        }

        [TestMethod]
        public void Call_MentionFresh_NoTimestamp_AndSendersNewestFirst()
        {
            DateTime now = _day.AddHours(9);
            Notice n = NoticeTexts.Call(now, "X", 3, new[] { "A", "B", "C" }, 1, now.AddMinutes(-1), new[] { "新しい人さんが…", "古い人" }, new List<MeetingInfo>());
            Assert.AreEqual("メールの未読：3 件（A・B ほか 1 件）", n.Line1);
            Assert.AreEqual("Teams のメンション未読：1 件（新しい人）", n.Line2);
            Assert.AreEqual("本日のこのあとの会議：ありません", n.Line3);
        }

        [TestMethod]
        public void Call_MentionNeverCounted()
        {
            Notice n = NoticeTexts.Call(_day.AddHours(9), "X", 0, Array.Empty<string>(), null, null, Array.Empty<string>(), null);
            Assert.AreEqual("メールの未読：0 件", n.Line1);
            Assert.AreEqual("Teams のメンション未読：まだ数えられていません（Teams のチャット画面を一度開いてください）", n.Line2);
        }

        [TestMethod]
        public void Schedule_TruncatesAt8AndMarksState()
        {
            DateTime now = _day.AddHours(13).AddMinutes(10);
            var meetings = new List<MeetingInfo>();
            for (int i = 0; i < 10; i++)
            {
                meetings.Add(new MeetingInfo($"M{i}", _day.AddHours(9 + i), _day.AddHours(9 + i).AddMinutes(30), string.Empty));
            }
            Notice n = NoticeTexts.Schedule(now, meetings);
            Assert.AreEqual(NoticeKind.Schedule, n.Kind);
            Assert.AreEqual("参加予定の会議は 10 件（このあと 6 件）です。", n.Line1);
            string[] rows = n.Line3!.Split(Environment.NewLine);
            Assert.AreEqual(9, rows.Length);
            Assert.AreEqual("09:00　M0（終了）", rows[0]);
            Assert.AreEqual("13:00　M4（開催中）", rows[4]);
            Assert.AreEqual("14:00　M5", rows[5]);
            Assert.AreEqual("ほか 2 件", rows[8]);
        }

        [TestMethod]
        public void Schedule_NoMeetingsOrOutlookAbsent()
        {
            Assert.AreEqual("本日、参加予定の会議はありません。", NoticeTexts.Schedule(_day, new List<MeetingInfo>()).Line1);
            Assert.AreEqual("Outlook が起動していないようです", NoticeTexts.Schedule(_day, null).Line1);
        }

        [TestMethod]
        public void Meeting_BeforeStart_RoundsUpMinutesAndAddsLocation()
        {
            var m = new MeetingInfo("定例", _day.AddHours(10), _day.AddHours(11), "会議室A");
            Notice? n = NoticeTexts.Meeting(_day.AddHours(10).AddMinutes(-4).AddSeconds(-1), m);
            Assert.IsNotNull(n);
            Assert.AreEqual("まもなく会議が始まります", n!.Headline);
            Assert.AreEqual("10:00～ 定例", n.Line1);
            Assert.AreEqual("あと 5 分です　場所：会議室A", n.Line2);
        }

        [TestMethod]
        public void Meeting_Started_AndTeamsLocationOmitted()
        {
            var m = new MeetingInfo("定例", _day.AddHours(10), _day.AddHours(11), "Microsoft Teams 会議");
            Notice? n = NoticeTexts.Meeting(_day.AddHours(10).AddMinutes(1), m);
            Assert.AreEqual("会議が始まっています", n!.Headline);
            Assert.AreEqual("10:00 から開催中です", n.Line2);
        }

        [TestMethod]
        public void Meeting_Ended_ReturnsNull()
        {
            var m = new MeetingInfo("定例", _day.AddHours(10), _day.AddHours(11), string.Empty);
            Assert.IsNull(NoticeTexts.Meeting(_day.AddHours(11), m));
        }

        [TestMethod]
        public void Mail_EmptySubject()
        {
            Notice n = NoticeTexts.Mail("山田", " ");
            Assert.AreEqual("メールが届いています", n.Headline);
            Assert.AreEqual("差出人：山田", n.Line1);
            Assert.AreEqual("件名：（件名なし）", n.Line2);
        }

        [TestMethod]
        public void Teams_FirstLineIsSenderRestJoined()
        {
            Notice n = NoticeTexts.Teams(new[] { "佐藤", "@山田 さん", "確認お願いします" }, true);
            Assert.AreEqual("Teams でお呼びがかかっています", n.Headline);
            Assert.AreEqual("佐藤", n.Line1);
            Assert.AreEqual("@山田 さん 確認お願いします", n.Line2);
            Assert.AreEqual("Teams にメッセージです", NoticeTexts.Teams(new[] { "佐藤" }, false).Headline);
        }

        [TestMethod]
        public void Pile_WithAndWithoutMeetings()
        {
            Assert.AreEqual("メール 2 件・Teams 1 件・会議 1 件のお知らせがあります。", NoticeTexts.Pile(2, 1, 1).Line1);
            Assert.AreEqual("メール 3 件・Teams 1 件が届いています。", NoticeTexts.Pile(3, 1, 0).Line1);
            Assert.AreEqual("受信トレイ・Teams をご確認ください。", NoticeTexts.Pile(3, 1, 0).Line2);
        }

        [TestMethod]
        public void IsCalled_OnlyGreetingAndSchedule()
        {
            Assert.IsTrue(new Notice(NoticeKind.Greeting, "h").IsCalled);
            Assert.IsTrue(new Notice(NoticeKind.Schedule, "h").IsCalled);
            Assert.IsFalse(new Notice(NoticeKind.Mail, "h").IsCalled);
            Assert.IsFalse(new Notice(NoticeKind.Teams, "h").IsCalled);
            Assert.IsFalse(new Notice(NoticeKind.Meeting, "h").IsCalled);
        }

        [TestMethod]
        public void RestrictDate_UsesSlashFormat()
        {
            Assert.AreEqual("2026/10/30 09:05", NoticeTexts.RestrictDate(_day.AddHours(9).AddMinutes(5)));
        }
    }
}
