using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FFEditForWpf
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

        public String _base_dir = "";
        public List<String> _file_list;
        public ChangeType _change_type;
        public String _param1 = "";
        public String _param2 = "";
        public int _first_number = 0;
        public int _pad_number = 0; // 0埋めする桁数
        public Boolean _keep_org_name = false;

        private FileMng fm = new FileMng();
        
        public Boolean Restore()
        {
            return fm.RestoreAll();
        }

        public String Execute()
        {
            String ErrorList = "";

            for (int i = 0; i < _file_list.Count; i++)
            {
                String TargetName = _file_list[i];
                String SrcName = _base_dir + '\\' + TargetName;
                String DestName = _base_dir + '\\' + GetChangedName(TargetName, i);

                // 同一だったら処理しない
                if (SrcName == DestName)
                {
                    continue;
                }

                if (fm.Move(SrcName, DestName))
                {
                    // 復元用に処理を覚えておく
                    fm.SetRestoreList(SrcName, DestName);
                }
                else
                {
                    // エラー発生
                    ErrorList += "Src=" + SrcName + Environment.NewLine;
                    ErrorList += "Dst=" + DestName + Environment.NewLine;
                    ErrorList += Environment.NewLine;
                }
            }
            fm.IncrementRegistNumber();

            return ErrorList;
        }

        private String GetChangedName(String SrcName, int LoopCnt = 0)
        {
            String TargetName = Path.GetFileName(SrcName);

            switch(_change_type)
            {
                case ChangeType.Number:
                    TargetName = GetAddNumberName(TargetName, LoopCnt);
                    break;
                case ChangeType.DelNum:
                    TargetName = GetDelNumberName(TargetName);
                    break;
                case ChangeType.Add:
                    TargetName = GetAddName(TargetName);
                    break;
                case ChangeType.Delete:
                    TargetName = TargetName.Replace(_param1, "");
                    break;
                case ChangeType.Replace:
                    TargetName = TargetName.Replace(_param1, _param2);
                    break;
                case ChangeType.OnlyExt:
                    TargetName = Path.GetFileNameWithoutExtension(TargetName);
                    TargetName += "." + _param1;
                    break;
                case ChangeType.AddDirName:
                    TargetName = SrcName.Replace('\\', '_');
                    break;
                default:
                    break;
            }

            String FullPathName = "";
            String DirectoryPath = Path.GetDirectoryName(SrcName);
            if (DirectoryPath != String.Empty)
            {
                FullPathName += DirectoryPath.TrimEnd('\\') + @"\";
            }
            FullPathName += TargetName;

            return FullPathName;
        }

        private String GetAddNumberName(String SrcName, int LoopCnt)
        {
            String DestName = "";
            int Number = LoopCnt + _first_number;

            // 文字列生成
            DestName += Number.ToString().PadLeft(_pad_number, '0');
            
            if (_keep_org_name)
            {
                DestName += Path.GetFileNameWithoutExtension(SrcName);
            }
            DestName += Path.GetExtension(SrcName);

            return DestName;
        }

        private String GetDelNumberName(String SrcName)
        {
            String DestName = "";
            if (_param1 != String.Empty)
            {
                DestName = SrcName.Remove(0, int.Parse(_param1));
                SrcName = DestName;
            }

            if (_param2 != String.Empty)
            {
                String FileNameWithOutExt = Path.GetFileNameWithoutExtension(SrcName);
                DestName = FileNameWithOutExt.Remove(FileNameWithOutExt.Length - int.Parse(_param2));
                DestName += Path.GetExtension(SrcName);
            }

            return DestName;
        }

        private String GetAddName(String SrcName)
        {
            String DestName = SrcName;

            // 先頭に追加するときはシンプルに。
            if (_param1 != String.Empty)
            {
                DestName = _param1 + SrcName;
            }

            // 後方に追加するときは、「ファイル名＋追加文字＋拡張子」に。
            if (_param2 != String.Empty)
            {
                // 先頭に文字追加しているケースをcare
                SrcName = DestName;

                DestName = Path.GetFileNameWithoutExtension(SrcName);
                DestName += _param2;
                DestName += Path.GetExtension(SrcName);
            }

            return DestName;
        }
    }
}
