using System;
using System.Collections.Generic;
using System.IO;

namespace FFEditForWpf
{
    class TimeStamp
    {
        public String _base_dir = "";
        public List<String> _file_list;
        public long _base_tick_time = 0;
        public long _update_tick_time = 0;
        public Boolean _target_time_create = false;
        public Boolean _target_time_last_write = false;
        public Boolean _target_time_access = false;

        public void Execute()
        {
            for (int i = 0; i < _file_list.Count; i++)
            {
                String SrcName = _base_dir + '\\' + _file_list[i];
                long tick = _update_tick_time * i;
                Update(SrcName, _base_tick_time + tick);
            }
        }

        private void Update(String SrcName, long tick)
        {
            DateTime dt = new DateTime(tick);

            // ファイル情報更新
            FileInfo fi = new FileInfo(SrcName);
            if (_target_time_create)
            {
                fi.CreationTime = dt;
            }
            if (_target_time_last_write)
            {
                fi.LastWriteTime = dt;
            }
            if (_target_time_access)
            {
                fi.LastAccessTime = dt;
            }
        }
    }
}
