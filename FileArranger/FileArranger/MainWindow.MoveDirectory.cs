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
        private void md_button_Listup_Click(object sender, RoutedEventArgs e)
        {
            ListupMoveDirectory();
        }

        private void md_button_MoveTopDir_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedDirectories(true);
        }

        private void md_button_MoveSubDir_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedDirectories(false);
        }

        private void md_listBox_Listup_KeyDown(object sender, KeyEventArgs e)
        {
            HandleListBoxKeyDown(md_listBox_Listup, e, () => MoveSelectedDirectories(false));
        }

        private void md_listBox_Listup_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            String firstName = WpfControlHelper.GetSelectedNames(md_listBox_Listup).FirstOrDefault();
            if (firstName != null)
            {
                _util.ExecutePath(Path.Combine(md_textBox_SourceDir.Text, firstName));
            }
        }

        private void md_listBox_Listup_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            md_label_SelectNum.Text = FormatSelectedCount(md_listBox_Listup.SelectedItems.Count);
        }

        // 削除/移動の前提チェック(格納先フォルダの確保と選択有無)
        private Boolean CanOperateSelectedDirectories()
        {
            return _fio.EnsureDirectory(md_comboBox_TargetDir.Text)
                && HasSelectedItems(md_listBox_Listup.SelectedItems.Count);
        }

        private void md_button_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!CanOperateSelectedDirectories())
            {
                return;
            }

            foreach (String selectedName in WpfControlHelper.GetSelectedNames(md_listBox_Listup))
            {
                Directory.Delete(Path.Combine(md_textBox_SourceDir.Text, selectedName), true);
            }

            // リストを更新
            ListupMoveDirectory();
        }

        // 選択したフォルダを移動先へ移動する(WinForms版Move_Directory)。
        // isMoveTopDir=trueなら最上位のフォルダごと、falseなら末端のフォルダだけを移動する
        private void MoveSelectedDirectories(Boolean isMoveTopDir)
        {
            if (!CanOperateSelectedDirectories())
            {
                return;
            }

            List<String> selectedNames = WpfControlHelper.GetSelectedNames(md_listBox_Listup);
            for (int i = 0; i < selectedNames.Count; i++)
            {
                // 最上位フォルダごと移動する時は、格納元直下のフォルダ名をそのまま移動先でも使う。
                // 選択したフォルダだけ移動する時は、途中の階層を持っていかず末尾のフォルダ名だけにする
                String sourceTargetName = isMoveTopDir ? _fio.GetFirstPathName(selectedNames[i]) : selectedNames[i];
                String destTargetName = isMoveTopDir ? sourceTargetName : _fio.GetLastPathName(selectedNames[i]);

                String sourcePath = Path.Combine(md_textBox_SourceDir.Text, sourceTargetName);
                String destPath = Path.Combine(md_comboBox_TargetDir.Text, destTargetName);

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
            // フォルダパスの末尾に'\\'があったら削除
            md_textBox_SourceDir.Text = md_textBox_SourceDir.Text.TrimEnd('\\', '/');

            ScrollViewer scroller = FindScrollViewer(md_listBox_Listup);
            double scrollBarPos = (keepScrollPosition && scroller != null) ? scroller.VerticalOffset : 0;

            // フォルダ直下にファイルが1つでもあればリストアップ。
            // EnumerateFilesは遅延列挙なので、最初の1件が見つかった時点でAny()が打ち切ってくれる
            String[] names = ListupInto(md_listBox_Listup, md_label_TotalNum, "フォルダ数", md_textBox_SourceDir.Text,
                folder => Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories)
                    .Where(dir => Directory.EnumerateFiles(dir).Any()));

            // スクロール位置を戻す(WinForms版のTopIndex相当)
            if (names != null && scroller != null)
            {
                md_listBox_Listup.UpdateLayout();
                scroller.ScrollToVerticalOffset(scrollBarPos);
            }
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
