using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using StandardTemplate;
using Forms = System.Windows.Forms;

namespace Cheetos
{
    // CaptureWindow
    public partial class MainWindow
    {
        // CurrentScreenキャプチャで「このウィンドウが今あるモニタ」を判定するための目印(CaptWindow.TargetWindowに渡す)
        private Forms.Control _captureTargetWindow;

        // マウス座標はWinForms版と同じくCursor.Position(スクリーン座標)を表示する。
        // DataGridのMouseX/MouseYにそのまま書き写して使う値なので、CaptWindow(SetCursorPos)と同じ座標系にしておく
        private void tabPageCaptureWindow_MouseMove(object sender, MouseEventArgs e)
        {
            System.Drawing.Point pos = Forms.Cursor.Position;
            cw_TextBox_Status.Text = "X=" + pos.X.ToString() + ", Y=" + pos.Y.ToString();
        }

        // ドラッグして離した位置の座標を、表の選択中の行のMouseX/MouseYに入れる。
        // タブの余白か座標の表示欄でマウスを押して始め、押している間はマウスを捕まえておくので、
        // このウィンドウの外(座標を取りたい別のアプリの上)で離しても拾える
        private Boolean _isCoordinateDragging = false;
        private System.Drawing.Point _coordinateDragStart;

        private void tabPageCaptureWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 子のコントロール(表やボタン)の上で押したときは始めない
            if (e.OriginalSource == sender)
            {
                StartCoordinateDrag(e);
            }
        }

