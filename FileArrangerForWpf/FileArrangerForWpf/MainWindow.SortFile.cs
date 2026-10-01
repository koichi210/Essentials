using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArrangerForWpf
{
    // ファイル並べ替えタブ(sf)の処理(WinForms版Form1.SortFile.csから移植)
    public partial class MainWindow
    {
        private void sf_textBox_TargetFile_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(sf_textBox_TargetFile.Text, e);
        }

        private void sf_button_Listup_TargetFile_Click(object sender, RoutedEventArgs e)
        {
            if (!IsValidFolderPath(sf_textBox_TargetFile.Text))
            {
                return;
            }

            // フォルダをリストアップ
            String[] folders = Directory.GetDirectories(sf_textBox_TargetFile.Text);
            sf_listBox_Target.Items.Clear();
            List<String> names = new List<String>();
            foreach (String folder in folders)
            {
                names.Add(GetDisplayName(folder, sf_textBox_TargetFile.Text));
            }
            foreach (String folderName in SortedByName(names))
            {
                sf_listBox_Target.Items.Add(folderName);
            }

            sf_label_TotalNum.Text = "フォルダ数：" + folders.Length.ToString();
        }

        private void sf_button_SortFileRename_Click(object sender, RoutedEventArgs e)
        {
            SortFileRename();
        }

        private void SortFileRename()
        {
            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(sf_listBox_Target);
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            foreach (Object item in selectedItems)
            {
                String folderPath = sf_textBox_TargetFile.Text + @"\" + item.ToString();
                sorter.SortFolder(folderPath);
            }
            sorter.CommitBatch();
        }

        private void sf_button_Sort_Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!sorter.Restore())
            {
                MessageBox.Show("これ以上復元できません");
            }
        }

        private void sf_listBox_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            sf_label_SelectNum.Text = "選択数：" + sf_listBox_Target.SelectedItems.Count.ToString();
        }

        private void sf_listBox_Target_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SortFileRename();
            }
            else
            {
                WpfControlHelper.SelectAll(sf_listBox_Target, e);
            }
        }
    }
}
