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
            // 実行中だったら停止する(他タブのBackgroundWorkerと同じ「もう一度押すと中断」。
            // isCaptureRunningはスレッドが終わるまでtrueのままにしておく(ここでfalseにすると、
            // スレッドの終了処理が終わる前にもう一度押されたとき二重起動してしまう)
            if (isCaptureRunning)
            {
                cw.Stop();
                return;
            }

            CommitGridEdit();
            captureStartCursorPosition = Forms.Cursor.Position;

            fio.EnsureDirectory(cw_TextBox_SavePath.Text, true);
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
            String sleepMsecText = cw_TextBox_Sleep.Text;
            List<List<String>> gridRows = cw_Rows.Select(row => row.ToList()).ToList();

            isCaptureRunning = true;
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
                util.ExecutePath(cw_TextBox_SavePath.Text);
            }
        }

        // 以前はループ全体をDispatcher.Invoke(...)の中(UIスレッド同期実行)で回していたため、
        // ループ中UIスレッドが占有されっぱなしになり、「中断」のためのボタン再クリックが
        // ループ終了まで処理されなかった。(SendKeys/ClipboardはSTAスレッドが必要なため、専用の
        // STAスレッドでループを回し、WPFのコントロールに触る部分だけDispatcher.Invokeで
        // UIスレッドに戻す。UIスレッドはクリックを即座に処理できるので中断が効くようになる)
        private void CaptureForeground(String filePathPrefix, int loopCount, CaptWindow.CAPTURE_TARGET captureTarget, String sleepMsecText, List<List<String>> gridRows)
        {
            Thread captureThread = new Thread(() =>
            {
                try
                {
                    // 初期化
                    cw.Initialize();

                    // マウス移動後にもとの位置へ戻すか
                    cw.SetRestoreMousePosition(false);

                    // 実行前のSleep
                    cw.SetSleepTimeMsec(sleepMsecText);
                    cw.ExecuteSleep();

                    cw.SetCaptureTarget(captureTarget);

                    debug.WriteData("Capture: START", false);
                    for (int i = 1; i <= loopCount && !cw.IsStopRequest; i++)
                    {
                        debug.WriteData("Capture: Loop=" + i.ToString() + "/" + loopCount.ToString());

                        // ファイルのIndex番号を初期化
                        cw.SetFileIdx(1);

                        // ファイル名生成
                        String fileFormat = filePathPrefix + String.Format("{0:D4}", i);
                        cw.SetFileFormat(fileFormat);
                        debug.WriteData(" Capture: Filename=" + fileFormat);

                        // いまのところマウス移動しないユースケースは無い
                        cw.SetMouseMove(true);

                        // 順次Capture実行
                        for (int j = 0; j < gridRows.Count && !cw.IsStopRequest; j++)
                        {
                            debug.WriteData(" Capture: RowCnt=" + j.ToString() + "/" + gridRows.Count.ToString());
                            List<String> row = gridRows[j];

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
                    debug.WriteData("Capture: END" + Environment.NewLine);

                    String errLog = cw.GetErrorLog();
                    Dispatcher.Invoke((Action)(() =>
                    {
                        TextBox_Status.Text += " 完了";

                        // Capture処理が全て終わったら、ボタンを押した時のマウス座標へ戻す
                        Forms.Cursor.Position = captureStartCursorPosition;

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
                        Forms.Cursor.Position = captureStartCursorPosition;
                        MessageBox.Show("キャプチャ中にエラーが発生したよ" + Environment.NewLine + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }));
                }
                finally
                {
                    isCaptureRunning = false;
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
