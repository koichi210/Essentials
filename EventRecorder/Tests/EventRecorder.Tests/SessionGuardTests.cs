using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using StandardTemplate.Wpf.Tests;
using Keys = System.Windows.Forms.Keys;
using Stroke = GlobalHook.KeyboardHook.Stroke;

namespace EventRecorder.Tests
{
    /// <summary>
    /// 画面ロック・サインイン・シャットダウン時の扱いのテスト。
    /// 実際にロックやシャットダウンはできないので、OSの通知を受けた後の処理(SessionGuardの判定と、
    /// MainWindowのHandleSessionChange/PrepareForExit)を直接呼んで確かめる。
    /// テスト用のコンストラクタはフックを張らず、SendInputも呼ばない
    /// </summary>
    [TestClass]
    public class SessionGuardTests
    {
        private TempFolder temp;

        [TestInitialize]
        public void SetUp()
        {
            temp = new TempFolder();
        }

        [TestCleanup]
        public void TearDown()
        {
            temp.Dispose();
        }

        [TestMethod]
        public void 画面ロックやユーザー切替やリモート切断は記録再生を止める側に振り分ける()
        {
            Assert.AreEqual(SessionChange.Suspend, SessionGuard.Classify(SessionSwitchReason.SessionLock));
            Assert.AreEqual(SessionChange.Suspend, SessionGuard.Classify(SessionSwitchReason.SessionLogoff));
            Assert.AreEqual(SessionChange.Suspend, SessionGuard.Classify(SessionSwitchReason.ConsoleDisconnect));
            Assert.AreEqual(SessionChange.Suspend, SessionGuard.Classify(SessionSwitchReason.RemoteDisconnect));
        }

        [TestMethod]
        public void ロック解除やサインインは立て直す側に振り分ける()
        {
            Assert.AreEqual(SessionChange.Resume, SessionGuard.Classify(SessionSwitchReason.SessionUnlock));
            Assert.AreEqual(SessionChange.Resume, SessionGuard.Classify(SessionSwitchReason.SessionLogon));
            Assert.AreEqual(SessionChange.Resume, SessionGuard.Classify(SessionSwitchReason.ConsoleConnect));
            Assert.AreEqual(SessionChange.Resume, SessionGuard.Classify(SessionSwitchReason.RemoteConnect));
            Assert.AreEqual(SessionChange.None, SessionGuard.Classify(SessionSwitchReason.SessionRemoteControl));
        }

        [TestMethod]
        public void 待機は終わっていればすぐtrueで終わらなければ時間切れでfalse()
        {
            Assert.IsTrue(SessionGuard.WaitWhilePumping(() => true, () => { }, 1000));
            Assert.IsFalse(SessionGuard.WaitWhilePumping(() => false, () => { }, 100));
        }

        [TestMethod]
        public void 画面ロックで記録中の記録が止まり記録済みの行は残る()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(temp.Path);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);   // 記録開始
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F1, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.A, Keys.None);
                Assert.AreEqual("EventRecorder - 記録中", window.Title);

                window.HandleSessionChange(SessionChange.Suspend);

                Assert.AreEqual("EventRecorder", window.Title);
                Assert.AreEqual("記録", window.button_Record.Content);
                CollectionAssert.Contains(window.eventRows.Select(r => r.Type + ":" + r.Key).ToList(), "KEY_DOWN:A");

                // 止まった後のキー操作は記録しない
                int count = window.eventRows.Count;
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.B, Keys.None);
                Assert.AreEqual(count, window.eventRows.Count);
            });
        }

        [TestMethod]
        public void ロック解除でホットキーの押しっぱなし扱いが消え次の1回が無視されない()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(temp.Path);

                // F1を押した直後にロックされ、KeyUpを取りこぼした状態を作る
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);
                Assert.AreEqual("EventRecorder - 記録中", window.Title);

                window.HandleSessionChange(SessionChange.Resume);

                // 対策前は「F1がまだ押されている(リピート)」扱いで無視され、記録を止められなかった
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);
                Assert.AreEqual("EventRecorder", window.Title);
            });
        }

        [TestMethod]
        public void 終了処理は記録を止め以後はホットキーで記録も再生も始めない()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(temp.Path);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F1, Keys.None);
                Assert.AreEqual("EventRecorder - 記録中", window.Title);

                window.PrepareForExit();
                Assert.AreEqual("EventRecorder", window.Title);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F1, Keys.None);
                Assert.AreEqual("EventRecorder", window.Title, "終了処理中は記録を始めない");

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.IMEConvert, Keys.None);
                Assert.AreEqual("EventRecorder", window.Title, "終了処理中は再生を始めない");

                // 2回目(Closing→SessionEnding等で重ねて呼ばれた場合)は何もしない
                window.PrepareForExit();
            });
        }

        /// <summary>
        /// シャットダウン時に実際に起きる形の再現: 再生スレッドは停止要求を受けた後、後片付けとして
        /// UIスレッドへDispatcher.Invokeしてから終わる。終了処理がUIスレッドで単純に待つとお互いを待ち合って
        /// デッドロックする(時間切れまで固まり、再生スレッドは終わらない)。
        /// PrepareForExitがUIスレッドの処理を回しながら待つので、時間切れより十分早く再生スレッドが終わることを確かめる。
        /// 本物の再生(SendInput)は動かせないので、再生スレッドの代役のTaskを差し込む
        /// </summary>
        [TestMethod]
        public void 終了処理は再生スレッドに停止を伝えUIスレッドへの依頼を通しながら止まるまで待つ()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(temp.Path);
                Boolean uiUpdatedByWorker = false;

                SetField(window, "isPlaying", true);
                Task worker = Task.Run(() =>
                {
                    // 停止要求が来るまで「再生中」
                    while (!(Boolean)GetField(window, "stopPlayRequested"))
                    {
                        Thread.Sleep(5);
                    }

                    // 本物のPlayLoop/PlaylistPlayLoopのfinallyと同じく、UIスレッドで画面を戻してから終わる
                    window.InvokeOnUi(() => uiUpdatedByWorker = true);
                    SetField(window, "isPlaying", false);
                });
                SetField(window, "playbackTask", worker);

                DateTime start = DateTime.Now;
                window.PrepareForExit();
                TimeSpan elapsed = DateTime.Now - start;

                Assert.IsTrue(worker.IsCompleted, "再生スレッドは止まっている");
                Assert.IsTrue(uiUpdatedByWorker, "再生スレッドからのUIスレッドへの依頼も処理されている");
                Assert.IsTrue(elapsed.TotalMilliseconds < SessionGuard.ExitWaitTimeoutMs, "時間切れではなく、止まったので待ち終わった");
            });
        }

        private static void SetField(Object target, String name, Object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static Object GetField(Object target, String name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }
    }
}
