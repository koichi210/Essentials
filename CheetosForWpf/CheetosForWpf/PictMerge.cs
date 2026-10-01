using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Picture;
using Drawing = System.Drawing;

namespace CheetosForWpf
{
    public class PictMerge
    {
        public String BackUpDirPath { get; set; } = String.Empty;
        public String SourceFolderPath { get; set; } = String.Empty;
        public String SourceFile1Prefix { get; set; } = String.Empty;
        public String SourceFile2Prefix { get; set; } = String.Empty;

        // 切り出す高さの範囲。1要素が"開始,終了"("-"は開始なら0、終了なら画像の高さ)
        public String[] TrimHeightRanges { get; set; }

        private String TargetFileName = String.Empty;
        private String Prefix1 = String.Empty;
        private String Prefix2 = String.Empty;
        private String SourceFileFullName = String.Empty;
        private String SourceBackUpFullName = String.Empty;
        private String MergeFileFullName = String.Empty;
        private String MergeBackUpFullName = String.Empty;
        private String ErrorLog = String.Empty;

        public bool SetTargetFileName(String targetFileName)
        {
            if (targetFileName == String.Empty)
            {
                // 空行だったら処理しない
                return false;
            }
            TargetFileName = targetFileName;
            return true;
        }

        public bool IsProcTarget()
        {
            // 文字列が部分一致したら処理
            Prefix1 = SourceFile1Prefix + Path.GetExtension(TargetFileName);
            Prefix2 = SourceFile2Prefix + Path.GetExtension(TargetFileName);
            if (TargetFileName.IndexOf(Prefix1) == -1)
            {
                // 対象外のファイル
                return false;
            }
            return true;
        }

        public bool CreateMergeSourceFile()
        {
            SourceFileFullName = SourceFolderPath + @"\" + TargetFileName;
            SourceBackUpFullName = BackUpDirPath + @"\" + TargetFileName;

            if (!File.Exists(SourceFileFullName))
            {
                ErrorLog += "ファイルが存在しません。" + SourceFileFullName + Environment.NewLine;
                return false;
            }
            File.Copy(SourceFileFullName, SourceBackUpFullName, true);
            return true;
        }

        public bool CreateMergeTargetFile()
        {
            String MergeFileName = TargetFileName.Replace(Prefix1, Prefix2);
            MergeFileFullName = SourceFolderPath + @"\" + MergeFileName;
            MergeBackUpFullName = BackUpDirPath + @"\" + MergeFileName;

            if (!File.Exists(MergeFileFullName))
            {
                ErrorLog += "ファイルが存在しません。" + MergeFileFullName + Environment.NewLine;
                return false;
            }
            return true;
        }

        // "-"なら既定値、それ以外は数値として読む
        public int GetHeight(String heightStr, int defaultHeight = 0)
        {
            if (heightStr == "-")
            {
                return defaultHeight;
            }
            return int.Parse(heightStr);
        }

        public bool MergeExecute()
        {
            // キャンバス作成
            PicEdit mrg = new PicEdit(SourceBackUpFullName);

            mrg.CreateSourceImg(MergeFileFullName);
            Drawing.Size sz = mrg.GetCanvasSize();

            for (int i = 0; i < TrimHeightRanges.Length; i++)
            {
                if (TrimHeightRanges[i] == String.Empty)
                {
                    continue;
                }

                string[] range = TrimHeightRanges[i].Split(new[] { "," }, StringSplitOptions.None);
                if (range.Length != 2)
                {
                    // 想定外の値
                    MessageBoxResult dr = MessageBox.Show("フォーマットが不正です。[" + TrimHeightRanges[i] + "]" +
                        Environment.NewLine + "処理を中断しますか？",
                        "Error",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error);
                    if (dr == MessageBoxResult.Yes)
                    {
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }

                int startHeight = GetHeight(range[0], 0);
                int endHeight = GetHeight(range[1], sz.Height);
                if (sz.Height < startHeight)
                {
                    // 画像サイズよりも指定されたサイズが大きい
                    break;
                }
                Drawing.Rectangle cutParam = new Drawing.Rectangle(0, startHeight, sz.Width, endHeight - startHeight);
                mrg.MergeExec(cutParam);
            }

            mrg.ReleaseSourceImg();

            // キャンバス保存
            mrg.SaveCanvas(SourceFileFullName);

            // マージ元ファイルをバックアップへ移動
            File.Move(MergeFileFullName, MergeBackUpFullName);

            mrg.Dispose();
            return true;
        }

        public String GetErrorMessage()
        {
            return ErrorLog;
        }
    }

