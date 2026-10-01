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
        public static bool IsPortrait(String targetFileName, int whiteWidth, int whiteCoef, Boolean isSample = false)
        {
            Boolean isPortrait = true;

            // 1回だけデコードしたBitmapを使い回すことでデコード回数を1回に減らす。
            using (Bitmap sourceImg = new Bitmap(targetFileName))
            {
                Size pictSize = new Size(sourceImg.Width, sourceImg.Height);

                // 指定幅より画像サイズが小さければ、画像サイズの幅に合わせる
                int width = Math.Min(whiteWidth, pictSize.Width);

                // whiteCoefは、WhiteAreaを算出するための係数(実測値)
                int baseSize = width * pictSize.Height / whiteCoef;

                // 左端
                Rectangle leftCutParam = new Rectangle(0, 0, width, pictSize.Height);
                long leftPictSize = GetBinSize(sourceImg, leftCutParam);
                if (baseSize < leftPictSize)
                {
                    isPortrait = false;
                }

                // 右端(左端で縦長でないと分かったら見ない)
                long rightPictSize = 0;
                if (isPortrait)
                {
                    Rectangle rightCutParam = new Rectangle(pictSize.Width - width, 0, width, pictSize.Height);
                    rightPictSize = GetBinSize(sourceImg, rightCutParam);
                    if (baseSize < rightPictSize)
                    {
                        isPortrait = false;
                    }
                }

                if (isSample)
                {
                    String resultStr = "IsPortrait=" + isPortrait.ToString() + Environment.NewLine +
                        "BaseSize=" + baseSize.ToString() + Environment.NewLine +
                        "LeftPictSize=" + leftPictSize.ToString() + Environment.NewLine +
                        "RightPictSize=" + rightPictSize.ToString();
                    System.Windows.MessageBox.Show(resultStr, "画像情報");
                }

                return isPortrait;
            }
        }

        /// <summary>画像の指定範囲を切り出して、PNGエンコードした場合のバイト数を返す。</summary>
        public static long GetBinSize(String fileName, Rectangle cutParam)
        {
            using (Bitmap sourceImg = new Bitmap(fileName))
            {
                return GetBinSize(sourceImg, cutParam);
            }
        }

        /// <summary>既にデコード済みのBitmapから指定範囲を切り出し、PNGエンコードした場合のバイト数を返す。</summary>
        private static long GetBinSize(Bitmap sourceImg, Rectangle cutParam)
        {
            PicEdit trm = new PicEdit(cutParam.Width, cutParam.Height);

            // 切り取り
            trm.TrimExec(sourceImg, cutParam, new Point(0, 0));

            // メモリ上でPNGエンコードしてそのバイト数を見る
            long length = trm.GetCanvasPngByteLength();

            trm.Dispose();
            return length;
        }

        /// <summary>
        /// キャプチャ画像のファイル名の先頭部分（保存先＋接頭辞＋任意でタイムスタンプ）を組み立てる。
        /// </summary>
        public static String GetFileBaseFormat(String directoryPath, String prefix, Boolean addTimeStamp)
        {
            String fileBaseFormat = directoryPath + @"\";
            if (prefix != String.Empty)
            {
                fileBaseFormat += prefix + "_";
            }
            if (addTimeStamp)
            {
                fileBaseFormat += System.DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss_");
            }

            return fileBaseFormat;
        }

        /// <summary>
        /// テキストボックスの数値を、上下キーで+1/-1する。数値でなければ変更しない。
        /// </summary>
        public static String UpdateValue(String baseValue, Key key)
        {
            int addValue = 0;
            switch (key)
            {
                case Key.Up:
                    addValue = 1;
                    break;
                case Key.Down:
                    addValue = -1;
                    break;
                default:
                    break;
            }

            int val;
            if (Int32.TryParse(baseValue.ToString(), out val))
            {
                val += addValue;
                return val.ToString();
            }
            return baseValue;
        }
    }
}
