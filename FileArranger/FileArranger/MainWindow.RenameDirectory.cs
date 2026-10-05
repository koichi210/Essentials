using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private void rd_button_Execute_Rename_Click(object sender, RoutedEventArgs e)
        {
            ExecuteRenameFolder();
        }

        private void rd_button_RenameFolderRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!_renameDirMemory.RestoreLastBatch(Directory.Move))
            {
                MessageBox.Show("これ以上復元できません");
                return;
            }

            ListupRenameTargetDirectory();
        }

        private void ExecuteRenameFolder()
        {
            List<int> selectedIndices = WpfControlHelper.GetSelectedIndices(rd_listView_Target);
            if (!HasSelectedItems(selectedIndices.Count))
            {
                return;
            }

            for (int i = 0; i < selectedIndices.Count; i++)
            {
                ListViewRow row = (ListViewRow)rd_listView_Target.Items[selectedIndices[i]];
                String srcName = Path.Combine(rd_comboBox_RenameDir.Text, row[_renameSrcIdx]);
                String destName = Path.Combine(rd_comboBox_RenameDir.Text, row[_renameDestIdx]);

                // フォルダ名の重複回避
                _util.AvoidFileNameConflict(ref destName, i);
                _fio.MoveDirectory(srcName, destName);
                _renameDirMemory.AddRestoreItem(srcName, destName);
            }
            _renameDirMemory.IncrementSerialNumber();

            // 最後にリネームした項目の位置が見えるようにしておく
            ListupRenameTargetDirectory(selectedIndices[selectedIndices.Count - 1]);
        }

        // scrollToIdx: リストアップ後に表示位置を合わせる項目のインデックス
        private void ListupRenameTargetDirectory(int scrollToIdx = 0)
        {
            String[] folders = ListupInto(rd_listView_Target, rd_label_TotalNum, "フォルダ数", rd_comboBox_RenameDir.Text,
                Directory.GetDirectories, name => new ListViewRow(name, ""));
            if (folders == null)
            {
                return;
            }

            scrollToIdx = Math.Min(scrollToIdx, folders.Length - 1);
            if (scrollToIdx > 0)
            {
                rd_listView_Target.ScrollIntoView(rd_listView_Target.Items[scrollToIdx]);
            }

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
            // 一旦全行の[変更後]を消してから、選択中の行だけ作り直す
            WpfControlHelper.SetColumnText(rd_listView_Target.Items.Cast<ListViewRow>(), "", _renameDestIdx);
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
            List<ListViewRow> selectedRows = WpfControlHelper.GetSelectedRows(rd_listView_Target);
            rd_label_SelectNum.Text = FormatSelectedCount(selectedRows.Count);

            // 番号以外の部分は全行共通なので、ループの外で1回だけ組み立てる
            String prefix = rd_comboBox_MergeWord.Text + rd_textBox_AddTitlePreWord.Text;
            String postfix = rd_comboBox_AddTitlePostWord.Text;

            foreach (ListViewRow row in selectedRows)
            {
                //文字列から数値を取得
                String srcString = Logic.ChangeWide2Narrow(row[_renameSrcIdx]);
                long destNumber = _util.GetNumberFromRear(srcString,
                    rd_textBox_SearchTitleLine.Text,
                    rd_textBox_SearchTitleLength.Text);

                row[_renameDestIdx] = prefix + Logic.ToPaddedNumberString(destNumber) + postfix;
            }
        }

        // 項目のダブルクリック(WinForms版ListView.DoubleClickは項目上でだけ発生していたため、項目側で受ける)
        private void rd_listView_Rename_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            ListViewRow firstRow = WpfControlHelper.GetSelectedRows(rd_listView_Target).FirstOrDefault();
            if (firstRow == null)
            {
                return;
            }

            // 先頭の選択項目を開く
            String openPath = Path.Combine(rd_comboBox_RenameDir.Text, firstRow[_renameSrcIdx]);
            if (!Directory.Exists(openPath))
            {
                return;
            }

            if (rd_checkBox_FileOpen.IsChecked == true)
            {
                // 先頭の1件だけ分かればよいので、サブフォルダ以下を全部列挙せずに最初の1件で打ち切る
                openPath = Directory.EnumerateFiles(openPath, "*", SearchOption.AllDirectories).FirstOrDefault() ?? openPath;
            }
            System.Diagnostics.Process.Start(openPath);
            rd_comboBox_MergeWord.Focus();
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

        // rdタブの各入力欄で共通: Ctrl+Enterでリネーム実行
        private void rd_RenameInput_KeyDown(object sender, KeyEventArgs e)
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
                WpfControlHelper.CopyToClipboard(e, rd_listView_Target, item => ((ListViewRow)item)[_renameSrcIdx]);
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
    }
}
