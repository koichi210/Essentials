using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArranger
{
    // フォルダ振り分けタブ(pf)の処理(WinForms版Form1.PartitionFile.csから移植)
    public partial class MainWindow
    {
        private void pf_button_Listup_Target_Click(object sender, RoutedEventArgs e)
        {
            ListupPartitionTargetFiles();
        }

        private void ResizePartitionColumnsEvenly()
        {
            // 列(対象/移動前名称/移動後名称)はXAMLで定義済み。幅はListViewの幅を等分する
            double width = pf_listView_Target.ActualWidth;
            if (width <= 0)
            {
                return;
            }
            // 枠線の分だけ引いて、横スクロールバーが出ないようにする
            double column = Math.Max((width - 8) / 3, 20);
            pf_column_Target.Width = column;
            pf_column_MoveSrc.Width = column;
            pf_column_MoveDest.Width = column;
        }

        private void pf_listView_Target_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // WinForms版はウィンドウのResizeEndで列幅を等分し直していた
            ResizePartitionColumnsEvenly();
        }

        private void ListupPartitionTargetFiles(bool showErrorPopup = true)
        {
            String[] files = ListupInto(pf_listView_Target, pf_label_TotalNum, "ファイル数", pf_textBox_TargetFile.Text,
                Directory.GetFiles, name => new ListViewRow(name, "", ""), showErrorPopup);
            if (files == null)
            {
                return;
            }

            ResizePartitionColumnsEvenly();
        }

        private void pf_listView_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearPartitionMoveNames();
            List<ListViewRow> selectedRows = UpdatePartitionFileList();
            pf_label_SelectNum.Text = FormatSelectedCount(selectedRows.Count);

            // 単独ファイル選択時はコンボボックスに表示する
            if (selectedRows.Count != 0)
            {
                pf_comboBox_MoveDestDirName.Text = selectedRows[0][_partitionMoveDestIdx];
            }
        }

        // 選択中の各ファイルについて[移動前名称][移動後名称]を決めて表示する。決めた対象(選択行)を返す
        private List<ListViewRow> UpdatePartitionFileList()
        {
            List<ListViewRow> selectedRows = WpfControlHelper.GetSelectedRows(pf_listView_Target);
            // 番号の加算数を数える時に使う、選択中ファイル名の一覧(ループ中は変わらないので1回だけ取る)
            List<String> selectedNames = selectedRows.Select(row => row[_partitionTargetIdx]).ToList();

            foreach (ListViewRow row in selectedRows)
            {
                String srcFileName = row[_partitionTargetIdx];
                String srcFolderName;
                String targetFolderName;

                // ①選択中の別の行で既に決まっていれば流用 ②無ければ振り分け先の候補から検索
                // ③それも無く新規作成ONなら新しいフォルダ名を作る
                if (!TryGetPartitionNameFromListView(selectedRows, srcFileName, out srcFolderName, out targetFolderName)
                    && !TryGetPartitionNameFromComboBox(selectedNames, srcFileName, out srcFolderName, out targetFolderName)
                    && pf_checkBox_CreateNewDir.IsChecked == true)
                {
                    CreatePartitionName(selectedNames, srcFileName, out srcFolderName, out targetFolderName);
                }

                row[_partitionMoveSrcIdx] = srcFolderName;
                row[_partitionMoveDestIdx] = targetFolderName;
            }

            return selectedRows;
        }

        private Boolean TryGetPartitionNameFromListView(List<ListViewRow> selectedRows, String srcFileName, out String srcFolderName, out String targetFolderName)
        {
            ListViewRow sameRow = _util.FindRow(selectedRows, _partitionTargetIdx, srcFileName, pf_textBox_TargetSeparator.Text, true);
            srcFolderName = sameRow != null ? sameRow[_partitionMoveSrcIdx] : "";
            targetFolderName = sameRow != null ? sameRow[_partitionMoveDestIdx] : "";

            return targetFolderName != String.Empty;
        }

        private Boolean TryGetPartitionNameFromComboBox(List<String> selectedNames, String srcFileName, out String srcFolderName, out String targetFolderName)
        {
            String foundFolderName = WpfControlHelper.FindStringFromComboBox(pf_comboBox_MoveDestDirName, srcFileName, pf_textBox_TargetSeparator.Text, true);
            if (foundFolderName == String.Empty)
            {
                srcFolderName = "";
                targetFolderName = "";
                return false;
            }

            // 期待するフォルダ名が見つかった。移動後は番号をインクリした名前にする
            srcFolderName = foundFolderName;
            targetFolderName = GetPartitionTargetNameWithNumber(selectedNames, foundFolderName, srcFileName);
            return targetFolderName != String.Empty;
        }

        // 期待するフォルダ名が見つからなかった時に、ファイル名から新しいフォルダ名を作る
        private void CreatePartitionName(List<String> selectedNames, String srcFileName, out String srcFolderName, out String targetFolderName)
        {
            srcFolderName = "";
            String sampleSrcFolderName = _util.CreateNewFolderName(srcFileName, pf_textBox_TargetSeparator.Text, true)
                + cmn_textBox_AddListSuffix.Text;

            // 数値を考慮した文字列(複数ファイル選択時にインクリしてくれる)
            targetFolderName = GetPartitionTargetNameWithNumber(selectedNames, sampleSrcFolderName, srcFileName);
        }

        private String GetPartitionTargetNameWithNumber(List<String> selectedNames, String srcFolderName, String srcFileName)
        {
            long srcNumber = _util.GetNumberFromRear(srcFolderName, pf_textBox_SearchTitleLine.Text, pf_textBox_SearchTitleLength.Text, "0");
            int addCount = Logic.GetAddCount(selectedNames, srcFileName, pf_textBox_TargetSeparator.Text, true);

            String number = Logic.ToPaddedNumberString(srcNumber, addCount);
            int srcNumberDigits = Logic.GetPaddingDigits(srcNumber);

            return srcFolderName.Substring(0, srcFolderName.Length - srcNumberDigits) + number;
        }

        private void pf_button_ClearSelect_Click(object sender, RoutedEventArgs e)
        {
            ClearPartitionMoveNames();
        }

        // 選択解除(移動前名称/移動後名称を空にする)
        private void ClearPartitionMoveNames()
        {
            WpfControlHelper.SetColumnText(pf_listView_Target.Items.Cast<ListViewRow>(), "", _partitionMoveSrcIdx, _partitionMoveDestIdx);
        }

        // 選択中の行の[移動後名称]をまとめて書き換える
        private void SetSelectedMoveDestName(String moveDestName)
        {
            WpfControlHelper.SetColumnText(WpfControlHelper.GetSelectedRows(pf_listView_Target), moveDestName, _partitionMoveDestIdx);
        }

        private void pf_button_CreateFolderExecute_Click(object sender, RoutedEventArgs e)
        {
            MovePartitionFile();
        }

        private void MovePartitionFile()
        {
            List<ListViewRow> selectedRows = WpfControlHelper.GetSelectedRows(pf_listView_Target);
            if (!HasSelectedItems(selectedRows.Count))
            {
                return;
            }

            // WinForms版のBackgroundWorkerは実行中に再度RunWorkerAsyncすると例外になっていた。
            // WPF版では実行中なら何もしない(二重実行の防止)
            if (_bgPartition.IsBusy)
            {
                return;
            }

            // 別スレッドを非同期実行
            PartitionWorkerParam param = new PartitionWorkerParam
            {
                TargetFilePath = pf_textBox_TargetFile.Text,
                TargetDir = pf_textBox_ReferenceFile.Text,
            };
            param.Items.AddRange(selectedRows.Select(row => new PartitionWorkerParam.Item
            {
                TargetName = row[_partitionTargetIdx],
                MoveSrc = row[_partitionMoveSrcIdx],
                MoveDest = row[_partitionMoveDestIdx],
            }));

            ResetProgress(param.Items.Count);
            _bgPartition.RunWorkerAsync(param);   // ⇒bgPartition_DoWork()
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void pf_listView_Target_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            ListViewRow firstRow = WpfControlHelper.GetSelectedRows(pf_listView_Target).FirstOrDefault();
            if (firstRow == null)
            {
                return;
            }

            String dirPath = Path.Combine(pf_textBox_ReferenceFile.Text, firstRow[_partitionMoveSrcIdx]);
            if (Directory.Exists(dirPath))
            {
                _util.ExecutePath(dirPath);
            }
        }

        private void pf_listView_Target_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                MovePartitionFile();
            }
            else if (e.Key == Key.Delete)
            {
                SetSelectedMoveDestName("");
            }
        }

        private void pf_comboBox_MoveDestDirName_KeyUp(object sender, KeyEventArgs e)
        {
            if (IsCtrlEnter(e))
            {
                MovePartitionFile();
            }
            else
            {
                SetSelectedMoveDestName(pf_comboBox_MoveDestDirName.Text);
            }
        }

        private void bgPartition_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない
            BackgroundWorker worker = (BackgroundWorker)sender;
            PartitionWorkerParam param = (PartitionWorkerParam)e.Argument;

            // このスレッドから直接MessageBoxを出さず、完了時にUIスレッドへまとめて渡す
            List<String> messages = new List<String>();
            for (int i = 0; i < param.Items.Count; i++)
            {
                PartitionWorkerParam.Item item = param.Items[i];

                // ここまでに終わった件数(スキップした項目も1件と数える)
                worker.ReportProgress(i);      // ⇒bgWorker_ProgressChanged()

                if (item.MoveDest == String.Empty)
                {
                    // [移動後名称]が無い項目は処理対象外
                    continue;
                }

                // 振り分け先フォルダ(リネーム前 / リネーム後)
                String oldDestDir = Path.Combine(param.TargetDir, item.MoveSrc);
                String newDestDir = Path.Combine(param.TargetDir, item.MoveDest);

                String srcFilePath = Path.Combine(param.TargetFilePath, item.TargetName);
                String destFilePath = Path.Combine(newDestDir, item.TargetName);

                try
                {
                    // 移動先フォルダを生成
                    if (item.MoveSrc == String.Empty || !Directory.Exists(oldDestDir))
                    {
                        // 元フォルダが無かったら新規フォルダなので、先フォルダを作成
                        Directory.CreateDirectory(newDestDir);
                    }
                    else if (oldDestDir != newDestDir)
                    {
                        // フォルダ名が変わるのであればリネーム
                        Directory.Move(oldDestDir, newDestDir);
                    }

                    // ファイル名の重複回避
                    if (!_util.AvoidFileNameConflict(ref destFilePath, i))
                    {
                        messages.Add("ファイル名が重複したので処理をスキップしました：" + item.TargetName);
                        continue;
                    }
                    File.Move(srcFilePath, destFilePath);
                }
                catch (Exception)
                {
                    messages.Add("エラーが発生したので処理を中断しました。" + Environment.NewLine +
                                 "移動元：" + srcFilePath + Environment.NewLine +
                                 "移動先：" + destFilePath);
                    break;
                }
            }
            worker.ReportProgress(param.Items.Count);

            // ⇒bgPartition_RunWorkerCompleted()
            e.Result = messages;
        }

        private void bgPartition_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (IsWorkerCompletedNormally(e, "フォルダ分けの途中でエラーが発生しました"))
            {
                // 別スレッド側で溜めたメッセージを、UIスレッドであるここでまとめて出す
                List<String> messages = (List<String>)e.Result;
                if (messages.Count > 0)
                {
                    MessageBox.Show(String.Join(Environment.NewLine, messages),
                                    "Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                }
            }

            // 選択解除
            ClearPartitionMoveNames();

            // リストを更新(リファレンスフォルダは自動更新しない)
            ListupPartitionTargetFiles(false);
        }

        private void pf_label_ReferenceFile_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            pf_textBox_ReferenceFile.IsReadOnly = !pf_textBox_ReferenceFile.IsReadOnly;
        }

        private void pf_comboBox_MoveDestDirName_DropDown(object sender, EventArgs e)
        {
            UpdateMoveDestDirComboBox();
        }

        // pfタブの番号入力欄で共通: Ctrl+Enterでファイル移動
        private void pf_PartitionInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsCtrlEnter(e))
            {
                MovePartitionFile();
            }
        }

        private void pf_checkBox_CreateNewDir_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ListupPartitionTargetFiles(false);
        }
    }
}
