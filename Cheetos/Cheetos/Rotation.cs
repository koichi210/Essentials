using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Drawing = System.Drawing;

namespace Cheetos
{
    public class Rotation
    {
        public int BaseX { get; set; }
        public int BaseY { get; set; }
        public int Angle { get; set; }
        public String SourceFolderPath { get; set; } = String.Empty;
        public String BackUpDirPath { get; set; } = String.Empty;

        private String _targetFileName = String.Empty;
        private String _filePath = String.Empty;
        private String _backUpFilePath = String.Empty;

        public bool SetTargetFileName(String targetFileName)
        {
            if (targetFileName == String.Empty)
            {
                // 空行だったら処理しない
                return false;
            }
            _targetFileName = targetFileName;
            return true;
        }

        public bool BackUpTargetFile()
        {
            _filePath = SourceFolderPath + @"\" + _targetFileName;
            _backUpFilePath = BackUpDirPath + @"\" + _targetFileName;

            File.Copy(_filePath, _backUpFilePath, true);
            return true;
        }

        public void RotateExecute()
        {
            // 途中で失敗しても画像ファイルがロックされたまま残らないよう、usingとfinallyで必ず解放する
            Drawing.Bitmap canvas = null;
            try
            {
                // 読み込み元と同じファイルへ上書き保存するため、imgは保存より前に解放しておく必要がある
                using (Drawing.Bitmap img = new Drawing.Bitmap(_filePath))
                {
                    int length = (int)Math.Sqrt(img.Width * img.Width + img.Height * img.Height);

                    // WinForms版はPictureBoxを作ってそのサイズでキャンバスを作っていたが、結果は同じなので直接作る
                    canvas = new Drawing.Bitmap(length, length);
                    DrawRotated(canvas, img, BaseX, BaseY, Angle);
                }

                canvas.Save(_filePath);
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.Dispose();
                }
            }
        }

        // imgを(originX, originY)を基点にangle度回転させてcanvasへ描く(回転実行とプレビューで共通)
        public static void DrawRotated(Drawing.Bitmap canvas, Drawing.Bitmap img, float originX, float originY, int angle)
        {
            //ラジアン単位に変換
            double d = angle / (180 / Math.PI);
            float cos = (float)Math.Cos(d);
            float sin = (float)Math.Sin(d);

            //新しい座標位置を計算する
            Drawing.PointF[] destinationPoints =
            {
                new Drawing.PointF(originX, originY),
                new Drawing.PointF(originX + img.Width * cos, originY + img.Width * sin),
                new Drawing.PointF(originX - img.Height * sin, originY + img.Height * cos)
            };

            using (Drawing.Graphics g = Drawing.Graphics.FromImage(canvas))
            {
                //画像を表示
                g.DrawImage(img, destinationPoints);
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
            ListUpRotation();
        }

        private void ListUpRotation()
        {
            ListUpFolderFiles(pr_SourceFolderPath, pr_ListBox_ListUp);
        }

        private void pr_Button_Rotation_Click(object sender, RoutedEventArgs e)
        {
            if (pr_ListBox_ListUp.SelectedItems.Count == 0)
            {
                MessageBox.Show("ファイルが選択されていません。");
                return;
            }

            // キャンセル
            if (_bkgWorkerRotation.IsBusy)
            {
                _bkgWorkerRotation.CancelAsync();
                return;
            }

            String backUpDirPath = pr_SourceFolderPath.Text + @"\Bk_Rotate";
            if (!EnsureDirectoryWithMessage(backUpDirPath))
            {
                return;
            }

            WpfUtils.ClearIfNotInteger(pr_BaseX);
            WpfUtils.ClearIfNotInteger(pr_BaseY);
            WpfUtils.ClearIfNotInteger(pr_Angle);

            InitProgressBar(pr_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            RotationWorkerParam param = new RotationWorkerParam
            {
                BaseX = pr_BaseX.Text,
                BaseY = pr_BaseY.Text,
                Angle = pr_Angle.Text,
                SourceFolderPath = pr_SourceFolderPath.Text,
                BackUpDirPath = backUpDirPath,

                // ListBoxの値を配列で取得
                TargetFileNames = WpfUtils.GetSelectedStrArray(pr_ListBox_ListUp),
            };

            SetStartTime();
            pr_Button_Rotation.Content = "中断";
            _bkgWorkerRotation.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void bkgWorkerRotation_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない

            // senderの値はbgWorkerの値と同じ
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            RotationWorkerParam param = (RotationWorkerParam)e.Argument;

            Rotation rt = new Rotation
            {
                BaseX = int.Parse(param.BaseX),
                BaseY = int.Parse(param.BaseY),
                Angle = int.Parse(param.Angle),
                SourceFolderPath = param.SourceFolderPath,
                BackUpDirPath = param.BackUpDirPath,
            };
            String[] targetFileNames = param.TargetFileNames;

            for (int itemIdx = 0; itemIdx < targetFileNames.Length; itemIdx++)
            {
                if (!rt.SetTargetFileName(targetFileNames[itemIdx]))
                {
                    continue;
                }

                rt.BackUpTargetFile();
                rt.RotateExecute();

                // 進捗率
                worker.ReportProgress(itemIdx);      // ⇒ProgressChanged()

                // キャンセルされてないかチェック
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
            }
            worker.ReportProgress(targetFileNames.Length);      // ⇒ProgressChanged()
        }

        private void bkgWorkerRotation_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ShowWorkerCompletion(e);
            TextBox_Status.Text += " 完了";
            pr_Button_Rotation.Content = "回転実行";

            // リストを更新
            ListUpRotation();
        }
    }
}
