using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Drawing = System.Drawing;

namespace CheetosForWpf
{
    public class Rotation
    {
        public int BaseX = 0;
        public int BaseY = 0;
        public int Angle = 0;
        public String SourceFolderPath = String.Empty;
        public String BackUpDirPath = String.Empty;

        private String TargetFileName = String.Empty;
        private String FilePath = String.Empty;
        private String BackUpFilePath = String.Empty;

        public bool SetTargetFileName(String target_file_name)
        {
            if (target_file_name == String.Empty)
            {
                // 空行だったら処理しない
                return false;
            }
            TargetFileName = target_file_name;
            return true;
        }

        public bool CreateRotateFile()
        {
            FilePath = SourceFolderPath + @"\" + TargetFileName;
            BackUpFilePath = BackUpDirPath + @"\" + TargetFileName;

            File.Copy(FilePath, BackUpFilePath, true);
            return true;
        }

        public void RotateExecute()
        {
            // 途中で失敗しても画像ファイルがロックされたまま残らないよう、usingとfinallyで必ず解放する
            Drawing.Bitmap canvas = null;
            try
            {
                // 読み込み元と同じファイルへ上書き保存するため、imgは保存より前に解放しておく必要がある
                using (Drawing.Bitmap img = new Drawing.Bitmap(FilePath))
                {
                    int length = (int)Math.Sqrt(img.Width * img.Width + img.Height * img.Height);

                    // WinForms版はPictureBoxを作ってそのサイズでキャンバスを作っていたが、結果は同じなので直接作る
                    canvas = new Drawing.Bitmap(length, length);

                    //ラジアン単位に変換
                    double d = Angle / (180 / Math.PI);

                    //新しい座標位置を計算する
                    float x1 = BaseX + img.Width * (float)Math.Cos(d);
                    float y1 = BaseY + img.Width * (float)Math.Sin(d);
                    float x2 = BaseX - img.Height * (float)Math.Sin(d);
                    float y2 = BaseY + img.Height * (float)Math.Cos(d);

                    //PointF配列を作成
                    Drawing.PointF[] destinationPoints =
                    {
                        new Drawing.PointF(BaseX, BaseY),
                        new Drawing.PointF(x1, y1),
                        new Drawing.PointF(x2, y2)
                    };

                    using (Drawing.Graphics g = Drawing.Graphics.FromImage(canvas))
                    {
                        //画像を表示
                        g.DrawImage(img, destinationPoints);
                    }
                }

                canvas.Save(FilePath);
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.Dispose();
                }
            }
        }
    }

    public partial class MainWindow
    {
        private void pr_Button_RotationPreview_Click(object sender, RoutedEventArgs e)
        {
            RotationPreview rp = new RotationPreview(pr_BaseX.Text, pr_BaseY.Text, pr_Angle.Text);
            rp.Owner = this;

            if (rp.ShowDialog() == true)
            {
                pr_BaseX.Text = rp.OriginX;
                pr_BaseY.Text = rp.OriginY;
                pr_Angle.Text = rp.Angle;
            }
        }

        private void pr_Button_Listup_Click(object sender, RoutedEventArgs e)
        {
            ListupRotation();
        }

        private void ListupRotation()
        {
            ListupFolderFiles(pr_SourceFolderPath, pr_ListBox_ListUp);
        }

        private void pr_Button_Rotation_Click(object sender, RoutedEventArgs e)
        {
            if (pr_ListBox_ListUp.SelectedItems.Count == 0)
            {
                MessageBox.Show("ファイルが選択されていません。");
                return;
            }

            // キャンセル
            if (bkgWorkerRotation.IsBusy)
            {
                bkgWorkerRotation.CancelAsync();
                return;
            }

            String BackUpDirPath = pr_SourceFolderPath.Text + @"\" + @"Bk_Rotate";
            if (!fio.EnsureDirectory(BackUpDirPath))
            {
                MessageBox.Show("無効なフォルダパスです。\n" + BackUpDirPath);
                return;
            }

            int val;
            if (!Int32.TryParse(pr_BaseX.Text.ToString(), out val))
            {
                pr_BaseX.Text = "";
            }
            if (!Int32.TryParse(pr_BaseY.Text.ToString(), out val))
            {
                pr_BaseY.Text = "";
            }
            if (!Int32.TryParse(pr_Angle.Text.ToString(), out val))
            {
                pr_Angle.Text = "";
            }

            InitProgressBar(pr_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            RotationWorkerParam param = new RotationWorkerParam
            {
                BaseX = pr_BaseX.Text,
                BaseY = pr_BaseY.Text,
                Angle = pr_Angle.Text,
                SourceFolderPath = pr_SourceFolderPath.Text,
                BackUpDirPath = BackUpDirPath,

                // ListBoxの値を配列で取得
                TargetNameAry = WpfUtils.GetSelectedStrArray(pr_ListBox_ListUp),
            };

            SetStartTime();
            pr_Button_Rotation.Content = "中断";
            bkgWorkerRotation.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void bkgWorkerRotation_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない

            // senderの値はbgWorkerの値と同じ
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            RotationWorkerParam param = (RotationWorkerParam)e.Argument;

            Rotation rt = new Rotation();

            rt.BaseX = int.Parse(param.BaseX);
            rt.BaseY = int.Parse(param.BaseY);
            rt.Angle = int.Parse(param.Angle);
            rt.SourceFolderPath = param.SourceFolderPath;
            rt.BackUpDirPath = param.BackUpDirPath;
            String[] TargetNameAry = param.TargetNameAry;

            for (int ItemIdx = 0; ItemIdx < TargetNameAry.Length; ItemIdx++)
            {
                if (!rt.SetTargetFileName(TargetNameAry[ItemIdx]))
                {
                    continue;
                }

                rt.CreateRotateFile();
                rt.RotateExecute();

                // 進捗率
                worker.ReportProgress(ItemIdx);      // ⇒ProgressChanged()

                // キャンセルされてないかチェック
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
            }
            worker.ReportProgress(TargetNameAry.Length);      // ⇒ProgressChanged()
        }

        private void bkgWorkerRotation_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                MessageBox.Show("キャンセルされました");
                // この場合はe.Resultにはアクセスできない
            }
            else if (!(e.Error == null))
            {
                MessageBox.Show("エラーが発生しました[" + e.Error.Message + "]");
            }
            else
            {
                // 処理結果の表示
                //MessageBox.Show("正常に完了しました");
            }
            TextBox_Status.Text += " 完了";
            pr_Button_Rotation.Content = "回転実行";

            // リストを更新
            ListupRotation();
        }
    }
}
