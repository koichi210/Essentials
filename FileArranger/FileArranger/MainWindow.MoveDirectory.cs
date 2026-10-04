using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileArranger
{
    // フォルダ移動タブ(md)の処理(WinForms版Form1.MoveDirectory.csから移植)
    public partial class MainWindow
    {
        private void md_textBox_SourceDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(md_textBox_SourceDir.Text, e);
        }

        private void md_comboBox_TargetDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(md_comboBox_TargetDir.Text, e);
        }

        private void md_button_Listup_Click(object sender, RoutedEventArgs e)
        {
            ListupMoveDirectory();
        }

        private void md_button_MoveTopDir_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedDirectories(true);
        }

        private void md_listBox_Listup_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                MoveSelectedDirectories(false);
            }
            else
            {
                WpfControlHelper.SelectAll(md_listBox_Listup, e);
            }
        }

        private void md_listBox_Listup_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(md_listBox_Listup);
            if (selectedItems.Count > 0)
            {
                String targetPath = md_textBox_SourceDir.Text + @"\" + selectedItems[0].ToString();
                _util.ExecutePath(targetPath);
            }
        }

        private void md_button_MoveSubDir_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedDirectories(false);
        }

        private void md_button_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!_fio.EnsureDirectory(md_comboBox_TargetDir.Text))
            {
                return;
            }

            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(md_listBox_Listup);
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            foreach (Object item in selectedItems)
            {
                String delPath = md_textBox_SourceDir.Text + @"\" + item.ToString();
                new DirectoryInfo(delPath).Delete(true);
            }

            // リストを更新
            ListupMoveDirectory();
        }

        private void md_listBox_Listup_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            md_label_SelectNum.Text = "選択数：" + md_listBox_Listup.SelectedItems.Count.ToString();
        }

        // 選択したフォルダを移動先へ移動する(WinForms版Move_Directory)。
        // isMoveTopDir=trueなら最上位のフォルダごと、falseなら末端のフォルダだけを移動する
        private void MoveSelectedDirectories(Boolean isMoveTopDir)
        {
            if (!_fio.EnsureDirectory(md_comboBox_TargetDir.Text))
            {
                return;
            }

            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(md_listBox_Listup);
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            for (int i = 0; i < selectedItems.Count; i++)
            {
                String sourceTargetName;
                String destTargetName;
                if (isMoveTopDir)
                {
                    sourceTargetName = _fio.GetFirstPathName(selectedItems[i].ToString());
                    destTargetName = sourceTargetName;
                }
                else
                {
                    sourceTargetName = selectedItems[i].ToString();
                    destTargetName = _fio.GetLastPathName(selectedItems[i].ToString());
                }

                String sourcePath = md_textBox_SourceDir.Text + @"\" + sourceTargetName;
                String destPath = md_comboBox_TargetDir.Text + @"\" + destTargetName;

                // Top階層ごと移動した場合などで、すでにDirectoryが存在しないケースをcare
                if (!Directory.Exists(sourcePath))
                {
                    continue;
                }

                // 移動先にすでにフォルダがある場合は重複回避
                _util.AvoidFolderNameConflict(ref destPath, i);

                _fio.MoveDirectory(sourcePath, destPath);
            }

            // リストを更新
            ListupMoveDirectory(true);
        }

        private void ListupMoveDirectory(Boolean keepScrollPosition = false)
        {
            if (!IsValidFolderPath(md_textBox_SourceDir.Text))
            {
                return;
            }

            // フォルダパスの末尾に'\\'があったら削除
            char[] trimChars = { '\\', '/' };
            md_textBox_SourceDir.Text = md_textBox_SourceDir.Text.TrimEnd(trimChars);

            ScrollViewer scroller = FindScrollViewer(md_listBox_Listup);
            double scrollBarPos = 0;
            if (keepScrollPosition && scroller != null)
            {
                scrollBarPos = scroller.VerticalOffset;
            }
            md_listBox_Listup.Items.Clear();
            // フォルダ直下にファイルが1つでもあればリストアップ
            String[] names = GetSortedDisplayNames(
                Directory.GetDirectories(md_textBox_SourceDir.Text, "*", SearchOption.AllDirectories)
                    .Where(dir => Directory.EnumerateFiles(dir).Any()),
                md_textBox_SourceDir.Text);
            foreach (String name in names)
            {
                md_listBox_Listup.Items.Add(name);
            }

            // スクロール位置を戻す(WinForms版のTopIndex相当)
            if (scroller != null)
            {
                md_listBox_Listup.UpdateLayout();
                scroller.ScrollToVerticalOffset(scrollBarPos);
            }
            md_label_TotalNum.Text = "フォルダ数：" + names.Length.ToString();
        }

        // ListBox内部のScrollViewerを探す(テンプレート適用前はnull)
        private static ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                ScrollViewer viewer = child as ScrollViewer ?? FindScrollViewer(child);
                if (viewer != null)
                {
                    return viewer;
                }
            }
            return null;
        }

        public void UpdateMoveDestDirComboBox()
        {
            WpfControlHelper.SetComboBoxFromArray(pf_comboBox_MoveDestDirName, ReferenceCandidateFolders, pf_textBox_ReferenceFile.Text);
        }
    }
}
