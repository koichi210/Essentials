// BackgroundWorkerへ渡すパラメータ。
//
// 以前は List<object> に値を順番に詰めて、DoWork側で genericlist[3 + i * 3 + 1] のように
// 位置を計算して取り出していた。対象が増えるたびに3個ずつ詰めるという暗黙のルールがあり、
// 並び順がずれても気づけないため、専用のクラスで名前を付けて受け渡しするようにした。
using System;
using System.Collections.Generic;

namespace FileArrangerForWpf
{
    // ファイル移動(mfタブ)
    class MoveFileWorkerParam
    {
        public String SourceDir;
        public String TargetDir;
        public List<String> TargetNames = new List<String>();
    }

    // フォルダ振り分け(pfタブ)
    class PartitionWorkerParam
    {
        // 1件分の移動情報(対象ファイル名 / 移動前のフォルダ名 / 移動後のフォルダ名)
        public class Item
        {
            public String TargetName;
            public String MoveSrc;
            public String MoveDest;
        }

        public String TargetFilePath;
        public String TargetDir;
        public List<Item> Items = new List<Item>();
    }
}
