using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cheetos.Tests
{
    /// <summary>
    /// PictMergeタブの切り取る高さの指定方法(TrimHeightPattern)と、結合後のファイル名のテスト。
    /// WinForms版とWPF版で同じファイル。
    /// </summary>
    [TestClass]
    public class TrimHeightPatternTests
    {
        // ------------------------------------------------------------------
        // BuildFromStarts(開始位置だけを並べて、高さは共通)
        // ------------------------------------------------------------------

        [TestMethod]
        public void BuildFromStartsは開始位置ごとに共通の高さの範囲を作る()
        {
            string[] lines = TrimHeightPattern.BuildFromStarts("530\r\n820,1110", 45);

            CollectionAssert.AreEqual(new[] { "530,575", "820,865", "1110,1155" }, lines);
        }

        [TestMethod]
        public void BuildFromStartsは空の項目と空白を無視する()
        {
            string[] lines = TrimHeightPattern.BuildFromStarts(" 10 \r\n\r\n,20,", 5);

            CollectionAssert.AreEqual(new[] { "10,15", "20,25" }, lines);
        }

        [TestMethod]
        public void BuildFromStartsは数値でない項目があれば例外にしてその値を知らせる()
        {
            try
            {
                TrimHeightPattern.BuildFromStarts("10\r\nabc", 5);
                Assert.Fail("FormatExceptionになるはず");
            }
            catch (FormatException ex)
            {
                StringAssert.Contains(ex.Message, "abc");
            }
        }

        // ------------------------------------------------------------------
        // BuildFromPitch(開始位置・ずらす幅・高さ・回数)
        // ------------------------------------------------------------------

        [TestMethod]
        public void BuildFromPitchは開始から間隔ずつずらした帯を回数分作る()
        {
            string[] lines = TrimHeightPattern.BuildFromPitch(530, 290, 45, 3, 0);

            CollectionAssert.AreEqual(new[] { "530,575", "820,865", "1110,1155" }, lines);
        }

        [TestMethod]
        public void BuildFromPitchは回数が0なら画像の下端に収まるところまで作る()
        {
            // 0-10, 30-40, 60-70, 90-100 (120-130は高さ100を超えるので作らない)
            string[] lines = TrimHeightPattern.BuildFromPitch(0, 30, 10, 0, 100);

            CollectionAssert.AreEqual(new[] { "0,10", "30,40", "60,70", "90,100" }, lines);
        }

        [TestMethod]
        public void BuildFromPitchは回数が0で画像の高さが分からなければ何も作らない()
        {
            Assert.AreEqual(0, TrimHeightPattern.BuildFromPitch(0, 30, 10, 0, 0).Length);
        }

        [TestMethod]
        public void BuildFromPitchは間隔や高さが0以下なら何も作らない()
        {
            Assert.AreEqual(0, TrimHeightPattern.BuildFromPitch(0, 0, 10, 3, 100).Length);
            Assert.AreEqual(0, TrimHeightPattern.BuildFromPitch(0, 10, 0, 3, 100).Length);
        }

        // ------------------------------------------------------------------
        // PictMerge.GetMergedFileName / 結合後のファイル名
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetMergedFileNameは識別子1を除いた名前を返す()
        {
            Assert.AreEqual("001.png", PictMerge.GetMergedFileName("001_1.png", "_1"));
            Assert.AreEqual("a_1b.png", PictMerge.GetMergedFileName("a_1b_1.png", "_1"), "拡張子の直前の識別子だけを除く");
        }

        [TestMethod]
        public void GetMergedFileNameは除くと名前が無くなるか識別子が空なら元の名前のまま()
        {
            Assert.AreEqual("left.jpg", PictMerge.GetMergedFileName("left.jpg", "left"));
            Assert.AreEqual("001_1.png", PictMerge.GetMergedFileName("001_1.png", ""));
        }

        [TestMethod]
        public void MergeExecuteは識別子1を除いた名前で保存し元の2ファイルをバックアップに残す()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosTrimHeightPatternTests_" + Guid.NewGuid().ToString("N"));
            string backupDirectory = Path.Combine(tempDirectory, "backup");
            Directory.CreateDirectory(backupDirectory);
            try
            {
                CreateImage(Path.Combine(tempDirectory, "001_1.png"), Color.Red);
                CreateImage(Path.Combine(tempDirectory, "001_2.png"), Color.Blue);

                var pm = new PictMerge
                {
                    SourceFolderPath = tempDirectory,
                    BackUpDirPath = backupDirectory,
                    SourceFile1Prefix = "_1",
                    SourceFile2Prefix = "_2",
                };
                SetTrimHeights(pm, new[] { "0,4" });
                pm.SetTargetFileName("001_1.png");
                Assert.IsTrue(pm.IsProcTarget());
                Assert.IsTrue(pm.BackUpSourceFile());
                Assert.IsTrue(pm.ResolveMergeFilePath());

                Assert.IsTrue(pm.MergeExecute());

                Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "001.png")), "結合結果は_1を除いた名前");
                Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "001_1.png")), "結合先の元ファイルはバックアップへ移る");
                Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "001_2.png")));
                Assert.IsTrue(File.Exists(Path.Combine(backupDirectory, "001_1.png")));
                Assert.IsTrue(File.Exists(Path.Combine(backupDirectory, "001_2.png")));
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                }
                catch (IOException)
                {
                    // 後片付けの失敗はテストの成否に関係ない
                }
            }
        }

        private static void CreateImage(string path, Color color)
        {
            using (var bmp = new Bitmap(8, 8))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                bmp.Save(path);
            }
        }

        // WinForms版はTrimHeightAry、WPF版はTrimHeightRangesという名前なので、リフレクションで入れる
        private static void SetTrimHeights(PictMerge pm, string[] lines)
        {
            var property = typeof(PictMerge).GetProperty("TrimHeightRanges") ?? typeof(PictMerge).GetProperty("TrimHeightAry");
            property.SetValue(pm, lines, null);
        }
    }
}
