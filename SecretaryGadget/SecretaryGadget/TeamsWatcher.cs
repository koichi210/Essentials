using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace SecretaryGadget
{
    /// <summary>
    /// 予備の通知センター監視(既定オフ。UIA と同じくメモリが残るため)。
    /// UserNotificationListener.Current.RequestAccessAsync() が Allowed のとき、起動時の通知は既読扱い、
    /// NotificationChanged を購読(パッケージIDが無いと例外→無視)＋1秒ごとのポーリング。
    /// AUMID に MSTeams を含むトーストの ToastGeneric テキストを MentionFilter に掛ける。
    /// </summary>
    internal sealed class TeamsWatcher : IDisposable
    {
        private readonly Func<Settings> _settings;
        private readonly Action<Notice> _onMention;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly HashSet<uint> _seen = new HashSet<uint>();
        private readonly SemaphoreSlim _checkGate = new SemaphoreSlim(1, 1);
        private UserNotificationListener? _listener;

        public TeamsWatcher(Func<Settings> settings, Action<Notice> onMention)
        {
            _settings = settings;
            _onMention = onMention;
        }

        /// <summary>UI スレッドから呼ぶ(許可の問い合わせがあるため)。</summary>
        public async Task StartAsync()
        {
            try
            {
                UserNotificationListener listener = UserNotificationListener.Current;
                UserNotificationListenerAccessStatus status = await listener.RequestAccessAsync();
                if (status != UserNotificationListenerAccessStatus.Allowed)
                {
                    Log.Write("通知センターへのアクセスが許可されていないため監視しません");
                    return;
                }
                _listener = listener;
                foreach (UserNotification n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
                {
                    _seen.Add(n.Id);
                }
                try
                {
                    listener.NotificationChanged += (_, _) => _ = CheckAsync();
                }
                catch (Exception)
                {
                    // パッケージIDが無いと購読できない。ポーリングだけで続ける
                }
                _ = Task.Run(() => PollLoopAsync(_cts.Token));
                Log.Write("通知センターの監視を開始しました");
            }
            catch (Exception ex)
            {
                Log.Write("通知センターの監視を開始できませんでした: " + ex.GetType().Name);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings().TeamsPollSeconds), token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                await CheckAsync().ConfigureAwait(false);
            }
        }

        private async Task CheckAsync()
        {
            if (_listener == null || !await _checkGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
            try
            {
                IReadOnlyList<UserNotification> list = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().ConfigureAwait(false);
                foreach (UserNotification n in list)
                {
                    if (!_seen.Add(n.Id))
                    {
                        continue;
                    }
                    string aumid = n.AppInfo?.AppUserModelId ?? string.Empty;
                    if (aumid.IndexOf("MSTeams", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    NotificationBinding? binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (binding == null)
                    {
                        continue;
                    }
                    List<string> texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (!MentionFilter.IsMention(texts, _settings().TeamsMentionKeywords))
                    {
                        continue;
                    }
                    _onMention(new Notice(NoticeKind.Teams, "Teams でメンションされました", texts.FirstOrDefault(), texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : null)
                    {
                        Open = WindowFocus.FocusTeamsMain,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Write("通知センターの読み取りに失敗しました: " + ex.GetType().Name);
            }
            finally
            {
                _checkGate.Release();
            }
        }
    }
}
