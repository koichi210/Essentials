using System;
using System.Collections.Generic;
using System.IO;

namespace FFEditForWpf
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

        public void Execute()
        {
            for (int i = 0; i < FileList.Count; i++)
            {
                String srcName = BaseDir + '\\' + FileList[i];
                Update(srcName, BaseTicks + IntervalTicks * i);
            }
        }

        private void Update(String srcName, long tick)
        {
            DateTime dt = new DateTime(tick);

            // ファイル情報更新
            FileInfo fi = new FileInfo(srcName);
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
    }
}
