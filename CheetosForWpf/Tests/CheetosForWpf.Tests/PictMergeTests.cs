using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CheetosForWpf.Tests
{
    /// <summary>
    /// PictMerge（PictMerge.cs のフォーム部分とは別に定義されている、画像合成ロジック。
    /// public class、Form非依存）のテスト。
    /// </summary>
    [TestClass]
    public class PictMergeTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosForWpfPictMergeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(Path.Combine(tempDirectory, "backup"));
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
        // SetTargetFileName / IsProcTarget
        // ------------------------------------------------------------------

        [TestMethod]
        public void SetTargetFileNameは空文字なら失敗する()
        {
            var pm = new PictMerge();

            Assert.IsFalse(pm.SetTargetFileName(""));
        }

        [TestMethod]
        public void IsProcTargetは接頭辞1が付いていれば対象と判定する()
        {
            var pm = new PictMerge
            {
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
            };
            pm.SetTargetFileName("left.jpg");

            Assert.IsTrue(pm.IsProcTarget());
        }

        [TestMethod]
        public void IsProcTargetは接頭辞1が無ければ対象外と判定する()
        {
            var pm = new PictMerge
            {
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
            };
            pm.SetTargetFileName("other.jpg");

            Assert.IsFalse(pm.IsProcTarget());
        }

        // ------------------------------------------------------------------
        // GetHeight
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetHeightはハイフンなら既定値を返す()
        {
            var pm = new PictMerge();

            Assert.AreEqual(100, pm.GetHeight("-", 100));
        }

        [TestMethod]
        public void GetHeightは数値ならその値を返す()
        {
            var pm = new PictMerge();

            Assert.AreEqual(50, pm.GetHeight("50", 100));
        }

        // ------------------------------------------------------------------
        // BackUpSourceFile / ResolveMergeFilePath
        // ------------------------------------------------------------------

        [TestMethod]
        public void BackUpSourceFileは元ファイルが無ければ失敗しエラーを記録する()
        {
            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
            };
            pm.SetTargetFileName("no_such_file.jpg");

            bool result = pm.BackUpSourceFile();

            Assert.IsFalse(result);
            StringAssert.Contains(pm.GetErrorMessage(), "no_such_file.jpg");
        }

        [TestMethod]
        public void BackUpSourceFileは元ファイルがあればバックアップにコピーする()
        {
            CreateImage("left.jpg", 10, 10, Color.Red);

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
            };
            pm.SetTargetFileName("left.jpg");

            bool result = pm.BackUpSourceFile();

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "backup", "left.jpg")));
        }

        [TestMethod]
        public void ResolveMergeFilePathは対となるファイルが無ければ失敗する()
        {
            CreateImage("left.jpg", 10, 10, Color.Red);

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
            };
            pm.SetTargetFileName("left.jpg");
            pm.IsProcTarget(); // Prefix1/Prefix2 を内部に確定させる
            pm.BackUpSourceFile();

            bool result = pm.ResolveMergeFilePath();

            Assert.IsFalse(result, "right.jpg が存在しないので失敗するはず");
        }

        // ------------------------------------------------------------------
        // MergeExecute（縦方向に2枚を結合する、もっとも基本的なケースのみ）
        // ------------------------------------------------------------------

        [TestMethod]
        public void MergeExecuteは2つの画像を縦に結合する()
        {
            // left.jpg（結合先の土台）と right.jpg（継ぎ足す元）を用意する。
            CreateImage("left.jpg", 10, 10, Color.Red);
            CreateImage("right.jpg", 10, 10, Color.Blue);

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
                // "-" 開始・"-" 終了 で画像全体を対象にする、正しい形式の1要素だけを渡す
                TrimHeightRanges = new[] { "-,-" },
            };
            pm.SetTargetFileName("left.jpg");
            Assert.IsTrue(pm.IsProcTarget());
            Assert.IsTrue(pm.BackUpSourceFile());
            Assert.IsTrue(pm.ResolveMergeFilePath());

            bool result = pm.MergeExecute();

            Assert.IsTrue(result);
            // マージ元(right.jpg)はバックアップへ移動される
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "right.jpg")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "backup", "right.jpg")));
            // 結合結果は left.jpg（SourceFileFullName）へ上書き保存される
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "left.jpg")));
        }

        [TestMethod]
        public void MergeExecuteは空文字の行を無視する()
        {
            CreateImage("left.jpg", 8, 8, Color.Green);
            CreateImage("right.jpg", 8, 8, Color.Yellow);

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
                // 空文字の行を混ぜても、フォーマットチェックには引っかからず単に読み飛ばされる
                TrimHeightRanges = new[] { "", "-,-", "" },
            };
            pm.SetTargetFileName("left.jpg");
            pm.IsProcTarget();
            pm.BackUpSourceFile();
            pm.ResolveMergeFilePath();

            bool result = pm.MergeExecute();

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void MergeExecuteは不正な行をダイアログを出さずに飛ばす()
        {
            CreateImage("left.jpg", 8, 8, Color.Green);
            CreateImage("right.jpg", 8, 8, Color.Yellow);

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
                TrimHeightRanges = new[] { "abc", "-,-", "1,2,3" },
            };
            pm.SetTargetFileName("left.jpg");
            pm.IsProcTarget();
            pm.BackUpSourceFile();
            pm.ResolveMergeFilePath();

            bool result = pm.MergeExecute();

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void FindInvalidTrimHeightsは開始終了の形でない行だけを返す()
        {
            String[] invalid = PictMerge.FindInvalidTrimHeights(new[] { "", "-,-", "0,100", "abc", "1,2,3", "a,5" });

            CollectionAssert.AreEqual(new[] { "abc", "1,2,3", "a,5" }, invalid);
        }
    }
}
