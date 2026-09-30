using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public String _base_dir = "";
        public String _target_dir = "";
        public List<String> _file_list;
        public FunctionType _function_type;

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
                String SrcName = _base_dir + '\\' + _file_list[i];
                String DestName = "";
                switch (_function_type)
                {
                    case FunctionType.DelEmptyDir:
                        fm.DeleteBlankDir(SrcName);
                        break;

                    case FunctionType.Move:
                        DestName = _target_dir + '\\' + Path.GetFileName(_file_list[i]);
                        if (SrcName == DestName)
                        {
                            // 同一だったら処理しない
                            continue;
                        }

                        Directory.CreateDirectory(_target_dir);
                        if (fm.Move(SrcName, DestName))
                        {
                            // 復元用に設定を覚えておく
                            fm.SetRestoreList(SrcName, DestName);
                        }
                        else
                        {
                            ErrorList += "Src=" + SrcName + Environment.NewLine;
                            ErrorList += "Dst=" + DestName + Environment.NewLine;
                            ErrorList += Environment.NewLine;
                        }
                        break;

                    case FunctionType.Copy:
                        DestName = _target_dir + '\\' + Path.GetFileName(_file_list[i]);
                        if (SrcName == DestName)
                        {
                            // 同一だったら処理しない
                            continue;
                        }

                        Directory.CreateDirectory(_target_dir);
                        if (fm.Copy(SrcName, DestName))
                        {
                            // コピーのときは処理を覚えない
                        }
                        else
                        {
                            ErrorList += "Src=" + SrcName + Environment.NewLine;
                            ErrorList += "Dst=" + DestName + Environment.NewLine;
                            ErrorList += Environment.NewLine;
                        }
                        break;
                }
            }
            fm.IncrementRegistNumber();

            return ErrorList;
        }
    }
}
