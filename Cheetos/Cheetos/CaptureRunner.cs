// CaptureWindowタブの一連の処理(表の各行の「マウス操作→Sleep→キャプチャ」を繰り返し数だけ)を、
// 専用のSTAスレッドで回す(SendKeys/ClipboardはSTAスレッドでないと使えない)。
// WinForms版(CS_Form\Cheetos)とWPF版(stable\Essentials\Cheetos)で同じファイルを使い、
// 画面への反映は呼び出し側のコールバックの中でUIスレッドへ戻してもらう。
//
// ■次の操作の予定時刻と見張り(WatchDog)
// 各行を始めるたびに「その行のSleepが終わる=次の行を始める予定の時刻」を記録する(NextEventTime)。
// 画面側はタイマーで定期的にCheckWatchdogを呼ぶ。予定時刻をWatchdogSeconds秒過ぎても次の行が
// 始まっていなければ、止まった行を飛ばして次の行から新しいスレッドでやり直す。
// 止まったスレッドは.NETでは安全に止めさせられないので、中断要求だけ出して見捨てる
// (後で動き出しても、世代番号(_generation)が古いので画面にも状態にも触らずに終わる)。
// やり直しはMaxRecoveryCount回まで。それを超えたら処理を終える。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StandardTemplate;

namespace Cheetos
{
    internal sealed class CaptureRunner
    {
        // 表の1行分
        internal sealed class Step
        {
            public String SleepMsec = "";
            public String MouseX = "";
            public String MouseY = "";
            public CaptWindow.MouseEventType MouseEvent = CaptWindow.MouseEventType.LeftClick;
            public Boolean IsCapture;
        }

        // 止まった行の次から再開する位置
        internal sealed class ResumePosition
        {
            public int Loop;                    // 繰り返しの何回目か(1始まり)
            public int Row;                     // 表の何行目か(0始まり)
            public int FileIdx;                 // 次に撮る画像の撮影番号
            public Boolean IsLoopCompleted;     // 止まった行がその繰り返しの最後の行だった(その繰り返しは終わった扱い)
            public Boolean IsFinished;          // もう続きが無い
        }

        public const int MaxRecoveryCount = 5;

        // ---- Startの前に設定する ----
        public String FilePathPrefix = "";
        public int LoopCount = 1;
        public CaptWindow.CaptureTargetType CaptureTarget = CaptWindow.CaptureTargetType.FullScreen;
        public String InitialSleepMsec = "";
        public List<Step> Steps = new List<Step>();
        public Control TargetWindow;
        public int WatchdogSeconds;     // 0以下なら自動復旧しない

        // ---- コールバック(処理用スレッドかUIスレッドから呼ばれる。UIへの反映は呼び出し側でUIスレッドへ戻す) ----
        public Action<int> LoopCompleted;       // 引数: 終わった繰り返しの番号(1始まり)
        public Action<String> Completed;        // 引数: エラーログ(撮れなかったファイル・自動復旧の記録。無ければ空)
        public Action<Exception> Failed;
        public Action<String> Log;

        private readonly object _lock = new object();
        private int _generation;
        private CaptWindow _cw;
        private Boolean _isFinished = true;

        // 今やっている行(見張り・再開位置の計算用)
        private int _loop;
        private int _row;
        private int _fileIdxAtRowStart;
        private Boolean _rowIsCapture;
        private TimeSpan _rowSleepTime;
        private DateTime? _nextEventTime;

        private int _recoveryCount;
        private readonly StringBuilder _abandonedErrorLog = new StringBuilder();
        private readonly StringBuilder _recoveryLog = new StringBuilder();

        public Boolean IsRunning
        {
            get { lock (_lock) { return !_isFinished; } }
        }

        // 次の行を始める予定の時刻(Sleepが終わる時刻)。動いていない・最後の行の後はnull
        public DateTime? NextEventTime
        {
            get { lock (_lock) { return _nextEventTime; } }
        }

        public int RecoveryCount
        {
            get { lock (_lock) { return _recoveryCount; } }
        }

        public void Start()
        {
            int gen;
            CaptWindow cw;
            lock (_lock)
            {
                _generation++;
                gen = _generation;
                _isFinished = false;
                _recoveryCount = 0;
                _nextEventTime = null;
                _abandonedErrorLog.Length = 0;
                _recoveryLog.Length = 0;
                cw = CreateCaptWindow();
                _cw = cw;
            }
            StartThread(gen, cw, 1, -1, 1, TimeSpan.Zero);
        }

