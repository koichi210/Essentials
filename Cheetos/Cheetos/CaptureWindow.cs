using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using StandardTemplate;
using Forms = System.Windows.Forms;

namespace Cheetos
{
    // CaptureWindow
    public partial class MainWindow
    {
        private readonly CaptWindow _cw = new CaptWindow();

        // マウス座標はWinForms版と同じくCursor.Position(スクリーン座標)を表示する。
        // DataGridのMouseX/MouseYにそのまま書き写して使う値なので、CaptWindow(SetCursorPos)と同じ座標系にしておく
        private void tabPageCaptureWindow_MouseMove(object sender, MouseEventArgs e)
        {
            System.Drawing.Point pos = Forms.Cursor.Position;
            cw_TextBox_Status.Text = "X=" + pos.X.ToString() + ", Y=" + pos.Y.ToString();
        }

        // Captureボタンを押した瞬間のマウス座標。一連のCapture処理が終わったらここへ戻す
        private System.Drawing.Point _captureStartCursorPosition;

        private void Button_Capture_Click(object sender, RoutedEventArgs e)
        {
            // 実行中だったら停止する(他タブのBackgroundWorkerと同じ「もう一度押すと中断」。
            // _isCaptureRunningはスレッドが終わるまでtrueのままにしておく(ここでfalseにすると、
            // スレッドの終了処理が終わる前にもう一度押されたとき二重起動してしまう)
            if (_isCaptureRunning)
            {
                _cw.Stop();
                return;
            }

            CommitGridEdit();
            _captureStartCursorPosition = Forms.Cursor.Position;

            _fio.EnsureDirectory(cw_TextBox_SavePath.Text, true);
            String filePathPrefix = Logic.BuildFilePathPrefix(cw_TextBox_SavePath.Text, cw_TextBox_SaveFilePrefix.Text, cw_checkBox_AddTimeStamp.IsChecked == true);

            int loopCount = 1;
            if (cw_TextBox_Loop.Text != String.Empty)
            {
                loopCount = int.Parse(cw_TextBox_Loop.Text);
            }
            InitProgressBar(loopCount);
            SetStartTime();

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
            String sleepMsecText = cw_TextBox_Sleep.Text;
            List<List<String>> gridRows = _cwRows.Select(row => row.ToList()).ToList();

            _isCaptureRunning = true;
            cw_Button_Capture.Content = "中断";

            CaptureForeground(filePathPrefix, loopCount, captureTarget, sleepMsecText, gridRows);
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

        // 以前はループ全体をDispatcher.Invoke(...)の中(UIスレッド同期実行)で回していたため、
        // ループ中UIスレッドが占有されっぱなしになり、「中断」のためのボタン再クリックが
        // ループ終了まで処理されなかった。(SendKeys/ClipboardはSTAスレッドが必要なため、専用の
        // STAスレッドでループを回し、WPFのコントロールに触る部分だけDispatcher.Invokeで
        // UIスレッドに戻す。UIスレッドはクリックを即座に処理できるので中断が効くようになる)
        private void CaptureForeground(String filePathPrefix, int loopCount, CaptWindow.CaptureTargetType captureTarget, String sleepMsecText, List<List<String>> gridRows)
        {
            Thread captureThread = new Thread(() =>
            {
                try
                {
                    // 初期化
                    _cw.Initialize();

                    // マウス移動後にもとの位置へ戻すか
                    _cw.SetRestoreMousePosition(false);

                    // 実行前のSleep
                    _cw.SetSleepTimeMsec(sleepMsecText);
                    _cw.ExecuteSleep();

                    _cw.SetCaptureTarget(captureTarget);

                    // 列の位置はループ中に変わらないので先に求めておく
                    int sleepIdx = GetDataGridColumnIdx(_gridHeaderSleepStr);
                    int mouseXIdx = GetDataGridColumnIdx(_gridHeaderMouseXStr);
                    int mouseYIdx = GetDataGridColumnIdx(_gridHeaderMouseYStr);
                    int mouseActionIdx = GetDataGridColumnIdx(_gridHeaderMouseActionStr);
                    int captureIdx = GetDataGridColumnIdx(_gridHeaderCaptureStr);

                    _debug.WriteData("Capture: START", false);
                    for (int i = 1; i <= loopCount && !_cw.IsStopRequest; i++)
                    {
                        _debug.WriteData("Capture: Loop=" + i.ToString() + "/" + loopCount.ToString());

                        // ファイルのIndex番号を初期化
                        _cw.SetFileIdx(1);

                        // ファイル名生成
                        String fileFormat = filePathPrefix + i.ToString("D4");
                        _cw.SetFileFormat(fileFormat);
                        _debug.WriteData(" Capture: Filename=" + fileFormat);

                        // いまのところマウス移動しないユースケースは無い
                        _cw.SetMouseMove(true);

                        // 順次Capture実行
                        for (int j = 0; j < gridRows.Count && !_cw.IsStopRequest; j++)
                        {
                            _debug.WriteData(" Capture: RowCnt=" + j.ToString() + "/" + gridRows.Count.ToString());
                            List<String> row = gridRows[j];

                            _cw.SetCaptureCase(IsCaptureEvent(row[captureIdx]));
                            _debug.WriteData("  Capture: SetCaptureCase() Done");

                            String pointX = row[mouseXIdx];
                            String pointY = row[mouseYIdx];

                            if (_cw.SetMousePoint(pointX, pointY))
                            {
                                _cw.SetMouseEvent(GetMouseEvent(row[mouseActionIdx]));

                                _cw.MouseProc();
                                _debug.WriteData("  Capture: MouseEvent() Complete");
                            }

                            _cw.SetSleepTimeMsec(row[sleepIdx]);
                            _cw.ExecuteSleep();
                            _debug.WriteData("  Capture: ExecuteSleep() Complete");

                            _cw.CaptureProc();
                            _debug.WriteData("  Capture: CaptureProc() Complete");
                        }

                        // 終了予想時間・進捗バー更新
                        Dispatcher.Invoke((Action)(() =>
                        {
                            if (i == 1)
                            {
                                SetExpectEndTime(loopCount);
                            }
                            UpdateProgressBar();
                        }));
                    }
                    _debug.WriteData("Capture: END" + Environment.NewLine);

                    String errLog = _cw.GetErrorLog();
                    Dispatcher.Invoke((Action)(() =>
                    {
                        TextBox_Status.Text += " 完了";

                        // Capture処理が全て終わったら、ボタンを押した時のマウス座標へ戻す
                        Forms.Cursor.Position = _captureStartCursorPosition;

                        if (errLog != String.Empty)
                        {
                            MessageBox.Show(errLog, "エラー", MessageBoxButton.OK);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke((Action)(() =>
                    {
                        Forms.Cursor.Position = _captureStartCursorPosition;
                        MessageBox.Show("キャプチャ中にエラーが発生したよ" + Environment.NewLine + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }));
                }
                finally
                {
                    _isCaptureRunning = false;
                    Dispatcher.Invoke((Action)(() =>
                    {
                        cw_Button_Capture.Content = "Capture";
                    }));
                }
            });

            // SendKeys/ClipboardはSTAスレッドでないと使えないため、STAで起動する
            captureThread.SetApartmentState(ApartmentState.STA);
            captureThread.IsBackground = true;
            captureThread.Start();
        }

        // 進捗バーを1つ進める(最大値で止める)
        private void UpdateProgressBar()
        {
            ShowProgress((int)Math.Min(ProgressBar_Status.Value + 1, ProgressBar_Status.Maximum));
        }
    }
}
