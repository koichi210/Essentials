// 各タブのBackgroundWorkerへ渡すパラメータ。
//
// 以前は List<object> に値を順番に詰めて、DoWork側で genericlist[4] のように位置で
// 取り出していた。並び順がずれても気づけず、取り出し時のキャストミスも実行するまで
// 分からないため、タブごとに専用のクラスを用意して名前で受け渡しするようにした。
using System;

namespace CheetosForWpf
{
    // トリミング(PictTrim)
    class TrimWorkerParam
    {
        public String BaseX;
        public String BaseY;
        public int TargetWidth;
        public int TargetHeight;
        public String SourceFolderPath;
        public String BackUpDirPath;
        public String[] TargetNameAry;
    }

    // 画像の合成(PictMerge)
    class MergeWorkerParam
    {
        public String BackUpDirPath;
        public String SourceFolderPath;
        public String SourceFile1Prefix;
        public String SourceFile2Prefix;
        public String[] TrimHeightAry;
        public String[] TargetNameAry;
    }

    // 回転(Rotation)
    class RotationWorkerParam
    {
        public String BaseX;
        public String BaseY;
        public String Angle;
        public String SourceFolderPath;
        public String BackUpDirPath;
        public String[] TargetNameAry;
    }

    // 縦横の振り分け(DistOrient)
    class OrientWorkerParam
    {
        public int WhiteLength;
        public int WhiteCoef;
        public String DestPortFolderPath;
        public String DestLandFolderPath;
        public String[] Files;
    }
}
