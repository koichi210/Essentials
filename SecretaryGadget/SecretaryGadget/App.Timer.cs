using System;
using System.Collections.Generic;
using System.IO;

namespace SecretaryGadget
{
    /// <summary>タイマー(時刻指定・何分後)。期限は Flush の3秒ごとの確認で拾い、時間になったら吹き出しを出しっぱなしにする。</summary>
    public partial class App
    {
        private readonly TimerKeeper _timers = new TimerKeeper(Path.Combine(Settings.LocalDir, "timers.json"));

        private void LoadTimers()
        {
            _timers.Load();
            if (_timers.Items.Count > 0)
            {
                Log.Write($"タイマーを {_timers.Items.Count} 件引き継ぎました");
            }
        }

        /// <summary>
        /// 吹き出しの選択肢「タイマー」。セット中のタイマーが無ければそのまま入力ダイアログ、
        /// あれば吹き出しで「セット」か「どれを取り消すか」を選んでもらう。
        /// </summary>
        private void ShowTimerMenu()
        {
            if (_timers.Items.Count == 0)
            {
                AskTimer();
                return;
            }
            var menu = new List<BubbleMenuItem> { new BubbleMenuItem("タイマーをセット…", AskTimer) };
            foreach (TimerEntry t in _timers.Items)
            {
                TimerEntry captured = t;
                string day = t.Due.Date == DateTime.Today ? string.Empty : "明日 ";
                menu.Add(new BubbleMenuItem($"{day}{t.Due:HH:mm}（{t.Label}）を取り消す", () => CancelTimer(captured)));
            }
            Enqueue(new Notice(NoticeKind.Greeting, $"タイマーは {_timers.Items.Count} 件セット中です", "どうしますか？") { Menu = menu });
        }

        /// <summary>タイマーをセットする。入力ダイアログで時刻か何分後かを聞く。</summary>
        private void AskTimer()
        {
            TimerEntry? entry = TimerDialog.Ask();
            if (entry == null)
            {
                return;
            }
            _timers.Add(entry);
            Log.Write($"タイマーをセット: {entry.Label}");
            Enqueue(NoticeTexts.TimerSet(DateTime.Now, entry));
        }

        private void CancelTimer(TimerEntry entry)
        {
            _timers.Remove(entry);
            Log.Write($"タイマーを取り消し: {entry.Label}");
            Enqueue(NoticeTexts.TimerCanceled(entry));
        }

        /// <summary>期限が来たタイマーを吹き出しのキューへ入れる。</summary>
        private void CheckTimers(DateTime now)
        {
            foreach (TimerEntry due in _timers.TakeDue(now))
            {
                Log.Write($"タイマーの時間: {due.Label}");
                Enqueue(NoticeTexts.TimerDue(due));
            }
        }
    }
}
