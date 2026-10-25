using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate.Wpf;

namespace FileArranger
{
    // ファイル並べ替えタブ(sf)の処理(WinForms版Form1.SortFile.csから移植)
    public partial class MainWindow
    {
        private void sf_button_Listup_TargetFile_Click(object sender, RoutedEventArgs e)
        {
            if (ListupInto(sf_listBox_Target, sf_label_TotalNum, "フォルダ数", sf_textBox_TargetFile.Text, Directory.GetDirectories) != null)
            {
                _sfListedDir = sf_textBox_TargetFile.Text;
            }
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

            _sorter.SortFolders(WpfControlUtils.GetSelectedNames(sf_listBox_Target)
                .Select(selectedName => Path.Combine(_sfListedDir, selectedName)));
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