        // 中断要求(今の行の操作が終わったところで止まる)
        public void Stop()
        {
            lock (_lock)
            {
                if (_cw != null)
                {
                    _cw.Stop();
                }
            }
        }

        /// <summary>
        /// UIスレッドのタイマーから定期的に呼ぶ。予定時刻をWatchdogSeconds秒過ぎても次の行が始まっていなければ
        /// 次の行から再開し(上限を超えたら終了し)、その内容を返す。何もしなかったらnull。
        /// </summary>
        public String CheckWatchdog(DateTime now)
        {
            int gen;
            CaptWindow newCw = null;
            ResumePosition pos = null;
            TimeSpan inheritedSleep;
            String message;
            String errLog = null;
            lock (_lock)
            {
                if (_isFinished || WatchdogSeconds <= 0 || _nextEventTime == null
                    || !Logic.IsOverdue(now, _nextEventTime.Value, TimeSpan.FromSeconds(WatchdogSeconds)))
                {
                    return null;
                }

                // 止まっているスレッドを見捨てる
                _cw.Stop();
                _abandonedErrorLog.Append(_cw.GetErrorLog());
                _generation++;
                gen = _generation;
                _nextEventTime = null;
                inheritedSleep = _rowSleepTime;
                String where = DescribePosition(_loop, _row);

                if (_recoveryCount >= MaxRecoveryCount)
                {
                    message = "自動復旧の上限(" + MaxRecoveryCount + "回)に達したので止めました(" + where + "で止まっていました)";
                    _recoveryLog.AppendLine(message);
                    _isFinished = true;
                    errLog = BuildErrorLog(null);
                }
                else
                {
                    _recoveryCount++;
                    pos = GetResumePosition(_loop, _row, Steps.Count, LoopCount, _fileIdxAtRowStart, _rowIsCapture);
                    message = "自動復旧(" + _recoveryCount + "回目): " + where + "で止まっていたので、次の行から続けました";
                    _recoveryLog.AppendLine(now.ToString("HH:mm:ss") + " " + message);
                    if (pos.IsFinished)
                    {
                        _isFinished = true;
                        errLog = BuildErrorLog(null);
                    }
                    else
                    {
                        newCw = CreateCaptWindow();
                        _cw = newCw;
                    }
                }
            }

            WriteLog("Capture: Watchdog " + message);
            if (pos != null && pos.IsLoopCompleted && LoopCompleted != null)
            {
                LoopCompleted(pos.Loop - 1);
            }
            if (newCw != null)
            {
                StartThread(gen, newCw, pos.Loop, pos.Row, pos.FileIdx, inheritedSleep);
            }
            else if (Completed != null)
            {
                Completed(errLog);
            }
            return message;
        }

        /// <summary>
        /// loop回目のrow行目(-1は開始前のSleep)で止まったときに、再開する位置を求める。
        /// 止まった行は飛ばし、撮影番号はその行を撮ったことにして(キャプチャする行なら1つ進めて)続ける。
        /// </summary>
        public static ResumePosition GetResumePosition(int loop, int row, int rowCount, int loopCount, int fileIdxAtRowStart, Boolean rowIsCapture)
        {
            ResumePosition pos = new ResumePosition
            {
                Loop = loop,
                Row = row + 1,
                FileIdx = fileIdxAtRowStart + (rowIsCapture ? 1 : 0),
            };
            if (pos.Row >= rowCount)
            {
                pos.Loop = loop + 1;
                pos.Row = 0;
                pos.FileIdx = 1;
                pos.IsLoopCompleted = true;
            }
            pos.IsFinished = pos.Loop > loopCount;
            return pos;
        }

        private static String DescribePosition(int loop, int row)
        {
            return (row < 0) ? "開始前のSleep" : loop + "回目の" + (row + 1) + "行目";
        }

        private CaptWindow CreateCaptWindow()
        {
            return new CaptWindow { TargetWindow = TargetWindow };
        }

