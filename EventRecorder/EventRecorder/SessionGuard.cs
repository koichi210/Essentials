using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace EventRecorder
{
    // 画面ロック・サインイン(セッション切替)の通知を受けた時に、記録/再生をどう扱うか
    internal enum SessionChange
    {
        // 何もしない
        None,

        // 画面ロック・ユーザー切替・リモート切断など、このセッションの画面から離れる時。
        // ロック中はSendInputがロック画面(別デスクトップ)に届かず、フックにも入力が来ないため、
        // 記録・再生を続けても意味が無い(再生はロック解除後に途中から勝手に動き出してしまう)
        Suspend,

        // ロック解除・サインインなど、このセッションの画面に戻ってきた時
        Resume,
    }

    // セッション切替・シャットダウン時の扱いのうち、画面(Form)に依存しない判定と待機処理。
    // MainWindowから使う(WinForms版EventRecorderと同じ内容)。単体テストできるよう、OSの通知やフック・SendInputには一切触らない
    internal static class SessionGuard
    {
        // シャットダウン・ログオフ・アプリ終了時に、再生スレッドの停止を待つ最大時間(ms)。
        // 再生スレッドは50ms刻みで停止要求を見ているので通常は一瞬で止まる。
        // Windowsは終了処理で応答の無いアプリを数秒で「終了を妨げているアプリ」扱いにするため、それより短くしておく
        public const int ExitWaitTimeoutMs = 3000;

        // SystemEvents.SessionSwitchの理由を、記録/再生への扱いに振り分ける
        public static SessionChange Classify(SessionSwitchReason reason)
        {
            switch (reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.SessionLogoff:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    return SessionChange.Suspend;

                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                    return SessionChange.Resume;

                default:
                    return SessionChange.None;
            }
        }

        // isDoneがtrueになるまで、pump(UIスレッドのメッセージ処理)を回しながら待つ。
        // 再生スレッドは画面更新のためにUIスレッドへInvokeしてくるので、UIスレッドで単純にWait()すると
        // お互いを待ち合ってデッドロックする。メッセージを処理しながら待つことでInvokeを通してやる。
        // timeoutMs以内に終わればtrue、時間切れならfalse
        public static Boolean WaitWhilePumping(Func<Boolean> isDone, Action pump, int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!isDone())
            {
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return false;
                }

                pump();
                Thread.Sleep(10);
            }

            return true;
        }
    }
}
