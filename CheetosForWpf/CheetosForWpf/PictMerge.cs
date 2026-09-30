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
        public String BackUpDirPath = String.Empty;
        public String SourceFolderPath = String.Empty;
        public String SourceFile1Prefix = String.Empty;
        public String SourceFile2Prefix = String.Empty;
        public String[] TrimHeightAry = null;

        private String TargetFileName = String.Empty;
        private String Prefix1 = String.Empty;
        private String Prefix2 = String.Empty;
        private String SourceFileFullName = String.Empty;
        private String SourceBackUpFullName = String.Empty;
        private String MergeFileFullName = String.Empty;
        private String MergeBackUpFullName = String.Empty;
        private String ErrorList = String.Empty;

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
                ErrorList += "ファイルが存在しません。" + SourceFileFullName + Environment.NewLine;
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
                ErrorList += "ファイルが存在しません。" + MergeFileFullName + Environment.NewLine;
                return false;
            }
            return true;
        }

        public int GetHeight(String StartHeight, int DefaultHeight = 0)
        {
            if (StartHeight == "-")
            {
                return DefaultHeight;
            }
            return int.Parse(StartHeight);
        }

        public bool MergeExecute()
        {
            // キャンバス作成
            PicEdit mrg = new PicEdit(SourceBackUpFullName);

            mrg.CreateSourceImg(MergeFileFullName);
            Drawing.Size sz = mrg.GetCanvasSize();

            for (int i = 0; i < TrimHeightAry.Length; i++)
            {
                if (TrimHeightAry[i] == String.Empty)
                {
                    continue;
                }

                string[] TrimHeight = TrimHeightAry[i].Split(new[] { "," }, StringSplitOptions.None);
                if (TrimHeight.Length != 2)
                {
                    // 想定外の値
                    MessageBoxResult dr = MessageBox.Show("フォーマットが不正です。[" + TrimHeightAry[i] + "]" +
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

                int StartHeight = GetHeight(TrimHeight[0], 0);
                int EndHeight = GetHeight(TrimHeight[1], sz.Height);
                if (sz.Height < StartHeight)
                {
                    // 画像サイズよりも指定されたサイズが大きい
                    break;
                }
                Drawing.Rectangle CutParam = new Drawing.Rectangle(0, StartHeight, sz.Width, EndHeight - StartHeight);
                mrg.MergeExec(CutParam);
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
            return ErrorList;
        }
    }

    // PictMerge
    public partial class MainWindow
    {
        private void MergeExec()
        {
            Debug.WriteData("MergeExec_Click" + Environment.NewLine, false);

            // キャンセル
            if (bkgWorkerMerge.IsBusy)
            {
                bkgWorkerMerge.CancelAsync();
                return;
            }

            String BackUpDirPath = pm_SourceFolderPath.Text + @"\" + @"Bk_Merge";
            if (!fio.EnsureDirectory(BackUpDirPath))
            {
                MessageBox.Show("無効なフォルダパスです。\n" + BackUpDirPath);
                return;
            }

            InitProgressBar(pm_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            MergeWorkerParam param = new MergeWorkerParam
            {
                BackUpDirPath = BackUpDirPath,
                SourceFolderPath = pm_SourceFolderPath.Text,
                SourceFile1Prefix = pm_SourceFile1Prefix.Text,
                SourceFile2Prefix = pm_SourceFile2Prefix.Text,
            };

            Debug.WriteData("BackUpDirPath = " + BackUpDirPath);
            Debug.WriteData("SourceFolderPath = " + pm_SourceFolderPath.Text);
            Debug.WriteData("Prefix1 = " + pm_SourceFile1Prefix.Text);
            Debug.WriteData("Prefix2 = " + pm_SourceFile2Prefix.Text);

            // 切断基準となる高さ
            param.TrimHeightAry = pm_TrimingHeight.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            // ListBoxの値を配列で取得
            param.TargetNameAry = WpfUtils.GetSelectedStrArray(pm_ListBox_ListUp);

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

            PictMerge pm = new PictMerge();
            pm.BackUpDirPath = param.BackUpDirPath;
            pm.SourceFolderPath = param.SourceFolderPath;
            pm.SourceFile1Prefix = param.SourceFile1Prefix;
            pm.SourceFile2Prefix = param.SourceFile2Prefix;
            pm.TrimHeightAry = param.TrimHeightAry;
            String[] TargetNameAry = param.TargetNameAry;

            for (int ItemIdx = 0; ItemIdx < TargetNameAry.Length; ItemIdx++)
            {
                if (!pm.SetTargetFileName(TargetNameAry[ItemIdx]))
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
                worker.ReportProgress(ItemIdx);      // ⇒ProgressChanged()
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
            }
            e.Result = pm.GetErrorMessage();
            worker.ReportProgress(TargetNameAry.Length);      // ⇒ProgressChanged()
        }

        private void bkgWorkerMerge_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
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
                String Result = e.Result.ToString();
                if (Result != String.Empty)
                {
                    MessageBox.Show("処理中にエラーが発生しました。" + Environment.NewLine + Result,
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    // 処理結果の表示
                    //MessageBox.Show("正常に完了しました");
                }
            }
            TextBox_Status.Text += " 完了";
            pm_MergeExec_Click.Content = "結合";

            // リスト更新
            ListupPictMerge();
        }
    }
}
