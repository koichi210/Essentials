using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace SecretaryGadget
{
    /// <summary>
    /// Teams のメンション未読数。60秒ごと(初回に見つからなければ5秒後に1回再試行)に、
    /// 可視・TeamsWebView・幅&gt;600 の窓から ^メンション(?:\s+(\d+)\s*未読)?$ に一致する TreeItem を探す(数字なし=0件)。
    /// 見つからなければ前回値を残す。「秘書さんを呼ぶ」時は CountNowAsync() で即時に数え直す(ロックで定期処理と排他)。
    /// </summary>
    internal sealed class TeamsMentionCounter : IDisposable
    {
        private static readonly Regex _mentionItem = new Regex(@"^メンション(?:\s+(\d+)\s*未読)?$");
        private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromSeconds(5);

        private readonly Func<Settings> _settings;
        private readonly object _countLock = new object();
        private readonly Timer _timer;
        private bool _firstDone;
        private volatile bool _disposed;

        public TeamsMentionCounter(Func<Settings> settings)
        {
            _settings = settings;
            _timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>最後に数えた値。一度も数えられていなければ null。</summary>
        public int? Count { get; private set; }

        /// <summary>最後に数えた時刻</summary>
        public DateTime? CountedAt { get; private set; }

        public void Start()
        {
            _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        public Task CountNowAsync()
        {
            return Task.Run(() => CountCore());
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }

        /// <summary>メンションの TreeItem 名から未読数を読む。一致しなければ null。</summary>
        public static int? ParseMentionItemName(string? name)
        {
            Match m = _mentionItem.Match((name ?? string.Empty).Trim());
            if (!m.Success)
            {
                return null;
            }
            return m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private void OnTimer()
        {
            if (_disposed)
            {
                return;
            }
            bool found = CountCore();
            TimeSpan next = TimeSpan.FromSeconds(_settings().TeamsMentionCountSeconds);
            if (!_firstDone)
            {
                _firstDone = true;
                if (!found)
                {
                    next = _firstRetryDelay;
                }
            }
            if (!_disposed)
            {
                _timer.Change(next, Timeout.InfiniteTimeSpan);
            }
        }

        private bool CountCore()
        {
            lock (_countLock)
            {
                try
                {
                    foreach (TeamsWindow window in WindowFocus.EnumerateTeamsWindows().Where(w => w.Rect.Width > 600))
                    {
                        AutomationElement root = AutomationElement.FromHandle(window.Handle);
                        AutomationElementCollection items = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
                        foreach (AutomationElement item in items)
                        {
                            int? value;
                            try
                            {
                                value = ParseMentionItemName(item.Current.Name);
                            }
                            catch (ElementNotAvailableException)
                            {
                                continue;
                            }
                            if (value != null)
                            {
                                Count = value;
                                CountedAt = DateTime.Now;
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Teams のメンション数を数えられませんでした: " + ex.GetType().Name);
                }
                return false;
            }
        }
    }
}
