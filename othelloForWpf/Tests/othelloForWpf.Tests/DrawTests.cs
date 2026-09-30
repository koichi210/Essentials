using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace othelloForWpf.Tests
{
    /// <summary>
    /// Draw（オセロ盤面をBitmapに描画するクラス、MainWindowとは独立した
    /// 通常クラス）のテスト。描画結果のピクセルを確認することで検証する。
    /// WinForms版はPictureBoxに描いていたが、WPF版では描画先のサイズだけを受け取り
    /// Draw.Canvas(Bitmap)に描く形になったため、pb.Image → draw.Canvas に読み替えている。
    /// </summary>
    [TestClass]
    public class DrawTests
    {
        private Draw CreateDraw()
        {
            // CellMax=8なので、80x80(1マス10px)にしておくと座標計算がしやすい
            var draw = new Draw();
            draw.SetDrawArea(80, 80);
            return draw;
        }

        [TestMethod]
        public void SetDrawAreaで指定サイズのCanvasが作成される()
        {
            var draw = CreateDraw();

            Assert.IsNotNull(draw.Canvas);
            Assert.AreEqual(80, draw.Canvas.Width);
            Assert.AreEqual(80, draw.Canvas.Height);
        }

        [TestMethod]
        public void SetDrawAreaに0以下のサイズを渡すと既存のCanvasを残して何もしない()
        {
            var draw = CreateDraw();
            Bitmap before = draw.Canvas;

            draw.SetDrawArea(0, 80);

            Assert.AreSame(before, draw.Canvas);
        }

        [TestMethod]
        public void DeleteCanvasでCanvasがnullになる()
        {
            var draw = CreateDraw();
            Assert.IsNotNull(draw.Canvas);

            draw.DeleteCanvas();

            Assert.IsNull(draw.Canvas);
        }

        [TestMethod]
        public void FillBackgroundは指定した色で全体を塗りつぶす()
        {
            var draw = CreateDraw();

            draw.FillBackground(Brushes.Red);

            using (var bmp = new Bitmap(draw.Canvas))
            {
                Assert.AreEqual(Color.Red.ToArgb(), bmp.GetPixel(5, 5).ToArgb());
                Assert.AreEqual(Color.Red.ToArgb(), bmp.GetPixel(75, 75).ToArgb());
            }
        }

        [TestMethod]
        public void WriteLineは指定した色で線を引く()
        {
            var draw = CreateDraw();
            draw.FillBackground(Brushes.White);

            draw.WriteLine(new Point(0, 40), new Point(80, 40), Color.Blue, 2);

            using (var bmp = new Bitmap(draw.Canvas))
            {
                Assert.AreEqual(Color.Blue.ToArgb(), bmp.GetPixel(40, 40).ToArgb());
            }
        }

        [TestMethod]
        public void InitFieldは緑の盤面に黒い格子線を描画する()
        {
            var draw = CreateDraw();

            draw.InitField();

            using (var bmp = new Bitmap(draw.Canvas))
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
            var draw = CreateDraw();
            var gm = new GameMaster();
            gm.Initialize();

            draw.DrawField(gm.Table);

            using (var bmp = new Bitmap(draw.Canvas))
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
            Assert.IsTrue(Draw.TryGetCell(0, 0, 80, 80, out int x, out int y));
            Assert.AreEqual(0, x);
            Assert.AreEqual(0, y);

            Assert.IsTrue(Draw.TryGetCell(79, 45, 80, 80, out x, out y));
            Assert.AreEqual(7, x);
            Assert.AreEqual(4, y);

            // 割り切れないサイズ(100px→1マス12.5px): 罫線はx=25(i=2)なので24は1マス目、25は2マス目
            Assert.IsTrue(Draw.TryGetCell(24, 0, 100, 100, out x, out y));
            Assert.AreEqual(1, x);
            Assert.IsTrue(Draw.TryGetCell(25, 0, 100, 100, out x, out y));
            Assert.AreEqual(2, x);
        }

        [TestMethod]
        public void TryGetCellは盤面の外や無効サイズならfalseを返す()
        {
            Assert.IsFalse(Draw.TryGetCell(80, 0, 80, 80, out _, out _));
            Assert.IsFalse(Draw.TryGetCell(0, 80, 80, 80, out _, out _));
            Assert.IsFalse(Draw.TryGetCell(-1, 0, 80, 80, out _, out _));
            Assert.IsFalse(Draw.TryGetCell(0, 0, 0, 80, out _, out _));
        }

        [TestMethod]
        public void BoardImageはCanvasを同じピクセルのBitmapSourceに変換する()
        {
            var draw = CreateDraw();
            draw.InitField();

            var source = BoardImage.ToBitmapSource(draw.Canvas, 144, 144);

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
