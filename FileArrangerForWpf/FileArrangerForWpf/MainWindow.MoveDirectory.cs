using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileArrangerForWpf
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
            Move_Directory(true);
        }

        private void Move_Directory(Boolean IsMoveTopDir)
        {
            if (!fio.EnsureDirectory(md_comboBox_TargetDir.Text))
            {
                return;
            }

            List<Object> SelectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(md_listBox_Listup);
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            for (int i = 0; i < SelectedItems.Count; i++)
            {
                String SourceTargetName;
                String DestTargetName;
                if (IsMoveTopDir)
                {
                    SourceTargetName = fio.GetFirstPathName(SelectedItems[i].ToString());
                    DestTargetName = SourceTargetName;
                }
                else
                {
                    SourceTargetName = SelectedItems[i].ToString();
                    DestTargetName = fio.GetLastPathName(SelectedItems[i].ToString());
                }

                String SourcePath = md_textBox_SourceDir.Text + @"\" + SourceTargetName;
                String DestPath = md_comboBox_TargetDir.Text + @"\" + DestTargetName;

                // Top階層ごと移動した場合などで、すでにDirectoryが存在しないケースをcare
                if (!Directory.Exists(SourcePath))
                {
                    continue;
                }

                // 移動先にすでにフォルダがある場合は重複回避
                util.CreateFolderNameOverLapShirk(ref DestPath, i);

                fio.MoveDirectory(SourcePath, DestPath);
            }

            // リストを更新
            ListupMoveDirectory(true);
        }

        private void ListupMoveDirectory(Boolean IsRestoreScrollBarPos = false)
        {
            if (!IsValidFolderPath(md_textBox_SourceDir.Text))
            {
                return;
            }

            int RegistNum = 0;
            // フォルダパスの末尾に'\\'があったら削除
            char[] chTrims = { '\\', '/' };
            md_textBox_SourceDir.Text = md_textBox_SourceDir.Text.TrimEnd(chTrims);

            ScrollViewer Scroller = FindScrollViewer(md_listBox_Listup);
            double ScrollBarPos = 0;
            if (IsRestoreScrollBarPos && Scroller != null)
            {
                ScrollBarPos = Scroller.VerticalOffset;
            }
            md_listBox_Listup.Items.Clear();
            String[] files = Directory.GetDirectories(md_textBox_SourceDir.Text, "*", SearchOption.AllDirectories);
            List<String> Names = new List<String>();
            for (int i = 0; i < files.Length; i++)
            {
                // フォルダ直下にファイルが1つでもあればリストアップ
                if (Directory.EnumerateFiles(files[i]).Any())
                {
                    Names.Add(GetDisplayName(files[i], md_textBox_SourceDir.Text));
                    RegistNum++;
                }
            }
            foreach (String FileName in SortedByName(Names))
            {
                md_listBox_Listup.Items.Add(FileName);
            }

            // スクロール位置を戻す(WinForms版のTopIndex相当)
            if (Scroller != null)
            {
                md_listBox_Listup.UpdateLayout();
                Scroller.ScrollToVerticalOffset(ScrollBarPos);
            }
            md_label_TotalNum.Text = "フォルダ数：" + RegistNum.ToString();
        }

        // ListBox内部のScrollViewerを探す(テンプレート適用前はnull)
        private static ScrollViewer FindScrollViewer(DependencyObject Parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(Parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(Parent, i);
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
