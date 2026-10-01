using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FileArranger
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
            ExecuteRenameFolder();
        }

        private void rd_button_RenameFolderRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!renameDirMemory.DecrementSerialNumber())
            {
                MessageBox.Show("これ以上復元できません");
                return;
            }

            while (renameDirMemory.HasRestoreItem())
            {
                String srcName = "";
                String destName = "";
                renameDirMemory.PopRestoreItem(ref srcName, ref destName);
                Directory.Move(destName, srcName);
            }
            ListupRenameTargetDirectory();
        }

        private void ExecuteRenameFolder()
        {
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(rd_listView_Target);
            if (selectedIndices.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            int idx = 0;
            for (int i = 0; i < selectedIndices.Count; i++)
            {
                // 参照しているListViewのIdx
                idx = selectedIndices[i];
                ListViewRow row = (ListViewRow)rd_listView_Target.Items[idx];

                // 変更するファイル名
                String srcName = rd_comboBox_RenameDir.Text + @"\" + row[RenameSrcIdx];
                String destName = rd_comboBox_RenameDir.Text + @"\" + row[RenameDestIdx];

                // ファイル名の重複回避
                util.AvoidFileNameConflict(ref destName, i);
                fio.MoveDirectory(srcName, destName);
                renameDirMemory.AddRestoreItem(srcName, destName);
            }
            renameDirMemory.IncrementSerialNumber();

            ListupRenameTargetDirectory(idx);
        }

        // scrollToIdx: リストアップ後に表示位置を合わせる項目のインデックス
        private void ListupRenameTargetDirectory(int scrollToIdx = 0)
        {
            if (!IsValidFolderPath(rd_comboBox_RenameDir.Text))
            {
                return;
            }

            rd_listView_Target.Items.Clear();

            // フォルダをリストアップ
            String[] folders = Directory.GetDirectories(rd_comboBox_RenameDir.Text);
            List<String> names = new List<String>();
            foreach (String folder in folders)
            {
                names.Add(GetDisplayName(folder, rd_comboBox_RenameDir.Text));
            }
            foreach (String folderName in SortedByName(names))
            {
                rd_listView_Target.Items.Add(new ListViewRow(folderName, ""));
            }

            if (scrollToIdx >= folders.Length)
            {
                scrollToIdx = folders.Length - 1;
            }

            if (scrollToIdx > 0 && scrollToIdx < rd_listView_Target.Items.Count)
            {
                rd_listView_Target.ScrollIntoView(rd_listView_Target.Items[scrollToIdx]);
            }

            rd_label_TotalNum.Text = "フォルダ数：" + folders.Length.ToString();
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

            UpdateRenameDestNames();
        }

        // rd_comboBox_MergeWord/rd_comboBox_AddTitlePostWordのText変更(InitializeControlsで監視している)
        private void rd_comboBox_TextChanged(object sender, EventArgs e)
        {
            UpdateRenameDestNames();
        }

        // 区切り文字・番号前に追加・番号検索の後部/検索長のTextBox(WinForms版はrd_RenameSetting_TextChangedを共用していた)
        private void rd_textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRenameDestNames();
        }

        // 選択数の表示を更新し、選択項目の「変更後」列を作り直す(WinForms版rd_listView_Rename_UpdateListBox)
        private void UpdateRenameDestNames()
        {
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(rd_listView_Target);
            rd_label_SelectNum.Text = "選択数：" + selectedIndices.Count.ToString();
            foreach (int idx in selectedIndices)
            {
                ListViewRow row = (ListViewRow)rd_listView_Target.Items[idx];

                //文字列から数値を取得
                String srcString = Logic.ChangeWide2Narrow(row[RenameSrcIdx]);
                long destNumber = util.GetNumberFromRear(srcString,
                    rd_textBox_SearchTitleLine.Text,
                    rd_textBox_SearchTitleLength.Text);

                String number = Logic.ToPaddedNumberString(destNumber);

                // 変更後ファイル名を生成
                row[RenameDestIdx] = rd_comboBox_MergeWord.Text + rd_textBox_AddTitlePreWord.Text + number + rd_comboBox_AddTitlePostWord.Text;
            }
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void rd_listView_Rename_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(rd_listView_Target);
            if (selectedItems.Count == 0)
            {
                return;
            }

            // 先頭の選択項目を開く
            String filePath = rd_comboBox_RenameDir.Text + @"\" + ((ListViewRow)selectedItems[0])[RenameSrcIdx];
            if (Directory.Exists(filePath))
            {
                if (rd_checkBox_FileOpen.IsChecked == true)
                {
                    String[] files = Directory.GetFiles(filePath, "*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        filePath = files[0];
                    }
                }
                System.Diagnostics.Process.Start(filePath);
                rd_comboBox_MergeWord.Focus();
            }
        }

        private void ResizeRenameColumnsEvenly()
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
                ResizeRenameColumnsEvenly();
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
                ExecuteRenameFolder();
            }
        }

        private void rd_listView_Target_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsCtrlEnter(e))
            {
                ExecuteRenameFolder();
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
