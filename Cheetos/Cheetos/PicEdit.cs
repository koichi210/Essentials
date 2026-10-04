using System;
using System.Drawing;
using System.IO;

namespace Picture
{
    class PicEdit : IDisposable
    {
        // 描画先
        protected Bitmap _canvas;
        protected Bitmap _sourceImg;

        public PicEdit(String basePictFile)
        {
            //既存ファイルをもとに、描画先Imageオブジェクトを作成
            _canvas = new Bitmap(basePictFile);
        }

        public PicEdit(int destWidth, int destHeight)
        {
            //新規に描画先Imageオブジェクトを作成
            _canvas = new Bitmap(destWidth, destHeight);
        }

        // IDisposable。ReleaseImg が解放後に null を入れるので、二重に呼んでも
        // (SaveCanvas の後に呼んでも) 何も起きず安全。
        public void Dispose()
        {
            // リソース解放
            ReleaseImg(ref _canvas);
            ReleaseImg(ref _sourceImg);
        }

        public void SaveCanvas(String savePictFile)
        {
            _canvas.Save(savePictFile);

            // TODO：デストラクタでは想定したタイミングで呼ばれないため暫定。
            // リソース解放
            Dispose();
        }

        public void TrimExec(String basePictFile, Rectangle cutParam)
        {
            TrimExec(basePictFile, cutParam, new Point(cutParam.X, cutParam.Y));
        }

        public void TrimExec(String basePictFile, Rectangle cutParam, Point putParam)
        {
            //画像ファイルのImageオブジェクトを作成
            using (Bitmap img = new Bitmap(basePictFile))
            {
                TrimExec(img, cutParam, putParam);
            }
        }

        // 既にデコード済みのBitmapから切り取る版。呼び出し側が同じ元画像から複数回
        // 切り出したい場合(Cheetos.Logic.IsPortrait等)、ファイルパス版を複数回呼ぶと
        // その都度フルデコードが走ってしまうため、デコード済みのBitmapを使い回せるように
        // 用意した。渡されたBitmapの所有権は呼び出し側のままなので、ここではDisposeしない。
        public void TrimExec(Bitmap sourceImg, Rectangle cutParam, Point putParam)
        {
            //描画する部分の範囲を設定。位置(X, Y)、大きさ(Width, Height)
            Rectangle pasteRect = new Rectangle(putParam.X, putParam.Y, cutParam.Width, cutParam.Height);

            //ImageオブジェクトのGraphicsオブジェクトを作成し、画像の一部を描画
            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.DrawImage(sourceImg, pasteRect, cutParam, GraphicsUnit.Pixel);
            }
        }

        // キャンバスをディスクに書き出さずに、PNGエンコード後のバイト数だけを知りたい場合に使う。
        // SaveCanvasと違いキャンバスの破棄はしない(呼び出し側で明示的にDisposeする)。
        public long GetCanvasPngByteLength()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                _canvas.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.Length;
            }
        }

        public void CreateSourceImg(String sourceImgFile)
        {
            //加工元ファイルのImageオブジェクトを作成。
            // MergeExecは以前、呼ばれるたびに読み込んだ画像の複製(new Bitmap(Image))を作って描画していた。
            // 複製の中身は毎回同じなので、ここで1回だけ複製して使い回す(描画結果は同じ)。
            // 読み込んだ元のBitmapはすぐ解放するので、ファイルのロックも早く外れる
            ReleaseImg(ref _sourceImg);
            using (Bitmap loaded = new Bitmap(sourceImgFile))
            {
                _sourceImg = new Bitmap(loaded);
            }
        }

        public void ReleaseSourceImg()
        {
            ReleaseImg(ref _sourceImg);
        }

        public void MergeExec(Rectangle cutParam)
        {
            MergeExec(cutParam, new Point(cutParam.X, cutParam.Y));
        }

        public void MergeExec(Rectangle cutParam, Point putParam)
        {
            //CreateSourceImgで読み込んだ画像の複製から描画する
            TrimExec(_sourceImg, cutParam, putParam);
        }

        public Size GetCanvasSize()
        {
            return new Size(_canvas.Width, _canvas.Height);
        }

        private void ReleaseImg(ref Bitmap img)
        {
            if (img != null)
            {
                img.Dispose();
                img = null;
            }
        }
    }
}
