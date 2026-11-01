using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SecretaryGadget
{
    /// <summary>お知らせの種類。Greeting(あいさつ・まとめ・確認)とScheduleは「本人が呼んだもの」扱い。</summary>
    internal enum NoticeKind
    {
        Greeting,
        Mail,
        Teams,
        Meeting,
        Schedule,
        Timer,
    }

    /// <summary>吹き出し1回分のお知らせ。</summary>
    internal sealed class Notice
    {
        public Notice(NoticeKind kind, string headline, string? line1 = null, string? line2 = null, string? line3 = null)
        {
            Kind = kind;
            Headline = headline;
            Line1 = line1;
            Line2 = line2;
            Line3 = line3;
        }

        public NoticeKind Kind { get; }

        public string Headline { get; }

        /// <summary>1行目(最大2行で切る)</summary>
        public string? Line1 { get; }

        /// <summary>2行目(最大3行で切る)</summary>
        public string? Line2 { get; }

        /// <summary>3行目(予定一覧など。高さ制限なし)</summary>
        public string? Line3 { get; }

        /// <summary>吹き出しクリックで実行する動作。無い「まとめ」系は何もしない。</summary>
        public Action? Open { get; init; }

        /// <summary>出す直前に現在時刻で作り直す(会議)。nullを返したら捨てる。</summary>
        public Func<DateTime, Notice?>? Refresh { get; init; }

        /// <summary>おやすみ・休憩中でも出すもの</summary>
        public bool IsCalled => Kind == NoticeKind.Greeting || Kind == NoticeKind.Schedule || Kind == NoticeKind.Timer;

        /// <summary>自動で隠れず、クリックされるまで出し続ける(タイマー)</summary>
        public bool IsSticky => Kind == NoticeKind.Timer;

        /// <summary>本文の高さ制限なし(本日の予定)</summary>
        public bool IsUnlimited => Kind == NoticeKind.Schedule;
    }

    /// <summary>会議1件の情報(Outlookから読んだもの)。</summary>
    internal sealed record MeetingInfo(string Subject, DateTime Start, DateTime End, string Location);

    /// <summary>
    /// 吹き出しの文面を組み立てる。時刻を引数で受け取る純粋な関数だけを置き、単体テストできるようにしている。
    /// </summary>
    internal static class NoticeTexts
    {
        private static readonly Regex _trailingParen = new Regex(@"\s*[\(（][^\(\)（）]*[\)）]\s*$");
        private static readonly Regex _sanGaKara = new Regex(@"^(.+?)さん(が|から)");

        /// <summary>10:30前「おはようございます」、18:00前「お疲れさまです」、以降「遅くまでお疲れさまです」</summary>
        public static string Greeting(DateTime now)
        {
            TimeSpan t = now.TimeOfDay;
            if (t < new TimeSpan(10, 30, 0))
            {
                return "おはようございます";
            }
            if (t < new TimeSpan(18, 0, 0))
            {
                return "お疲れさまです";
            }
            return "遅くまでお疲れさまです";
        }

        public static string GreetingHeadline(DateTime now, string userName)
        {
            return $"{Greeting(now)}、{userName}";
        }

        public static Notice Startup(DateTime now, string userName)
        {
            return new Notice(NoticeKind.Greeting, GreetingHeadline(now, userName), "秘書がお知らせを監視しています。", "本日もよろしくお願いいたします。");
        }

        /// <summary>送り主の名前を整える。末尾の「(…)」「（…）」を除き、「Xさんが…」「Xさんから…」は X にする。</summary>
        public static string CleanSenderName(string name)
        {
            string s = (name ?? string.Empty).Trim();
            Match m = _sanGaKara.Match(s);
            if (m.Success)
            {
                s = m.Groups[1].Value.Trim();
            }
            string previous;
            do
            {
                previous = s;
                s = _trailingParen.Replace(s, string.Empty).Trim();
            }
            while (s != previous && s.Length > 0);
            return s;
        }

        /// <summary>
        /// 送り主のまとめ「A・B ほか N 件」。namesNewestFirstは新しい順の送り主(1件1要素)、totalは件数。
        /// 重複を除いて2人まで「・」で連結し、その2人の分を除いた残りを「ほか N 件」にする。
        /// </summary>
        public static string SummarizeSenders(IEnumerable<string> namesNewestFirst, int total)
        {
            List<string> cleaned = namesNewestFirst.Select(CleanSenderName).Where(n => n.Length > 0).ToList();
            List<string> shown = cleaned.Distinct().Take(2).ToList();
            if (shown.Count == 0)
            {
                return string.Empty;
            }
            int coveredByShown = cleaned.Count(n => shown.Contains(n));
            int others = Math.Max(0, total - coveredByShown);
            string joined = string.Join("・", shown);
            return others > 0 ? $"{joined} ほか {others} 件" : joined;
        }

        /// <summary>「秘書さんを呼ぶ」の文面。unreadがnullならOutlook不在(古い件数は使わない)。</summary>
        public static Notice Call(
            DateTime now,
            string userName,
            int? unread,
            IReadOnlyList<string> unreadSendersNewestFirst,
            int? mentionCount,
            DateTime? mentionCountedAt,
            IReadOnlyList<string> teamsSendersNewestFirst,
            IReadOnlyList<MeetingInfo>? upcoming)
        {
            string mailLine;
            if (unread == null)
            {
                mailLine = "メール：Outlook が起動していないようです";
            }
            else
            {
                string summary = SummarizeSenders(unreadSendersNewestFirst, unread.Value);
                mailLine = $"メールの未読：{unread.Value} 件" + (summary.Length > 0 ? $"（{summary}）" : string.Empty);
            }

            string teamsLine;
            if (mentionCount == null)
            {
                teamsLine = "Teams のメンション未読：まだ数えられていません（Teams のチャット画面を一度開いてください）";
            }
            else
            {
                var sb = new StringBuilder($"Teams のメンション未読：{mentionCount.Value} 件");
                string summary = SummarizeSenders(teamsSendersNewestFirst.Take(mentionCount.Value), mentionCount.Value);
                if (summary.Length > 0)
                {
                    sb.Append($"（{summary}）");
                }
                if (mentionCountedAt != null && now - mentionCountedAt.Value > TimeSpan.FromMinutes(3))
                {
                    sb.Append($"（{mentionCountedAt.Value:HH:mm} 時点）");
                }
                teamsLine = sb.ToString();
            }

            string? meetingLine = null;
            if (upcoming != null)
            {
                meetingLine = upcoming.Count == 0
                    ? "本日のこのあとの会議：ありません"
                    : "本日のこのあとの会議：" + Environment.NewLine + string.Join(Environment.NewLine, upcoming.Select(m => FormatMeetingRow(now, m)));
            }

            return new Notice(NoticeKind.Greeting, GreetingHeadline(now, userName), mailLine, teamsLine, meetingLine);
        }

        /// <summary>「本日の予定」。今日の会議(終了済み含む)を8件まで、残りは「ほか N 件」。todayがnullならOutlook不在。</summary>
        public static Notice Schedule(DateTime now, IReadOnlyList<MeetingInfo>? today)
        {
            const string headline = "本日の予定です";
            if (today == null)
            {
                return new Notice(NoticeKind.Schedule, headline, "Outlook が起動していないようです");
            }
            if (today.Count == 0)
            {
                return new Notice(NoticeKind.Schedule, headline, "本日、参加予定の会議はありません。");
            }
            int later = today.Count(m => m.End > now);
            var rows = today.Take(8).Select(m => FormatMeetingRow(now, m)).ToList();
            if (today.Count > 8)
            {
                rows.Add($"ほか {today.Count - 8} 件");
            }
            return new Notice(NoticeKind.Schedule, headline, $"参加予定の会議は {today.Count} 件（このあと {later} 件）です。", null, string.Join(Environment.NewLine, rows));
        }

        /// <summary>予定一覧の1行「HH:mm　件名」＋「（終了）」「（開催中）」</summary>
        public static string FormatMeetingRow(DateTime now, MeetingInfo m)
        {
            string state = m.End <= now ? "（終了）" : (m.Start <= now ? "（開催中）" : string.Empty);
            return $"{m.Start:HH:mm}　{m.Subject}{state}";
        }

        /// <summary>
        /// 会議通知の文面。終了済みならnull(出さない)。出す直前のRefreshでも同じ関数を現在時刻で呼ぶ。
        /// </summary>
        public static Notice? Meeting(DateTime now, MeetingInfo m)
        {
            if (now >= m.End)
            {
                return null;
            }
            string headline;
            string line2;
            if (now < m.Start)
            {
                int minutes = (int)Math.Ceiling((m.Start - now).TotalMinutes);
                headline = "まもなく会議が始まります";
                line2 = $"あと {minutes} 分です";
            }
            else
            {
                headline = "会議が始まっています";
                line2 = $"{m.Start:HH:mm} から開催中です";
            }
            string location = (m.Location ?? string.Empty).Trim();
            if (location.Length > 0 && location.IndexOf("Teams", StringComparison.OrdinalIgnoreCase) < 0)
            {
                line2 += $"　場所：{location}";
            }
            return new Notice(NoticeKind.Meeting, headline, $"{m.Start:HH:mm}～ {m.Subject}", line2);
        }

        public static Notice Mail(string senderName, string subject)
        {
            string s = string.IsNullOrWhiteSpace(subject) ? "（件名なし）" : subject;
            return new Notice(NoticeKind.Mail, "メールが届いています", $"差出人：{senderName}", $"件名：{s}");
        }

        /// <summary>Teams組み込みポップアップ由来。1行目=先頭行(送り主)、2行目=残りを空白連結。</summary>
        public static Notice Teams(IReadOnlyList<string> lines, bool isMention)
        {
            string headline = isMention ? "Teams でお呼びがかかっています" : "Teams にメッセージです";
            string? line1 = lines.Count > 0 ? lines[0] : null;
            string? line2 = lines.Count > 1 ? string.Join(" ", lines.Skip(1)) : null;
            return new Notice(NoticeKind.Teams, headline, line1, line2);
        }

        /// <summary>キューが4件以上たまったときのまとめ。</summary>
        public static Notice Pile(int mail, int teams, int meeting)
        {
            string line1 = meeting > 0
                ? $"メール {mail} 件・Teams {teams} 件・会議 {meeting} 件のお知らせがあります。"
                : $"メール {mail} 件・Teams {teams} 件が届いています。";
            return new Notice(NoticeKind.Greeting, "お知らせがたまっています", line1, "受信トレイ・Teams をご確認ください。");
        }

        public static Notice SizeChanged()
        {
            return new Notice(NoticeKind.Greeting, "大きさを変えました");
        }

        public static Notice HideTimeChanged()
        {
            return new Notice(NoticeKind.Greeting, "隠れるまでの時間を変えました");
        }

        public static Notice SettingChanged(string item, bool on)
        {
            return new Notice(NoticeKind.Greeting, "設定を変えました", $"{item}：{(on ? "ON" : "OFF")}");
        }

        public static Notice SettingsDirChanged(string dir, bool loadedExisting)
        {
            return new Notice(NoticeKind.Greeting, "設定を変えました", $"設定ファイルのフォルダ：{dir}", loadedExisting ? "このフォルダにあった設定を読み込みました。" : "今の設定をこのフォルダに保存しました。");
        }

        public static Notice SettingsDirFailed(string dir)
        {
            return new Notice(NoticeKind.Greeting, "設定ファイルのフォルダを変更できませんでした", $"このフォルダに保存できません：{dir}");
        }

        public static Notice SkinChanged(string skinName)
        {
            return new Notice(NoticeKind.Greeting, "担当を交代しました", $"本日からは{skinName}がお知らせを担当します。", "よろしくお願いいたします。");
        }

        public static Notice BreakStarted(DateTime resumeAt)
        {
            return new Notice(NoticeKind.Greeting, "休憩します", $"{resumeAt:HH:mm} に再開します。");
        }

        public static Notice BreakEnded(int missed)
        {
            return new Notice(NoticeKind.Greeting, "おかえりなさいませ", missed > 0 ? $"休憩中に {missed} 件のお知らせがありました。" : null);
        }

        public static Notice TimerSet(DateTime now, TimerEntry t)
        {
            TimeSpan left = t.Due - now;
            string when = t.Due.Date == now.Date ? $"{t.Due:HH:mm}" : $"明日 {t.Due:HH:mm}";
            string remain = left.TotalMinutes < 1 ? "まもなく" : $"あと {TimerInput.FormatSpan((int)left.TotalHours, left.Minutes)}";
            return new Notice(NoticeKind.Greeting, "タイマーをセットしました", $"{when} にお知らせします。", $"（{remain}）");
        }

        /// <summary>時間になったときの吹き出し。クリックされるまで消えない。</summary>
        public static Notice TimerDue(DateTime now, TimerEntry t)
        {
            string line2 = now - t.Due >= TimeSpan.FromMinutes(1) ? $"{t.Due:HH:mm} の予定でした。" : "クリックすると閉じます。";
            return new Notice(NoticeKind.Timer, "タイマーの時間です", $"{t.Label}にセットしたタイマーです。", line2);
        }

        public static Notice TimerCanceled(TimerEntry t)
        {
            return new Notice(NoticeKind.Greeting, "タイマーを取り消しました", $"{t.Due:HH:mm} のタイマーです。");
        }

        public static Notice Test()
        {
            return new Notice(NoticeKind.Greeting, "メールが届いています", "差出人：テスト送信者", "件名：これはテスト通知です。秘書さんの見た目と動きを確認できます。");
        }

        /// <summary>Outlookの Restrict に渡す日時の書式 'yyyy/MM/dd HH:mm'</summary>
        public static string RestrictDate(DateTime t)
        {
            return t.ToString("yyyy'/'MM'/'dd HH':'mm", CultureInfo.InvariantCulture);
        }
    }
}
