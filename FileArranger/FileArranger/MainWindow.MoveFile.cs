using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArranger
{
    // ファイル移動タブ(mf)の処理(WinForms版Form1.MoveFile.csから移植)
    public partial class MainWindow
    {
        private void mf_textBox_TargetDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(mf_textBox_TargetDir.Text, e);
        }

        private void mf_button_Listup_Click(object sender, RoutedEventArgs e)
        {
            ListupMoveFileTargets();
        }

        private void ListupMoveFileTargets()
        {
            if (!IsValidFolderPath(mf_textBox_SourceDir.Text))
            {
                return;
            }

            // 移動元フォルダをリストアップ
            String[] files = Directory.GetFiles(mf_textBox_SourceDir.Text);
            mf_listBox_Target.Items.Clear();
            foreach (String fileName in GetSortedDisplayNames(files, mf_textBox_SourceDir.Text))
            {
                mf_listBox_Target.Items.Add(fileName);
            }
            mf_label_TotalNum.Text = "ファイル数：" + files.Length.ToString();
        }

        private void mf_listBox_Target_KeyDown(object sender, KeyEventArgs e)
        {
            WpfControlHelper.SelectAll(mf_listBox_Target, e);
        }

        private void mf_listBox_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            mf_label_SelectNum.Text = "選択数：" + mf_listBox_Target.SelectedItems.Count.ToString();
        }

        private void mf_button_MoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (!_fio.EnsureDirectory(mf_textBox_TargetDir.Text))
            {
                return;
            }

            List<Object> selectedItems = WpfControlHelper.GetSelectedItemsInIndexOrder(mf_listBox_Target);
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("項目が選択されていません。");
                return;
            }

            // WinForms版のBackgroundWorkerは実行中に再度RunWorkerAsyncすると例外になっていた。
            // WPF版でも同じ動作にするため、実行中なら何もしない(二重実行の防止)
            if (_bgWorkerMove.IsBusy)
            {
                return;
            }

            ResetProgress(selectedItems.Count);

            // 別スレッドを非同期実行
            MoveFileWorkerParam param = new MoveFileWorkerParam
            {
                SourceDir = mf_textBox_SourceDir.Text,
                TargetDir = mf_textBox_TargetDir.Text,
            };
            foreach (Object item in selectedItems)
            {
                param.TargetNames.Add(item.ToString());
            }

            _bgWorkerMove.RunWorkerAsync(param);   // ⇒bgWorkerMove_DoWork()
        }

        private void mf_textBox_SourceDir_KeyDown(object sender, KeyEventArgs e)
        {
            ExecutePathOnEnter(mf_textBox_SourceDir.Text, e);
        }

        private void bgWorkerMove_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない
            BackgroundWorker worker = (BackgroundWorker)sender;

            // このメソッドへのパラメータ
            MoveFileWorkerParam param = (MoveFileWorkerParam)e.Argument;

            for (int i = 0; i < param.TargetNames.Count; i++)
            {
                String targetName = param.TargetNames[i];
                String sourcePath = param.SourceDir + @"\" + targetName;
                String targetPath = param.TargetDir + @"\" + _fio.GetLastPathName(targetName);

                // 移動先に同名のファイルがある場合は重複回避
                // (targetPathはファイルパスなので、フォルダの有無しか見ないAvoidFolderNameConflictでは
                //  同名ファイルの存在を検知できず、Move処理に失敗してしまう。AvoidFileNameConflictで
                //  ファイル/フォルダ両方の存在をチェックしてリネームする)
                _util.AvoidFileNameConflict(ref targetPath, i);
                _fio.MoveDirectory(sourcePath, targetPath);

                worker.ReportProgress(i);      // ⇒ProgressChanged()
            }
            worker.ReportProgress(param.TargetNames.Count);

            // このメソッドからの戻り値
            e.Result = "すべて完了";

            // ⇒RunWorkerCompleted()
        }

        private void bgWorkerMove_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ShowProgress(e.ProgressPercentage);
        }

        private void bgWorkerMove_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                // この場合はe.Resultにはアクセスできない
                MessageBox.Show("キャンセルされました");
            }
            else if (e.Error != null)
            {
                MessageBox.Show("ファイルの移動中にエラーが発生しました" + Environment.NewLine + e.Error.Message);
            }

            // リストを更新
            ListupMoveFileTargets();
        }
    }
}
