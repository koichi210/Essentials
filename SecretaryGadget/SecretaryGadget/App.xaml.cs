using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SecretaryGadget
{
    /// <summary>
    /// 常駐の本体。見張り役(Outlook・Teams)から届いたお知らせを Notice キューで順番に秘書さんへ渡す。
    /// ふだんはタスクトレイにだけ常駐し、お知らせがあるときだけ表示、一定時間で引っ込む。
    /// </summary>
    public partial class App : Application
    {
        private const string _mutexName = @"Local\SecretaryGadget.SingleInstance";
        private const string _exitEventName = @"Local\SecretaryGadget.Exit";
        private const int _pileThreshold = 4;
        private const int _maxTeamsSenders = 200;
        private static readonly TimeSpan _waitInstanceTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan _noHideDuration = TimeSpan.FromMinutes(5);

        private readonly LinkedList<Notice> _queue = new LinkedList<Notice>();
        // ポップアップ由来の Teams の送り主(1行目)を古い順に記憶。メンション未読数が0になったら全消去
        private readonly List<string> _teamsSenders = new List<string>();
        private StartupOptions _options = new StartupOptions();
        private Settings _settings = new Settings();
        private List<SkinInfo> _skins = new List<SkinInfo>();
        private SkinInfo _skin = SkinCatalog.BuiltIn;
        private SecretaryWindow? _window;
        private OutlookWatcher? _outlook;
        private TeamsPopupWatcher? _popupWatcher;
        private TeamsMentionCounter? _mentionCounter;
        private TeamsWatcher? _toastWatcher;
        private Mutex? _mutex;
        private bool _ownsMutex;
        private EventWaitHandle? _exitEvent;
        private DispatcherTimer? _debounceTimer;
        private DispatcherTimer? _flushTimer;
        private DispatcherTimer? _suppressTimer;
        private DispatcherTimer? _memoryTimer;
        private int _memoryTicks;
        private DateTime? _breakUntil;
        private int _breakMinutes;
        private volatile bool _onBreak;
        private int _breakMissed;
        private DateTime? _noHideUntil;
        private bool _exiting;

        private double EffectiveScale => _options.Scale ?? _settings.SkinScale;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _options = StartupOptions.Parse(e.Args);
            if (_options.Exit)
            {
                SignalExit();
                Shutdown();
                return;
            }
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Write("想定外のエラー: " + args.Exception.GetType().Name);
                args.Handled = true;
            };
            if (_options.RenderPreview != null)
            {
                RunRenderPreview(_options.RenderPreview);
                return;
            }
            if (!AcquireSingleInstance())
            {
                Log.Write("二重起動のため終了します");
                Shutdown();
                return;
            }
            System.Windows.Forms.Application.EnableVisualStyles();
            StartExitListener();
            SessionEnding += (_, __) => ExitApp();

            _settings = Settings.Load();
            LoadTimers();
            LoadSkins();
            _window = new SecretaryWindow { DisplaySeconds = _settings.DisplaySeconds };
            _window.Retracted += () => Dispatcher.BeginInvoke(new Action(() => ShowNext(false)));
            _window.BubbleOpened += OpenNotice;
            if (_options.HasDiag("capture-on"))
            {
                _window.SetCaptureExcluded(false);
            }
            ApplySkinToWindow();
            CreateTray();
            FixAutoStartPath();
            StartWatchers();
            StartTimers();
            Log.Write("起動しました");

            RunStartupDiagnostics();
            if (_options.HasDiag("call"))
            {
                Call();
            }
            else if (!_settings.Paused)
            {
                // 起動時のあいさつ(おやすみ中は出さない)
                Enqueue(NoticeTexts.Startup(DateTime.Now, _settings.UserName));
            }
        }

        /// <summary>--exit を受けたとき・メニューの「終了」。後片付けして Shutdown。</summary>
        private void ExitApp()
        {
            if (_exiting)
            {
                return;
            }
            _exiting = true;
            Log.Write("終了します");
            _debounceTimer?.Stop();
            _flushTimer?.Stop();
            _suppressTimer?.Stop();
            _memoryTimer?.Stop();
            // 本家 Teams の小窓を透明化したまま終わると表示されなくなるため、Dispose で全て戻す
            _popupWatcher?.Dispose();
            _mentionCounter?.Dispose();
            _toastWatcher?.Dispose();
            _outlook?.Dispose();
            DisposeTray();
            _window?.Close();
            if (_ownsMutex)
            {
                _mutex?.ReleaseMutex();
                _ownsMutex = false;
            }
            _mutex?.Dispose();
            Shutdown();
        }

        /// <summary>二重起動は Mutex で拒否。--wait-instance なら最大30秒待つ。AbandonedMutex は取得扱い。</summary>
        private bool AcquireSingleInstance()
        {
            _mutex = new Mutex(false, _mutexName);
            try
            {
                _ownsMutex = _mutex.WaitOne(_options.WaitInstance ? _waitInstanceTimeout : TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
            return _ownsMutex;
        }

        /// <summary>名前付きイベント Local\SecretaryGadget.Exit を Set して、動いている秘書さんを終了させる。</summary>
        private static void SignalExit()
        {
            if (EventWaitHandle.TryOpenExisting(_exitEventName, out EventWaitHandle? handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }

        private void StartExitListener()
        {
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _exitEventName);
            var thread = new Thread(() =>
            {
                try
                {
                    _exitEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                Dispatcher.BeginInvoke(new Action(ExitApp));
            })
            {
                IsBackground = true,
                Name = "ExitListener",
            };
            thread.Start();
        }

        private void StartWatchers()
        {
            if (!_options.HasDiag("no-outlook"))
            {
                _outlook = new OutlookWatcher(
                    () => _settings,
                    notice => Post(() =>
                    {
                        if (_settings.NotifyOutlookMail)
                        {
                            Enqueue(notice);
                        }
                    }),
                    notice => Post(() =>
                    {
                        if (_settings.NotifyMeetings)
                        {
                            Enqueue(notice);
                        }
                    }));
                _outlook.Start();
            }
            if (!_options.HasDiag("no-popup"))
            {
                _popupWatcher = new TeamsPopupWatcher(
                    () => _settings,
                    () => _settings.NotifyTeams && !_settings.Paused && !_onBreak,
                    item => Post(() => OnTeamsPopupItem(item)));
                _popupWatcher.Start();
            }
            if (!_options.HasDiag("no-mention-count"))
            {
                _mentionCounter = new TeamsMentionCounter(() => _settings);
                _mentionCounter.Start();
            }
            if (_settings.TeamsToastWatch && !_options.HasDiag("no-toast"))
            {
                _toastWatcher = new TeamsWatcher(() => _settings, notice => Post(() =>
                {
                    if (!string.IsNullOrEmpty(notice.Line1))
                    {
                        RememberTeamsSender(notice.Line1);
                    }
                    if (_settings.NotifyTeams)
                    {
                        Enqueue(notice);
                    }
                }));
                _ = _toastWatcher.StartAsync();
            }
        }

        private void StartTimers()
        {
            // 新着は0.3秒のデバウンス後に表示中でも差し替えて最新を出す
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                ShowNext(true);
            };
            // 3秒ごとの flush: 休憩の期限・「隠さない」の期限・メンション0での送り主消去・次の表示
            _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _flushTimer.Tick += (_, __) => Flush();
            _flushTimer.Start();
            // 表示中も1秒ごとにロック・プレゼン等を確認し、該当したら即 Hide してそのお知らせをキュー先頭へ戻す
            _suppressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _suppressTimer.Tick += (_, __) =>
            {
                if (_window != null && _window.IsShowing && IsSuppressed())
                {
                    Notice? current = _window.HideNow();
                    if (current != null)
                    {
                        _queue.AddFirst(current);
                    }
                    Log.Write("ロック・プレゼン等のため表示を中断しました");
                }
            };
            _suppressTimer.Start();
            _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _memoryTimer.Tick += (_, __) => CheckMemory();
            _memoryTimer.Start();
        }

        private void Flush()
        {
            DateTime now = DateTime.Now;
            if (_onBreak && _breakUntil != null && now >= _breakUntil.Value)
            {
                EndBreak();
            }
            if (_noHideUntil != null && now >= _noHideUntil.Value)
            {
                SetNoHide(false);
            }
            CheckTimers(now);
            if (_mentionCounter?.Count == 0 && _teamsSenders.Count > 0)
            {
                _teamsSenders.Clear();
            }
            if (_window != null && !_window.IsShowing)
            {
                ShowNext(false);
            }
            UpdateTrayText();
        }

        /// <summary>見張り役のスレッドから UI スレッドへ渡す。</summary>
        private void Post(Action action)
        {
            if (!_exiting)
            {
                Dispatcher.BeginInvoke(action);
            }
        }

        /// <summary>
        /// 新着はキューの先頭に入れ、残っている古い Greeting/Schedule は捨てる。
        /// Greeting/Schedule 以外は、おやすみ中は捨て、休憩中は件数だけ数える。呼ぶ系は即時、それ以外は0.3秒のデバウンス。
        /// </summary>
        private void Enqueue(Notice notice)
        {
            if (_exiting)
            {
                return;
            }
            if (!notice.IsCalled)
            {
                if (_settings.Paused)
                {
                    return;
                }
                if (_onBreak)
                {
                    _breakMissed++;
                    return;
                }
            }
            LinkedListNode<Notice>? node = _queue.First;
            while (node != null)
            {
                LinkedListNode<Notice>? next = node.Next;
                // タイマーは取りこぼさないよう、古いものも残す
                if (node.Value.IsCalled && node.Value.Kind != NoticeKind.Timer)
                {
                    _queue.Remove(node);
                }
                node = next;
            }
            _queue.AddFirst(notice);
            _debounceTimer?.Stop();
            if (notice.IsCalled)
            {
                ShowNext(true);
            }
            else
            {
                _debounceTimer?.Start();
            }
        }

        /// <summary>
        /// 次のお知らせを出す。出す直前に Refresh を持つ Notice(会議)を現在時刻で作り直し、null は捨てる。
        /// キューが4件以上なら1件に集約。残りがあればフッターに「ほか N 件」。
        /// </summary>
        private void ShowNext(bool replace)
        {
            if (_window == null || _exiting)
            {
                return;
            }
            if (_window.IsShowing && !replace)
            {
                return;
            }
            // 出しっぱなしのタイマーは、新着で差し替えず、クリックで閉じられたあとに順番に出す
            if (_window.IsShowing && _window.CurrentNotice?.IsSticky == true)
            {
                return;
            }
            if (_queue.Count == 0 || IsSuppressed())
            {
                return;
            }
            DateTime now = DateTime.Now;
            LinkedListNode<Notice>? node = _queue.First;
            while (node != null)
            {
                LinkedListNode<Notice>? next = node.Next;
                if (node.Value.Refresh != null)
                {
                    Notice? rebuilt = node.Value.Refresh(now);
                    if (rebuilt == null)
                    {
                        _queue.Remove(node);
                        Log.Write("終了済みの会議通知を捨てました");
                    }
                    else
                    {
                        node.Value = rebuilt;
                    }
                }
                node = next;
            }
            if (_queue.Count == 0)
            {
                return;
            }
            // タイマーは他の新着より先に出す
            LinkedListNode<Notice>? timerNode = _queue.First;
            while (timerNode != null && timerNode.Value.Kind != NoticeKind.Timer)
            {
                timerNode = timerNode.Next;
            }
            if (timerNode != null && timerNode != _queue.First)
            {
                _queue.Remove(timerNode);
                _queue.AddFirst(timerNode);
            }
            // 本人が呼んだもの(先頭の Greeting/Schedule)は集約に飲み込ませず先に出す
            if (_queue.Count >= _pileThreshold && !_queue.First!.Value.IsCalled)
            {
                int mail = _queue.Count(n => n.Kind == NoticeKind.Mail);
                int teams = _queue.Count(n => n.Kind == NoticeKind.Teams);
                int meeting = _queue.Count(n => n.Kind == NoticeKind.Meeting);
                _queue.Clear();
                Log.Write($"お知らせを集約: メール {mail} 件・Teams {teams} 件・会議 {meeting} 件");
                _window.ShowNotice(NoticeTexts.Pile(mail, teams, meeting), null);
                return;
            }
            Notice notice = _queue.First!.Value;
            _queue.RemoveFirst();
            Log.Write($"お知らせを表示: {notice.Kind}");
            _window.ShowNotice(notice, BuildFooter(_queue.Count, notice.Open != null));
        }

        internal static string? BuildFooter(int remaining, bool clickable)
        {
            var parts = new List<string>();
            if (remaining > 0)
            {
                parts.Add($"ほか {remaining} 件");
            }
            if (clickable)
            {
                parts.Add("クリックで開きます");
            }
            return parts.Count > 0 ? string.Join("　", parts) : null;
        }

        private void OpenNotice(Notice notice)
        {
            try
            {
                notice.Open?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Write("お知らせを開けませんでした: " + ex.GetType().Name);
            }
        }

        /// <summary>SHQueryUserNotificationState が NotPresent(ロック)/Busy/RunningD3DFullScreen/PresentationMode なら出さない。</summary>
        private static bool IsSuppressed()
        {
            if (NativeMethods.SHQueryUserNotificationState(out NativeMethods.UserNotificationState state) != 0)
            {
                return false;
            }
            return state == NativeMethods.UserNotificationState.NotPresent
                || state == NativeMethods.UserNotificationState.Busy
                || state == NativeMethods.UserNotificationState.RunningD3DFullScreen
                || state == NativeMethods.UserNotificationState.PresentationMode;
        }

        private void OnTeamsPopupItem(TeamsPopupItem item)
        {
            if (!item.IsMention && _settings.TeamsMentionOnly)
            {
                return;
            }
            if (item.Lines.Count > 0)
            {
                // 通知 OFF でも記憶する
                RememberTeamsSender(item.Lines[0]);
            }
            if (!_settings.NotifyTeams)
            {
                return;
            }
            Notice notice = NoticeTexts.Teams(item.Lines, item.IsMention);
            Enqueue(new Notice(notice.Kind, notice.Headline, notice.Line1, notice.Line2) { Open = WindowFocus.FocusTeamsMain });
        }

        private void RememberTeamsSender(string line)
        {
            _teamsSenders.Add(line);
            if (_teamsSenders.Count > _maxTeamsSenders)
            {
                _teamsSenders.RemoveAt(0);
            }
        }

        /// <summary>
        /// 秘書さんを呼ぶ: Peek → 並行して Teams メンション数を即時計測、Outlook スレッドで「今日のこのあとの会議」→「未読差出人」を読む
        /// → 全部そろったら即表示 → 表示後に裏で検索フォルダー MarkReadSearchFolder を既読化。
        /// </summary>
        private async void Call()
        {
            if (_window == null)
            {
                return;
            }
            _window.Peek();
            Task mentionTask = _mentionCounter?.CountNowAsync() ?? Task.CompletedTask;
            var outlookResult = new TaskCompletionSource<OutlookCallResult>();
            if (_outlook != null)
            {
                _outlook.RequestCall(r => outlookResult.TrySetResult(r));
            }
            else
            {
                outlookResult.TrySetResult(new OutlookCallResult(false, null, Array.Empty<string>(), null));
            }
            await Task.WhenAny(Task.WhenAll(mentionTask, outlookResult.Task), Task.Delay(_callTimeout));
            OutlookCallResult result = outlookResult.Task.IsCompleted ? outlookResult.Task.Result : new OutlookCallResult(false, null, Array.Empty<string>(), null);
            Notice notice = NoticeTexts.Call(
                DateTime.Now,
                _settings.UserName,
                result.Unread,
                result.UnreadSendersNewestFirst,
                _mentionCounter?.Count,
                _mentionCounter?.CountedAt,
                Enumerable.Reverse(_teamsSenders).ToList(),
                result.Connected ? result.Upcoming : null);
            Log.Write($"秘書さんを呼ぶ: Outlook {(result.Connected ? "接続" : "不在")}");
            Enqueue(notice);
            if (result.Connected && _outlook != null && !string.IsNullOrWhiteSpace(_settings.MarkReadSearchFolder))
            {
                _outlook.RequestMarkSearchFolderRead(_settings.MarkReadSearchFolder);
            }
        }

        /// <summary>本日の予定: 今日の会議(終了済み含む)。</summary>
        private async void ShowSchedule()
        {
            var meetings = new TaskCompletionSource<IReadOnlyList<MeetingInfo>?>();
            if (_outlook != null)
            {
                _outlook.RequestTodayMeetings(m => meetings.TrySetResult(m));
            }
            else
            {
                meetings.TrySetResult(null);
            }
            await Task.WhenAny(meetings.Task, Task.Delay(_callTimeout));
            Enqueue(NoticeTexts.Schedule(DateTime.Now, meetings.Task.IsCompleted ? meetings.Task.Result : null));
        }

        private void StartBreak(TimeSpan duration)
        {
            _breakUntil = DateTime.Now.Add(duration);
            _breakMinutes = (int)duration.TotalMinutes;
            _breakMissed = 0;
            _onBreak = true;
            Log.Write($"休憩: {_breakMinutes} 分");
            Enqueue(NoticeTexts.BreakStarted(_breakUntil.Value));
            UpdateTrayText();
        }

        /// <summary>休憩終わり。明けたら「おかえりなさいませ／休憩中に N 件のお知らせがありました。」</summary>
        private void EndBreak()
        {
            if (!_onBreak)
            {
                return;
            }
            _onBreak = false;
            _breakUntil = null;
            int missed = _breakMissed;
            _breakMissed = 0;
            Enqueue(NoticeTexts.BreakEnded(missed));
            UpdateTrayText();
        }

        /// <summary>「自動的に隠さない」。チェック中はキャプチャ除外も外す(5分で自動的に戻す)。</summary>
        private void SetNoHide(bool on)
        {
            if (_window == null)
            {
                return;
            }
            _noHideUntil = on ? DateTime.Now.Add(_noHideDuration) : null;
            _window.SetAutoHide(!on);
            _window.SetCaptureExcluded(!on && !_options.HasDiag("capture-on"));
            UpdateTrayText();
        }

        private void LoadSkins()
        {
            _skins = SkinCatalog.Scan();
            _skin = SkinCatalog.Pick(_skins, _options.Skin ?? _settings.Skin);
        }

        private void ApplySkinToWindow()
        {
            if (_window == null)
            {
                return;
            }
            _window.ApplySkin(_skin, SkinCatalog.LoadImage(_skin), EffectiveScale, _settings.DisplayHeight);
            UpdateTrayIcon();
        }

        /// <summary>1分ごとに Private メモリが RestartMemoryMB を超えていれば(表示中でないとき)自分を --wait-instance 付きで起動して終了。10分ごとにメモリ内訳をログ。</summary>
        private void CheckMemory()
        {
            using Process self = Process.GetCurrentProcess();
            long privateMb = self.PrivateMemorySize64 / (1024 * 1024);
            _memoryTicks++;
            if (_memoryTicks % 10 == 0)
            {
                Log.Write($"メモリ: Private {privateMb} MB / WorkingSet {self.WorkingSet64 / (1024 * 1024)} MB / Managed {GC.GetTotalMemory(false) / (1024 * 1024)} MB / Handle {self.HandleCount}");
            }
            int limit = _options.RestartMb ?? _settings.RestartMemoryMB;
            if (limit > 0 && privateMb > limit && _window != null && !_window.IsShowing)
            {
                Log.Write($"メモリが {privateMb} MB になったため再起動します");
                RestartSelf();
            }
        }

        private void RestartSelf()
        {
            string? exe = Environment.ProcessPath;
            if (exe == null)
            {
                return;
            }
            var args = new List<string> { "--wait-instance" };
            if (_options.Skin != null)
            {
                args.Add("--skin");
                args.Add(_options.Skin);
            }
            if (_options.Scale != null)
            {
                args.Add("--scale");
                args.Add(_options.Scale.Value.ToString(CultureInfo.InvariantCulture));
            }
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }
            Process.Start(psi);
            ExitApp();
        }

        /// <summary>--render-preview: 吹き出し付きの見た目を PNG にして終了(Outlook・Teams には触らない)。</summary>
        private void RunRenderPreview(string path)
        {
            _settings = Settings.Load();
            LoadSkins();
            DateTime now = DateTime.Now;
            Notice notice;
            if (_options.PreviewCall)
            {
                notice = NoticeTexts.Call(
                    now,
                    _settings.UserName,
                    12,
                    new[] { "山田 太郎 (営業部)", "佐藤さんからのメッセージ", "山田 太郎 (営業部)", "鈴木 花子" },
                    2,
                    now.AddMinutes(-5),
                    new[] { "高橋さんがあなたをメンションしました", "伊藤 次郎" },
                    new[]
                    {
                        new MeetingInfo("週次定例", now.AddMinutes(-10), now.AddMinutes(20), "会議室A"),
                        new MeetingInfo("設計レビュー", now.AddHours(2), now.AddHours(3), "Microsoft Teams 会議"),
                    });
            }
            else if (_options.PreviewSchedule)
            {
                var meetings = Enumerable.Range(0, 10)
                    .Select(i => new MeetingInfo($"サンプル会議 {i + 1}", now.Date.AddHours(9 + i), now.Date.AddHours(9.5 + i), string.Empty))
                    .ToList();
                notice = NoticeTexts.Schedule(now, meetings);
            }
            else
            {
                notice = NoticeTexts.Startup(now, _settings.UserName);
            }
            var window = new SecretaryWindow();
            window.ApplySkin(_skin, SkinCatalog.LoadImage(_skin), EffectiveScale, _settings.DisplayHeight);
            try
            {
                window.RenderPreview(notice, _options.PreviewCall ? BuildFooter(2, true) : null, path);
            }
            catch (Exception ex)
            {
                Log.Write("プレビューを書き出せませんでした: " + ex.GetType().Name);
            }
            window.Close();
            Shutdown();
        }

        /// <summary>--diag-* の確認用の動作。</summary>
        private void RunStartupDiagnostics()
        {
            if (_options.HasDiag("snooze-1min"))
            {
                StartBreak(TimeSpan.FromMinutes(1));
            }
            if (_options.HasDiag("hakoniwa"))
            {
                OpenHakoniwa();
            }
            if (_options.HasDiag("stockradar"))
            {
                OpenStockRadar();
            }
            if (_options.HasDiag("summary"))
            {
                for (int i = 1; i <= 3; i++)
                {
                    Enqueue(NoticeTexts.Mail($"テスト送信者{i}", $"集約の確認 {i}"));
                }
                Enqueue(NoticeTexts.Teams(new[] { "テスト送信者", "集約の確認" }, true));
                Enqueue(new Notice(NoticeKind.Meeting, "まもなく会議が始まります", "00:00～ 集約の確認", "あと 1 分です"));
            }
            if (_options.HasDiag("meeting-expiry"))
            {
                // 5秒後にキューへ入れる。1件目は3秒後に終わる会議なので、表示直前の Refresh で捨てられるのが正しい
                DateTime now = DateTime.Now;
                var expired = new MeetingInfo("期限切れの確認", now.AddMinutes(-10), now.AddSeconds(3), string.Empty);
                var upcoming = new MeetingInfo("開始前の確認", now.AddMinutes(2), now.AddMinutes(32), "会議室B");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    foreach (MeetingInfo m in new[] { upcoming, expired })
                    {
                        MeetingInfo captured = m;
                        Func<DateTime, Notice?> refresh = t => NoticeTexts.Meeting(t, captured);
                        _queue.AddFirst(new Notice(NoticeKind.Meeting, "まもなく会議が始まります", captured.Subject) { Refresh = refresh });
                    }
                    ShowNext(true);
                };
                timer.Start();
            }
        }
    }

    /// <summary>起動引数。</summary>
    internal sealed class StartupOptions
    {
        private readonly HashSet<string> _diag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>--exit: 名前付きイベントを Set して即終了</summary>
        public bool Exit { get; private set; }

        /// <summary>--wait-instance: Mutex を最大30秒待つ(自己再起動・アップデート用)</summary>
        public bool WaitInstance { get; private set; }

        /// <summary>--skin &lt;ID&gt;: 設定を変えずにスキンを指定</summary>
        public string? Skin { get; private set; }

        /// <summary>--scale &lt;倍率&gt;</summary>
        public double? Scale { get; private set; }

        /// <summary>--render-preview &lt;png&gt;</summary>
        public string? RenderPreview { get; private set; }

        public bool PreviewCall { get; private set; }

        public bool PreviewSchedule { get; private set; }

        /// <summary>--diag-restart-mb N</summary>
        public int? RestartMb { get; private set; }

        public bool HasDiag(string name)
        {
            return _diag.Contains(name);
        }

        /// <summary>メニューで大きさを選んだら、起動引数の指定より設定を優先する。</summary>
        public void ClearScale()
        {
            Scale = null;
        }

        /// <summary>メニューでスキンを選んだら、起動引数の指定より設定を優先する。</summary>
        public void ClearSkin()
        {
            Skin = null;
        }

        public static StartupOptions Parse(IReadOnlyList<string> args)
        {
            var o = new StartupOptions();
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                string? next = i + 1 < args.Count ? args[i + 1] : null;
                switch (a.ToLowerInvariant())
                {
                    case "--exit":
                        o.Exit = true;
                        break;
                    case "--wait-instance":
                        o.WaitInstance = true;
                        break;
                    case "--skin":
                        o.Skin = next;
                        i++;
                        break;
                    case "--scale":
                        if (double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0)
                        {
                            o.Scale = scale;
                        }
                        i++;
                        break;
                    case "--render-preview":
                        o.RenderPreview = next;
                        i++;
                        break;
                    case "--preview-call":
                        o.PreviewCall = true;
                        break;
                    case "--preview-schedule":
                        o.PreviewSchedule = true;
                        break;
                    case "--diag-restart-mb":
                        o._diag.Add("restart-mb");
                        if (int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mb))
                        {
                            o.RestartMb = mb;
                        }
                        i++;
                        break;
                    default:
                        if (a.StartsWith("--diag-", StringComparison.OrdinalIgnoreCase))
                        {
                            o._diag.Add(a.Substring("--diag-".Length));
                        }
                        break;
                }
            }
            return o;
        }
    }
}
