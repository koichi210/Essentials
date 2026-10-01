using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace othello.Tests
{
    /// <summary>
    /// BoardRenderer（オセロ盤面をBitmapに描画するクラス、MainWindowとは独立した
    /// 通常クラス）のテスト。描画結果のピクセルを確認することで検証する。
    /// WinForms版はPictureBoxに描いていたが、WPF版では描画先のサイズだけを受け取り
    /// BoardRenderer.Canvas(Bitmap)に描く形になったため、pb.Image → renderer.Canvas に読み替えている。
    /// </summary>
    [TestClass]
    public class BoardRendererTests
    {
        private BoardRenderer CreateRenderer()
        {
            // CellMax=8なので、80x80(1マス10px)にしておくと座標計算がしやすい
            var renderer = new BoardRenderer();
            renderer.SetDrawArea(80, 80);
            return renderer;
        }

        [TestMethod]
        public void SetDrawAreaで指定サイズのCanvasが作成される()
        {
            var renderer = CreateRenderer();

            Assert.IsNotNull(renderer.Canvas);
            Assert.AreEqual(80, renderer.Canvas.Width);
            Assert.AreEqual(80, renderer.Canvas.Height);
        }

        [TestMethod]
        public void SetDrawAreaに0以下のサイズを渡すと既存のCanvasを残して何もしない()
        {
            var renderer = CreateRenderer();
            Bitmap before = renderer.Canvas;

            renderer.SetDrawArea(0, 80);

            Assert.AreSame(before, renderer.Canvas);
        }

        [TestMethod]
        public void DeleteCanvasでCanvasがnullになる()
        {
            var renderer = CreateRenderer();
            Assert.IsNotNull(renderer.Canvas);

            renderer.DeleteCanvas();

            Assert.IsNull(renderer.Canvas);
        }

        [TestMethod]
        public void FillBackgroundは指定した色で全体を塗りつぶす()
        {
            var renderer = CreateRenderer();

            renderer.FillBackground(Brushes.Red);

            using (var bmp = new Bitmap(renderer.Canvas))
            {
                Assert.AreEqual(Color.Red.ToArgb(), bmp.GetPixel(5, 5).ToArgb());
                Assert.AreEqual(Color.Red.ToArgb(), bmp.GetPixel(75, 75).ToArgb());
            }
        }

        [TestMethod]
        public void DrawLineは指定した色で線を引く()
        {
            var renderer = CreateRenderer();
            renderer.FillBackground(Brushes.White);

            renderer.DrawLine(new Point(0, 40), new Point(80, 40), Color.Blue, 2);

            using (var bmp = new Bitmap(renderer.Canvas))
            {
                Assert.AreEqual(Color.Blue.ToArgb(), bmp.GetPixel(40, 40).ToArgb());
            }
        }

        [TestMethod]
        public void InitFieldは緑の盤面に黒い格子線を描画する()
        {
            var renderer = CreateRenderer();

            renderer.InitField();

            using (var bmp = new Bitmap(renderer.Canvas))
            {
                // マスの内部(格子線から離れた場所)は緑
                Assert.AreEqual(Color.Green.ToArgb(), bmp.GetPixel(5, 5).ToArgb());

                // 中央の格子線の交点付近は黒
                Assert.AreEqual(Color.Black.ToArgb(), bmp.GetPixel(40, 40).ToArgb());
            }
        }

        [TestMethod]
        public void DrawFieldは初期配置の4石を描画する()
        {
            var renderer = CreateRenderer();
            var gm = new GameMaster();
            gm.Initialize();

            renderer.DrawField(gm.Table);

            using (var bmp = new Bitmap(renderer.Canvas))
            {
                // Table[y,x]: (3,3)=白, (4,3)=黒 → マス中心は(35,35),(45,35)
                Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(35, 35).ToArgb());
                Assert.AreEqual(Color.Black.ToArgb(), bmp.GetPixel(45, 35).ToArgb());
                // 石の無いマスは緑のまま
                Assert.AreEqual(Color.Green.ToArgb(), bmp.GetPixel(5, 5).ToArgb());
            }
        }

        [TestMethod]
        public void TryGetCellは罫線と同じ計算式でピクセル座標をマス目に変換する()
        {
            Assert.IsTrue(BoardRenderer.TryGetCell(0, 0, 80, 80, out int x, out int y));
            Assert.AreEqual(0, x);
            Assert.AreEqual(0, y);

            Assert.IsTrue(BoardRenderer.TryGetCell(79, 45, 80, 80, out x, out y));
            Assert.AreEqual(7, x);
            Assert.AreEqual(4, y);

            // 割り切れないサイズ(100px→1マス12.5px): 罫線はx=25(i=2)なので24は1マス目、25は2マス目
            Assert.IsTrue(BoardRenderer.TryGetCell(24, 0, 100, 100, out x, out y));
            Assert.AreEqual(1, x);
            Assert.IsTrue(BoardRenderer.TryGetCell(25, 0, 100, 100, out x, out y));
            Assert.AreEqual(2, x);
        }

        [TestMethod]
        public void TryGetCellは盤面の外や無効サイズならfalseを返す()
        {
            Assert.IsFalse(BoardRenderer.TryGetCell(80, 0, 80, 80, out _, out _));
            Assert.IsFalse(BoardRenderer.TryGetCell(0, 80, 80, 80, out _, out _));
            Assert.IsFalse(BoardRenderer.TryGetCell(-1, 0, 80, 80, out _, out _));
            Assert.IsFalse(BoardRenderer.TryGetCell(0, 0, 0, 80, out _, out _));
        }

        [TestMethod]
        public void BoardImageはCanvasを同じピクセルのBitmapSourceに変換する()
        {
            var renderer = CreateRenderer();
            renderer.InitField();

            var source = BoardImage.ToBitmapSource(renderer.Canvas, 144, 144);

            Assert.AreEqual(80, source.PixelWidth);
            Assert.AreEqual(80, source.PixelHeight);
            Assert.AreEqual(144.0, source.DpiX, 0.001);
            Assert.IsTrue(source.IsFrozen);

            var pixels = new byte[80 * 80 * 4];
            source.CopyPixels(pixels, 80 * 4, 0);
            // (5,5)は緑(0,128,0) BGRA順
            int i = (5 * 80 + 5) * 4;
            Assert.AreEqual(0, pixels[i]);
            Assert.AreEqual(128, pixels[i + 1]);
            Assert.AreEqual(0, pixels[i + 2]);
            Assert.AreEqual(255, pixels[i + 3]);
        }
    }
}
