using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

        private readonly FileMng _fm = new FileMng();

        public Boolean Restore()
        {
            return _fm.RestoreAll();
        }

        public String Execute()
        {
            StringBuilder errorList = new StringBuilder();

            for (int i = 0; i < FileList.Count; i++)
            {
                String targetName = FileList[i];
                String srcName = BaseDir + '\\' + targetName;
                String destName;
                try
                {
                    destName = BaseDir + '\\' + GetChangedName(targetName, i);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentOutOfRangeException)
                {
                    // 削除する文字数が数値でない・名前より長い等。以前は例外でループごと抜けてしまい、
                    // それまでに済んだ分の復元用の番号(IncrementSerialNumber)が進まず、次回の実行と
                    // 一緒くたに復元されていた。この1件だけエラーにして続ける
                    errorList.Append("Src=" + srcName + Environment.NewLine
                        + "Error=" + ex.Message + Environment.NewLine
                        + Environment.NewLine);
                    continue;
                }

                // 同一だったら処理しない
                if (srcName == destName)
                {
                    continue;
                }

                if (_fm.Move(srcName, destName))
                {
                    // 復元用に処理を覚えておく
                    _fm.AddRestoreItem(srcName, destName);
                }
                else
                {
                    // エラー発生
                    errorList.Append(FileMng.GetErrorText(srcName, destName));
                }
            }
            _fm.IncrementSerialNumber();

            return errorList.ToString();
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
                    // 「.jpg」とドット付きで入れても「name..jpg」にならないよう、先頭のドットは無視する
                    targetName += "." + Param1.TrimStart('.');
                    break;
                case ChangeType.AddDirName:
                    targetName = srcName.Replace('\\', '_');
                    break;
                default:
                    break;
            }

            String directoryPath = Path.GetDirectoryName(srcName);
            return directoryPath == String.Empty ? targetName : directoryPath.TrimEnd('\\') + @"\" + targetName;
        }

        // 連番の0埋め桁数が[自動]のときの桁数。
        // 以前は件数の桁数だけで決めていたため、開始番号が大きいと(例: 95から10件で95～104)
        // 途中で桁数が変わり、名前順に並ばなくなっていた。最後の番号の桁数も見る
        // (件数の桁数の方が多い場合は、これまでどおりそちらに合わせる)
        public static int GetAutoPaddingDigits(int firstNumber, int count)
        {
            int digits = count.ToString().Length;
            long lastNumber = (long)firstNumber + count - 1;
            if (lastNumber >= 0)
            {
                digits = Math.Max(digits, lastNumber.ToString().Length);
            }
            return digits;
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
            // 両方空欄なら元の名前のまま(以前は空文字を返し、フォルダ名へリネームしようとしてエラーになっていた)
            String destName = srcName;
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
