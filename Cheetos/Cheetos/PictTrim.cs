using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Picture;
using Drawing = System.Drawing;

namespace Cheetos
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

        // 終点指定⇔サイズ指定の切り替えに合わせて、pt_TargetX/Yの値を換算し直す
        private void UpdatePictTrimSize()
        {
            Drawing.Point target = new Drawing.Point();
            if (pt_Radio_SelectPointOfEnd.IsChecked == true)
            {
                target.X = int.Parse(pt_TargetX.Text) + int.Parse(pt_BaseX.Text);
                target.Y = int.Parse(pt_TargetY.Text) + int.Parse(pt_BaseY.Text);
            }
            else // (pt_Radio_SelectSizeOfEnd.IsChecked == true)
            {
                target.X = int.Parse(pt_TargetX.Text) - int.Parse(pt_BaseX.Text);
                target.Y = int.Parse(pt_TargetY.Text) - int.Parse(pt_BaseY.Text);
            }

            pt_TargetX.Text = target.X.ToString();
            pt_TargetY.Text = target.Y.ToString();
        }

        private void ListupTrim()
        {
            ListupFolderFiles(pt_SourceFolderPath, pt_ListBox_ListUp);
        }

        private void Button_Trim_Click(object sender, RoutedEventArgs e)
        {
            debug.WriteData("Button_Trim_Click" + Environment.NewLine, false);

            // キャンセル
            if (bkgWorkerTrim.IsBusy)
            {
                bkgWorkerTrim.CancelAsync();
                return;
            }

            String backUpDirPath = pt_SourceFolderPath.Text + @"\" + @"Bk_Trim";
            if (!fio.EnsureDirectory(backUpDirPath))
            {
                MessageBox.Show("無効なフォルダパスです。\n" + backUpDirPath);
                return;
            }

            if (pt_TargetX.Text == String.Empty || pt_TargetY.Text == String.Empty)
            {
                MessageBox.Show("サイズが指定されていません");
                return;
            }

            int targetWidth;
            int targetHeight;
            if (pt_Radio_SelectPointOfEnd.IsChecked == true)
            {
                targetWidth = int.Parse(pt_TargetX.Text) - int.Parse(pt_BaseX.Text);
                targetHeight = int.Parse(pt_TargetY.Text) - int.Parse(pt_BaseY.Text);
            }
            else // if ( pt_Radio_SelectSizeOfEnd.IsChecked.Value )
            {
                targetWidth = int.Parse(pt_TargetX.Text);
                targetHeight = int.Parse(pt_TargetY.Text);
            }

            InitProgressBar(pt_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            TrimWorkerParam param = new TrimWorkerParam
            {
                BaseX = pt_BaseX.Text,
                BaseY = pt_BaseY.Text,
                TargetWidth = targetWidth,
                TargetHeight = targetHeight,
                SourceFolderPath = pt_SourceFolderPath.Text,
                BackUpDirPath = backUpDirPath,
            };

            debug.WriteData("Source = " + pt_SourceFolderPath.Text);
            debug.WriteData("Backup = " + backUpDirPath);
            debug.WriteData("Pos(" + pt_BaseX.Text + "," + pt_BaseY.Text + ")");
            debug.WriteData("Size(" + targetWidth + "," + targetHeight + ")");

            // ListBoxの値を配列で取得
            param.TargetFileNames = WpfUtils.GetSelectedStrArray(pt_ListBox_ListUp);

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

            int baseX = int.Parse(param.BaseX);
            int baseY = int.Parse(param.BaseY);
            String[] targetFileNames = param.TargetFileNames;

            for (int itemIdx = 0; itemIdx < targetFileNames.Length; itemIdx++)
            {
                if (targetFileNames[itemIdx] == String.Empty)
                {
                    continue;
                }

                String filePath = param.SourceFolderPath + @"\" + targetFileNames[itemIdx];
                String backUpFilePath = param.BackUpDirPath + @"\" + targetFileNames[itemIdx];

                // オリジナルファイルをバックアップ
                File.Copy(filePath, backUpFilePath, true);

                // トリミング
                // キャンバス作成(途中で失敗しても画像ファイルがロックされたまま残らないようusingで必ず解放する)
                using (PicEdit trm = new PicEdit(param.TargetWidth, param.TargetHeight))
                {
                    // 切り取り
                    Drawing.Rectangle cutParam = new Drawing.Rectangle(baseX, baseY, param.TargetWidth, param.TargetHeight);

                    Drawing.Point putParam = new Drawing.Point(0, 0);
                    trm.TrimExec(backUpFilePath, cutParam, putParam);

                    // キャンバス保存
                    trm.SaveCanvas(filePath);
                }

                // 進捗率
                worker.ReportProgress(itemIdx);      // ⇒ProgressChanged()

                // キャンセルされてないかチェック
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
            }
            worker.ReportProgress(targetFileNames.Length);      // ⇒ProgressChanged()
        }

        private void bkgWorkerTrim_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                MessageBox.Show("キャンセルされました");
                // この場合はe.Resultにはアクセスできない
            }
            else if (e.Error != null)
            {
                MessageBox.Show("エラーが発生しました[" + e.Error.Message + "]");
            }
            TextBox_Status.Text += " 完了";
            pt_Button_Trim.Content = "切り取り";

            // リストを更新
            ListupTrim();
        }
    }
}
