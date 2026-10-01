using System;
using System.Windows;
using System.IO;
using StandardTemplate;

namespace FFEditForWpf
{
    class FileMng : StcProcessMemory
    {
        private readonly StcUtils util = new StcUtils();
        private readonly StcFileInputOutput fileIO = new StcFileInputOutput();

        // 直前の操作1回分を、記録しておいた移動元へ戻す。
        // Rename/Functionの両方に同じ実装が置かれていたためここへ集約した
        public Boolean RestoreAll()
        {
            if (!DecrementSerialNumber())
            {
                return false;
            }

            while (HasRestoreItem())
            {
                String srcName = "";
                String destName = "";
                PopRestoreItem(ref srcName, ref destName);
                Move(destName, srcName);
            }

            return true;
        }

        public bool Move(String srcName, String destName, Boolean showErrorPopup = false)
        {
            bool success = true;

            try
            {
                if (File.Exists(srcName))
                {
                    File.Move(srcName, destName);
                }
                else if (Directory.Exists(srcName))
                {
                    Directory.Move(srcName, destName);
                }
                else
                {
                    success = false;
                }
            }
            catch (Exception)
            {
                if (showErrorPopup)
                {
                    MessageBox.Show("指定パスが移動できませんでした。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                }
                success = false;
            }

            return success;
        }

        public bool Copy(String srcName, String destName, Boolean showErrorPopup = false)
        {
            bool success = true;

            try
            {
                if (File.Exists(srcName))
                {
                    File.Copy(srcName, destName);
                }
                else if (Directory.Exists(srcName))
                {
                    MessageBox.Show("ディレクトリコピーは未対応です。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                    success = false;
                }
                else
                {
                    success = false;
                }
            }
            catch (Exception)
            {
                if (showErrorPopup)
                {
                    MessageBox.Show("指定パスが移動できませんでした。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                }
                success = false;
            }

            return success;
        }

        public void DeleteBlankDir(String dirPath)
        {
            String command = @"for /f ""delims="" %%d in ('dir """ + dirPath + @""" /ad /b /s') do rd ""%%d""" + Environment.NewLine;

            String batchFile = fileIO.CreateTempFile("bat");
            fileIO.CreateFile(batchFile, command);

            util.ExecuteProcess(batchFile, true);
        }
    }
}
