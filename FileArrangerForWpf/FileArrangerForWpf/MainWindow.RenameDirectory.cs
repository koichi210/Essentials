using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FileArrangerForWpf
{
    // フォルダ名変更タブ(rd)の処理(WinForms版Form1.RenameDirectory.csから移植)
    public partial class MainWindow
    {
        private void rd_button_Listup_Target_Click(object sender, RoutedEventArgs e)
        {
            ListupRenameTargetDirectory();
        }

        private void rd_textBox_ExistItemDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(rd_textBox_ExistItemDir.Text, e);
        }

        private void rd_button_Execute_Rename_Click(object sender, RoutedEventArgs e)
        {
            RenameFolderExecute();
        }

        private void rd_button_RenameFolderRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!pmd.DecrementRegistNumber())
            {
                MessageBox.Show("これ以上復元できません");
                return;
            }

            while (pmd.IsExistRestoreList())
            {
                String SrcName = "";
                String DestName = "";
                pmd.GetRestoreList(ref SrcName, ref DestName);
                Directory.Move(DestName, SrcName);
            }
            ListupRenameTargetDirectory();
        }

        private void RenameFolderExecute()
        {
            List<int> SelectedIndices = WpfControlHelper.GetSelectedIndices(rd_listView_Target);
            if (SelectedIndices.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            int idx = 0;
            for (int i = 0; i < SelectedIndices.Count; i++)
            {
                // 参照しているListViewのIdx
                idx = SelectedIndices[i];
                ListViewRow row = (ListViewRow)rd_listView_Target.Items[idx];

                // 変更するファイル名
                String SrcName = rd_comboBox_RenameDir.Text + @"\" + row[RenameSrcIdx];
                String DestName = rd_comboBox_RenameDir.Text + @"\" + row[RenameDestIdx];

                // ファイル名の重複回避
                util.CreateFileNameOverLapShirk(ref DestName, i);
                fio.MoveDirectory(SrcName, DestName);
                pmd.SetRestoreList(SrcName, DestName);
            }
            pmd.IncrementRegistNumber();

            ListupRenameTargetDirectory(idx);
        }

        private void ListupRenameTargetDirectory(int ScrollbarPos = 0)
        {
            if (!IsValidFolderPath(rd_comboBox_RenameDir.Text))
            {
                return;
            }

            rd_listView_Target.Items.Clear();

            // フォルダをリストアップ
            String[] Folders = Directory.GetDirectories(rd_comboBox_RenameDir.Text);
            List<String> Names = new List<String>();
            for (int i = 0; i < Folders.Length; i++)
            {
                Names.Add(GetDisplayName(Folders[i], rd_comboBox_RenameDir.Text));
            }
            foreach (String FileName in SortedByName(Names))
            {
                rd_listView_Target.Items.Add(new ListViewRow(FileName, ""));
            }

            if (ScrollbarPos > Folders.Length)
            {
                ScrollbarPos = Folders.Length - 1;
            }

            if (ScrollbarPos > 0 && ScrollbarPos < rd_listView_Target.Items.Count)
            {
                rd_listView_Target.ScrollIntoView(rd_listView_Target.Items[ScrollbarPos]);
            }

            rd_label_TotalNum.Text = "フォルダ数：" + Folders.Length.ToString();
            AutoResizeRenameColumns();
        }

        public void UpdateRenameComboBox()
        {
            WpfControlHelper.SetComboBoxFromArraySubString(
                rd_comboBox_MergeWord,
                ReferenceCandidateFolders,
                rd_textBox_ExistItemDir.Text.Length + 1,
                rd_textBox_SplitWord3.Text,
                rd_comboBox_MergeWord.Text,
                true);
        }

        private void rd_listView_Rename_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            // 選択解除
            foreach (ListViewRow row in rd_listView_Target.Items)
            {
                row[RenameDestIdx] = "";
            }

            rd_listView_Rename_UpdateListBox();
        }

        // rd_comboBox_MergeWord/rd_comboBox_AddTitlePostWordのText変更(InitializeControlsで監視している)
        private void rd_comboBox_MergeWord_TextChanged(object sender, EventArgs e)
        {
            rd_listView_Rename_UpdateListBox();
        }

        // 区切り文字・番号前に追加・番号検索の後部/検索長のTextBox(WinForms版はrd_comboBox_MergeWord_TextChangedを共用していた)
        private void rd_textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            rd_listView_Rename_UpdateListBox();
        }

        private void rd_listView_Rename_UpdateListBox()
        {
            List<int> SelectedIndices = WpfControlHelper.GetSelectedIndices(rd_listView_Target);
            rd_label_SelectNum.Text = "選択数：" + SelectedIndices.Count.ToString();
            foreach (int idx in SelectedIndices)
            {
                ListViewRow row = (ListViewRow)rd_listView_Target.Items[idx];

                //文字列から数値を取得
                String SrcString = Logic.ChangeWide2Narrow(row[RenameSrcIdx]);
                long DestNumber = util.GetNumberFromRear(SrcString,
                    rd_textBox_SearchTitleLine.Text,
                    rd_textBox_SearchTitleLength.Text);

                String Number = Logic.GetNumber(DestNumber);

                // 変更後ファイル名を生成
                String DestName = rd_comboBox_MergeWord.Text + rd_textBox_AddTitlePreWord.Text + Number + rd_comboBox_AddTitlePostWord.Text;
                row[RenameDestIdx] = DestName;
            }
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void rd_listView_Rename_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            List<Object> SelectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(rd_listView_Target);
            if (SelectedItems.Count == 0)
            {
                return;
            }

            String FilePath = rd_comboBox_RenameDir.Text + @"\" + ((ListViewRow)SelectedItems[SortFileRenameTargetIdx])[RenameSrcIdx];
            if (Directory.Exists(FilePath))
            {
                if (rd_checkBox_FileOpen.IsChecked == true)
                {
                    String[] files = Directory.GetFiles(FilePath, "*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        FilePath = files[0];
                    }
                }
                System.Diagnostics.Process.Start(FilePath);
                rd_comboBox_MergeWord.Focus();
            }
        }

        private void rd_listView_Target_Update()
        {
            // 列(変更前/変更後)はXAMLで定義済み。幅はListViewの幅を等分する
            double width = rd_listView_Target.ActualWidth;
            if (width <= 0)
            {
                return;
            }
            // 枠線の分だけ引いて、横スクロールバーが出ないようにする
            double column = Math.Max((width - 8) / 2, 20);
            rd_column_Source.Width = column;
            rd_column_Dest.Width = column;
        }

        // WinForms版のAutoResizeColumns(HeaderSize)相当。先頭列は内容に合わせ、最後の列は残りの幅いっぱいにする
        private void AutoResizeRenameColumns()
        {
            rd_column_Source.Width = Double.NaN;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                double rest = rd_listView_Target.ActualWidth - rd_column_Source.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
                rd_column_Dest.Width = Math.Max(rest, 60);
            }));
        }

        private void rd_listView_Target_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // WinForms版はウィンドウのResizeEndでAutoResizeColumnsしていた
            if (rd_listView_Target.Items.Count == 0)
            {
                rd_listView_Target_Update();
            }
            else
            {
                AutoResizeRenameColumns();
            }
        }

        private void rd_comboBox_MergeWord_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsCtrlEnter(e))
            {
                RenameFolderExecute();
            }
        }

        private void rd_listView_Target_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsCtrlEnter(e))
            {
                RenameFolderExecute();
                rd_comboBox_MergeWord.Focus();
            }
            else
            {
                WpfControlHelper.CopyToClipboard(e, rd_listView_Target, item => ((ListViewRow)item)[RenameSrcIdx]);
            }
        }

        private void rd_label_ExistItemDir_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            rd_textBox_ExistItemDir.IsReadOnly = !rd_textBox_ExistItemDir.IsReadOnly;
        }

        private void rd_comboBox_MergeWord_DropDown(object sender, EventArgs e)
        {
            UpdateRenameComboBox();
        }

        private void rd_comboBox_RenameDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(rd_comboBox_RenameDir.Text, e);
        }
    }
}
