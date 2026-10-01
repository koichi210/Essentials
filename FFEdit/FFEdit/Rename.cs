using System;
using System.Collections.Generic;
using System.IO;

namespace FFEdit
{
    class Rename
    {
        public enum ChangeType
        {
            Number,
            DelNum,
            Add,
            Delete,
            Replace,
            OnlyExt,
            AddDirName,
        }

        public String BaseDir { get; set; } = "";
        public List<String> FileList { get; set; }
        public ChangeType Type { get; set; }
        public String Param1 { get; set; } = "";
        public String Param2 { get; set; } = "";
        public int FirstNumber { get; set; }
        public int PaddingDigits { get; set; } // 0埋めする桁数
        public Boolean KeepOriginalName { get; set; }

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
                String targetName = FileList[i];
                String srcName = BaseDir + '\\' + targetName;
                String destName = BaseDir + '\\' + GetChangedName(targetName, i);

                // 同一だったら処理しない
                if (srcName == destName)
                {
                    continue;
                }

                if (fm.Move(srcName, destName))
                {
                    // 復元用に処理を覚えておく
                    fm.AddRestoreItem(srcName, destName);
                }
                else
                {
                    // エラー発生
                    errorList += "Src=" + srcName + Environment.NewLine;
                    errorList += "Dst=" + destName + Environment.NewLine;
                    errorList += Environment.NewLine;
                }
            }
            fm.IncrementSerialNumber();

            return errorList;
        }

        private String GetChangedName(String srcName, int index)
        {
            String targetName = Path.GetFileName(srcName);

            switch (Type)
            {
                case ChangeType.Number:
                    targetName = GetNumberedName(targetName, index);
                    break;
                case ChangeType.DelNum:
                    targetName = GetCharsRemovedName(targetName);
                    break;
                case ChangeType.Add:
                    targetName = GetAddedName(targetName);
                    break;
                case ChangeType.Delete:
                    targetName = targetName.Replace(Param1, "");
                    break;
                case ChangeType.Replace:
                    targetName = targetName.Replace(Param1, Param2);
                    break;
                case ChangeType.OnlyExt:
                    targetName = Path.GetFileNameWithoutExtension(targetName);
                    targetName += "." + Param1;
                    break;
                case ChangeType.AddDirName:
                    targetName = srcName.Replace('\\', '_');
                    break;
                default:
                    break;
            }

            String fullPathName = "";
            String directoryPath = Path.GetDirectoryName(srcName);
            if (directoryPath != String.Empty)
            {
                fullPathName += directoryPath.TrimEnd('\\') + @"\";
            }
            fullPathName += targetName;

            return fullPathName;
        }

        // 連番のファイル名(元の名前を残す指定なら「連番＋元の名前」)
        private String GetNumberedName(String srcName, int index)
        {
            int number = index + FirstNumber;

            // 文字列生成
            String destName = number.ToString().PadLeft(PaddingDigits, '0');

            if (KeepOriginalName)
            {
                destName += Path.GetFileNameWithoutExtension(srcName);
            }
            destName += Path.GetExtension(srcName);

            return destName;
        }

        // 先頭からParam1文字・(拡張子を除いた)後方からParam2文字を削除したファイル名
        private String GetCharsRemovedName(String srcName)
        {
            String destName = "";
            if (Param1 != String.Empty)
            {
                destName = srcName.Remove(0, int.Parse(Param1));
                srcName = destName;
            }

            if (Param2 != String.Empty)
            {
                String fileNameWithoutExt = Path.GetFileNameWithoutExtension(srcName);
                destName = fileNameWithoutExt.Remove(fileNameWithoutExt.Length - int.Parse(Param2));
                destName += Path.GetExtension(srcName);
            }

            return destName;
        }

        private String GetAddedName(String srcName)
        {
            String destName = srcName;

            // 先頭に追加するときはシンプルに。
            if (Param1 != String.Empty)
            {
                destName = Param1 + srcName;
            }

            // 後方に追加するときは、「ファイル名＋追加文字＋拡張子」に。
            if (Param2 != String.Empty)
            {
                // 先頭に文字追加しているケースをcare
                srcName = destName;

                destName = Path.GetFileNameWithoutExtension(srcName);
                destName += Param2;
                destName += Path.GetExtension(srcName);
            }

            return destName;
        }
    }
}