        private void StartThread(int gen, CaptWindow cw, int startLoop, int startRow, int startFileIdx, TimeSpan inheritedSleep)
        {
            Thread thread = new Thread(() => Run(gen, cw, startLoop, startRow, startFileIdx, inheritedSleep));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Run(int gen, CaptWindow cw, int startLoop, int startRow, int startFileIdx, TimeSpan inheritedSleep)
        {
            try
            {
                cw.Initialize();
                cw.SetRestoreMousePosition(false);
                cw.SetCaptureTarget(CaptureTarget);

                // いまのところマウス移動しないユースケースは無い
                cw.SetMouseMove(true);

                // Sleep列が空欄の行は直前の待ち時間を引き継ぐので、やり直した時は止まった行の待ち時間から始める
                cw.SleepTime = inheritedSleep;

                int firstRow = startRow;
                if (firstRow < 0)
                {
                    // 実行前のSleep
                    cw.SetSleepTimeMsec(InitialSleepMsec);
                    if (!MarkStepStart(gen, startLoop, -1, startFileIdx, false, cw))
                    {
                        return;
                    }
                    cw.ExecuteSleep();
                    firstRow = 0;
                }

                WriteLog("Capture: START loop=" + startLoop + " row=" + firstRow);
                for (int i = startLoop; i <= LoopCount && !cw.IsStopRequest; i++)
                {
                    WriteLog("Capture: Loop=" + i + "/" + LoopCount);

                    // ファイルのIndex番号(繰り返しごとに1から)とファイル名
                    cw.SetFileIdx((i == startLoop) ? startFileIdx : 1);
                    cw.SetFileFormat(FilePathPrefix + i.ToString("D4"));

                    for (int j = (i == startLoop) ? firstRow : 0; j < Steps.Count && !cw.IsStopRequest; j++)
                    {
                        Step step = Steps[j];
                        WriteLog(" Capture: RowCnt=" + j + "/" + Steps.Count);

                        // 予定時刻を出すため、待ち時間はマウス操作の前に決めておく(設定するだけなので順番を変えても動きは同じ)
                        cw.SetSleepTimeMsec(step.SleepMsec);
                        if (!MarkStepStart(gen, i, j, cw.FileIdx, step.IsCapture, cw))
                        {
                            return;
                        }

                        cw.SetCaptureCase(step.IsCapture);
                        if (cw.SetMousePoint(step.MouseX, step.MouseY))
                        {
                            cw.SetMouseEvent(step.MouseEvent);
                            cw.MouseProc();
                        }

                        cw.ExecuteSleep();
                        cw.CaptureProc();
                    }

                    if (cw.IsStopRequest || !IsCurrent(gen))
                    {
                        break;
                    }
                    if (LoopCompleted != null)
                    {
                        LoopCompleted(i);
                    }
                }
                WriteLog("Capture: END" + Environment.NewLine);

                String errLog;
                if (TryFinish(gen, cw, out errLog) && Completed != null)
                {
                    Completed(errLog);
                }
            }
            catch (Exception ex)
            {
                String errLog;
                if (TryFinish(gen, cw, out errLog) && Failed != null)
                {
                    Failed(ex);
                }
            }
        }

        // 行を始める時に呼ぶ。見捨てられた(世代が古い)スレッドならfalse
        private Boolean MarkStepStart(int gen, int loop, int row, int fileIdx, Boolean isCapture, CaptWindow cw)
        {
            lock (_lock)
            {
                if (gen != _generation || _isFinished)
                {
                    return false;
                }
                _loop = loop;
                _row = row;
                _fileIdxAtRowStart = fileIdx;
                _rowIsCapture = isCapture;
                _rowSleepTime = cw.SleepTime;
                _nextEventTime = Logic.CalcSleepEndTime(DateTime.Now, cw.SleepTime, cw.SleepCycle);
                return true;
            }
        }

        private Boolean IsCurrent(int gen)
        {
            lock (_lock)
            {
                return gen == _generation && !_isFinished;
            }
        }

        // 今の世代のスレッドなら終了状態にしてtrue(エラーログを返す)。見捨てられたスレッドならfalse
        private Boolean TryFinish(int gen, CaptWindow cw, out String errLog)
        {
            lock (_lock)
            {
                errLog = null;
                if (gen != _generation || _isFinished)
                {
                    return false;
                }
                _isFinished = true;
                _nextEventTime = null;
                errLog = BuildErrorLog(cw);
                return true;
            }
        }

        // _lockの中で呼ぶ
        private String BuildErrorLog(CaptWindow cw)
        {
            return _abandonedErrorLog.ToString() + ((cw == null) ? "" : cw.GetErrorLog()) + _recoveryLog.ToString();
        }

        private void WriteLog(String text)
        {
            if (Log != null)
            {
                Log(text);
            }
        }
    }
}
