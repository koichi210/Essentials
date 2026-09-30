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
            String[] Folders = Directory.GetDirectories(sf_textBox_TargetFile.Text);
            sf_listBox_Target.Items.Clear();
            List<String> Names = new List<String>();
            for (int i = 0; i < Folders.Length; i++)
            {
                Names.Add(GetDisplayName(Folders[i], sf_textBox_TargetFile.Text));
            }
            foreach (String FolderName in SortedByName(Names))
            {
                sf_listBox_Target.Items.Add(FolderName);
            }

            sf_label_TotalNum.Text = "フォルダ数：" + Folders.Length.ToString();
        }

        private void sf_button_SortFileRename_Click(object sender, RoutedEventArgs e)
        {
            SortFileRename();
        }

        private void SortFileRename()
        {
            List<Object> SelectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(sf_listBox_Target);
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            for (int i = 0; i < SelectedItems.Count; i++)
            {
                String FilePath = sf_textBox_TargetFile.Text + @"\" + SelectedItems[i].ToString();
                sorter.SortFolder(FilePath);
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
