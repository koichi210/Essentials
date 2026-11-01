using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace SecretaryGadget
{
    /// <summary>Teams ポップアップから読んだ1件分(カード1枚、またはカード無しの窓全体)。</summary>
    internal sealed record TeamsPopupItem(IReadOnlyList<string> Lines, bool IsMention);

    /// <summary>
    /// Teams 組み込みポップアップ(右下の紫の小窓。クラス TeamsWebView、プロセス ms-teams)の見張り役(主経路)。
    /// 窓探しは EnumWindows、UI Automation は小窓が出ている間だけ使う(UIA は呼ぶたびにネイティブ側のメモリが残るため)。
    /// 通知データベースを直接読む方式は個人情報の扱い上採用しない。
    /// </summary>
    internal sealed class TeamsPopupWatcher : IDisposable
    {
        private const int _maxDepth = 30;
        private const int _maxElements = 60;
        private const int _everyTickReads = 80;
        private const int _noCardReadsToIgnore = 10;
        private const int _maxLinesForNotice = 15;
        private static readonly TimeSpan _slowReadInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan _burstDuration = TimeSpan.FromSeconds(3);
        private const int _burstIntervalMs = 100;

        private readonly Func<Settings> _settings;
        private readonly Func<bool> _canHide;
        private readonly Action<TeamsPopupItem> _onItem;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Dictionary<IntPtr, Session> _sessions = new Dictionary<IntPtr, Session>();
        private readonly object _sessionLock = new object();
        private readonly Thread _thread;
        private volatile bool _stop;
        private long _burstUntilTicks;
        private IntPtr _hook = IntPtr.Zero;
        // GC に回収されないよう、フックのデリゲートはフィールドで持つ
        private NativeMethods.WinEventDelegate? _hookProc;

        public TeamsPopupWatcher(Func<Settings> settings, Func<bool> canHide, Action<TeamsPopupItem> onItem)
        {
            _settings = settings;
            _canHide = canHide;
            _onItem = onItem;
            _thread = new Thread(Run) { IsBackground = true, Name = "TeamsPopupWatcher" };
        }

        /// <summary>UI スレッドから呼ぶ(フックはメッセージループのあるスレッドに置く)。</summary>
        public void Start()
        {
            RestoreLeftovers();
            _hookProc = OnWinEvent;
            _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW, IntPtr.Zero, _hookProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            _thread.Start();
        }

        /// <summary>終了時は隠した本家ポップアップを必ず元へ戻す。</summary>
        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
            if (_thread.IsAlive)
            {
                _thread.Join(2000);
            }
            lock (_sessionLock)
            {
                foreach (Session s in _sessions.Values)
                {
                    Restore(s);
                }
                _sessions.Clear();
            }
        }

        /// <summary>起動時、前回の残骸(WS_EX_TRANSPARENT が付いた TeamsWebView)を探して元に戻す。</summary>
        public static void RestoreLeftovers()
        {
            foreach (IntPtr hwnd in WindowFocus.EnumerateTeamsClassWindows())
            {
                long ex = NativeMethods.GetExStyle(hwnd);
                if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0)
                {
                    NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
                    NativeMethods.SetExStyle(hwnd, ex & ~(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED));
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                    Log.Write("前回透明化したままの Teams の小窓を元に戻しました");
                }
            }
        }

        /// <summary>EVENT_OBJECT_SHOW で起こし、3秒間は0.1秒ごとに確認する。</summary>
        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero)
            {
                return;
            }
            if (NativeMethods.GetClassNameOf(hwnd) != WindowFocus.TeamsClassName)
            {
                return;
            }
            Interlocked.Exchange(ref _burstUntilTicks, DateTime.UtcNow.Add(_burstDuration).Ticks);
            _wake.Set();
        }

        private void Run()
        {
            while (!_stop)
            {
                bool burst = DateTime.UtcNow.Ticks < Interlocked.Read(ref _burstUntilTicks);
                _wake.WaitOne(burst ? _burstIntervalMs : _settings().TeamsPopupPollMilliseconds);
                if (_stop)
                {
                    break;
                }
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log.Write("Teams ポップアップの確認に失敗しました: " + ex.GetType().Name);
                }
            }
        }

        private void Tick()
        {
            // 可視・TeamsWebView・幅<600・高さ<900 の小窓だけを相手にする
            List<TeamsWindow> popups = WindowFocus.EnumerateTeamsWindows().Where(w => !w.IsIconic && w.Rect.Width < 600 && w.Rect.Height < 900).ToList();
            var alive = new HashSet<IntPtr>(popups.Select(p => p.Handle));
            lock (_sessionLock)
            {
                // 窓ハンドル単位のセッションは、窓が見えなくなったら破棄(隠していたら戻す)
                foreach (IntPtr gone in _sessions.Keys.Where(h => !alive.Contains(h)).ToList())
                {
                    Restore(_sessions[gone]);
                    _sessions.Remove(gone);
                }
                foreach (TeamsWindow popup in popups)
                {
                    if (!_sessions.TryGetValue(popup.Handle, out Session? session))
                    {
                        session = new Session(popup.Handle);
                        _sessions[popup.Handle] = session;
                    }
                    if (session.Ignored)
                    {
                        continue;
                    }
                    // 80回までは毎回、以降は10秒ごと
                    if (session.ReadCount >= _everyTickReads && DateTime.UtcNow - session.LastRead < _slowReadInterval)
                    {
                        continue;
                    }
                    Read(session);
                }
            }
        }

        private void Read(Session session)
        {
            session.ReadCount++;
            session.LastRead = DateTime.UtcNow;
            if (!TryCollect(session.Handle, out List<(string Id, string Name)> cards, out List<string> rawLines))
            {
                return;
            }
            List<string> lines = PopupText.RemoveConcatenated(rawLines);
            if (cards.Count == 0 && lines.Count == 0)
            {
                // 未公開なので次周期で再読
                return;
            }
            List<(string Key, List<string> Lines)> items = PopupText.SplitIntoItems(cards, lines);
            if (cards.Count == 0)
            {
                session.NoCardReads++;
                // カード0かつ行>15 は通知ではない(アクティビティ一覧等)。10回読んでカード無しなら以後無視
                if (lines.Count > _maxLinesForNotice || session.NoCardReads >= _noCardReadsToIgnore)
                {
                    session.Ignored = true;
                    if (lines.Count > _maxLinesForNotice)
                    {
                        return;
                    }
                }
            }

            Settings settings = _settings();
            var fresh = new List<TeamsPopupItem>();
            foreach (var item in items)
            {
                if (session.KnownKeys.Add(item.Key))
                {
                    fresh.Add(new TeamsPopupItem(item.Lines, MentionFilter.IsMention(item.Lines, settings.TeamsMentionKeywords)));
                }
            }
            if (fresh.Count == 0)
            {
                return;
            }

            bool hasMention = fresh.Any(f => f.IsMention);
            bool hasOther = fresh.Any(f => !f.IsMention);
            if (hasOther && session.Hidden)
            {
                // 普通の通知が来たら本家を元へ戻す
                Restore(session);
            }
            else if (!hasOther && hasMention && !session.Hidden && settings.TeamsHideMentionPopups && _canHide())
            {
                Hide(session);
            }
            Log.Write($"Teams ポップアップ: 新規 {fresh.Count} 件(メンション {fresh.Count(f => f.IsMention)} 件)");
            foreach (TeamsPopupItem item in fresh)
            {
                _onItem(item);
            }
        }

        /// <summary>
        /// UIA で文字を集める(ControlViewWalker で再帰、深さ30・60個まで)。要素が途中で消えることがあるので
        /// FindAll は使わず1つずつたどり、消えた要素は飛ばす。
        /// </summary>
        private static bool TryCollect(IntPtr hwnd, out List<(string Id, string Name)> cards, out List<string> lines)
        {
            var foundCards = new List<(string Id, string Name)>();
            var foundLines = new List<string>();
            cards = foundCards;
            lines = foundLines;
            AutomationElement root;
            try
            {
                root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception)
            {
                return false;
            }
            TreeWalker walker = TreeWalker.ControlViewWalker;
            int collected = 0;

            void Visit(AutomationElement element, int depth)
            {
                if (depth > _maxDepth || collected >= _maxElements)
                {
                    return;
                }
                try
                {
                    string automationId = element.Current.AutomationId ?? string.Empty;
                    string name = element.Current.Name ?? string.Empty;
                    if (name.Trim().Length > 0)
                    {
                        if (automationId.StartsWith("card-title", StringComparison.Ordinal))
                        {
                            foundCards.Add((automationId, name.Trim()));
                            collected++;
                        }
                        else if (element.Current.ControlType == ControlType.Text)
                        {
                            string text = name.Trim();
                            // メールアドレスを含む行はアカウント行なので捨てる
                            if (!MentionFilter.ContainsAddress(text) && !foundLines.Contains(text))
                            {
                                foundLines.Add(text);
                                collected++;
                            }
                        }
                    }
                }
                catch (ElementNotAvailableException)
                {
                    return;
                }

                AutomationElement? child;
                try
                {
                    child = walker.GetFirstChild(element);
                }
                catch (ElementNotAvailableException)
                {
                    return;
                }
                while (child != null && collected < _maxElements)
                {
                    Visit(child, depth + 1);
                    try
                    {
                        child = walker.GetNextSibling(child);
                    }
                    catch (ElementNotAvailableException)
                    {
                        break;
                    }
                }
            }

            try
            {
                Visit(root, 0);
            }
            catch (Exception ex)
            {
                Log.Write("Teams ポップアップの読み取りに失敗しました: " + ex.GetType().Name);
                return false;
            }
            return true;
        }

        /// <summary>本家ポップアップを隠す: WS_EX_LAYERED|WS_EX_TRANSPARENT ＋ α=0。失敗時は (-32000,-32000) へ移動。</summary>
        private static void Hide(Session s)
        {
            IntPtr hwnd = s.Handle;
            s.OriginalExStyle = NativeMethods.GetExStyle(hwnd);
            bool ok = NativeMethods.SetExStyle(hwnd, s.OriginalExStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT)
                && NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
            if (ok)
            {
                s.Hidden = true;
                s.Moved = false;
                return;
            }
            NativeMethods.SetExStyle(hwnd, s.OriginalExStyle);
            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)
                && NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
            {
                s.OriginalRect = rect;
                s.Hidden = true;
                s.Moved = true;
            }
        }

        private static void Restore(Session s)
        {
            if (!s.Hidden)
            {
                return;
            }
            s.Hidden = false;
            if (!NativeMethods.IsWindow(s.Handle))
            {
                return;
            }
            if (s.Moved)
            {
                NativeMethods.SetWindowPos(s.Handle, IntPtr.Zero, s.OriginalRect.Left, s.OriginalRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                return;
            }
            if ((s.OriginalExStyle & NativeMethods.WS_EX_LAYERED) != 0)
            {
                NativeMethods.SetLayeredWindowAttributes(s.Handle, 0, 255, NativeMethods.LWA_ALPHA);
            }
            NativeMethods.SetExStyle(s.Handle, s.OriginalExStyle);
            NativeMethods.SetWindowPos(s.Handle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }

        /// <summary>窓ハンドル単位の読み取り状態。</summary>
        private sealed class Session
        {
            public Session(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }

            public int ReadCount { get; set; }

            public DateTime LastRead { get; set; }

            public int NoCardReads { get; set; }

            public bool Ignored { get; set; }

            /// <summary>窓ごとの既出キー集合</summary>
            public HashSet<string> KnownKeys { get; } = new HashSet<string>();

            public bool Hidden { get; set; }

            public bool Moved { get; set; }

            public long OriginalExStyle { get; set; }

            public NativeMethods.RECT OriginalRect { get; set; }
        }
    }

    /// <summary>ポップアップから集めた文字の整理(UIA に依存しない部分。単体テスト用に分けている)。</summary>
    internal static class PopupText
    {
        /// <summary>他の行を部分文字列として含む「連結見出し」は除く。</summary>
        public static List<string> RemoveConcatenated(IReadOnlyList<string> lines)
        {
            return lines.Where(l => !lines.Any(o => o != l && l.Contains(o, StringComparison.Ordinal))).ToList();
        }

        /// <summary>
        /// 通知カードに分ける。カードごとの行=カード名に含まれる行(無ければカード名)。キー="card:"+ID+"\n"+カード名
        /// (同じ番号で文面だけ変わる更新も新規扱い)。カード無しなら窓全体で1件。
        /// </summary>
        public static List<(string Key, List<string> Lines)> SplitIntoItems(IReadOnlyList<(string Id, string Name)> cards, IReadOnlyList<string> lines)
        {
            var result = new List<(string Key, List<string> Lines)>();
            if (cards.Count == 0)
            {
                if (lines.Count > 0)
                {
                    result.Add(("window:" + string.Join("\n", lines), lines.ToList()));
                }
                return result;
            }
            foreach (var card in cards)
            {
                List<string> own = lines.Where(l => card.Name.Contains(l, StringComparison.Ordinal)).ToList();
                if (own.Count == 0)
                {
                    own.Add(card.Name);
                }
                result.Add(("card:" + card.Id + "\n" + card.Name, own));
            }
            return result;
        }
    }
}
