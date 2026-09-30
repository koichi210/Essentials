using System;
using System.Drawing;
using System.IO;

namespace Picture
{
    class PicEdit
    {
        // 描画先
        protected Bitmap m_Canvas;
        protected Bitmap m_SourceImg;

        public PicEdit(String BasePictFile)
        {
            //既存ファイルをもとに、描画先Imageオブジェクトを作成
            m_Canvas = new Bitmap(BasePictFile);
        }

        public PicEdit(int DestWidth, int DestHeight)
        {
            //新規に描画先Imageオブジェクトを作成
            m_Canvas = new Bitmap(DestWidth, DestHeight);
        }

        ~PicEdit()
        {
        }
        public void Dispose()
        {
            //// リソース解放
            ReleaseImg(ref m_Canvas);
            ReleaseImg(ref m_SourceImg);
        }

        public void SaveCanvas(String SavePictFile)
        {
            m_Canvas.Save(SavePictFile);

            // TODO：デストラクタでは想定したタイミングで呼ばれないため暫定。
            // リソース解放
            ReleaseImg(ref m_Canvas);
            ReleaseImg(ref m_SourceImg);
        }

        public void TrimExec(String BasePictFile, Rectangle CutParam)
        {
            TrimExec(BasePictFile, CutParam, new Point(CutParam.X, CutParam.Y));
        }

        public void TrimExec(String BasePictFile, Rectangle CutParam, Point PutParam)
        {
            //描画する部分の範囲を設定。位置(X, Y)、大きさ(Width, Height)
            Rectangle PasteRect = new Rectangle(PutParam.X, PutParam.Y, CutParam.Width, CutParam.Height);

            //画像ファイルのImageオブジェクトを作成
            using (Bitmap img = new Bitmap(BasePictFile))
            {
                //ImageオブジェクトのGraphicsオブジェクトを作成
                using (Graphics g = Graphics.FromImage(m_Canvas))
                {
                    //画像の一部を描画
                    g.DrawImage(img, PasteRect, CutParam, GraphicsUnit.Pixel);

                    //Graphicsオブジェクトのリソースを解放
                    //g.Dispose();
                }
                //img.Dispose();
            }
        }

        // 既にデコード済みのBitmapから切り取る版。呼び出し側が同じ元画像から複数回
        // 切り出したい場合(Cheetos.Logic.IsPortrait等)、ファイルパス版を複数回呼ぶと
        // その都度フルデコードが走ってしまうため、デコード済みのBitmapを使い回せるように
        // 用意した。渡されたBitmapの所有権は呼び出し側のままなので、ここではDisposeしない。
        public void TrimExec(Bitmap SourceImg, Rectangle CutParam, Point PutParam)
        {
            Rectangle PasteRect = new Rectangle(PutParam.X, PutParam.Y, CutParam.Width, CutParam.Height);

            using (Graphics g = Graphics.FromImage(m_Canvas))
            {
                g.DrawImage(SourceImg, PasteRect, CutParam, GraphicsUnit.Pixel);
            }
        }

        // キャンバスをディスクに書き出さずに、PNGエンコード後のバイト数だけを知りたい場合に使う。
        // SaveCanvasと違いキャンバスの破棄はしない(呼び出し側で明示的にDisposeする)。
        public long GetCanvasPngByteLength()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                m_Canvas.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.Length;
            }
        }

        public void CreateSourceImg(String SourceImgFile)
        {
            //加工元ファイルのImageオブジェクトを作成
            m_SourceImg = new Bitmap(SourceImgFile);
        }

        public void ReleaseSourceImg()
        {
            ReleaseImg(ref m_SourceImg);
        }

        public void MergeExec(Rectangle CutParam)
        {
            MergeExec(CutParam, new Point(CutParam.X, CutParam.Y));
        }

        public void MergeExec(Rectangle CutParam, Point PutParam)
        {
            //描画する部分の範囲を設定。位置(X, Y)、大きさ(Width, Height)
            Rectangle PasteRect = new Rectangle(PutParam.X, PutParam.Y, CutParam.Width, CutParam.Height);

            //画像ファイルのImageオブジェクトを作成
            using (Bitmap img = new Bitmap(m_SourceImg))
            {
                //ImageオブジェクトのGraphicsオブジェクトを作成
                using (Graphics g = Graphics.FromImage(m_Canvas))
                {
                    //画像の一部を描画
                    g.DrawImage(img, PasteRect, CutParam, GraphicsUnit.Pixel);

                    //Graphicsオブジェクトのリソースを解放
                    //g.Dispose();
                }
                //img.Dispose();
            }
        }

        public Size GetCanvasSize()
        {
            Size sz = new Size();
            sz.Width = m_Canvas.Width;
            sz.Height = m_Canvas.Height;
            return sz;
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