        private void cw_TextBox_Status_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartCoordinateDrag(e);
        }

        private void StartCoordinateDrag(MouseButtonEventArgs e)
        {
            if (_isCaptureRunning)
            {
                return;
            }
            _coordinateDragStart = Forms.Cursor.Position;
            _isCoordinateDragging = cw_Grid.CaptureMouse();
            if (_isCoordinateDragging)
            {
                Mouse.OverrideCursor = Cursors.Cross;
                e.Handled = true;
            }
        }

        private void tabPageCaptureWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isCoordinateDragging)
            {
                return;
            }
            System.Drawing.Point pos = Forms.Cursor.Position;
            EndCoordinateDrag();

            // 押しただけ(ほとんど動かしていない)のときは何もしない
            if (Math.Abs(pos.X - _coordinateDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _coordinateDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            SetCurrentRowMousePoint(pos);
        }

        private void tabPageCaptureWindow_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndCoordinateDrag();
        }

        private void EndCoordinateDrag()
        {
            if (!_isCoordinateDragging)
            {
                return;
            }
            _isCoordinateDragging = false;
            Mouse.OverrideCursor = null;
            if (cw_Grid.IsMouseCaptured)
            {
                cw_Grid.ReleaseMouseCapture();
            }
        }

        private void SetCurrentRowMousePoint(System.Drawing.Point pos)
        {
            CommitGridEdit();
            int rowIdx = GetCurrentGridRowIndex();
            if (rowIdx >= _cwRows.Count)
            {
                return;
            }
            _cwRows[rowIdx].MouseX = pos.X.ToString();
            _cwRows[rowIdx].MouseY = pos.Y.ToString();
            cw_TextBox_Status.Text = "X=" + pos.X + ", Y=" + pos.Y + " →" + (rowIdx + 1) + "行目";
        }

        // Captureボタンを押した瞬間のマウス座標。一連のCapture処理が終わったらここへ戻す
        private System.Drawing.Point _captureStartCursorPosition;

        // 次の行の予定時刻をこの時間過ぎても始まっていなければ、赤字で知らせる
        // (キャプチャはクリップボードへ入るのを最大2秒待つので、その分は余裕を見る)
        private static readonly TimeSpan _captureOverdueGrace = TimeSpan.FromSeconds(5);

        private CaptureRunner _captureRunner;
        private DispatcherTimer _captureMonitorTimer;
        private Boolean _isOverdueWarningShown = false;

        private void Button_Capture_Click(object sender, RoutedEventArgs e)
        {
            // 実行中だったら停止する(他タブのBackgroundWorkerと同じ「もう一度押すと中断」。
            // _isCaptureRunningは終了処理(FinishCapture)が終わるまでtrueのままにしておく(ここでfalseにすると、
            // 終了処理が終わる前にもう一度押されたとき二重起動してしまう)
            if (_isCaptureRunning)
            {
                if (_captureRunner != null)
                {
                    _captureRunner.Stop();
                }
                return;
            }

            CommitGridEdit();

            // 空欄なら1回。数値以外はint.Parseで例外になっていたので先に確かめる
            int loopCount = 1;
            if (cw_TextBox_Loop.Text != String.Empty
                && !TryGetIntWithMessage(cw_TextBox_Loop, "繰り返し数", 1, out loopCount))
            {
                return;
            }

            // 空欄か0なら自動復旧しない
            int watchdogSeconds = 0;
            if (cw_TextBox_Watchdog.Text != String.Empty
                && !TryGetIntWithMessage(cw_TextBox_Watchdog, "自動復旧(秒)", 0, out watchdogSeconds))
            {
                return;
            }

            _fio.EnsureDirectory(cw_TextBox_SavePath.Text, true);
            String filePathPrefix = Logic.BuildFilePathPrefix(cw_TextBox_SavePath.Text, cw_TextBox_SaveFilePrefix.Text, cw_checkBox_AddTimeStamp.IsChecked == true);

            // キャプチャ対象を設定。以降はバックグラウンドスレッドで動くため、
            // コントロール/コレクションの値はここ(UIスレッド)で読み取っておく
            CaptWindow.CaptureTargetType captureTarget;
            if (cw_Radio_FullScreen.IsChecked == true)
            {
                captureTarget = CaptWindow.CaptureTargetType.FullScreen;
            }
            else if (cw_Radio_CurrentScreen.IsChecked == true)
            {
                captureTarget = CaptWindow.CaptureTargetType.CurrentScreen;
            }
            else // cw_Radio_CurrentWindow
            {
                captureTarget = CaptWindow.CaptureTargetType.CurrentWindow;
            }
            List<CaptureRunner.Step> steps = BuildCaptureSteps();

            // 保存先に同じ名前の画像があれば、上書きしてよいか先に聞く
            // (途中で聞くと、無人で回している間そこで止まってしまうため、始める前に1回だけ聞く)
            if (!ConfirmOverwriteCaptureFiles(filePathPrefix, loopCount, steps.Count(step => step.IsCapture)))
            {
                return;
            }

            _captureStartCursorPosition = Forms.Cursor.Position;
            InitProgressBar(loopCount);
            SetStartTime();
            textBox_NextEventTime.Text = "";
            ShowCaptureWarning("");

            CaptureRunner runner = new CaptureRunner
            {
                FilePathPrefix = filePathPrefix,
                LoopCount = loopCount,
                CaptureTarget = captureTarget,
                InitialSleepMsec = cw_TextBox_Sleep.Text,
                Steps = steps,
                TargetWindow = _captureTargetWindow,
                WatchdogSeconds = watchdogSeconds,
            };
            runner.LoopCompleted = doneLoop => Dispatcher.Invoke((Action)(() =>
            {
                if (textBox_ExpectEndTime.Text == "")
                {
                    SetExpectEndTime(loopCount, doneLoop);
                }
                UpdateProgressBar();
            }));
            runner.Completed = errLog => Dispatcher.Invoke((Action)(() => FinishCapture(errLog, null)));
            runner.Failed = ex => Dispatcher.Invoke((Action)(() => FinishCapture(null, ex)));
            runner.Log = text => _debug.WriteData(text);

            _debug.WriteData("Capture: START", false);
            _captureRunner = runner;
            _isCaptureRunning = true;
            cw_Button_Capture.Content = "中断";
            runner.Start();
            StartCaptureMonitor();
        }

        // 表の各行を、処理用スレッドへ渡す形にする
        private List<CaptureRunner.Step> BuildCaptureSteps()
        {
            int sleepIdx = GetDataGridColumnIdx(_gridHeaderSleepStr);
            int mouseXIdx = GetDataGridColumnIdx(_gridHeaderMouseXStr);
            int mouseYIdx = GetDataGridColumnIdx(_gridHeaderMouseYStr);
            int mouseActionIdx = GetDataGridColumnIdx(_gridHeaderMouseActionStr);
            int captureIdx = GetDataGridColumnIdx(_gridHeaderCaptureStr);

            return _cwRows.Select(row => new CaptureRunner.Step
            {
                SleepMsec = row[sleepIdx],
                MouseX = row[mouseXIdx],
                MouseY = row[mouseYIdx],
                MouseEvent = GetMouseEvent(row[mouseActionIdx]),
                IsCapture = IsCaptureEvent(row[captureIdx]),
            }).ToList();
        }

        // これから保存する画像と同じ名前のファイルが既にあれば、上書きしてよいか聞く。
        // 無ければ聞かずにtrue、「いいえ」ならfalse(Captureを始めない)
        private Boolean ConfirmOverwriteCaptureFiles(String filePathPrefix, int loopCount, int captureRowCount)
        {
            List<String> existingFiles = Logic.BuildCaptureFilePaths(filePathPrefix, loopCount, captureRowCount)
                .Where(File.Exists)
                .ToList();
            if (existingFiles.Count == 0)
            {
                return true;
            }

            const int maxNamesInMessage = 5;
            String names = String.Join(Environment.NewLine, existingFiles.Take(maxNamesInMessage).Select(Path.GetFileName));
            if (existingFiles.Count > maxNamesInMessage)
            {
                names += Environment.NewLine + "ほか" + (existingFiles.Count - maxNamesInMessage) + "件";
            }
            MessageBoxResult result = MessageBox.Show(
                "保存先に同じ名前の画像が" + existingFiles.Count + "件あります。上書きしてよいですか？" + Environment.NewLine + names,
                "上書きの確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }

        // 一連のCaptureが終わった(中断・エラー・自動復旧の上限も含む)ときにUIスレッドで呼ぶ
        private void FinishCapture(String errLog, Exception error)
        {
            StopCaptureMonitor();
            _isCaptureRunning = false;
            cw_Button_Capture.Content = "Capture";
            textBox_NextEventTime.Text = "";
            if (_isOverdueWarningShown)
            {
                ShowCaptureWarning("");
            }

            // Capture処理が全て終わったら、ボタンを押した時のマウス座標へ戻す
            Forms.Cursor.Position = _captureStartCursorPosition;

            if (error != null)
            {
                MessageBox.Show("キャプチャ中にエラーが発生したよ" + Environment.NewLine + error.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TextBox_Status.Text += " 完了";
            if (!String.IsNullOrEmpty(errLog))
            {
                MessageBox.Show(errLog, "エラー", MessageBoxButton.OK);
            }
        }

        // 実行中は0.5秒ごとに、次の行の予定時刻の表示・遅れの赤字表示・自動復旧(WatchDog)を行う
        private void StartCaptureMonitor()
        {
            if (_captureMonitorTimer == null)
            {
                _captureMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _captureMonitorTimer.Tick += CaptureMonitorTimer_Tick;
            }
            _captureMonitorTimer.Start();
        }

        private void StopCaptureMonitor()
        {
            if (_captureMonitorTimer != null)
            {
                _captureMonitorTimer.Stop();
            }
        }

        private void CaptureMonitorTimer_Tick(object sender, EventArgs e)
        {
            CaptureRunner runner = _captureRunner;
            if (runner == null || !_isCaptureRunning)
            {
                return;
            }

            DateTime now = DateTime.Now;
            String recovered = runner.CheckWatchdog(now);
            if (recovered != null)
            {
                // 自動復旧したことは、次に遅れるか終わるまで残しておく
                ShowCaptureWarning(recovered);
                _isOverdueWarningShown = false;
            }

            DateTime? next = runner.NextEventTime;
            textBox_NextEventTime.Text = next.HasValue ? next.Value.ToString("HH:mm:ss") : "";

            if (next.HasValue && Logic.IsOverdue(now, next.Value, _captureOverdueGrace))
            {
                ShowCaptureWarning("予定(" + next.Value.ToString("HH:mm:ss") + ")を過ぎても次の操作が始まっていません");
                _isOverdueWarningShown = true;
            }
            else if (_isOverdueWarningShown)
            {
                ShowCaptureWarning("");
            }
        }

        // 処理状況の欄に赤字で出す(メッセージボックスは使わない。無人で回している時に止まってしまうため)
        private void ShowCaptureWarning(String message)
        {
            textBlock_CaptureWarning.Text = message;
            textBlock_CaptureWarning.ToolTip = (message == "") ? null : message;
            _isOverdueWarningShown = false;
        }

        private void cw_TextBox_SavePath_KeyUp(object sender, KeyEventArgs e)
        {
            pt_SourceFolderPath.Text = cw_TextBox_SavePath.Text;
            pr_SourceFolderPath.Text = cw_TextBox_SavePath.Text;
            do_SourceFolderPath.Text = cw_TextBox_SavePath.Text;
            do_DestPortFolderPath.Text = cw_TextBox_SavePath.Text + "_port";
            do_DestLandFolderPath.Text = cw_TextBox_SavePath.Text + "_land";
            pm_SourceFolderPath.Text = cw_TextBox_SavePath.Text;
            fc_SourceFolderPath.Text = cw_TextBox_SavePath.Text;
        }

        private void cw_TextBox_SavePath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _util.ExecutePath(cw_TextBox_SavePath.Text);
            }
        }

        // 進捗バーを1つ進める(最大値で止める)
        private void UpdateProgressBar()
        {
            ShowProgress((int)Math.Min(ProgressBar_Status.Value + 1, ProgressBar_Status.Maximum));
        }
    }
}
