using System;
using System.Windows;
using System.IO;
using StandardTemplate;

namespace FFEdit
{
    class FileMng : StcProcessMemory
    {
        private readonly StcUtils _util = new StcUtils();
        private readonly StcFileInputOutput _fileIO = new StcFileInputOutput();

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
            try
            {
                if (File.Exists(srcName))
                {
                    File.Move(srcName, destName);
                    return true;
                }
                if (Directory.Exists(srcName))
                {
                    Directory.Move(srcName, destName);
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                if (showErrorPopup)
                {
                    MessageBox.Show("指定パスが移動できませんでした。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                }
                return false;
            }
        }

        public bool Copy(String srcName, String destName, Boolean showErrorPopup = false)
        {
            try
            {
                if (File.Exists(srcName))
                {
                    File.Copy(srcName, destName);
                    return true;
                }
                if (Directory.Exists(srcName))
                {
                    MessageBox.Show("ディレクトリコピーは未対応です。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                }
                return false;
            }
            catch (Exception)
            {
                if (showErrorPopup)
                {
                    MessageBox.Show("指定パスがコピーできませんでした。" + Environment.NewLine +
                        srcName + Environment.NewLine +
                        destName);
                }
                return false;
            }
        }

        // Execute()で処理に失敗した1件分のエラー表示用テキスト(Rename/Functionで共通)
        public static String GetErrorText(String srcName, String destName)
        {
            return "Src=" + srcName + Environment.NewLine
                + "Dst=" + destName + Environment.NewLine
                + Environment.NewLine;
        }

        public void DeleteBlankDir(String dirPath)
        {
            String command = @"for /f ""delims="" %%d in ('dir """ + dirPath + @""" /ad /b /s') do rd ""%%d""" + Environment.NewLine;

            String batchFile = _fileIO.CreateTempFile("bat");
            _fileIO.CreateFile(batchFile, command);

            _util.ExecuteProcess(batchFile, true);
        }
    }
}
