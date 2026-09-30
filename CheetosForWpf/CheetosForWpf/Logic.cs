using System;
using System.Drawing;
using System.Windows.Input;
using Picture;

namespace CheetosForWpf
{
    /// <summary>
    /// もともと Cheetos フォームの各タブ（DistOrient.cs / CaptureWindow.cs /
    /// RotationPreview.cs）に private メソッドとして埋め込まれていた純粋なロジックを、
    /// テストできる形に切り出したもの(WinForms版Cheetosの Logic.cs をそのまま移植)。
    ///
    /// WPF版での変更点は2つだけ:
    /// ・IsPortrait の結果表示を WPF の MessageBox にした
    /// ・UpdateValue の引数を WinForms の KeyEventArgs から WPF の Key にした
    ///   (WPFのKeyEventArgsはPresentationSource無しでは作れず、テストしづらいため)
    /// </summary>
    internal static class Logic
    {
        /// <summary>
        /// 画像の左右の白フチの太さを比較し、縦長（Portrait）向けの画像かどうかを判定する。
        /// IsSample=true のときは判定結果をポップアップ表示する（もとの実装のまま）。
        /// </summary>
        public static bool IsPortrait(String TargetFileName, int WhiteWidth, int WhiteCoef, Boolean IsSample = false)
        {
            Boolean IsPort = true;

            // 1回だけデコードしたBitmapを使い回すことでデコード回数を1回に減らす。
            using (Bitmap SourceImg = new Bitmap(TargetFileName))
            {
                Size pict_sz = new Size(SourceImg.Width, SourceImg.Height);

                // 指定幅より画像サイズが小さければ、画像サイズの幅に合わせる
                int width = Math.Min(WhiteWidth, pict_sz.Width);

                // WhiteCoefは、WhiteAreaを算出するための係数(実測値)
                int BaseSize = width * pict_sz.Height / WhiteCoef;

                // 左端
                long LeftPictSize = 0;
                if (IsPort == true)
                {
                    Rectangle CutParam = new Rectangle(0, 0, width, pict_sz.Height);
                    LeftPictSize = GetBinSize(SourceImg, CutParam);
                    if (BaseSize < LeftPictSize)
                    {
                        IsPort = false;
                    }
                }

                // 右端
                long RightPictSize = 0;
                if (IsPort == true)
                {
                    Rectangle CutParam = new Rectangle(pict_sz.Width - width, 0, width, pict_sz.Height);
                    RightPictSize = GetBinSize(SourceImg, CutParam);
                    if (BaseSize < RightPictSize)
                    {
                        IsPort = false;
                    }
                }

                if (IsSample)
                {
                    String ResultStr = "IsPortrait=" + IsPort.ToString() + Environment.NewLine +
                        "BaseSize=" + BaseSize.ToString() + Environment.NewLine +
                        "LeftPictSize=" + LeftPictSize.ToString() + Environment.NewLine +
                        "RightPictSize=" + RightPictSize.ToString();
                    System.Windows.MessageBox.Show(ResultStr, "画像情報");
                }

                return IsPort;
            }
        }

        /// <summary>画像の指定範囲を切り出して、PNGエンコードした場合のバイト数を返す。</summary>
        public static long GetBinSize(String FileName, Rectangle CutParam)
        {
            using (Bitmap SourceImg = new Bitmap(FileName))
            {
                return GetBinSize(SourceImg, CutParam);
            }
        }

        /// <summary>既にデコード済みのBitmapから指定範囲を切り出し、PNGエンコードした場合のバイト数を返す。</summary>
        private static long GetBinSize(Bitmap SourceImg, Rectangle CutParam)
        {
            PicEdit trm = new PicEdit(CutParam.Width, CutParam.Height);

            // 切り取り
            Point PutParam = new Point(0, 0);
            trm.TrimExec(SourceImg, CutParam, PutParam);

            // メモリ上でPNGエンコードしてそのバイト数を見る
            long Length = trm.GetCanvasPngByteLength();

            trm.Dispose();
            return Length;
        }

        /// <summary>
        /// キャプチャ画像のファイル名の先頭部分（保存先＋接頭辞＋任意でタイムスタンプ）を組み立てる。
        /// </summary>
        public static String GetFileBaseFormat(String DirectoryPath, String Prefix, Boolean AddTimeStamp)
        {
            String FileBaseFormat = DirectoryPath + @"\";
            if (Prefix != String.Empty)
            {
                FileBaseFormat += Prefix + "_";
            }
            if (AddTimeStamp)
            {
                FileBaseFormat += System.DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss_");
            }

            return FileBaseFormat;
        }

        /// <summary>
        /// テキストボックスの数値を、上下キーで+1/-1する。数値でなければ変更しない。
        /// </summary>
        public static String UpdateValue(String base_value, Key key)
        {
            int add_value = 0;
            switch (key)
            {
                case Key.Up:
                    add_value = 1;
                    break;
                case Key.Down:
                    add_value = -1;
                    break;
                case Key.Enter:
                    break;
                default:
                    break;
            }

            int val;
            if (Int32.TryParse(base_value.ToString(), out val))
            {
                val += add_value;
                return val.ToString();
            }
            return base_value;
        }
    }
}
