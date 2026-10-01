using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using StandardTemplate;
using Forms = System.Windows.Forms;

namespace CheetosForWpf
{
    // CaptureWindow
    public partial class MainWindow
    {
        private readonly CaptWindow cw = new CaptWindow();

        // マウス座標はWinForms版と同じくCursor.Position(スクリーン座標)を表示する。
        // DataGridのMouseX/MouseYにそのまま書き写して使う値なので、CaptWindow(SetCursorPos)と同じ座標系にしておく
        private void tabPageCaptureWindow_MouseMove(object sender, MouseEventArgs e)
        {
            System.Drawing.Point pos = Forms.Cursor.Position;
            cw_TextBox_Status.Text = "X=" + pos.X.ToString() + ", Y=" + pos.Y.ToString();
        }

        // Captureボタンを押した瞬間のマウス座標。一連のCapture処理が終わったらここへ戻す
        private System.Drawing.Point captureStartCursorPosition;

        private void Button_Capture_Click(object sender, RoutedEventArgs e)
        {
            // 実行中だったら停止する
            if (isCaptureRunning)
            {
                isCaptureRunning = false;
                cw.Stop();
                return;
            }

            CommitGridEdit();
            captureStartCursorPosition = Forms.Cursor.Position;

            fio.EnsureDirectory(cw_TextBox_SavePath.Text, true);
            String fileBaseFormat = Logic.GetFileBaseFormat(cw_TextBox_SavePath.Text, cw_TextBox_SaveFilePrefix.Text, cw_checkBox_AddTimeStamp.IsChecked == true);

            int loopCount = 1;
            if (cw_TextBox_Loop.Text != String.Empty)
            {
                loopCount = int.Parse(cw_TextBox_Loop.Text);
            }
            InitProgressBar(loopCount);
            SetStartTime();

            CaptureForeground(fileBaseFormat, loopCount);

            String errLog = cw.GetErrorLog();
            if (errLog != String.Empty)
            {
                MessageBox.Show(errLog, "エラー", MessageBoxButton.OK);
            }
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
                util.ExecutePath(cw_TextBox_SavePath.Text);
            }
        }

        private void CaptureForeground(String fileBaseFormat, int loopCount)
        {
            Task task = new Task(() =>
            {
                isCaptureRunning = true;
                // WinForms版と同じく、キャプチャ処理の本体はUIスレッドで実行する
                // (SendKeys/クリップボード/マウス操作がUIスレッド前提のため)
                Dispatcher.Invoke((Action)(() =>
                {
                    // 初期化
                    cw.Initialize();

                    // マウス移動後にもとの位置へ戻すか
                    cw.RestoreMousePosition(false);

                    // 実行前のSleep
                    cw.SetSleepTimeMsec(cw_TextBox_Sleep.Text);
                    cw.ExecuteSleep();

                    // キャプチャ対象を設定
                    CaptWindow.CAPTURE_TARGET captureTarget;
                    if (cw_Radio_FullScreen.IsChecked == true)
                    {
                        captureTarget = CaptWindow.CAPTURE_TARGET.FULL_SCREEN;
                    }
                    else if (cw_Radio_CurrentScreen.IsChecked == true)
                    {
                        captureTarget = CaptWindow.CAPTURE_TARGET.CURRENT_SCREEN;
                    }
                    else // cw_Radio_CurrentWindow
                    {
                        captureTarget = CaptWindow.CAPTURE_TARGET.CURRENT_WINDOW;
                    }
                    cw.SetCaptureTarget(captureTarget);

                    debug.WriteData("Capture: START", false);
                    for (int i = 1; i <= loopCount && isCaptureRunning; i++, UpdateProgressBar())
                    {
                        debug.WriteData("Capture: Loop=" + i.ToString() + "/" + loopCount.ToString());

                        // ファイルのIndex番号を初期化
                        cw.SetFileIdx(1);

                        // ファイル名生成
                        String fileFormat = fileBaseFormat + String.Format("{0:D4}", i);
                        cw.SetFileFormat(fileFormat);
                        debug.WriteData(" Capture: Filename=" + fileFormat);

                        // いまのところマウス移動しないユースケースは無い
                        cw.SetMouseMove(true);

                        // 順次Capture実行
                        for (int j = 0; j < cw_Rows.Count; j++)
                        {
                            debug.WriteData(" Capture: RowCnt=" + j.ToString() + "/" + cw_Rows.Count.ToString());
                            CaptureGridRow row = cw_Rows[j];

                            cw.SetCaptureCase(IsCaptureEvent(row[GetDataGridColumnIdx(GridHeaderCaptureStr)]));
                            debug.WriteData("  Capture: SetCaptureCase() Done");

                            String pointX = row[GetDataGridColumnIdx(GridHeaderMouseXStr)];
                            String pointY = row[GetDataGridColumnIdx(GridHeaderMouseYStr)];

                            if (cw.SetMousePoint(pointX, pointY))
                            {
                                cw.SetMouseEvent(GetMouseEvent(row[GetDataGridColumnIdx(GridHeaderMouseActionStr)]));

                                cw.MouseProc();
                                debug.WriteData("  Capture: MouseEvent() Complete");
                            }

                            cw.SetSleepTimeMsec(row[GetDataGridColumnIdx(GridHeaderSleepStr)]);
                            cw.ExecuteSleep();
                            debug.WriteData("  Capture: ExecuteSleep() Complete");

                            cw.CaptureProc();
                            debug.WriteData("  Capture: CaptureProc() Complete");
                        }

                        // 終了予想時間
                        if (i == 1)
                        {
                            SetExpectEndTime(loopCount);
                        }
                    }
                    debug.WriteData("Capture: END" + Environment.NewLine);
                    TextBox_Status.Text += " 完了";

                    // Capture処理が全て終わったら、ボタンを押した時のマウス座標へ戻す
                    Forms.Cursor.Position = captureStartCursorPosition;
                }));
                isCaptureRunning = false;
            });

            // Task内の例外はどこにも通知されず消えてしまい、isCaptureRunningもtrueのまま残るため、失敗時はUIスレッドで後始末と通知を行う
            task.ContinueWith(t =>
            {
                isCaptureRunning = false;
                Forms.Cursor.Position = captureStartCursorPosition;
                MessageBox.Show("キャプチャ中にエラーが発生したよ" + Environment.NewLine + t.Exception.GetBaseException().Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }, System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext());
            task.Start();
        }

        private void UpdateProgressBar()
        {
            if (ProgressBar_Status.Value < ProgressBar_Status.Maximum)
            {
                ProgressBar_Status.Value++;
            }
            TextBox_Status.Text = ((int)ProgressBar_Status.Value).ToString() + "/" + ((int)ProgressBar_Status.Maximum).ToString();
        }
    }
}
