using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFEdit
{
    class TimeStamp
    {
        public String BaseDir { get; set; } = "";
        public List<String> FileList { get; set; }
        public long BaseTicks { get; set; }        // 1件目に設定する日時(Tick)
        public long IntervalTicks { get; set; }    // 2件目以降、1件ごとに加算する時間(Tick)
        public Boolean UpdateCreationTime { get; set; }
        public Boolean UpdateLastWriteTime { get; set; }
        public Boolean UpdateLastAccessTime { get; set; }

        // JPEGのEXIFにある撮影日時も書き換える([[ExifDate.cs]])
        public Boolean UpdateDateTaken { get; set; }

        // 戻り値はエラー一覧(撮影日時を書き換えられなかったファイルなど)。無ければ空文字
        public String Execute()
        {
            StringBuilder errorList = new StringBuilder();
            for (int i = 0; i < FileList.Count; i++)
            {
                String srcName = BaseDir + '\\' + FileList[i];
                Update(srcName, BaseTicks + IntervalTicks * i, errorList);
            }
            return errorList.ToString();
        }

        private void Update(String srcName, long tick, StringBuilder errorList)
        {
            DateTime dt = new DateTime(tick);

            // ファイル情報更新
            FileInfo fi = new FileInfo(srcName);
            if (UpdateDateTaken)
            {
                UpdateExifDate(fi, dt, errorList);
            }
            if (UpdateCreationTime)
            {
                fi.CreationTime = dt;
            }
            if (UpdateLastWriteTime)
            {
                fi.LastWriteTime = dt;
            }
            if (UpdateLastAccessTime)
            {
                fi.LastAccessTime = dt;
            }
        }

        // EXIFを書き換えるとファイルの更新日時も今の時刻に変わってしまうので、
        // 更新対象にしていない日時は書き換え前の値に戻す
        private static void UpdateExifDate(FileInfo fi, DateTime dt, StringBuilder errorList)
        {
            if (!fi.Exists)
            {
                return;
            }

            DateTime originalCreationTime = fi.CreationTime;
            DateTime originalLastWriteTime = fi.LastWriteTime;
            DateTime originalLastAccessTime = fi.LastAccessTime;
            try
            {
                if (ExifDate.Write(fi.FullName, dt) == 0)
                {
                    AppendError(errorList, fi.FullName, "撮影日時の欄が無いため書き換えませんでした(JPEG以外、またはEXIFに日時が無いJPEG)");
                    return;
                }
                fi.CreationTime = originalCreationTime;
                fi.LastWriteTime = originalLastWriteTime;
                fi.LastAccessTime = originalLastAccessTime;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppendError(errorList, fi.FullName, "撮影日時を書き換えられませんでした: " + ex.Message);
            }
        }

        private static void AppendError(StringBuilder errorList, String path, String message)
        {
            errorList.Append("Src=" + path + Environment.NewLine
                + "Error=" + message + Environment.NewLine
                + Environment.NewLine);
        }
    }
}
