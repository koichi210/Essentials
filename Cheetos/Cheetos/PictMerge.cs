using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Picture;
using StandardTemplate.Wpf;
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

        // 開始前の確認メッセージで、一部の画像だけで不正な行1つにつき並べる画像名の数
        private const int _maxImageNamesInMessage = 10;

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
            // バックアップ先は毎回新しいフォルダなので、前のバックアップを上書きすることはない(念のため同名なら別名にする)
            _sourceBackUpFullName = Logic.GetNonConflictFilePath(_sourceBackUpFullName);
            File.Copy(_sourceFileFullName, _sourceBackUpFullName, false);
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
        // imageHeightを渡すと、終了がその高さより大きい行も返す(省略時は高さでは判定しない)。
        // 結合開始前の確認(画像ごとの判定)はClassifyInvalidTrimHeightsで行う
        public static String[] FindInvalidTrimHeights(String[] trimHeights, int imageHeight = int.MaxValue)
        {
            return trimHeights.Where(line => line != String.Empty && !IsValidTrimHeight(SplitTrimHeight(line), imageHeight)).ToArray();
        }

        // 選択された結合対象(接頭辞1が付いたもの)の、画像名と高さの一覧。
        // 開始前の確認(終了が画像の高さより大きい行)に使う。無いファイル・画像として読めないファイルは入れない
        public static List<KeyValuePair<String, int>> GetImageHeights(String sourceFolderPath, String sourceFile1Prefix, String[] targetFileNames)
        {
            PictMerge pm = new PictMerge
            {
                SourceFolderPath = sourceFolderPath,
                SourceFile1Prefix = sourceFile1Prefix,
            };
            List<KeyValuePair<String, int>> imageHeights = new List<KeyValuePair<String, int>>();
            foreach (String targetFileName in targetFileNames)
            {
                if (!pm.SetTargetFileName(targetFileName) || !pm.IsProcTarget())
                {
                    continue;
                }
                String filePath = sourceFolderPath + @"\" + targetFileName;
                if (!File.Exists(filePath))
                {
                    continue;
                }
                try
                {
                    // 高さだけ分かればよいので、画像データの検証はしない(ヘッダだけ読む)
                    using (FileStream fs = File.OpenRead(filePath))
                    using (Drawing.Image img = Drawing.Image.FromStream(fs, false, false))
                    {
                        imageHeights.Add(new KeyValuePair<String, int>(targetFileName, img.Height));
                    }
                }
                catch (ArgumentException)
                {
                    // 画像でないファイルは結合時のエラーに任せる
                }
                catch (IOException)
                {
                    // 読めないファイルは結合時のエラーに任せる
                }
            }
            return imageHeights;
        }

        // 不正な行を、全部の画像で不正な行(invalidForAll)と、一部の画像だけで不正な行とその画像名(invalidForSome)に分ける。
        // 書式が違う・開始が負の数・終了が開始より小さい行は、画像によらず全部の画像で不正。
        // 終了が画像の高さより大きいかは画像ごとに判定する(以前はいちばん低い画像の高さで判定していたので、
        // 高い画像では有効な行まで不正として挙がっていた)。結合時もその画像で不正な行だけを飛ばす
        public static void ClassifyInvalidTrimHeights(String[] trimHeights, List<KeyValuePair<String, int>> imageHeights,
            out List<String> invalidForAll, out List<KeyValuePair<String, String[]>> invalidForSome)
        {
            invalidForAll = new List<String>();
            invalidForSome = new List<KeyValuePair<String, String[]>>();
            foreach (String line in trimHeights)
            {
                if (line == String.Empty)
                {
                    continue;
                }
                String[] range = SplitTrimHeight(line);
                if (!IsValidTrimHeight(range, int.MaxValue))
                {
                    invalidForAll.Add(line);
                    continue;
                }
                String[] invalidImageNames = imageHeights
                    .Where(imageHeight => !IsValidTrimHeight(range, imageHeight.Value))
                    .Select(imageHeight => imageHeight.Key)
                    .ToArray();
                if (invalidImageNames.Length == 0)
                {
                    continue;
                }
                if (invalidImageNames.Length == imageHeights.Count)
                {
                    invalidForAll.Add(line);
                }
                else
                {
                    invalidForSome.Add(new KeyValuePair<String, String[]>(line, invalidImageNames));
                }
            }
        }

        // 開始前の確認メッセージ。不正な行が無ければString.Empty
        public static String BuildInvalidTrimHeightsMessage(String[] trimHeights, List<KeyValuePair<String, int>> imageHeights)
        {
            List<String> invalidForAll;
            List<KeyValuePair<String, String[]>> invalidForSome;
            ClassifyInvalidTrimHeights(trimHeights, imageHeights, out invalidForAll, out invalidForSome);
            if (invalidForAll.Count == 0 && invalidForSome.Count == 0)
            {
                return String.Empty;
            }

            StringBuilder message = new StringBuilder();
            message.Append("フォーマットが不正な行(「開始,終了」の形でない・開始が負の数・終了が開始より小さい・終了が画像の高さより大きい)があります。").AppendLine();
            if (invalidForAll.Count > 0)
            {
                message.Append("全部の画像で不正: [").Append(String.Join("] [", invalidForAll)).Append("]").AppendLine();
            }
            if (invalidForSome.Count > 0)
            {
                message.Append("一部の画像だけで不正(終了がその画像の高さより大きい):").AppendLine();
                foreach (KeyValuePair<String, String[]> item in invalidForSome)
                {
                    message.Append("  [").Append(item.Key).Append("] ").Append(String.Join(", ", item.Value.Take(_maxImageNamesInMessage)));
                    if (item.Value.Length > _maxImageNamesInMessage)
                    {
                        message.Append(" ほか").Append(item.Value.Length - _maxImageNamesInMessage).Append("件");
                    }
                    message.AppendLine();
                }
            }
            message.Append("不正な行を(その画像では)飛ばして結合しますか？");
            return message.ToString();
        }

        private static bool IsValidTrimHeight(String[] range, int imageHeight)
        {
            int height;
            return range.Length == 2
                && range.All(text => text == "-" || int.TryParse(text, out height))
                && !IsOutOfRange(range, imageHeight);
        }

        // 開始が負の数、終了が数値で開始("-"は0)より小さい、終了が画像の高さより大きい、のどれかか。
        // 以前は高さがマイナスの矩形や、画像からはみ出した矩形のまま描いていた
        // (終了が"-"(画像の高さ)の場合は、開始が画像より下なら結合時に打ち切る)
        private static bool IsOutOfRange(String[] range, int imageHeight)
        {
            int startHeight = (range[0] == "-") ? 0 : int.Parse(range[0]);
            if (startHeight < 0)
            {
                return true;
            }
            if (range[1] == "-")
            {
                return false;
            }
            int endHeight = int.Parse(range[1]);
            return endHeight < startHeight || imageHeight < endHeight;
        }

        private static String[] SplitTrimHeight(String line)
        {
            return line.Split(_rangeSeparator, StringSplitOptions.None);
        }

        public bool MergeExecute()
        {
            String outputFileName = GetMergedFileName(_targetFileName, SourceFile1Prefix);
            String outputFullName = (outputFileName == _targetFileName)
                ? _sourceFileFullName
                : Logic.GetNonConflictFilePath(SourceFolderPath + @"\" + outputFileName);

            // キャンバス作成
            using (PicEdit mrg = new PicEdit(_sourceBackUpFullName))
            {
                mrg.CreateSourceImg(_mergeFileFullName);
                Drawing.Size sz = mrg.GetCanvasSize();

                foreach (String line in TrimHeightRanges)
                {
                    // 不正な行は開始前にUIスレッドで確認済み(BuildInvalidTrimHeightsMessage)なので、ここでは飛ばすだけ
                    if (line == String.Empty)
                    {
                        continue;
                    }
                    String[] range = SplitTrimHeight(line);
                    if (!IsValidTrimHeight(range, sz.Height))
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

                // キャンバス保存。識別子1を除いた名前で保存する(同じ名前のファイルが既にあれば「 (2)」などを付けて上書きしない)
                mrg.SaveCanvas(outputFullName);
            }
            // ここ(usingの外)では画像はすべて解放済みで、ファイルのロックも外れている

            // 別の名前で保存したときは、結合先の元ファイルを消す(バックアップへコピー済み)
            if (outputFullName != _sourceFileFullName)
            {
                File.Delete(_sourceFileFullName);
            }

            // マージ元ファイルをバックアップへ移動。
            // 前のバックアップは上書きしない(バックアップ先は毎回新しいフォルダ。念のため同名があれば別名にする。
            // File.Moveは移動先があると例外になり、結合元だけ結合済みの状態で処理が止まっていた)
            File.Move(_mergeFileFullName, Logic.GetNonConflictFilePath(_mergeBackUpFullName));
            return true;
        }

        // 結合した画像の保存名。結合先の名前から識別子1を除いたもの(001_1.jpg → 001.jpg)。
        // 識別子1が空、または除くと名前が無くなる(識別子1+拡張子だけの名前)ときは、今まで通り結合先の名前のまま
        public static String GetMergedFileName(String targetFileName, String sourceFile1Prefix)
        {
            if (String.IsNullOrEmpty(sourceFile1Prefix))
            {
                return targetFileName;
            }
            String suffix = sourceFile1Prefix + Path.GetExtension(targetFileName);
            int idx = targetFileName.LastIndexOf(suffix, StringComparison.Ordinal);
            if (idx <= 0)
            {
                return targetFileName;
            }
            return targetFileName.Substring(0, idx) + targetFileName.Substring(idx + sourceFile1Prefix.Length);
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

            // ListBoxの値を配列で取得
            String[] targetFileNames = WpfControlUtils.GetSelectedNames(pm_ListBox_ListUp).ToArray();

            // 切断基準となる高さ。書式の確認はバックグラウンドでは聞けないため、開始前にここで1回だけ行う
            // (終了が画像の高さより大きい行も不正とするため、選択された画像ごとの高さで確認する)
            List<KeyValuePair<String, int>> imageHeights = global::Cheetos.PictMerge.GetImageHeights(pm_SourceFolderPath.Text, pm_SourceFile1Prefix.Text, targetFileNames);
            String[] trimHeightRanges = BuildTrimHeightRanges(imageHeights);
            if (trimHeightRanges == null)
            {
                return;
            }
            String invalidTrimHeightsMessage = global::Cheetos.PictMerge.BuildInvalidTrimHeightsMessage(trimHeightRanges, imageHeights);
            if (invalidTrimHeightsMessage != String.Empty)
            {
                MessageBoxResult dr = MessageBox.Show(invalidTrimHeightsMessage,
                    "Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                if (dr != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // バックアップ先は毎回新しいフォルダにする(入力の確認が済んでから作る)
            String backUpDirPath = CreateBackupDirectoryWithMessage(pm_SourceFolderPath.Text, "Merge");
            if (backUpDirPath == null)
            {
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
                TrimHeightRanges = trimHeightRanges,
                TargetFileNames = targetFileNames,
            };

            _debug.WriteData("BackUpDirPath = " + backUpDirPath);
            _debug.WriteData("SourceFolderPath = " + pm_SourceFolderPath.Text);
            _debug.WriteData("Prefix1 = " + pm_SourceFile1Prefix.Text);
            _debug.WriteData("Prefix2 = " + pm_SourceFile2Prefix.Text);

            SetStartTime();
            pm_Button_Merge.Content = "中断";
            _bkgWorkerMerge.RunWorkerAsync(param);   // ⇒DoWork()
        }

        // 「指定方法」に合わせて、切り取る範囲を今までと同じ「開始,終了」の行にする。
        // 入力が足りない・数値でないときはメッセージを出してnullを返す
        private String[] BuildTrimHeightRanges(List<KeyValuePair<String, int>> imageHeights)
        {
            if (pm_Radio_TrimStarts.IsChecked == true)
            {
                int height;
                if (!TryGetIntWithMessage(pm_PitchHeight, "高さ", 1, out height))
                {
                    return null;
                }
                String[] lines;
                try
                {
                    lines = TrimHeightPattern.BuildFromStarts(pm_TrimmingHeight.Text, height);
                }
                catch (FormatException ex)
                {
                    MessageBox.Show(ex.Message);
                    return null;
                }
                if (lines.Length == 0)
                {
                    MessageBox.Show("開始位置が1つもありません");
                    return null;
                }
                return lines;
            }

            if (pm_Radio_TrimPitch.IsChecked == true)
            {
                int start;
                int pitch;
                int height;
                int count = 0;
                if (!TryGetIntWithMessage(pm_PitchStart, "開始", 0, out start)
                    || !TryGetIntWithMessage(pm_PitchInterval, "間隔", 1, out pitch)
                    || !TryGetIntWithMessage(pm_PitchHeight, "高さ", 1, out height)
                    || (pm_PitchCount.Text != String.Empty && !TryGetIntWithMessage(pm_PitchCount, "回数", 1, out count)))
                {
                    return null;
                }

                // 回数が空欄なら、いちばん低い画像の下端に収まるところまで並べる
                int imageHeight = 0;
                if (count == 0)
                {
                    if (imageHeights.Count == 0)
                    {
                        MessageBox.Show("回数が空欄のときは画像の高さまで並べますが、高さの分かる画像(マージ先)が選ばれていません");
                        return null;
                    }
                    imageHeight = imageHeights.Min(item => item.Value);
                }

                String[] lines = TrimHeightPattern.BuildFromPitch(start, pitch, height, count, imageHeight);
                if (lines.Length == 0)
                {
                    MessageBox.Show("切り取る範囲が1つもできません(開始＋高さが画像の高さを超えています)");
                    return null;
                }
                return lines;
            }

            return pm_TrimmingHeight.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        }

        // 指定方法に合わせて、使う欄だけ入力できるようにする
        private void pm_Radio_TrimMode_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePictMergeModeControls();
        }

        private void UpdatePictMergeModeControls()
        {
            // InitializeComponentの途中(XAMLのIsChecked="True")でも呼ばれるので、まだ作られていないコントロールは触らない
            if (pm_TrimmingHeight == null || pm_PitchCount == null || pm_Label_TrimmingHeight == null)
            {
                return;
            }
            Boolean isStarts = (pm_Radio_TrimStarts.IsChecked == true);
            Boolean isPitch = (pm_Radio_TrimPitch.IsChecked == true);

            pm_PitchStart.IsEnabled = isPitch;
            pm_PitchInterval.IsEnabled = isPitch;
            pm_PitchHeight.IsEnabled = isStarts || isPitch;
            pm_PitchCount.IsEnabled = isPitch;
            pm_TrimmingHeight.IsEnabled = !isPitch;
            pm_Label_TrimmingHeight.Content = isStarts ? "開始位置の一覧：" : "切取る高さの範囲：";
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
                // キャンセルされてないかチェック。結合した項目のあとでだけ見ていたため、
                // 飛ばす項目(対象外・ファイルが無い)が続く間は中断できなかった。各項目の前で見る
                // (breakだとループ後の全件分の報告まで進み、中断しても「N/N」と出ていた。Trim/Orientと同じくreturnする)
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                // 空行・対象外・ファイルが無いものは飛ばす
                if (!pm.SetTargetFileName(targetFileNames[itemIdx])
                    || !pm.IsProcTarget()
                    || !pm.BackUpSourceFile()
                    || !pm.ResolveMergeFilePath())
                {
                    continue;
                }

                pm.MergeExecute();
                worker.ReportProgress(itemIdx + 1);      // ⇒ProgressChanged()(終わった件数)
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
