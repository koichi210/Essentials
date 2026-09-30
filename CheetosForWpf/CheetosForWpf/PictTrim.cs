using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Picture;
using Drawing = System.Drawing;

namespace CheetosForWpf
{
    // PictTrim
    public partial class MainWindow
    {
        private void Button_TrimListup_Click(object sender, RoutedEventArgs e)
        {
            ListupTrim();
        }

        private void pt_ListBox_ListUp_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            pt_TextBox_Status.Text = "ファイル数：" + pt_ListBox_ListUp.SelectedItems.Count.ToString();
        }

        private void UpdatePitTrimSize()
        {
            Drawing.Point Target = new Drawing.Point();
            if (pt_Radio_SelectPointOfEnd.IsChecked == true)
            {
                Target.X = int.Parse(pt_TargetX.Text) + int.Parse(pt_BaseX.Text);
                Target.Y = int.Parse(pt_TargetY.Text) + int.Parse(pt_BaseY.Text);
            }
            else // (pt_Radio_SelectPointOfEnd.Checked == true)
            {
                Target.X = int.Parse(pt_TargetX.Text) - int.Parse(pt_BaseX.Text);
                Target.Y = int.Parse(pt_TargetY.Text) - int.Parse(pt_BaseY.Text);
            }

            pt_TargetX.Text = Target.X.ToString();
            pt_TargetY.Text = Target.Y.ToString();
        }

        private void ListupTrim()
        {
            ListupFolderFiles(pt_SourceFolderPath, pt_ListBox_ListUp);
        }

        private void Button_Trim_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteData("Button_Trim_Click" + Environment.NewLine, false);

            // キャンセル
            if (bkgWorkerTrim.IsBusy)
            {
                bkgWorkerTrim.CancelAsync();
                return;
            }

            String BackUpDirPath = pt_SourceFolderPath.Text + @"\" + @"Bk_Trim";
            if (!fio.EnsureDirectory(BackUpDirPath))
            {
                MessageBox.Show("無効なフォルダパスです。\n" + BackUpDirPath);
                return;
            }

            if (pt_TargetX.Text == String.Empty || pt_TargetY.Text == String.Empty)
            {
                MessageBox.Show("サイズが指定されていません");
                return;
            }

            int Target_Width;
            int Target_Height;
            if (pt_Radio_SelectPointOfEnd.IsChecked == true)
            {
                Target_Width = int.Parse(pt_TargetX.Text) - int.Parse(pt_BaseX.Text);
                Target_Height = int.Parse(pt_TargetY.Text) - int.Parse(pt_BaseY.Text);
            }
            else // if ( pt_Radio_SelectSizeOfEnd.IsChecked.Value )
            {
                Target_Width = int.Parse(pt_TargetX.Text);
                Target_Height = int.Parse(pt_TargetY.Text);
            }

            InitProgressBar(pt_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            TrimWorkerParam param = new TrimWorkerParam
            {
                BaseX = pt_BaseX.Text,
                BaseY = pt_BaseY.Text,
                TargetWidth = Target_Width,
                TargetHeight = Target_Height,
                SourceFolderPath = pt_SourceFolderPath.Text,
                BackUpDirPath = BackUpDirPath,
            };

            Debug.WriteData("Source = " + pt_SourceFolderPath.Text);
            Debug.WriteData("Backup = " + BackUpDirPath);
            Debug.WriteData("Pos(" + pt_BaseX.Text + "," + pt_BaseY.Text + ")");
            Debug.WriteData("Size(" + Target_Width + "," + Target_Height + ")");

            // ListBoxの値を配列で取得
            param.TargetNameAry = WpfUtils.GetSelectedStrArray(pt_ListBox_ListUp);

            SetStartTime();
            pt_Button_Trim.Content = "中断";
            bkgWorkerTrim.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void bkgWorkerTrim_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない

            // senderの値はbgWorkerの値と同じ
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            TrimWorkerParam param = (TrimWorkerParam)e.Argument;

            int BaseX = int.Parse(param.BaseX);
            int BaseY = int.Parse(param.BaseY);
            int Target_Width = param.TargetWidth;
            int Target_Height = param.TargetHeight;
            String SourceFolderPath = param.SourceFolderPath;
            String BackUpDirPath = param.BackUpDirPath;
            String[] TargetNameAry = param.TargetNameAry;

            for (int ItemIdx = 0; ItemIdx < TargetNameAry.Length; ItemIdx++)
            {
                if (TargetNameAry[ItemIdx] == String.Empty)
                {
                    continue;
                }

                String FilePath = SourceFolderPath + @"\" + TargetNameAry[ItemIdx];
                String BackUpFilePath = BackUpDirPath + @"\" + TargetNameAry[ItemIdx];

                // オリジナルファイルをバックアップ
                File.Copy(FilePath, BackUpFilePath, true);

                // トリミング
                // キャンバス作成(途中で失敗しても画像ファイルがロックされたまま残らないようfinallyで必ず解放する)
                PicEdit trm = new PicEdit(Target_Width, Target_Height);
                try
                {
                    // 切り取り
                    Drawing.Rectangle CutParam = new Drawing.Rectangle(BaseX, BaseY, Target_Width, Target_Height);

                    Drawing.Point PutParam = new Drawing.Point(0, 0);
                    trm.TrimExec(BackUpFilePath, CutParam, PutParam);

                    // キャンバス保存
                    trm.SaveCanvas(FilePath);
                }
                finally
                {
                    trm.Dispose();
                }

                // 進捗率
                worker.ReportProgress(ItemIdx);      // ⇒ProgressChanged()

                // キャンセルされてないかチェック
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
            }
            worker.ReportProgress(TargetNameAry.Length);      // ⇒ProgressChanged()

            e.Result = "値も渡せます";
        }

        private void bkgWorkerTrim_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
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
            pt_Button_Trim.Content = "切り取り";

            // リストを更新
            ListupTrim();
        }
    }
}
