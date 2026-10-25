using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Picture;
using StandardTemplate.Wpf;
using Drawing = System.Drawing;

namespace Cheetos
{
    // PictTrim
    public partial class MainWindow
    {
        private void Button_TrimListup_Click(object sender, RoutedEventArgs e)
        {
            ListUpTrim();
        }

        private void pt_ListBox_ListUp_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            pt_TextBox_Status.Text = "ファイル数：" + pt_ListBox_ListUp.SelectedItems.Count.ToString();
        }

        // pt_TargetX/Yの値が今どちらの指定(true=終点の座標/false=サイズ)で入っているか。
        // ラジオボタンのClickは選択済みのボタンをもう一度押しても来るため、以前はそのたびに
        // 基点を足し引きしてしまい値が壊れていた。実際に指定が切り替わったときだけ換算する
        private Boolean _ptTargetIsPointOfEnd = true;

        // 設定ファイルの読み込みなどで、Clickを通らずにラジオボタンが変わった後に呼ぶ
        // (読み込んだ値はラジオボタンの指定どおりに入っているので、換算はしない)
        private void SyncPictTrimEndMode()
        {
            _ptTargetIsPointOfEnd = (pt_Radio_SelectPointOfEnd.IsChecked == true);
        }

        // 終点指定⇔サイズ指定の切り替えに合わせて、pt_TargetX/Yの値を換算し直す
        private void UpdatePictTrimSize()
        {
            Boolean isPointOfEnd = (pt_Radio_SelectPointOfEnd.IsChecked == true);
            if (isPointOfEnd == _ptTargetIsPointOfEnd)
            {
                // 選択済みのラジオボタンを押し直しただけ
                return;
            }
            _ptTargetIsPointOfEnd = isPointOfEnd;

            // 終点指定になったら(サイズ→終点なので)基点を足し、サイズ指定になったら引く
            int sign = isPointOfEnd ? 1 : -1;
            ConvertPictTrimTarget(pt_TargetX, pt_BaseX, sign);
            ConvertPictTrimTarget(pt_TargetY, pt_BaseY, sign);
        }

        // targetCtrlの値にsign×基点を足す。どちらかが空欄・数値以外なら換算しない
        // (以前はint.Parse("")で例外になっていた)
        private void ConvertPictTrimTarget(TextBox targetCtrl, TextBox baseCtrl, int sign)
        {
            int target;
            int baseValue;
            if (!Int32.TryParse(targetCtrl.Text, out target) || !Int32.TryParse(baseCtrl.Text, out baseValue))
            {
                return;
            }
            targetCtrl.Text = (target + sign * baseValue).ToString();
        }

        private void ListUpTrim()
        {
            ListUpFolderFiles(pt_SourceFolderPath, pt_ListBox_ListUp);
        }

        private void Button_Trim_Click(object sender, RoutedEventArgs e)
        {
            _debug.WriteData("Button_Trim_Click" + Environment.NewLine, false);

            // キャンセル
            if (_bkgWorkerTrim.IsBusy)
            {
                _bkgWorkerTrim.CancelAsync();
                return;
            }

            String backUpDirPath = pt_SourceFolderPath.Text + @"\Bk_Trim";
            if (!EnsureDirectoryWithMessage(backUpDirPath))
            {
                return;
            }

            if (pt_TargetX.Text == String.Empty || pt_TargetY.Text == String.Empty)
            {
                MessageBox.Show("サイズが指定されていません");
                return;
            }

            int baseX;
            int baseY;
            int targetX;
            int targetY;
            if (!TryGetIntWithMessage(pt_BaseX, "原点(X)", int.MinValue, out baseX)
                || !TryGetIntWithMessage(pt_BaseY, "原点(Y)", int.MinValue, out baseY)
                || !TryGetIntWithMessage(pt_TargetX, "X", int.MinValue, out targetX)
                || !TryGetIntWithMessage(pt_TargetY, "Y", int.MinValue, out targetY))
            {
                return;
            }

            int targetWidth;
            int targetHeight;
            if (pt_Radio_SelectPointOfEnd.IsChecked == true)
            {
                targetWidth = targetX - baseX;
                targetHeight = targetY - baseY;
            }
            else // pt_Radio_SelectSizeOfEnd
            {
                targetWidth = targetX;
                targetHeight = targetY;
            }

            // 幅・高さが0以下だとキャンバスを作れず、1件目のバックアップ後にエラーで止まっていた
            if (targetWidth <= 0 || targetHeight <= 0)
            {
                MessageBox.Show("切り取るサイズが0以下です(幅=" + targetWidth + "、高さ=" + targetHeight + ")");
                return;
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

            _debug.WriteData("Source = " + pt_SourceFolderPath.Text);
            _debug.WriteData("Backup = " + backUpDirPath);
            _debug.WriteData("Pos(" + pt_BaseX.Text + "," + pt_BaseY.Text + ")");
            _debug.WriteData("Size(" + targetWidth + "," + targetHeight + ")");

            // ListBoxの値を配列で取得
            param.TargetFileNames = WpfControlUtils.GetSelectedNames(pt_ListBox_ListUp).ToArray();

            SetStartTime();
            pt_Button_Trim.Content = "中断";
            _bkgWorkerTrim.RunWorkerAsync(param);   // ⇒DoWork()
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
                    trm.TrimExec(backUpFilePath, cutParam, Drawing.Point.Empty);

                    // キャンバス保存
                    trm.SaveCanvas(filePath);
                }

                // 進捗率(終わった件数)
                worker.ReportProgress(itemIdx + 1);      // ⇒ProgressChanged()

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
            ShowWorkerCompletion(e);
            TextBox_Status.Text += " 完了";
            pt_Button_Trim.Content = "切り取り";

            // リストを更新
            ListUpTrim();
        }
    }
}