    // PictMerge
    public partial class MainWindow
    {
        private void MergeExec()
        {
            debug.WriteData("MergeExec_Click" + Environment.NewLine, false);

            // キャンセル
            if (bkgWorkerMerge.IsBusy)
            {
                bkgWorkerMerge.CancelAsync();
                return;
            }

            String backUpDirPath = pm_SourceFolderPath.Text + @"\" + @"Bk_Merge";
            if (!fio.EnsureDirectory(backUpDirPath))
            {
                MessageBox.Show("無効なフォルダパスです。\n" + backUpDirPath);
                return;
            }

            InitProgressBar(pm_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            MergeWorkerParam param = new MergeWorkerParam
            {
                BackUpDirPath = backUpDirPath,
                SourceFolderPath = pm_SourceFolderPath.Text,
                SourceFile1Prefix = pm_SourceFile1Prefix.Text,
                SourceFile2Prefix = pm_SourceFile2Prefix.Text,
            };

            debug.WriteData("BackUpDirPath = " + backUpDirPath);
            debug.WriteData("SourceFolderPath = " + pm_SourceFolderPath.Text);
            debug.WriteData("Prefix1 = " + pm_SourceFile1Prefix.Text);
            debug.WriteData("Prefix2 = " + pm_SourceFile2Prefix.Text);

            // 切断基準となる高さ
            param.TrimHeightRanges = pm_TrimingHeight.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            // ListBoxの値を配列で取得
            param.TargetFileNames = WpfUtils.GetSelectedStrArray(pm_ListBox_ListUp);

            SetStartTime();
            pm_MergeExec_Click.Content = "中断";
            bkgWorkerMerge.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void ListupPictMerge()
        {
            ListupFolderFiles(pm_SourceFolderPath, pm_ListBox_ListUp);
        }

        private void bkgWorkerMerge_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない

            // senderの値はbgWorkerの値と同じ
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            MergeWorkerParam param = (MergeWorkerParam)e.Argument;

            PictMerge pm = new PictMerge
            {
                BackUpDirPath = param.BackUpDirPath,
                SourceFolderPath = param.SourceFolderPath,
                SourceFile1Prefix = param.SourceFile1Prefix,
                SourceFile2Prefix = param.SourceFile2Prefix,
                TrimHeightRanges = param.TrimHeightRanges,
            };
            String[] targetFileNames = param.TargetFileNames;

            for (int itemIdx = 0; itemIdx < targetFileNames.Length; itemIdx++)
            {
                if (!pm.SetTargetFileName(targetFileNames[itemIdx]))
                {
                    continue;
                }

                if (!pm.IsProcTarget())
                {
                    continue;
                }

                if (!pm.CreateMergeSourceFile())
                {
                    continue;
                }

                if (!pm.CreateMergeTargetFile())
                {
                    continue;
                }

                pm.MergeExecute();
                worker.ReportProgress(itemIdx);      // ⇒ProgressChanged()
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
            }
            e.Result = pm.GetErrorMessage();
            worker.ReportProgress(targetFileNames.Length);      // ⇒ProgressChanged()
        }

        private void bkgWorkerMerge_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
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
            else
            {
                String result = e.Result.ToString();
                if (result != String.Empty)
                {
                    MessageBox.Show("処理中にエラーが発生しました。" + Environment.NewLine + result,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            TextBox_Status.Text += " 完了";
            pm_MergeExec_Click.Content = "結合";

            // リスト更新
            ListupPictMerge();
        }
    }
}
