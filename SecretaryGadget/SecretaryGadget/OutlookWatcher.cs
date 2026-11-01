using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace SecretaryGadget
{
    /// <summary>「秘書さんを呼ぶ」で Outlook から読んだもの。Connected=false なら Outlook 不在。</summary>
    internal sealed record OutlookCallResult(bool Connected, int? Unread, IReadOnlyList<string> UnreadSendersNewestFirst, IReadOnlyList<MeetingInfo>? Upcoming);

    /// <summary>
    /// 従来版Outlookの見張り役。STAの専用スレッド1本でCOMを触る。
    /// BlockingCollection&lt;Action&gt; を要求キューにし、TryTake(timeout=OutlookPollSeconds) で要求が来れば実行、
    /// 来なければ定期確認(Poll)。他スレッドからの読み出しもすべてこのキュー経由で、結果はコールバックで返す。
    /// プロセス OUTLOOK が無ければ何もしない(勝手に起動させない)。
    /// </summary>
    internal sealed class OutlookWatcher : IDisposable
    {
        private const int _olFolderInbox = 6;
        private const int _olFolderCalendar = 9;
        private const int _olMail = 43;
        private const int _olMeetingRequest = 53;
        private const int _olResponseOrganized = 1;
        private const int _olResponseAccepted = 3;
        private const int _olNonMeeting = 0;
        private const int _olMeetingCanceled = 5;
        private const int _olMeetingReceivedAndCanceled = 7;

        private static readonly TimeSpan _resolveInterval = TimeSpan.FromMinutes(10);
        private static readonly Regex _teamsLink = new Regex(@"https://teams\.microsoft\.com/(l/meetup-join|meet)/[^\s""'<>]+", RegexOptions.IgnoreCase);

        private readonly Func<Settings> _settings;
        private readonly Action<Notice> _onMail;
        private readonly Action<Notice> _onMeeting;
        private readonly BlockingCollection<Action> _requests = new BlockingCollection<Action>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Thread _thread;
        private readonly DateTime _startedAt = DateTime.Now;

        // ここから下は Outlook スレッドだけが触る
        private dynamic? _app;
        private dynamic? _ns;
        private List<FolderRef> _folders = new List<FolderRef>();
        private DateTime _lastResolve = DateTime.MinValue;
        private readonly Dictionary<string, int> _lastCounts = new Dictionary<string, int>();
        private readonly HashSet<string> _knownEntryIds = new HashSet<string>();
        private readonly Dictionary<string, DateTime> _notifiedMeetings = new Dictionary<string, DateTime>();
        private DateTime _lastPoll = DateTime.MinValue;

        public OutlookWatcher(Func<Settings> settings, Action<Notice> onMail, Action<Notice> onMeeting)
        {
            _settings = settings;
            _onMail = onMail;
            _onMeeting = onMeeting;
            _thread = new Thread(Run) { IsBackground = true, Name = "OutlookWatcher" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public void Start()
        {
            _thread.Start();
        }

        /// <summary>Outlook スレッドで実行させる。</summary>
        public void Enqueue(Action action)
        {
            if (!_requests.IsAddingCompleted)
            {
                try
                {
                    _requests.Add(action);
                }
                catch (InvalidOperationException)
                {
                    // 終了処理と競合したら捨てる
                }
            }
        }

        /// <summary>「秘書さんを呼ぶ」用。今日のこのあとの会議 → 未読差出人 → 未読数 の順に読む。</summary>
        public void RequestCall(Action<OutlookCallResult> callback)
        {
            Enqueue(() =>
            {
                OutlookCallResult result = new OutlookCallResult(false, null, Array.Empty<string>(), null);
                try
                {
                    if (EnsureConnected())
                    {
                        EnsureFolders(force: _folders.Count == 0);
                        List<MeetingInfo> upcoming = ReadTodayMeetings(includeEnded: false);
                        List<string> senders = ReadUnreadSenders();
                        int? unread = ReadUnreadCount();
                        result = new OutlookCallResult(unread != null, unread, senders, upcoming);
                    }
                }
                catch (Exception ex)
                {
                    HandleComFailure(ex);
                    result = new OutlookCallResult(false, null, Array.Empty<string>(), null);
                }
                callback(result);
            });
        }

        /// <summary>「本日の予定」用。今日の会議(終了済み含む)。Outlook不在なら null。</summary>
        public void RequestTodayMeetings(Action<IReadOnlyList<MeetingInfo>?> callback)
        {
            Enqueue(() =>
            {
                List<MeetingInfo>? meetings = null;
                try
                {
                    if (EnsureConnected())
                    {
                        meetings = ReadTodayMeetings(includeEnded: true);
                    }
                }
                catch (Exception ex)
                {
                    HandleComFailure(ex);
                    meetings = null;
                }
                callback(meetings);
            });
        }

        /// <summary>検索フォルダーを既読化する。先に EntryID を控えてから1件ずつ UnRead=false・Save、上限1000。</summary>
        public void RequestMarkSearchFolderRead(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }
            Enqueue(() =>
            {
                try
                {
                    if (!EnsureConnected())
                    {
                        return;
                    }
                    dynamic searchFolders = _ns!.DefaultStore.GetSearchFolders();
                    dynamic? target = null;
                    int count = searchFolders.Count;
                    for (int i = 1; i <= count; i++)
                    {
                        dynamic f = searchFolders.Item(i);
                        if (string.Equals((string)f.Name, folderName, StringComparison.OrdinalIgnoreCase))
                        {
                            target = f;
                            break;
                        }
                    }
                    if (target == null)
                    {
                        return;
                    }
                    var ids = new List<string>();
                    dynamic items = target.Items.Restrict("[UnRead] = True");
                    dynamic item = items.GetFirst();
                    while (item is not null && ids.Count < 1000)
                    {
                        ids.Add((string)item.EntryID);
                        item = items.GetNext();
                    }
                    int done = 0;
                    foreach (string id in ids)
                    {
                        try
                        {
                            dynamic mail = _ns.GetItemFromID(id);
                            mail.UnRead = false;
                            mail.Save();
                            done++;
                        }
                        catch (COMException)
                        {
                            // 途中で消えた・移動したアイテムは飛ばす
                        }
                    }
                    Log.Write($"検索フォルダーを既読化: {done} 件");
                }
                catch (Exception ex)
                {
                    HandleComFailure(ex);
                }
                finally
                {
                    CollectComGarbage();
                }
            });
        }

        /// <summary>アイテムを開く(GetItemFromID(id).Display())。</summary>
        public void RequestDisplay(string entryId)
        {
            Enqueue(() =>
            {
                try
                {
                    if (EnsureConnected())
                    {
                        dynamic item = _ns!.GetItemFromID(entryId);
                        item.Display(false);
                    }
                }
                catch (Exception ex)
                {
                    HandleComFailure(ex);
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            _requests.CompleteAdding();
            if (_thread.IsAlive)
            {
                _thread.Join(3000);
            }
            _cts.Dispose();
        }

        private void Run()
        {
            while (!_cts.IsCancellationRequested)
            {
                int timeoutMs = Math.Max(1, _settings().OutlookPollSeconds) * 1000;
                bool got;
                Action? action = null;
                try
                {
                    got = _requests.TryTake(out action, timeoutMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                if (got && action != null)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        HandleComFailure(ex);
                    }
                    // 要求が続いて Poll が止まらないよう、間隔が過ぎていれば続けて Poll する
                    if (DateTime.Now - _lastPoll < TimeSpan.FromMilliseconds(timeoutMs))
                    {
                        continue;
                    }
                }
                Poll();
            }
            Disconnect();
        }

        private void Poll()
        {
            _lastPoll = DateTime.Now;
            try
            {
                if (!EnsureConnected())
                {
                    return;
                }
                EnsureFolders(force: false);
                PollNewMail();
                PollMeetings();
            }
            catch (Exception ex)
            {
                HandleComFailure(ex);
            }
            finally
            {
                CollectComGarbage();
            }
        }

        /// <summary>
        /// フォルダごとに Items.Count を前回と比べ、増えたフォルダだけ受信日時の新しい順に min(増分+2, 30) 件を見る。
        /// 受信日時が起動時刻より前なら打ち切り、既知の EntryID・既読は除外。初回観測のフォルダは件数を覚えるだけ。
        /// </summary>
        private void PollNewMail()
        {
            var found = new List<(DateTime Received, string EntryId, string Sender, string Subject)>();
            foreach (FolderRef folderRef in _folders)
            {
                dynamic folder = _ns!.GetFolderFromID(folderRef.EntryId, folderRef.StoreId);
                dynamic items = folder.Items;
                int count = items.Count;
                bool firstSeen = !_lastCounts.TryGetValue(folderRef.EntryId, out int last);
                _lastCounts[folderRef.EntryId] = count;
                if (firstSeen || count <= last)
                {
                    continue;
                }
                int toCheck = Math.Min(count - last + 2, 30);
                items.Sort("[ReceivedTime]", true);
                dynamic item = items.GetFirst();
                for (int i = 0; i < toCheck && item is not null; i++)
                {
                    try
                    {
                        int cls = item.Class;
                        if (cls == _olMail || cls == _olMeetingRequest)
                        {
                            DateTime received = item.ReceivedTime;
                            if (received < _startedAt)
                            {
                                break;
                            }
                            string id = item.EntryID;
                            if (item.UnRead && !_knownEntryIds.Contains(id))
                            {
                                _knownEntryIds.Add(id);
                                found.Add((received, id, (string?)item.SenderName ?? string.Empty, (string?)item.Subject ?? string.Empty));
                            }
                        }
                    }
                    catch (COMException)
                    {
                        // 読めないアイテム(配信レポート等)は飛ばす
                    }
                    item = items.GetNext();
                }
            }
            if (_knownEntryIds.Count > 5000)
            {
                _knownEntryIds.Clear();
            }
            if (found.Count == 0)
            {
                return;
            }
            Log.Write($"新着メール: {found.Count} 件");
            foreach (var mail in found.OrderBy(m => m.Received))
            {
                string entryId = mail.EntryId;
                Notice notice = NoticeTexts.Mail(mail.Sender, mail.Subject);
                _onMail(new Notice(notice.Kind, notice.Headline, notice.Line1, notice.Line2) { Open = () => RequestDisplay(entryId) });
            }
        }

        /// <summary>
        /// 予定表の [Start] が「今−2分 〜 今＋MeetingLeadMinutes」の会議。Count は当てにならないので GetFirst/GetNext で最大50件。
        /// 重複防止キーは「EntryID|開始日時」。
        /// </summary>
        private void PollMeetings()
        {
            Settings settings = _settings();
            DateTime now = DateTime.Now;
            dynamic items = OpenCalendarItems();
            string filter = $"[Start] >= '{NoticeTexts.RestrictDate(now.AddMinutes(-2))}' AND [Start] <= '{NoticeTexts.RestrictDate(now.AddMinutes(settings.MeetingLeadMinutes))}'";
            dynamic restricted = items.Restrict(filter);
            dynamic appt = restricted.GetFirst();
            int newCount = 0;
            for (int i = 0; i < 50 && appt is not null; i++)
            {
                try
                {
                    if (IsTargetMeeting(appt, settings.MeetingIncludeOrganized))
                    {
                        DateTime start = appt.Start;
                        string key = $"{(string)appt.EntryID}|{start:yyyy-MM-ddTHH:mm}";
                        if (!_notifiedMeetings.ContainsKey(key))
                        {
                            _notifiedMeetings[key] = start;
                            MeetingInfo info = ToMeetingInfo(appt);
                            string? link = FindTeamsLink(appt);
                            string entryId = appt.EntryID;
                            Notice? first = NoticeTexts.Meeting(now, info);
                            if (first != null)
                            {
                                newCount++;
                                _onMeeting(WithMeetingActions(first, info, link, entryId));
                            }
                        }
                    }
                }
                catch (COMException)
                {
                    // 読めない予定は飛ばす
                }
                appt = restricted.GetNext();
            }
            foreach (string old in _notifiedMeetings.Where(kv => kv.Value < now.AddDays(-1)).Select(kv => kv.Key).ToList())
            {
                _notifiedMeetings.Remove(old);
            }
            if (newCount > 0)
            {
                Log.Write($"会議の開始前通知: {newCount} 件");
            }
        }

        /// <summary>クリック動作と Refresh(表示直前に現在時刻で作り直す)を付ける。</summary>
        private Notice WithMeetingActions(Notice notice, MeetingInfo info, string? link, string entryId)
        {
            Action open = () =>
            {
                if (link != null)
                {
                    WindowFocus.OpenUri(ToTeamsUri(link));
                }
                else
                {
                    RequestDisplay(entryId);
                }
            };
            Func<DateTime, Notice?>? refresh = null;
            refresh = now =>
            {
                Notice? rebuilt = NoticeTexts.Meeting(now, info);
                return rebuilt == null ? null : new Notice(rebuilt.Kind, rebuilt.Headline, rebuilt.Line1, rebuilt.Line2) { Open = open, Refresh = refresh };
            };
            return new Notice(notice.Kind, notice.Headline, notice.Line1, notice.Line2) { Open = open, Refresh = refresh };
        }

        /// <summary>/l/ 形式なら先頭を msteams: に置き換える。</summary>
        public static string ToTeamsUri(string link)
        {
            const string prefix = "https://teams.microsoft.com";
            if (link.StartsWith(prefix + "/l/", StringComparison.OrdinalIgnoreCase))
            {
                return "msteams:" + link.Substring(prefix.Length);
            }
            return link;
        }

        public static string? FindTeamsLinkInText(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }
            Match m = _teamsLink.Match(body);
            return m.Success ? m.Value : null;
        }

        private static string? FindTeamsLink(dynamic appt)
        {
            try
            {
                return FindTeamsLinkInText((string?)appt.Body);
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>終日を除く・キャンセルを除く。承諾、または主催かつ人を呼んでいない自分の予定。MeetingIncludeOrganized なら主催全部。</summary>
        private static bool IsTargetMeeting(dynamic appt, bool includeOrganized)
        {
            if ((bool)appt.AllDayEvent)
            {
                return false;
            }
            int meetingStatus = appt.MeetingStatus;
            if (meetingStatus == _olMeetingCanceled || meetingStatus == _olMeetingReceivedAndCanceled)
            {
                return false;
            }
            int response = appt.ResponseStatus;
            if (response == _olResponseAccepted)
            {
                return true;
            }
            return response == _olResponseOrganized && (meetingStatus == _olNonMeeting || includeOrganized);
        }

        private static MeetingInfo ToMeetingInfo(dynamic appt)
        {
            return new MeetingInfo((string?)appt.Subject ?? string.Empty, (DateTime)appt.Start, (DateTime)appt.End, (string?)appt.Location ?? string.Empty);
        }

        private dynamic OpenCalendarItems()
        {
            dynamic items = _ns!.GetDefaultFolder(_olFolderCalendar).Items;
            items.IncludeRecurrences = true;
            items.Sort("[Start]");
            return items;
        }

        /// <summary>今日の会議(今日0時〜翌0時、最大200件)。</summary>
        private List<MeetingInfo> ReadTodayMeetings(bool includeEnded)
        {
            Settings settings = _settings();
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            dynamic items = OpenCalendarItems();
            dynamic restricted = items.Restrict($"[Start] >= '{NoticeTexts.RestrictDate(today)}' AND [Start] < '{NoticeTexts.RestrictDate(today.AddDays(1))}'");
            var result = new List<MeetingInfo>();
            dynamic appt = restricted.GetFirst();
            for (int i = 0; i < 200 && appt is not null; i++)
            {
                try
                {
                    if (IsTargetMeeting(appt, settings.MeetingIncludeOrganized))
                    {
                        MeetingInfo info = ToMeetingInfo(appt);
                        if (includeEnded || info.End > now)
                        {
                            result.Add(info);
                        }
                    }
                }
                catch (COMException)
                {
                    // 読めない予定は飛ばす
                }
                appt = restricted.GetNext();
            }
            return result.OrderBy(m => m.Start).ToList();
        }

        /// <summary>未読差出人(各フォルダ Restrict("[UnRead] = True")、最大100件、受信日時の新しい順)。</summary>
        private List<string> ReadUnreadSenders()
        {
            var all = new List<(DateTime Received, string Sender)>();
            foreach (FolderRef folderRef in _folders)
            {
                dynamic folder = _ns!.GetFolderFromID(folderRef.EntryId, folderRef.StoreId);
                dynamic unread = folder.Items.Restrict("[UnRead] = True");
                unread.Sort("[ReceivedTime]", true);
                dynamic item = unread.GetFirst();
                for (int i = 0; i < 100 && item is not null; i++)
                {
                    try
                    {
                        all.Add(((DateTime)item.ReceivedTime, (string?)item.SenderName ?? string.Empty));
                    }
                    catch (Exception)
                    {
                        // 差出人を持たないアイテムは飛ばす
                    }
                    item = unread.GetNext();
                }
            }
            return all.OrderByDescending(a => a.Received).Take(100).Select(a => a.Sender).ToList();
        }

        /// <summary>見張りフォルダの UnReadItemCount 合計。未接続なら null。</summary>
        private int? ReadUnreadCount()
        {
            if (_ns == null)
            {
                return null;
            }
            int total = 0;
            foreach (FolderRef folderRef in _folders)
            {
                dynamic folder = _ns.GetFolderFromID(folderRef.EntryId, folderRef.StoreId);
                total += (int)folder.UnReadItemCount;
            }
            return total;
        }

        /// <summary>
        /// OutlookWatchFolders の各パス("/" か "\" 区切り)を EntryID に解決して保持し、10分ごとに解決し直す。子フォルダは含めない。
        /// 先頭が「受信トレイ」なら GetDefaultFolder(6)、それ以外は DefaultStore.GetRootFolder().Folders[名前] からたどる。
        /// </summary>
        private void EnsureFolders(bool force)
        {
            if (!force && _folders.Count > 0 && DateTime.Now - _lastResolve < _resolveInterval)
            {
                return;
            }
            _lastResolve = DateTime.Now;
            var resolved = new List<FolderRef>();
            foreach (string path in _settings().OutlookWatchFolders)
            {
                string[] parts = (path ?? string.Empty).Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                if (parts.Length == 0)
                {
                    continue;
                }
                try
                {
                    dynamic folder;
                    int index;
                    if (IsInboxName(parts[0]))
                    {
                        folder = _ns!.GetDefaultFolder(_olFolderInbox);
                        index = 1;
                    }
                    else
                    {
                        folder = _ns!.DefaultStore.GetRootFolder();
                        index = 0;
                    }
                    for (; index < parts.Length; index++)
                    {
                        folder = folder.Folders[parts[index]];
                    }
                    var folderRef = new FolderRef((string)folder.EntryID, (string)folder.StoreID);
                    if (!resolved.Any(r => r.EntryId == folderRef.EntryId))
                    {
                        resolved.Add(folderRef);
                    }
                }
                catch (Exception)
                {
                    Log.Write("見張りフォルダが見つかりません(設定の OutlookWatchFolders を確認)");
                }
            }
            _folders = resolved;
        }

        private static bool IsInboxName(string name)
        {
            return name == "受信トレイ" || string.Equals(name, "Inbox", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>プロセス OUTLOOK があるときだけ接続する。無ければ参照を手放す。</summary>
        private bool EnsureConnected()
        {
            Process[] processes = Process.GetProcessesByName("OUTLOOK");
            bool running = processes.Length > 0;
            foreach (Process p in processes)
            {
                p.Dispose();
            }
            if (!running)
            {
                if (_app != null)
                {
                    Log.Write("Outlook が終了したため切断しました");
                    Disconnect();
                }
                return false;
            }
            if (_app != null)
            {
                return true;
            }
            Type? type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null)
            {
                return false;
            }
            _app = Activator.CreateInstance(type);
            _ns = _app!.GetNamespace("MAPI");
            _folders = new List<FolderRef>();
            Log.Write("Outlook に接続しました");
            return true;
        }

        private void HandleComFailure(Exception ex)
        {
            // Outlook の終了中などは COM が失敗する。次回の Poll でつなぎ直す
            Log.Write("Outlook の読み取りに失敗しました: " + ex.GetType().Name);
            Disconnect();
        }

        private void Disconnect()
        {
            _ns = null;
            _app = null;
            _folders = new List<FolderRef>();
            _lastCounts.Clear();
            CollectComGarbage();
        }

        /// <summary>Outlook 終了時に COM 参照で居残らせないよう、毎回の Poll 後に回収する。</summary>
        private static void CollectComGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private sealed record FolderRef(string EntryId, string StoreId);
    }
}
