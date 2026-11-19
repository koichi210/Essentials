using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cheetos.Tests
{
    /// <summary>
    /// CaptureRunner(CaptureWindowタブの一連の処理と自動復旧)のうち、画面やマウスを使わずに確かめられる所のテスト。
    /// WinForms版とWPF版で同じファイル。
    /// </summary>
    [TestClass]
    public class CaptureRunnerTests
    {
        [TestMethod]
        public void GetResumePositionは止まった行の次の行から続ける()
        {
            CaptureRunner.ResumePosition pos = CaptureRunner.GetResumePosition(1, 0, 3, 2, 1, false);

            Assert.AreEqual(1, pos.Loop);
            Assert.AreEqual(1, pos.Row);
            Assert.AreEqual(1, pos.FileIdx, "キャプチャしない行なら撮影番号は進めない");
            Assert.IsFalse(pos.IsLoopCompleted);
            Assert.IsFalse(pos.IsFinished);
        }

        [TestMethod]
        public void GetResumePositionはキャプチャする行で止まったら撮影番号を1つ進める()
        {
            CaptureRunner.ResumePosition pos = CaptureRunner.GetResumePosition(2, 1, 3, 2, 4, true);

            Assert.AreEqual(2, pos.Loop);
            Assert.AreEqual(2, pos.Row);
            Assert.AreEqual(5, pos.FileIdx);
        }

        [TestMethod]
        public void GetResumePositionは最後の行で止まったら次の繰り返しの先頭から続ける()
        {
            CaptureRunner.ResumePosition pos = CaptureRunner.GetResumePosition(1, 2, 3, 2, 3, true);

            Assert.AreEqual(2, pos.Loop);
            Assert.AreEqual(0, pos.Row);
            Assert.AreEqual(1, pos.FileIdx, "撮影番号は繰り返しごとに1から");
            Assert.IsTrue(pos.IsLoopCompleted);
            Assert.IsFalse(pos.IsFinished);
        }

        [TestMethod]
        public void GetResumePositionは最後の繰り返しの最後の行なら終わり()
        {
            CaptureRunner.ResumePosition pos = CaptureRunner.GetResumePosition(2, 2, 3, 2, 3, false);

            Assert.IsTrue(pos.IsLoopCompleted);
            Assert.IsTrue(pos.IsFinished);
        }

        [TestMethod]
        public void GetResumePositionは開始前のSleepで止まったら先頭の行から続ける()
        {
            CaptureRunner.ResumePosition pos = CaptureRunner.GetResumePosition(1, -1, 3, 2, 1, false);

            Assert.AreEqual(1, pos.Loop);
            Assert.AreEqual(0, pos.Row);
            Assert.AreEqual(1, pos.FileIdx);
            Assert.IsFalse(pos.IsFinished);
        }

        [TestMethod]
        public void CheckWatchdogは動いていなければ何もしない()
        {
            CaptureRunner runner = new CaptureRunner { WatchdogSeconds = 1 };

            Assert.IsNull(runner.CheckWatchdog(DateTime.Now.AddHours(1)));
            Assert.IsFalse(runner.IsRunning);
        }

        [TestMethod]
        public void CheckWatchdogは予定時刻を過ぎても次の行が始まらなければ次の行から続ける()
        {
            // マウス操作もキャプチャもしない、1時間待つだけの行が2行。1行目で止まったことにする
            using (var done = new System.Threading.ManualResetEvent(false))
            {
                String log = null;
                int completedLoop = 0;
                CaptureRunner runner = new CaptureRunner
                {
                    FilePathPrefix = @"C:\never_used_",
                    LoopCount = 1,
                    Steps = new List<CaptureRunner.Step>
                    {
                        new CaptureRunner.Step { SleepMsec = "3600000" },
                        new CaptureRunner.Step { SleepMsec = "3600000" },
                    },
                    WatchdogSeconds = 10,
                };
                runner.LoopCompleted = loop => completedLoop = loop;
                runner.Completed = errLog =>
                {
                    log = errLog;
                    done.Set();
                };

                runner.Start();
                DateTime limit = DateTime.Now.AddSeconds(5);
                while (runner.NextEventTime == null && DateTime.Now < limit)
                {
                    System.Threading.Thread.Sleep(20);
                }
                Assert.IsNotNull(runner.NextEventTime, "1行目を始めたら予定時刻が出る");
                Assert.IsNull(runner.CheckWatchdog(DateTime.Now), "予定時刻の前は何もしない");

                // 1回目: 1行目で止まった → 2行目から続ける
                String first = runner.CheckWatchdog(DateTime.Now.AddHours(2));
                StringAssert.Contains(first, "1回目");
                Assert.IsTrue(runner.IsRunning);
                Assert.AreEqual(1, runner.RecoveryCount);

                limit = DateTime.Now.AddSeconds(5);
                while (runner.NextEventTime == null && DateTime.Now < limit)
                {
                    System.Threading.Thread.Sleep(20);
                }

                // 2回目: 最後の行で止まった → 続きが無いので終わる
                String second = runner.CheckWatchdog(DateTime.Now.AddHours(2));
                StringAssert.Contains(second, "2回目");
                Assert.IsTrue(done.WaitOne(5000));
                Assert.IsFalse(runner.IsRunning);
                Assert.AreEqual(1, completedLoop, "最後の行を飛ばして繰り返しが終わった扱い");
                StringAssert.Contains(log, "自動復旧");
            }
        }

        [TestMethod]
        public void 行が無ければすぐ終わってCompletedが呼ばれる()
        {
            using (var done = new System.Threading.ManualResetEvent(false))
            {
                String log = null;
                CaptureRunner runner = new CaptureRunner
                {
                    FilePathPrefix = @"C:\never_used_",
                    LoopCount = 2,
                    Steps = new List<CaptureRunner.Step>(),
                    WatchdogSeconds = 0,
                };
                runner.Completed = errLog =>
                {
                    log = errLog;
                    done.Set();
                };

                runner.Start();

                Assert.IsTrue(done.WaitOne(5000), "5秒以内に終わるはず");
                Assert.AreEqual("", log);
                Assert.IsFalse(runner.IsRunning);
                Assert.IsNull(runner.NextEventTime);
            }
        }
    }
}
