using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Othello
{
    /// <summary>
    /// BoardRenderer.Canvas(System.Drawing.Bitmap)をWPFのImageに表示できるBitmapSourceへ変換する。
    /// GetHbitmap+CreateBitmapSourceFromHBitmapだとGDIハンドルの解放忘れが起きやすいので、
    /// LockBitsでピクセルを直接コピーする方式にしている。
    /// </summary>
    internal static class BoardImage
    {
        public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap, double dpiX, double dpiY)
        {
            if (bitmap == null)
            {
                return null;
            }

            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
                rect,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                BitmapSource source = BitmapSource.Create(
                    bitmap.Width, bitmap.Height, dpiX, dpiY,
                    PixelFormats.Bgra32, null,
                    data.Scan0, Math.Abs(data.Stride) * bitmap.Height, Math.Abs(data.Stride));
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
