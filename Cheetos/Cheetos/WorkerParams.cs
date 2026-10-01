// 各タブのBackgroundWorkerへ渡すパラメータ。
//
// 以前は List<object> に値を順番に詰めて、DoWork側で genericlist[4] のように位置で
// 取り出していた。並び順がずれても気づけず、取り出し時のキャストミスも実行するまで
// 分からないため、タブごとに専用のクラスを用意して名前で受け渡しするようにした。
using System;

namespace Cheetos
{
    // トリミング(PictTrim)
    class TrimWorkerParam
    {
        public String BaseX { get; set; }
        public String BaseY { get; set; }
        public int TargetWidth { get; set; }
        public int TargetHeight { get; set; }
        public String SourceFolderPath { get; set; }
        public String BackUpDirPath { get; set; }
        public String[] TargetFileNames { get; set; }
    }

    // 画像の合成(PictMerge)
    class MergeWorkerParam
    {
        public String BackUpDirPath { get; set; }
        public String SourceFolderPath { get; set; }
        public String SourceFile1Prefix { get; set; }
        public String SourceFile2Prefix { get; set; }
        public String[] TrimHeightRanges { get; set; }
        public String[] TargetFileNames { get; set; }
    }

    // 回転(Rotation)
    class RotationWorkerParam
    {
        public String BaseX { get; set; }
        public String BaseY { get; set; }
        public String Angle { get; set; }
        public String SourceFolderPath { get; set; }
        public String BackUpDirPath { get; set; }
        public String[] TargetFileNames { get; set; }
    }

    // 縦横の振り分け(DistOrient)
    class OrientWorkerParam
    {
        public int WhiteLength { get; set; }
        public int WhiteCoef { get; set; }
        public String DestPortFolderPath { get; set; }
        public String DestLandFolderPath { get; set; }
        public String[] Files { get; set; }
    }
}
