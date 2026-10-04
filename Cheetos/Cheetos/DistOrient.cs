using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;

namespace Cheetos
{
    // DistOrient
    public partial class MainWindow
    {
        private void do_Distribute_Click(object sender, RoutedEventArgs e)
        {
            // キャンセル(他タブのBackgroundWorkerと同じ「もう一度押すと中断」。IsBusyを
            // 見ずにRunWorkerAsync()していたため、処理中に押すとInvalidOperationExceptionで落ちていた)
            if (_bkgWorkerOrient.IsBusy)
            {
                _bkgWorkerOrient.CancelAsync();
                return;
            }

            if (!Directory.Exists(do_SourceFolderPath.Text))
            {
                MessageBox.Show("フォルダパスが不正です");
                return;
            }

            if (!EnsureDirectoryWithMessage(do_DestPortFolderPath.Text)
                || !EnsureDirectoryWithMessage(do_DestLandFolderPath.Text))
            {
                return;
            }

            // 別スレッドを非同期実行
            String[] files = Directory.GetFiles(do_SourceFolderPath.Text, do_TargetFileName.Text, SearchOption.TopDirectoryOnly);
            OrientWorkerParam param = new OrientWorkerParam
            {
                WhiteLength = int.Parse(do_WhiteLength.Text),
                WhiteCoef = int.Parse(do_WhiteCoef.Text),
                DestPortFolderPath = do_DestPortFolderPath.Text,
                DestLandFolderPath = do_DestLandFolderPath.Text,
                Files = files,
            };

            InitProgressBar(files.Length);

            SetStartTime();
            do_Distribute.Content = "中断";
            _bkgWorkerOrient.RunWorkerAsync(param);   // ⇒DoWork()
        }

        private void do_GetSampleParam_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(do_SampleFilePath.Text))
            {
                MessageBox.Show("無効なファイルパスです。\n" + do_SampleFilePath.Text);
                return;
            }

            Logic.IsPortrait(do_SampleFilePath.Text, int.Parse(do_WhiteLength.Text), int.Parse(do_WhiteCoef.Text), true);
        }

        private void bkgWorkerOrient_DoWork(object sender, DoWorkEventArgs e)
        {
            // 別スレッドで実行されるため、このメソッドではGUIを操作してはいけない

            // senderの値はbgWorkerの値と同じ
            BackgroundWorker worker = (BackgroundWorker)sender;

            StringBuilder errorLog = new StringBuilder();

            // このメソッドへのパラメータ
            OrientWorkerParam param = (OrientWorkerParam)e.Argument;
            String[] files = param.Files;

            for (int i = 0; i < files.Length; i++)
            {
                String destFolderPath = Logic.IsPortrait(files[i], param.WhiteLength, param.WhiteCoef)
                    ? param.DestPortFolderPath
                    : param.DestLandFolderPath;
                String destPath = destFolderPath + @"\" + Path.GetFileName(files[i]);

                if (!_fio.FileMove(files[i], destPath))
                {
                    errorLog.Append("Move " + files[i] + " " + destPath + Environment.NewLine);
                }

                // 進捗率の表示
                worker.ReportProgress(i);      // ⇒ProgressChanged()

                // キャンセルされてないかチェック
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
            }

            worker.ReportProgress(files.Length);      // ⇒ProgressChanged()
            e.Result = errorLog.ToString();
        }

        private void bkgWorkerOrient_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (ShowWorkerCompletion(e))
            {
                String result = e.Result.ToString();
                if (result != String.Empty)
                {
                    MessageBox.Show("処理が失敗しました。" + Environment.NewLine + result,
                        "Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            TextBox_Status.Text += " 完了";
            do_Distribute.Content = "振り分け";
        }
    }
}
