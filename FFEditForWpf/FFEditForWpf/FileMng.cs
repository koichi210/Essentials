using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.IO;
using StandardTemplate;

namespace FFEditForWpf
{
    class FileMng : StcProcessMemory
    {
        private StcUtils util = new StcUtils();
        private StcFileInputOutput FileIO = new StcFileInputOutput();

        // 直前の操作1回分を、記録しておいた移動元へ戻す。
        // Rename/Functionの両方に同じ実装が置かれていたためここへ集約した
        public Boolean RestoreAll()
        {
            if (!DecrementRegistNumber())
            {
                return false;
            }

            while (IsExistRestoreList())
            {
                String SrcName = "";
                String DestName = "";
                GetRestoreList(ref SrcName, ref DestName);
                Move(DestName, SrcName);
            }

            return true;
        }

        public bool Move(String SrcName, String DestName, Boolean IsErrorPopup = false)
        {
            bool success = true;

            try
            {
                if (File.Exists(SrcName))
                {
                    File.Move(SrcName, DestName);
                }
                else if (Directory.Exists(SrcName))
                {
                    Directory.Move(SrcName, DestName);
                }
                else
                {
                    success = false;
                }
            }
            catch (Exception)
            {
                if (IsErrorPopup)
                {
                    MessageBox.Show("指定パスが移動できませんでした。" + Environment.NewLine +
                        SrcName + Environment.NewLine +
                        DestName);
                }
                success = false;
            }

            return success;
        }

        public bool Copy(String SrcName, String DestName, Boolean IsErrorPopup = false)
        {
            bool success = true;

            try
            {
                if (File.Exists(SrcName))
                {
                    File.Copy(SrcName, DestName);
                }
                else if (Directory.Exists(SrcName))
                {
                    MessageBox.Show("ディレクトリコピーは未対応です。" + Environment.NewLine +
                        SrcName + Environment.NewLine +
                        DestName);
                    success = false;
                    
                }
                else
                {
                    success = false;
                }
            }
            catch (Exception)
            {
                if (IsErrorPopup)
                {
                    MessageBox.Show("指定パスが移動できませんでした。" + Environment.NewLine +
                        SrcName + Environment.NewLine +
                        DestName);
                }
                success = false;
            }

            return success;
        }

        public void DeleteBlankDir(String DirPath)
        {
            String Command;
            Command = @"for /f ""delims="" %%d in ('dir """ + DirPath + @""" /ad /b /s') do rd ""%%d""" + Environment.NewLine;

            String BatchFile = FileIO.CreateTempFile("bat");
            FileIO.CreateFile(BatchFile, Command);

            util.ExecuteProcess(BatchFile, true );
        }
    }
}
