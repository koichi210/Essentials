using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Picture;
using Drawing = System.Drawing;

namespace Cheetos
{
    public class PictMerge
    {
        public String BackUpDirPath { get; set; } = String.Empty;
        public String SourceFolderPath { get; set; } = String.Empty;
        public String SourceFile1Prefix { get; set; } = String.Empty;
        public String SourceFile2Prefix { get; set; } = String.Empty;

        // 切り出す高さの範囲。1要素が"開始,終了"("-"は開始なら0、終了なら画像の高さ)
        public String[] TrimHeightRanges { get; set; }

        private static readonly String[] _rangeSeparator = { "," };

        private String _targetFileName = String.Empty;
        private String _prefix1 = String.Empty;
        private String _prefix2 = String.Empty;
        private String _sourceFileFullName = String.Empty;
        private String _sourceBackUpFullName = String.Empty;
        private String _mergeFileFullName = String.Empty;
        private String _mergeBackUpFullName = String.Empty;
        private readonly StringBuilder _errorLog = new StringBuilder();

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

        public bool IsProcTarget()
        {
            // 文字列が部分一致したら処理
            String extension = Path.GetExtension(_targetFileName);
            _prefix1 = SourceFile1Prefix + extension;
            _prefix2 = SourceFile2Prefix + extension;
            return _targetFileName.IndexOf(_prefix1) != -1;
        }

        public bool BackUpSourceFile()
        {
            _sourceFileFullName = SourceFolderPath + @"\" + _targetFileName;
            _sourceBackUpFullName = BackUpDirPath + @"\" + _targetFileName;

            if (!File.Exists(_sourceFileFullName))
            {
                AppendFileNotFound(_sourceFileFullName);
                return false;
            }
            File.Copy(_sourceFileFullName, _sourceBackUpFullName, true);
            return true;
        }

        public bool ResolveMergeFilePath()
        {
            String mergeFileName = _targetFileName.Replace(_prefix1, _prefix2);
            _mergeFileFullName = SourceFolderPath + @"\" + mergeFileName;
            _mergeBackUpFullName = BackUpDirPath + @"\" + mergeFileName;

            if (!File.Exists(_mergeFileFullName))
            {
                AppendFileNotFound(_mergeFileFullName);
                return false;
            }
            return true;
        }

        private void AppendFileNotFound(String filePath)
        {
            _errorLog.Append("ファイルが存在しません。" + filePath + Environment.NewLine);
        }

        // "-"なら既定値、それ以外は数値として読む
        public int GetHeight(String heightStr, int defaultHeight = 0)
        {
            return (heightStr == "-") ? defaultHeight : int.Parse(heightStr);
        }

        // 「開始,終了」(各値は数値か"-")の形になっていない行を返す。空行は対象外。
        // 確認ダイアログはバックグラウンドから出せないため、結合開始前にUIスレッドで使う
        public static String[] FindInvalidTrimHeights(String[] trimHeights)
        {
            return trimHeights.Where(line => line != String.Empty && !IsValidTrimHeight(line)).ToArray();
        }

        private static bool IsValidTrimHeight(String line)
        {
            return IsValidTrimHeight(SplitTrimHeight(line));
        }

        private static bool IsValidTrimHeight(String[] range)
        {
            int height;
            return range.Length == 2
                && range.All(text => text == "-" || int.TryParse(text, out height));
        }

        private static String[] SplitTrimHeight(String line)
        {
            return line.Split(_rangeSeparator, StringSplitOptions.None);
        }

        public bool MergeExecute()
        {
            // キャンバス作成
            using (PicEdit mrg = new PicEdit(_sourceBackUpFullName))
            {
                mrg.CreateSourceImg(_mergeFileFullName);
                Drawing.Size sz = mrg.GetCanvasSize();

                foreach (String line in TrimHeightRanges)
                {
                    // 不正な行は開始前にUIスレッドで確認済み(FindInvalidTrimHeights)なので、ここでは飛ばすだけ
                    if (line == String.Empty)
                    {
                        continue;
                    }
                    String[] range = SplitTrimHeight(line);
                    if (!IsValidTrimHeight(range))
                    {
                        continue;
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
                mrg.SaveCanvas(_sourceFileFullName);

                // マージ元ファイルをバックアップへ移動
                File.Move(_mergeFileFullName, _mergeBackUpFullName);
            }
            return true;
        }

        public String GetErrorMessage()
        {
            return _errorLog.ToString();
        }
    }

    // PictMerge
    public partial class MainWindow
    {
        private void MergeExec()
        {
            _debug.WriteData("MergeExec_Click" + Environment.NewLine, false);

            // キャンセル
            if (_bkgWorkerMerge.IsBusy)
            {
                _bkgWorkerMerge.CancelAsync();
                return;
            }

            String backUpDirPath = pm_SourceFolderPath.Text + @"\Bk_Merge";
            if (!EnsureDirectoryWithMessage(backUpDirPath))
            {
                return;
            }

            // 切断基準となる高さ。書式の確認はバックグラウンドでは聞けないため、開始前にここで1回だけ行う
            String[] trimHeightRanges = pm_TrimmingHeight.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            String[] invalidTrimHeights = global::Cheetos.PictMerge.FindInvalidTrimHeights(trimHeightRanges);
            if (invalidTrimHeights.Length > 0)
            {
                MessageBoxResult dr = MessageBox.Show("フォーマットが不正な行があります。" + Environment.NewLine
                    + "[" + String.Join("] [", invalidTrimHeights) + "]" + Environment.NewLine
                    + "不正な行を飛ばして結合しますか？",
                    "Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                if (dr != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            InitProgressBar(pm_ListBox_ListUp.SelectedItems.Count);

            // 別スレッドを非同期実行
            MergeWorkerParam param = new MergeWorkerParam
            {
                BackUpDirPath = backUpDirPath,
                SourceFolderPath = pm_SourceFolderPath.Text,
                SourceFile1Prefix = pm_SourceFile1Prefix.Text,
                SourceFile2Prefix = pm_SourceFile2Prefix.Text,
                TrimHeightRanges = trimHeightRanges,

                // ListBoxの値を配列で取得
                TargetFileNames = WpfUtils.GetSelectedStrArray(pm_ListBox_ListUp),
            };

            _debug.WriteData("BackUpDirPath = " + backUpDirPath);
            _debug.WriteData("SourceFolderPath = " + pm_SourceFolderPath.Text);
            _debug.WriteData("Prefix1 = " + pm_SourceFile1Prefix.Text);
            _debug.WriteData("Prefix2 = " + pm_SourceFile2Prefix.Text);

            SetStartTime();
            pm_Button_Merge.Content = "中断";
            _bkgWorkerMerge.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void ListUpPictMerge()
        {
            ListUpFolderFiles(pm_SourceFolderPath, pm_ListBox_ListUp);
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
                // 空行・対象外・ファイルが無いものは飛ばす
                if (!pm.SetTargetFileName(targetFileNames[itemIdx])
                    || !pm.IsProcTarget()
                    || !pm.BackUpSourceFile()
                    || !pm.ResolveMergeFilePath())
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
            if (ShowWorkerCompletion(e))
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
            pm_Button_Merge.Content = "結合";

            // リスト更新
            ListUpPictMerge();
        }
    }
}
