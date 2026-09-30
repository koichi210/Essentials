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
        internal CaptWindow cw = new CaptWindow();

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
            if (IsTaskRun)
            {
                IsTaskRun = false;
                cw.Stop();
                return;
            }

            CommitGridEdit();
            captureStartCursorPosition = Forms.Cursor.Position;

            fio.EnsureDirectory(cw_TextBox_SavePath.Text, true);
            String FileBaseFormat = Logic.GetFileBaseFormat(cw_TextBox_SavePath.Text, cw_TextBox_SaveFilePrefix.Text, cw_checkBox_AddTimeStamp.IsChecked == true);

            int Loop = 1;
            if (cw_TextBox_Loop.Text != String.Empty)
            {
                Loop = int.Parse(cw_TextBox_Loop.Text);
            }
            InitProgressBar(Loop);
            SetStartTime();

            CaptureForeground(FileBaseFormat, Loop);

            String ErrLog = cw.GetErrorLog();
            if (ErrLog != String.Empty)
            {
                MessageBox.Show(ErrLog, "エラー", MessageBoxButton.OK);
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

        private void CaptureForeground(String FileBaseFormat, int Loop)
        {
            Task task = new Task(() =>
            {
                IsTaskRun = true;
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
                    CaptWindow.CAPTURE_TARGET CaptTarget;
                    if (cw_Radio_FullScreen.IsChecked == true)
                    {
                        CaptTarget = CaptWindow.CAPTURE_TARGET.FULL_SCREEN;
                    }
                    else if (cw_Radio_CurrentScreen.IsChecked == true)
                    {
                        CaptTarget = CaptWindow.CAPTURE_TARGET.CURRENT_SCREEN;
                    }
                    else // cw_Radio_CurrentWindow
                    {
                        CaptTarget = CaptWindow.CAPTURE_TARGET.CURRENT_WINDOW;
                    }
                    cw.SetCaptureTarget(CaptTarget);

                    Debug.WriteData("Capture: START", false);
                    for (int i = 1; i <= Loop && IsTaskRun; i++, UpdateProgressBar())
                    {
                        Debug.WriteData("Capture: Loop=" + i.ToString() + "/" + Loop.ToString());

                        // ファイルのIdex番号を初期化
                        cw.SetFileIdx(1);

                        // ファイル名生成
                        String FileFormat = FileBaseFormat + String.Format("{0:D4}", i);
                        cw.SetFileFormat(FileFormat);
                        Debug.WriteData(" Capture: Filename=" + FileFormat);

                        // いまのところマウス移動しないユースケースは無い
                        cw.SetMouseMove(true);

                        // 順次Capture実行
                        for (int j = 0; j < cw_Rows.Count; j++)
                        {
                            Debug.WriteData(" Capture: RowCnt=" + j.ToString() + "/" + cw_Rows.Count.ToString());
                            CaptureGridRow row = cw_Rows[j];

                            String CaptureEventStr = row[GetDataGridColumnIdx(GridHeaderCaptureStr)];
                            Boolean IsCapture = IsCaptureEvent(CaptureEventStr);

                            cw.SetCaptureCase(IsCapture);
                            Debug.WriteData("  Capture: SetCaptureCase() Done");

                            String PointX = row[GetDataGridColumnIdx(GridHeaderMouseXStr)];
                            String PointY = row[GetDataGridColumnIdx(GridHeaderMouseYStr)];

                            if (cw.SetMousePoint(PointX, PointY))
                            {
                                String MouseEventStr = row[GetDataGridColumnIdx(GridHeaderMouseActionStr)];
                                CaptWindow.MOUSE_EVENT Event = GetMouseEvent(MouseEventStr);
                                cw.SetMouseEvent(Event);

                                cw.MouseProc();
                                Debug.WriteData("  Capture: MouseEvent() Complete");
                            }

                            String SleepMsec = row[GetDataGridColumnIdx(GridHeaderSleepStr)];
                            cw.SetSleepTimeMsec(SleepMsec);
                            cw.ExecuteSleep();
                            Debug.WriteData("  Capture: ExecuteSleep() Complete");

                            cw.CaptureProc();
                            Debug.WriteData("  Capture: CaptureProc() Complete");
                        }

                        // 終了予想時間
                        if (i == 1)
                        {
                            SetExpectEndTime(Loop);
                        }
                    }
                    Debug.WriteData("Capture: END" + Environment.NewLine);
                    TextBox_Status.Text += " 完了";

                    // Capture処理が全て終わったら、ボタンを押した時のマウス座標へ戻す
                    Forms.Cursor.Position = captureStartCursorPosition;
                }));
                IsTaskRun = false;
            });

            // Task内の例外はどこにも通知されず消えてしまい、IsTaskRunもtrueのまま残るため、失敗時はUIスレッドで後始末と通知を行う
            task.ContinueWith(t =>
            {
                IsTaskRun = false;
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
