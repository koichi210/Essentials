using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileArranger
{
    // ファイル移動タブ(mf)の処理(WinForms版Form1.MoveFile.csから移植)
    public partial class MainWindow
    {
        private void mf_button_Listup_Click(object sender, RoutedEventArgs e)
        {
            ListupMoveFileTargets();
        }

        private void ListupMoveFileTargets()
        {
            ListupInto(mf_listBox_Target, mf_label_TotalNum, "ファイル数", mf_textBox_SourceDir.Text, Directory.GetFiles);
        }

        private void mf_listBox_Target_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            mf_label_SelectNum.Text = FormatSelectedCount(mf_listBox_Target.SelectedItems.Count);
        }

        private void mf_button_MoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (!_fio.EnsureDirectory(mf_textBox_TargetDir.Text))
            {
                return;
            }

            if (!HasSelectedItems(mf_listBox_Target.SelectedItems.Count))
            {
                return;
            }

            // WinForms版のBackgroundWorkerは実行中に再度RunWorkerAsyncすると例外になっていた。
            // WPF版でも同じ動作にするため、実行中なら何もしない(二重実行の防止)
            if (_bgWorkerMove.IsBusy)
            {
                return;
            }

            // 別スレッドを非同期実行
            MoveFileWorkerParam param = new MoveFileWorkerParam
            {
                SourceDir = mf_textBox_SourceDir.Text,
                TargetDir = mf_textBox_TargetDir.Text,
            };
            param.TargetNames.AddRange(WpfControlHelper.GetSelectedNames(mf_listBox_Target));

            ResetProgress(param.TargetNames.Count);
            _bgWorkerMove.RunWorkerAsync(param);   // ⇒bgWorkerMove_DoWork()
        }

        private void bgWorkerMove_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない
            BackgroundWorker worker = (BackgroundWorker)sender;
            MoveFileWorkerParam param = (MoveFileWorkerParam)e.Argument;

            for (int i = 0; i < param.TargetNames.Count; i++)
            {
                String targetName = param.TargetNames[i];
                String sourcePath = Path.Combine(param.SourceDir, targetName);
                String targetPath = Path.Combine(param.TargetDir, _fio.GetLastPathName(targetName));

                // 移動先に同名のファイルがある場合は重複回避
                // (targetPathはファイルパスなので、フォルダの有無しか見ないAvoidFolderNameConflictでは
                //  同名ファイルの存在を検知できず、Move処理に失敗してしまう。AvoidFileNameConflictで
                //  ファイル/フォルダ両方の存在をチェックしてリネームする)
                _util.AvoidFileNameConflict(ref targetPath, i);
                _fio.MoveDirectory(sourcePath, targetPath);

                worker.ReportProgress(i + 1);      // ⇒bgWorker_ProgressChanged()
            }

            // ⇒bgWorkerMove_RunWorkerCompleted()
        }

        private void bgWorkerMove_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsWorkerCompletedNormally(e, "ファイルの移動中にエラーが発生しました");

            // リストを更新
            ListupMoveFileTargets();
        }
    }
}
