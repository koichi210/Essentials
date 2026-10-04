using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArranger
{
    // フォルダ振り分けタブ(pf)の処理(WinForms版Form1.PartitionFile.csから移植)
    public partial class MainWindow
    {
        private void pf_textBox_TargetFile_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(pf_textBox_TargetFile.Text, e);
        }

        private void pf_textBox_ReferenceFile_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(pf_textBox_ReferenceFile.Text, e);
        }

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
            if (!IsValidFolderPath(pf_textBox_TargetFile.Text, showErrorPopup))
            {
                return;
            }

            // 移動元フォルダをリストアップ
            String[] files = Directory.GetFiles(pf_textBox_TargetFile.Text);
            pf_listView_Target.Items.Clear();
            foreach (String fileName in GetSortedDisplayNames(files, pf_textBox_TargetFile.Text))
            {
                pf_listView_Target.Items.Add(new ListViewRow(fileName, "", ""));
            }
            pf_label_TotalNum.Text = "ファイル数：" + files.Length.ToString();

            ResizePartitionColumnsEvenly();
        }

        private void pf_listView_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearPartitionMoveNames();
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            pf_label_SelectNum.Text = "選択数：" + selectedIndices.Count.ToString();

            UpdatePartitionFileList();

            // 単独ファイル選択時はコンボボックスに表示する
            if (selectedIndices.Count != 0)
            {
                int idx = selectedIndices[0];
                pf_comboBox_MoveDestDirName.Text = ((ListViewRow)pf_listView_Target.Items[idx])[_partitionMoveDestIdx];
            }
        }

        private Boolean GetPartitionNameFromListView(ref String srcFolderName, ref String targetFolderName, String srcFileName)
        {
            int sameIdx = _util.FindSelectedRowIndex(pf_listView_Target, _partitionTargetIdx, srcFileName, pf_textBox_TargetSeparator.Text, true);
            if (0 <= sameIdx)
            {
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[sameIdx];
                srcFolderName = row[_partitionMoveSrcIdx];
                targetFolderName = row[_partitionMoveDestIdx];
            }

            return targetFolderName != String.Empty;
        }

        private Boolean GetPartitionNameFromComboBox(ref String srcFolderName, ref String targetFolderName, String srcFileName)
        {
            targetFolderName = WpfControlHelper.FindStringFromComboBox(pf_comboBox_MoveDestDirName, srcFileName, pf_textBox_TargetSeparator.Text, true);
            if (targetFolderName != String.Empty)
            {
                // 期待するフォルダ名が見つかった
                srcFolderName = targetFolderName;

                // 数値をインクリした文字列
                targetFolderName = GetPartitionTargetNameWithNumber(srcFolderName, srcFileName, "0");
            }

            return targetFolderName != String.Empty;
        }

        private void CreatePartitionName(ref String srcFolderName, ref String targetFolderName, String srcFileName)
        {
            // 期待するフォルダ名が見つからなかった
            srcFolderName = "";
            String sampleSrcFolderName = _util.CreateNewFolderName(srcFileName, pf_textBox_TargetSeparator.Text, true);
            sampleSrcFolderName += cmn_textBox_AddListSuffix.Text;

            // 数値を考慮した文字列
            targetFolderName = GetPartitionTargetNameWithNumber(sampleSrcFolderName, srcFileName, "0");    // 複数ファイル選択時にインクリしてくれる
        }

        private String GetPartitionTargetNameWithNumber(String srcFolderName, String srcFileName, String defaultNumber)
        {
            long srcNumber = _util.GetNumberFromRear(srcFolderName, pf_textBox_SearchTitleLine.Text, pf_textBox_SearchTitleLength.Text, defaultNumber);
            int addCount = Logic.GetAddCount(pf_listView_Target, srcFileName, pf_textBox_TargetSeparator.Text, true);

            String number = Logic.ToPaddedNumberString(srcNumber, addCount);
            int srcNumberDigits = Logic.GetPaddingDigits(srcNumber);

            return srcFolderName.Substring(0, srcFolderName.Length - srcNumberDigits) + number;
        }

        private void UpdatePartitionFileList()
        {
            foreach (int idx in WpfControlHelper.GetSelectedIndices(pf_listView_Target))
            {
                // 参照しているListViewのIdx
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[idx];

                String srcFileName = row[_partitionTargetIdx];
                String srcFolderName = "";
                String targetFolderName = "";

                // ListViewに既出であれば流用
                Boolean isSuccess = GetPartitionNameFromListView(ref srcFolderName, ref targetFolderName, srcFileName);

                if (!isSuccess)
                {
                    // ListViewに無ければComboBoxから検索
                    isSuccess = GetPartitionNameFromComboBox(ref srcFolderName, ref targetFolderName, srcFileName);
                }

                if (!isSuccess && pf_checkBox_CreateNewDir.IsChecked == true)
                {
                    // ComboBoxにもなかったら新規作成
                    CreatePartitionName(ref srcFolderName, ref targetFolderName, srcFileName);
                }

                row[_partitionMoveSrcIdx] = srcFolderName;
                row[_partitionMoveDestIdx] = targetFolderName;
            }
        }

        private void pf_button_ClearSelect_Click(object sender, RoutedEventArgs e)
        {
            ClearPartitionMoveNames();
        }

        // 選択解除(移動前名称/移動後名称を空にする)
        private void ClearPartitionMoveNames()
        {
            foreach (ListViewRow row in pf_listView_Target.Items)
            {
                row[_partitionMoveSrcIdx] = "";
                row[_partitionMoveDestIdx] = "";
            }
        }

        private void pf_button_CreateFolderExecute_Click(object sender, RoutedEventArgs e)
        {
            MovePartitionFile();
        }

        private void MovePartitionFile()
        {
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            if (selectedIndices.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            // WinForms版のBackgroundWorkerは実行中に再度RunWorkerAsyncすると例外になっていた。
            // WPF版では実行中なら何もしない(二重実行の防止)
            if (_bgPartition.IsBusy)
            {
                return;
            }

            ResetProgress(selectedIndices.Count);

            // 別スレッドを非同期実行
            PartitionWorkerParam param = new PartitionWorkerParam
            {
                TargetFilePath = pf_textBox_TargetFile.Text,
                TargetDir = pf_textBox_ReferenceFile.Text,
            };

            foreach (int idx in selectedIndices)
            {
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[idx];
                param.Items.Add(new PartitionWorkerParam.Item
                {
                    TargetName = row[_partitionTargetIdx],
                    MoveSrc = row[_partitionMoveSrcIdx],
                    MoveDest = row[_partitionMoveDestIdx],
                });
            }
            _bgPartition.RunWorkerAsync(param);   // ⇒bgPartition_DoWork()
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void pf_listView_Target_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            if (selectedIndices.Count == 0)
            {
                return;
            }

            int idx = selectedIndices[0];
            String dirPath = pf_textBox_ReferenceFile.Text + @"\" + ((ListViewRow)pf_listView_Target.Items[idx])[_partitionMoveSrcIdx];
            if (Directory.Exists(dirPath))
            {
                _util.ExecutePath(dirPath);
            }
        }

        private void pf_listView_Target_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    MovePartitionFile();
                    break;

                case Key.Delete:
                    foreach (int idx in WpfControlHelper.GetSelectedIndices(pf_listView_Target))
                    {
                        // 参照しているListViewのIdx
                        ((ListViewRow)pf_listView_Target.Items[idx])[_partitionMoveDestIdx] = "";
                    }
                    break;

                default:
                    break;
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
                foreach (int idx in WpfControlHelper.GetSelectedIndices(pf_listView_Target))
                {
                    ((ListViewRow)pf_listView_Target.Items[idx])[_partitionMoveDestIdx] = pf_comboBox_MoveDestDirName.Text;
                }
            }
        }

        private void bgPartition_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            PartitionWorkerParam param = (PartitionWorkerParam)e.Argument;
            int itemCount = param.Items.Count;

            // このスレッドから直接MessageBoxを出さず、完了時にUIスレッドへまとめて渡す
            List<String> messages = new List<String>();
            for (int i = 0; i < itemCount; i++)
            {
                String fileName = param.Items[i].TargetName;
                String moveSrc = param.Items[i].MoveSrc;
                String moveDest = param.Items[i].MoveDest;

                if (moveDest == String.Empty)
                {
                    // [移動後名称]が無い項目は処理対象外
                    continue;
                }

                String destPreDirName = param.TargetDir + @"\" + moveSrc;
                String destPostDirName = param.TargetDir + @"\" + moveDest;

                String srcFileName = param.TargetFilePath + @"\" + fileName;
                String destFileName = destPostDirName + @"\" + fileName;

                try
                {
                    // 移動先フォルダを生成
                    if (moveSrc != String.Empty &&          // 移動元がカラじゃない
                        Directory.Exists(destPreDirName)    // フォルダが存在する
                        )
                    {
                        // 元フォルダと先フォルダが違うときだけ移動
                        if (destPreDirName != destPostDirName)
                        {
                            // フォルダ名が変わるのであればリネーム
                            Directory.Move(destPreDirName, destPostDirName);
                        }
                    }
                    else
                    {
                        // 元フォルダが無かったら新規フォルダなので、先フォルダを作成
                        Directory.CreateDirectory(destPostDirName);
                    }

                    // ファイル名の重複回避
                    if (!_util.AvoidFileNameConflict(ref destFileName, i))
                    {
                        messages.Add("ファイル名が重複したので処理をスキップしました：" + fileName);
                        continue;
                    }
                    File.Move(srcFileName, destFileName);
                }
                catch (Exception)
                {
                    messages.Add("エラーが発生したので処理を中断しました。" + Environment.NewLine +
                                 "移動元：" + srcFileName + Environment.NewLine +
                                 "移動先：" + destFileName);
                    break;
                }

                worker.ReportProgress(i);      // ⇒ProgressChanged()
            }
            worker.ReportProgress(itemCount);

            // このメソッドからの戻り値
            e.Result = messages;

            // ⇒RunWorkerCompleted()
        }

        private void bgPartition_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ShowProgress(e.ProgressPercentage);
        }

        private void bgPartition_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                // この場合はe.Resultにはアクセスできない
                MessageBox.Show("キャンセルされました");
            }
            else if (e.Error != null)
            {
                MessageBox.Show("フォルダ分けの途中でエラーが発生しました" + Environment.NewLine + e.Error.Message);
            }
            else
            {
                // 別スレッド側で溜めたメッセージを、UIスレッドであるここでまとめて出す
                List<String> messages = e.Result as List<String>;
                if (messages != null && messages.Count > 0)
                {
                    MessageBox.Show(String.Join(Environment.NewLine, messages.ToArray()),
                                    "Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                }
            }

            // 選択解除
            ClearPartitionMoveNames();

            // リストを更新
            ListupPartitionTargetFiles(false);

            //リファレンスフォルダは自動更新しない
            //UpdateMoveDestDirComboBox();
        }

        private void pf_label_ReferenceFile_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            pf_textBox_ReferenceFile.IsReadOnly = !pf_textBox_ReferenceFile.IsReadOnly;
        }

        private void pf_comboBox_MoveDestDirName_DropDown(object sender, EventArgs e)
        {
            UpdateMoveDestDirComboBox();
        }

        private void pf_textBox_SearchTitleLine_KeyDown(object sender, KeyEventArgs e)
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
