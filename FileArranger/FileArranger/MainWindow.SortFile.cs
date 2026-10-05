using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArranger
{
    // ファイル並べ替えタブ(sf)の処理(WinForms版Form1.SortFile.csから移植)
    public partial class MainWindow
    {
        private void sf_button_Listup_TargetFile_Click(object sender, RoutedEventArgs e)
        {
            ListupInto(sf_listBox_Target, sf_label_TotalNum, "フォルダ数", sf_textBox_TargetFile.Text, Directory.GetDirectories);
        }

        private void sf_button_SortFileRename_Click(object sender, RoutedEventArgs e)
        {
            SortFileRename();
        }

        private void SortFileRename()
        {
            if (!HasSelectedItems(sf_listBox_Target.SelectedItems.Count))
            {
                return;
            }

            foreach (String selectedName in WpfControlHelper.GetSelectedNames(sf_listBox_Target))
            {
                _sorter.SortFolder(Path.Combine(sf_textBox_TargetFile.Text, selectedName));
            }
            _sorter.CommitBatch();
        }

        private void sf_button_Sort_Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!_sorter.Restore())
            {
                MessageBox.Show("これ以上復元できません");
            }
        }

        private void sf_listBox_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            sf_label_SelectNum.Text = FormatSelectedCount(sf_listBox_Target.SelectedItems.Count);
        }

        private void sf_listBox_Target_KeyDown(object sender, KeyEventArgs e)
        {
            HandleListBoxKeyDown(sf_listBox_Target, e, SortFileRename);
        }
    }
}
