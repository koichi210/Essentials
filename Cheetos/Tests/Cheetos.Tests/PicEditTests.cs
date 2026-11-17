using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Picture;

namespace Cheetos.Tests
{
    /// <summary>
    /// PicEdit（画像の切り貼りをする薄い GDI+ ラッパー）のテスト。
    ///
    /// Cheetos と PictMerge / PictTrim プロジェクトで実質同じクラスがコピーされている
    /// （名前空間 Picture・クラス名 PicEdit にそろえてある）。まずは Cheetos 版から
    /// 実際に画像を生成・保存して検証する形で固める。
    ///
    /// internal クラスなので、Cheetos の AssemblyInfo.cs に足した
    /// InternalsVisibleTo("Cheetos.Tests") でテストから見えている。
    /// </summary>
    [TestClass]
    public class PicEditTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosPicEditTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }

        /// <summary>2x2の田の字模様（左上赤・右上青・左下緑・右下黄）の画像ファイルを作る。</summary>
        private string CreateQuadrantImage(string fileName, int cellSize)
        {
            string path = Path.Combine(tempDirectory, fileName);
            using (var bmp = new Bitmap(cellSize * 2, cellSize * 2))
            using (var g = Graphics.FromImage(bmp))
            {
                g.FillRectangle(Brushes.Red, 0, 0, cellSize, cellSize);
                g.FillRectangle(Brushes.Blue, cellSize, 0, cellSize, cellSize);
                g.FillRectangle(Brushes.Lime, 0, cellSize, cellSize, cellSize);
                g.FillRectangle(Brushes.Yellow, cellSize, cellSize, cellSize, cellSize);
                bmp.Save(path);
            }
            return path;
        }

        private static void AssertPixel(Bitmap bmp, int x, int y, Color expected, string message)
        {
            Color actual = bmp.GetPixel(x, y);
            Assert.AreEqual(expected.ToArgb(), actual.ToArgb(),
                string.Format("{0} (座標 {1},{2})", message, x, y));
        }

        [TestMethod]
        public void 幅と高さを指定して作った白紙キャンバスのサイズが一致する()
        {
            using (var edit = new PicEdit(10, 20))
            {
                Size size = edit.GetCanvasSize();

                Assert.AreEqual(10, size.Width);
                Assert.AreEqual(20, size.Height);
            }
        }

        [TestMethod]
        public void 既存ファイルから作るとキャンバスのサイズがファイルと一致する()
        {
            string path = CreateQuadrantImage("base.bmp", 3);

            // PicEditは読み込んだ画像を保持してファイルを掴むため、後片付けで消せるようusingで閉じる
            using (var edit = new PicEdit(path))
            {
                Size size = edit.GetCanvasSize();
                Assert.AreEqual(6, size.Width);
                Assert.AreEqual(6, size.Height);
            }
        }

        [TestMethod]
        public void TrimExecで指定した範囲が指定した位置にコピーされる()
        {
            const int cell = 4;
            string basePath = CreateQuadrantImage("trim_base.bmp", cell);

            Bitmap canvasCopy;
            using (var edit = new PicEdit(cell * 2, cell * 2))
            {
                // 左上の赤い区画(cell x cell)を、キャンバスの右下へコピーする
                edit.TrimExec(basePath, new Rectangle(0, 0, cell, cell), new Point(cell, cell));

                canvasCopy = TakeSnapshot(edit);
            }

            using (canvasCopy)
            {
                AssertPixel(canvasCopy, cell + 1, cell + 1, Color.Red,"コピー先には元の左上(赤)が来るはず");

                // 未加工の白紙キャンバスは真っ黒とは限らない（Bitmap の初期値はアルファ0の透明で、
                // 保存形式によって見え方が変わる）ため、ここでは「コピー先の色が来ていないこと」だけを見る
                Color untouched = canvasCopy.GetPixel(1, 1);
                Assert.AreNotEqual(Color.Red.ToArgb(), untouched.ToArgb(), "コピーしていない左上に赤が来てはいけない");
            }
        }

        [TestMethod]
        public void TrimExecの2引数版は同じ位置に上書きする()
        {
            const int cell = 4;
            string basePath = CreateQuadrantImage("trim_same.bmp", cell);

            Bitmap canvasCopy;
            using (var edit = new PicEdit(cell * 2, cell * 2))
            {
                edit.TrimExec(basePath, new Rectangle(cell, 0, cell, cell)); // 右上(青)をそのままの位置へ
                canvasCopy = TakeSnapshot(edit);
            }

            using (canvasCopy)
            {
                AssertPixel(canvasCopy, cell + 1, 1, Color.Blue,"PutParam省略時はCutParamと同じ位置に描かれる");
            }
        }

        [TestMethod]
        public void MergeExecはCreateSourceImgで読み込んだ画像から合成する()
        {
            const int cell = 4;
            string sourcePath = CreateQuadrantImage("merge_source.bmp", cell);

            Bitmap canvasCopy;
            using (var edit = new PicEdit(cell * 2, cell * 2))
            {
                edit.CreateSourceImg(sourcePath);

                // 右下の黄色い区画を、キャンバスの左上へ合成する
                edit.MergeExec(new Rectangle(cell, cell, cell, cell), new Point(0, 0));
                edit.ReleaseSourceImg();

                canvasCopy = TakeSnapshot(edit);
            }

            using (canvasCopy)
            {
                AssertPixel(canvasCopy, 1, 1, Color.Yellow, "合成元(右下・黄)がキャンバス左上に来るはず");
            }
        }

        [TestMethod]
        public void SaveCanvasで保存した画像はサイズも中身も一致する()
        {
            const int cell = 3;
            string basePath = CreateQuadrantImage("save_base.bmp", cell);
            string savePath = Path.Combine(tempDirectory, "saved.bmp");

            using (var edit = new PicEdit(basePath))
            {
                edit.SaveCanvas(savePath);
            }

            using (var saved = new Bitmap(savePath))
            {
                Assert.AreEqual(cell * 2, saved.Width);
                Assert.AreEqual(cell * 2, saved.Height);
                AssertPixel(saved, 0, 0, Color.Red, "保存後も元の内容が残っているはず");
                AssertPixel(saved, cell + 1, cell + 1, Color.Yellow, "右下(黄)も残っているはず");
            }
        }

        [TestMethod]
        public void SaveCanvasのあとも続けて編集して保存できる()
        {
            // 以前は SaveCanvas が保存直後に自分で Dispose していた(ファイナライザ任せでは
            // 解放のタイミングが決まらないための暫定対策)。今は解放を Dispose(using)に任せ、
            // SaveCanvas は保存だけするので、保存後もキャンバスを使える
            const int cell = 4;
            string basePath = CreateQuadrantImage("save_twice_base.bmp", cell);
            string firstPath = Path.Combine(tempDirectory, "save_twice_1.bmp");
            string secondPath = Path.Combine(tempDirectory, "save_twice_2.bmp");

            using (var edit = new PicEdit(cell * 2, cell * 2))
            {
                edit.TrimExec(basePath, new Rectangle(0, 0, cell, cell)); // 左上(赤)
                edit.SaveCanvas(firstPath);

                Assert.AreEqual(cell * 2, edit.GetCanvasSize().Width, "保存後もキャンバスは使える");

                edit.TrimExec(basePath, new Rectangle(cell, cell, cell, cell)); // 右下(黄)
                edit.SaveCanvas(secondPath);
            }

            using (var first = new Bitmap(firstPath))
            using (var second = new Bitmap(secondPath))
            {
                AssertPixel(first, 1, 1, Color.Red, "1回目の保存には左上(赤)が入っている");
                AssertPixel(second, 1, 1, Color.Red, "2回目の保存にも1回目までの編集が残っている");
                AssertPixel(second, cell + 1, cell + 1, Color.Yellow, "2回目の保存には右下(黄)も入っている");
            }
        }

        [TestMethod]
        public void Disposeのあとに使うとObjectDisposedExceptionになる()
        {
            var edit = new PicEdit(4, 4);
            edit.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => edit.GetCanvasSize());
            Assert.ThrowsException<ObjectDisposedException>(() => edit.SaveCanvas(Path.Combine(tempDirectory, "disposed.bmp")));
        }

        /// <summary>
        /// PicEdit はキャンバスを外に出す手段（GetCanvasSize 以外）を持たないため、
        /// SaveCanvas でファイルに書き出してから読み直す形でテストからピクセルを見る。
        /// 渡した PicEdit は解放しない(呼び出し側の using で解放する)。
        /// </summary>
        private Bitmap TakeSnapshot(PicEdit edit)
        {
            string path = Path.Combine(tempDirectory, "snapshot_" + Guid.NewGuid().ToString("N") + ".bmp");
            edit.SaveCanvas(path);

            // new Bitmap(path)はファイルを掴んだままになり、後片付けでフォルダを消せずに残ってしまうため、
            // ファイルから読んだものはここで閉じて、ファイルと切り離したコピーを返す
            using (Bitmap fromFile = new Bitmap(path))
            {
                return new Bitmap(fromFile);
            }
        }

        // ---- IDisposable ----

        [TestMethod]
        public void IDisposableを実装していてusingで使える()
        {
            using (var edit = new PicEdit(4, 4))
            {
                Assert.IsInstanceOfType(edit, typeof(IDisposable));
            }
        }

        [TestMethod]
        public void Disposeを2回呼んでも例外にならない()
        {
            string sourcePath = CreateQuadrantImage("dispose_twice.png", 4);
            var edit = new PicEdit(8, 8);
            edit.CreateSourceImg(sourcePath);

            edit.Dispose();
            edit.Dispose();
        }

        [TestMethod]
        public void SaveCanvasの後にusingを抜けると元画像ファイルのロックが外れる()
        {
            // 呼び出し側(PictTrim/PictMerge)と同じ「usingの中でSaveCanvasしてから抜ける」使い方
            string basePath = CreateQuadrantImage("save_then_dispose_base.png", 4);
            using (var edit = new PicEdit(basePath))
            {
                edit.SaveCanvas(Path.Combine(tempDirectory, "save_then_dispose.bmp"));
            }

            File.Delete(basePath);
            Assert.IsFalse(File.Exists(basePath));
        }

        [TestMethod]
        public void usingを抜けると画像ファイルのロックが外れる()
        {
            string basePath = CreateQuadrantImage("lock.png", 4);
            using (var edit = new PicEdit(basePath))
            {
                edit.CreateSourceImg(basePath);
            }

            // Bitmap が解放されずにロックが残っていれば IOException になる
            File.Delete(basePath);
            Assert.IsFalse(File.Exists(basePath));
        }
    }
}
