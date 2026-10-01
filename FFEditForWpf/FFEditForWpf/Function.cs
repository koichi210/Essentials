using System;
using System.Collections.Generic;
using System.IO;

namespace FFEditForWpf
{
    class Function
    {
        public enum FunctionType
        {
            DelEmptyDir,
            Copy,
            Move,
        }

        public String BaseDir { get; set; } = "";
        public String DestDir { get; set; } = "";
        public List<String> FileList { get; set; }
        public FunctionType Type { get; set; }

        private readonly FileMng fm = new FileMng();

        public Boolean Restore()
        {
            return fm.RestoreAll();
        }

        public String Execute()
        {
            String errorList = "";

            for (int i = 0; i < FileList.Count; i++)
            {
                String srcName = BaseDir + '\\' + FileList[i];
                String destName;
                switch (Type)
                {
                    case FunctionType.DelEmptyDir:
                        fm.DeleteBlankDir(srcName);
                        break;

                    case FunctionType.Move:
                        destName = DestDir + '\\' + Path.GetFileName(FileList[i]);
                        if (srcName == destName)
                        {
                            // 同一だったら処理しない
                            continue;
                        }

                        Directory.CreateDirectory(DestDir);
                        if (fm.Move(srcName, destName))
                        {
                            // 復元用に設定を覚えておく
                            fm.SetRestoreList(srcName, destName);
                        }
                        else
                        {
                            errorList += GetErrorText(srcName, destName);
                        }
                        break;

                    case FunctionType.Copy:
                        destName = DestDir + '\\' + Path.GetFileName(FileList[i]);
                        if (srcName == destName)
                        {
                            // 同一だったら処理しない
                            continue;
                        }

                        Directory.CreateDirectory(DestDir);
                        // コピーのときは処理を覚えない
                        if (!fm.Copy(srcName, destName))
                        {
                            errorList += GetErrorText(srcName, destName);
                        }
                        break;
                }
            }
            fm.IncrementRegistNumber();

            return errorList;
        }

        private static String GetErrorText(String srcName, String destName)
        {
            return "Src=" + srcName + Environment.NewLine
                + "Dst=" + destName + Environment.NewLine
                + Environment.NewLine;
        }
    }
}
