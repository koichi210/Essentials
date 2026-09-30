using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArrangerForWpf
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
            ListupTargetMoveDirectory();
        }

        private void pf_listView_Target_Update()
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
            pf_listView_Target_Update();
        }

        private void ListupTargetMoveDirectory(bool IsErrorPopup = true)
        {
            if (!IsValidFolderPath(pf_textBox_TargetFile.Text, IsErrorPopup))
            {
                return;
            }

            // 移動元フォルダをリストアップ
            String[] Files = Directory.GetFiles(pf_textBox_TargetFile.Text);
            pf_listView_Target.Items.Clear();
            List<String> Names = new List<String>();
            for (int i = 0; i < Files.Length; i++)
            {
                Names.Add(GetDisplayName(Files[i], pf_textBox_TargetFile.Text));
            }
            foreach (String FileName in SortedByName(Names))
            {
                pf_listView_Target.Items.Add(new ListViewRow(FileName, "", ""));
            }
            pf_label_TotalNum.Text = "ファイル数：" + Files.Length.ToString();

            pf_listView_Target_Update();
        }

        private void pf_listView_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearPartitionSelect();
            List<int> SelectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            pf_label_SelectNum.Text = "選択数：" + SelectedIndices.Count.ToString();

            UpdatePartitionFileList();

            // 単独ファイル選択時はコンボボックスに表示する
            if (SelectedIndices.Count != 0)
            {
                int Idx = SelectedIndices[0];
                pf_comboBox_MoveDestDirName.Text = ((ListViewRow)pf_listView_Target.Items[Idx])[CreateFolderMoveDestIdx];
            }
        }

        private Boolean GetPartitionNameFromListView(ref String SrcFolderName, ref String TargetFolderName, String SrcFileName)
        {
            Boolean IsSuccess = true;

            int SameIdx = util.GetStringFromListViewInSelect(pf_listView_Target, CreateFolderTargetIdx, SrcFileName, pf_textBox_TargetSeparator.Text, true);
            if (0 <= SameIdx)
            {
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[SameIdx];
                SrcFolderName = row[CreateFolderMoveSrcIdx];
                TargetFolderName = row[CreateFolderMoveDestIdx];
            }

            if (TargetFolderName == String.Empty)
            {
                IsSuccess = false;
            }
            return IsSuccess;
        }

        private Boolean GetPartitionNameFromComboBox(ref String SrcFolderName, ref String TargetFolderName, String SrcFileName)
        {
            Boolean IsSuccess = true;

            TargetFolderName = WpfControlHelper.FindStringFromComboBox(pf_comboBox_MoveDestDirName, SrcFileName, pf_textBox_TargetSeparator.Text, true);
            if (TargetFolderName != String.Empty)
            {
                // 期待するフォルダ名が見つかった
                SrcFolderName = TargetFolderName;

                // 数値をインクリした文字列
                TargetFolderName = GetPartitionTargetNameWithNumber(SrcFolderName, SrcFileName, "0");
            }

            if (TargetFolderName == String.Empty)
            {
                IsSuccess = false;
            }
            return IsSuccess;
        }

        private void CreatePartitionName(ref String SrcFolderName, ref String TargetFolderName, String SrcFileName)
        {
            // 期待するフォルダ名が見つからなかった
            SrcFolderName = "";
            String SampleSrcFolderName = util.CreateNewFolderName(SrcFileName, pf_textBox_TargetSeparator.Text, true);
            SampleSrcFolderName += cmn_textBox_AddListSuffix.Text;

            // 数値を考慮した文字列
            TargetFolderName = GetPartitionTargetNameWithNumber(SampleSrcFolderName, SrcFileName, "0");    // 複数ファイル選択時にインクリしてくれる
        }

        private String GetPartitionTargetNameWithNumber(String SrcFolderName, String SrcFileName, String DefaultNumber)
        {
            long SrcNumber = util.GetNumberFromRear(SrcFolderName, pf_textBox_SearchTitleLine.Text, pf_textBox_SearchTitleLength.Text, DefaultNumber);
            int AddCount = Logic.GetAddCount(pf_listView_Target, SrcFileName, pf_textBox_TargetSeparator.Text, true);

            String Number = Logic.GetNumber(SrcNumber, AddCount);
            int SrcNumberDigit = Logic.GetPadding(SrcNumber);

            return SrcFolderName.Substring(0, SrcFolderName.Length - SrcNumberDigit) + Number;
        }

        private void UpdatePartitionFileList()
        {
            foreach (int idx in WpfControlHelper.GetSelectedIndices(pf_listView_Target))
            {
                // 参照しているListViewのIdx
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[idx];

                String SrcFileName = row[CreateFolderTargetIdx];
                String SrcFolderName = "";
                String TargetFolderName = "";

                Boolean IsSuccess = false;
                // ListViewに既出であれば流量
                IsSuccess = GetPartitionNameFromListView(ref SrcFolderName, ref TargetFolderName, SrcFileName);

                if (!IsSuccess)
                {
                    // ListViewに無ければCombBoxから検索
                    IsSuccess = GetPartitionNameFromComboBox(ref SrcFolderName, ref TargetFolderName, SrcFileName);
                }

                if (!IsSuccess)
                {
                    if (pf_checkBox_CreateNewDir.IsChecked == true)
                    {
                        // CombBoxにもなかったら新規作成
                        CreatePartitionName(ref SrcFolderName, ref TargetFolderName, SrcFileName);
                    }
                }

                row[CreateFolderMoveSrcIdx] = SrcFolderName;
                row[CreateFolderMoveDestIdx] = TargetFolderName;
            }
        }

        private void pf_button_ClearSelect_Click(object sender, RoutedEventArgs e)
        {
            ClearPartitionSelect();
        }

        // 選択解除(移動前名称/移動後名称を空にする)
        private void ClearPartitionSelect()
        {
            foreach (ListViewRow row in pf_listView_Target.Items)
            {
                row[CreateFolderMoveSrcIdx] = "";
                row[CreateFolderMoveDestIdx] = "";
            }
        }

        private void pf_button_CreateFolderExecute_Click(object sender, RoutedEventArgs e)
        {
            MovePartitionFile();
        }

        private void MovePartitionFile()
        {
            List<int> SelectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            if (SelectedIndices.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            // WinForms版のBackgroundWorkerは実行中に再度RunWorkerAsyncすると例外になっていた。
            // WPF版では実行中なら何もしない(二重実行の防止)
            if (bgPartition.IsBusy)
            {
                return;
            }

            progressBar.Maximum = SelectedIndices.Count;
            progressBar.Minimum = 0;
            progressBar.Value = 0;

            // 別スレッドを非同期実行
            PartitionWorkerParam param = new PartitionWorkerParam
            {
                TargetFilePath = pf_textBox_TargetFile.Text,
                TargetDir = pf_textBox_ReferenceFile.Text,
            };

            foreach (int idx in SelectedIndices)
            {
                ListViewRow row = (ListViewRow)pf_listView_Target.Items[idx];
                param.Items.Add(new PartitionWorkerParam.Item
                {
                    TargetName = row[CreateFolderTargetIdx],
                    MoveSrc = row[CreateFolderMoveSrcIdx],
                    MoveDest = row[CreateFolderMoveDestIdx],
                });
            }
            bgPartition.RunWorkerAsync(param);   // ⇒bgPartition_DoWork()
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void pf_listView_Target_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            List<int> SelectedIndices = WpfControlHelper.GetSelectedIndices(pf_listView_Target);
            if (SelectedIndices.Count == 0)
            {
                return;
            }

            int idx = SelectedIndices[0];
            String DirPath = pf_textBox_ReferenceFile.Text + @"\" + ((ListViewRow)pf_listView_Target.Items[idx])[CreateFolderMoveSrcIdx];
            if (Directory.Exists(DirPath))
            {
                util.ExecutePath(DirPath);
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
                        ((ListViewRow)pf_listView_Target.Items[idx])[CreateFolderMoveDestIdx] = "";
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
                    ((ListViewRow)pf_listView_Target.Items[idx])[CreateFolderMoveDestIdx] = pf_comboBox_MoveDestDirName.Text;
                }
            }
        }

        private void bgPartition_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            PartitionWorkerParam param = (PartitionWorkerParam)e.Argument;
            String TargetFilePath = param.TargetFilePath;
            String TargetDir = param.TargetDir;
            int ItemCount = param.Items.Count;

            // このスレッドから直接MessageBoxを出さず、完了時にUIスレッドへまとめて渡す
            List<String> Messages = new List<String>();
            for (int i = 0; i < ItemCount; i++)
            {
                String TargetName = param.Items[i].TargetName;
                String MoveSrc = param.Items[i].MoveSrc;
                String MoveDest = param.Items[i].MoveDest;

                if (MoveDest == String.Empty)
                {
                    // [移動後名称]が無い項目は処理対象外
                    continue;
                }

                String SrcDirName = TargetFilePath;
                String DestPreDirName = TargetDir + @"\" + MoveSrc;
                String DestPostDirName = TargetDir + @"\" + MoveDest;

                String FileName = TargetName;
                String SrcFileName = SrcDirName + @"\" + FileName;
                String DestFileName = DestPostDirName + @"\" + FileName;

                try
                {
                    // 移動先フォルダを生成
                    if (MoveSrc != String.Empty && // 移動元がカラじゃない
                         System.IO.Directory.Exists(DestPreDirName)     // フォルダが存在する
                         )
                    {
                        // 元フォルダと先フォルダが違うときだけ移動
                        if (DestPreDirName != DestPostDirName)
                        {
                            // フォルダ名が変わるのであればリネーム
                            System.IO.Directory.Move(DestPreDirName, DestPostDirName);
                        }
                    }
                    else
                    {
                        // 元フォルダが無かったら新規フォルダなので、先フォルダを作成
                        System.IO.Directory.CreateDirectory(DestPostDirName);
                    }

                    // ファイル名の重複回避
                    if (!util.CreateFileNameOverLapShirk(ref DestFileName, i))
                    {
                        Messages.Add("ファイル名が重複したので処理をスキップしました：" + FileName);
                        continue;
                    }
                    File.Move(SrcFileName, DestFileName);
                }
                catch (Exception)
                {
                    Messages.Add("エラーが発生したので処理を中断しました。" + Environment.NewLine +
                                 "移動元：" + SrcFileName + Environment.NewLine +
                                 "移動先：" + DestFileName);
                    break;
                }

                worker.ReportProgress(i);      // ⇒ProgressChanged()
            }
            worker.ReportProgress(ItemCount);

            // このメソッドからの戻り値
            e.Result = Messages;

            // ⇒RunWorkerCompleted()
        }

        private void bgPartition_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 進捗率の表示
            progressText.Text = e.ProgressPercentage + "/" + progressBar.Maximum + " 完了";
            progressBar.Value = e.ProgressPercentage;
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
                List<String> Messages = e.Result as List<String>;
                if (Messages != null && Messages.Count > 0)
                {
                    MessageBox.Show(String.Join(Environment.NewLine, Messages.ToArray()),
                                    "Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                }
            }

            // 選択解除
            ClearPartitionSelect();

            // リストを更新
            ListupTargetMoveDirectory(false);

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
            ListupTargetMoveDirectory(false);
        }
    }
}
