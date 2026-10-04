using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFEdit
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
                String srcName = BaseDir + '\\' + FileList[i];
                if (Type == FunctionType.DelEmptyDir)
                {
                    _fm.DeleteBlankDir(srcName);
                    continue;
                }

                // 移動/コピーは移動先のパスが同じ形なので共通にする
                String destName = DestDir + '\\' + Path.GetFileName(FileList[i]);
                if (srcName == destName)
                {
                    // 同一だったら処理しない
                    continue;
                }

                Directory.CreateDirectory(DestDir);
                if (Type == FunctionType.Move)
                {
                    if (_fm.Move(srcName, destName))
                    {
                        // 復元用に設定を覚えておく
                        _fm.AddRestoreItem(srcName, destName);
                    }
                    else
                    {
                        errorList.Append(FileMng.GetErrorText(srcName, destName));
                    }
                }
                else if (!_fm.Copy(srcName, destName))
                {
                    // コピーのときは処理を覚えない
                    errorList.Append(FileMng.GetErrorText(srcName, destName));
                }
            }
            _fm.IncrementSerialNumber();

            return errorList.ToString();
        }
    }
}
