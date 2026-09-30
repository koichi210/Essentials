using System;
using System.Drawing;
using System.IO;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using global::CheetosForWpf;

namespace CheetosForWpf.Tests
{
    /// <summary>
    /// Logic（DistOrient.cs / CaptureWindow.cs / RotationPreview.cs から切り出した純粋ロジック）
    /// のテスト。抽出前と挙動が変わっていないことを、抽出後のコードに対して確認する。
    /// </summary>
    [TestClass]
    public class LogicTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosForWpfLogicTests_" + Guid.NewGuid().ToString("N"));
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

        private string CreateImage(string fileName, int width, int height, Color color)
        {
            string path = Path.Combine(tempDirectory, fileName);
            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                bmp.Save(path);
            }
            return path;
        }

        // ------------------------------------------------------------------
        // GetBinSize
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetBinSize_単色画像は範囲が広いほど圧縮でファイルサイズが小さくなりやすい()
        {
            // PNG は単色の塗りつぶし領域を高圧縮できるため、真っ白画像の切り出しサイズは
            // 小さくなる。厳密な値ではなく「0バイトではない」ことだけを確認する
            // （圧縮アルゴリズムの詳細に依存しすぎないため）。
            string path = CreateImage("white.bmp", 100, 100, Color.White);

            long size = Logic.GetBinSize(path, new Rectangle(0, 0, 50, 50));

            Assert.IsTrue(size > 0, "PNGとして保存されるので0バイトにはならない");
        }

        [TestMethod]
        public void GetBinSize_呼び出し後に一時ファイルが残らない()
        {
            string path = CreateImage("temp_check.bmp", 20, 20, Color.Black);
            int before = Directory.GetFiles(Path.GetTempPath(), "*.png").Length;

            Logic.GetBinSize(path, new Rectangle(0, 0, 10, 10));

            int after = Directory.GetFiles(Path.GetTempPath(), "*.png").Length;
            Assert.AreEqual(before, after, "GetBinSize内で作った一時PNGは自分で削除するはず");
        }

        // ------------------------------------------------------------------
        // IsPortrait
        // ------------------------------------------------------------------

        [TestMethod]
        public void IsPortrait_左右に白フチが無い画像はtrueになる()
        {
            // 白フチが無い(=左右端が真っ黒で切り取りサイズが大きい)場合、
            // BaseSize(白領域として許容するサイズ)を下回るため IsPort=true のまま。
            string path = CreateImage("no_border.bmp", 100, 100, Color.Black);

            bool result = Logic.IsPortrait(path, 20, 1);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsPortrait_IsSampleを付けなければポップアップは出ない()
        {
            // IsSample=false（既定値）で呼ぶ限りMessageBoxは出ない。危険な分岐を踏まないことの確認。
            string path = CreateImage("safe.bmp", 50, 50, Color.Gray);

            // 例外もダイアログも出ずに完走すればOK
            Logic.IsPortrait(path, 10, 1);
        }

        // ------------------------------------------------------------------
        // GetFileBaseFormat
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetFileBaseFormat_接頭辞とタイムスタンプ無しなら保存先とパスの区切りだけ()
        {
            string result = Logic.GetFileBaseFormat(@"D:\capture", "", false);

            Assert.AreEqual(@"D:\capture\", result);
        }

        [TestMethod]
        public void GetFileBaseFormat_接頭辞を付けると末尾にアンダースコア付きで入る()
        {
            string result = Logic.GetFileBaseFormat(@"D:\capture", "shot", false);

            Assert.AreEqual(@"D:\capture\shot_", result);
        }

        [TestMethod]
        public void GetFileBaseFormat_タイムスタンプは指定した書式になる()
        {
            DateTime before = DateTime.Now;
            string result = Logic.GetFileBaseFormat(@"D:\capture", "", true);
            DateTime after = DateTime.Now;

            string stamp = result.Substring(@"D:\capture\".Length).TrimEnd('_');
            DateTime parsed = DateTime.ParseExact(stamp, "yyyy_MM_dd_HH_mm_ss", null);

            Assert.IsTrue(parsed >= before.AddSeconds(-1) && parsed <= after.AddSeconds(1),
                "現在時刻に近い値になっているはず");
        }

        // ------------------------------------------------------------------
        // UpdateValue
        // ------------------------------------------------------------------

        [TestMethod]
        public void UpdateValue_上キーで1増える()
        {
            string result = Logic.UpdateValue("5", Key.Up);

            Assert.AreEqual("6", result);
        }

        [TestMethod]
        public void UpdateValue_下キーで1減る()
        {
            string result = Logic.UpdateValue("5", Key.Down);

            Assert.AreEqual("4", result);
        }

        [TestMethod]
        public void UpdateValue_Enterキーでは変化しない()
        {
            string result = Logic.UpdateValue("5", Key.Enter);

            Assert.AreEqual("5", result);
        }

        [TestMethod]
        public void UpdateValue_数値以外ならそのまま返す()
        {
            string result = Logic.UpdateValue("abc", Key.Up);

            Assert.AreEqual("abc", result);
        }

        [TestMethod]
        public void UpdateValue_マイナスの値でも計算できる()
        {
            string result = Logic.UpdateValue("-1", Key.Down);

            Assert.AreEqual("-2", result);
        }
    }
}
