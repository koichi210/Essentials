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
            // dir /s は親フォルダを子より先に列挙するため、そのままだと「空フォルダだけを含むフォルダ」
            // (a\b が両方空など)は、rd の時点ではまだ子が残っていて消えなかった。
            // 逆順に並べ替えて、子(深い方)から先に rd する
            String command = @"for /f ""delims="" %%d in ('dir """ + dirPath + @""" /ad /b /s ^| sort /r') do rd ""%%d""" + Environment.NewLine;

            String batchFile = _fileIO.CreateTempFile("bat");
            _fileIO.CreateFile(batchFile, command);

            // 以前は完了を待たずに戻っていたため、直後の一覧更新に削除前のフォルダが残り、
            // 一時バッチファイルとプロセスのハンドルも残ったままだった
            _util.ExecutePathWithWait(batchFile, "", true);
            File.Delete(batchFile);
        }
    }
}
