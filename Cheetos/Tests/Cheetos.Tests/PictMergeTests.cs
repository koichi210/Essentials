using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cheetos.Tests
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
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosPictMergeTests_" + Guid.NewGuid().ToString("N"));
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
        public void MergeExecuteは前回のマージ元バックアップがあっても上書きして移動する()
        {
            // 以前はFile.Moveの移動先が既にあると例外になり、結合元だけ上書きされた状態で止まっていた
            CreateImage("left.jpg", 8, 8, Color.Red);
            CreateImage("right.jpg", 8, 8, Color.Blue);
            string oldBackUp = Path.Combine(tempDirectory, "backup", "right.jpg");
            File.WriteAllText(oldBackUp, "old");

            var pm = new PictMerge
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = Path.Combine(tempDirectory, "backup"),
                SourceFile1Prefix = "left",
                SourceFile2Prefix = "right",
                TrimHeightRanges = new[] { "-,-" },
            };
            pm.SetTargetFileName("left.jpg");
            Assert.IsTrue(pm.IsProcTarget());
            Assert.IsTrue(pm.BackUpSourceFile());
            Assert.IsTrue(pm.ResolveMergeFilePath());

            Assert.IsTrue(pm.MergeExecute());

            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "right.jpg")));
            Assert.AreNotEqual("old", File.ReadAllText(oldBackUp), "バックアップは今回のマージ元に置き換わる");
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

        // 回帰テスト: 終了<開始の行は高さがマイナスの矩形のまま描いていた
        [TestMethod]
        public void FindInvalidTrimHeightsは終了が開始より小さい行も返す()
        {
            String[] invalid = PictMerge.FindInvalidTrimHeights(new[] { "100,50", "-,-5", "50,50", "100,-", "-,10" });

            CollectionAssert.AreEqual(new[] { "100,50", "-,-5" }, invalid);
        }

        // 回帰テスト: 開始が負の数の行と、終了が画像の高さより大きい行がそのまま処理されていた
        [TestMethod]
        public void FindInvalidTrimHeightsは開始が負の数の行と終了が画像の高さより大きい行も返す()
        {
            String[] invalid = PictMerge.FindInvalidTrimHeights(new[] { "-5,10", "-5,-", "-,100", "0,101", "50,-", "-,-" }, 100);

            CollectionAssert.AreEqual(new[] { "-5,10", "-5,-", "0,101" }, invalid);
        }

        [TestMethod]
        public void GetImageHeightsは結合対象の画像名と高さを返す()
        {
            CreateImage("a_left.png", 10, 30, Color.Red);
            CreateImage("b_left.png", 10, 20, Color.Red);
            CreateImage("other.png", 10, 5, Color.Red);
            File.WriteAllText(Path.Combine(tempDirectory, "c_left.png"), "not an image");

            List<KeyValuePair<String, int>> heights = PictMerge.GetImageHeights(tempDirectory, "left",
                new[] { "a_left.png", "b_left.png", "other.png", "c_left.png", "none_left.png", "" });

            CollectionAssert.AreEqual(
                new[] { new KeyValuePair<String, int>("a_left.png", 30), new KeyValuePair<String, int>("b_left.png", 20) },
                heights,
                "接頭辞1が無いもの・画像でないもの・無いファイルは入れない");
            Assert.AreEqual(0, PictMerge.GetImageHeights(tempDirectory, "left", new[] { "other.png" }).Count);
        }

        // 回帰テスト: いちばん低い画像の高さで判定していたので、高い画像では有効な行まで不正として挙がっていた
        [TestMethod]
        public void ClassifyInvalidTrimHeightsは画像ごとに判定して全部の画像で不正な行と一部の画像だけで不正な行を分ける()
        {
            var heights = new List<KeyValuePair<String, int>>
            {
                new KeyValuePair<String, int>("a_left.png", 100),
                new KeyValuePair<String, int>("b_left.png", 200),
            };
            List<String> invalidForAll;
            List<KeyValuePair<String, String[]>> invalidForSome;

            PictMerge.ClassifyInvalidTrimHeights(new[] { "", "0,100", "0,150", "0,250", "abc", "100,50", "-5,10", "-,-" }, heights,
                out invalidForAll, out invalidForSome);

            CollectionAssert.AreEqual(new[] { "0,250", "abc", "100,50", "-5,10" }, invalidForAll);
            Assert.AreEqual(1, invalidForSome.Count);
            Assert.AreEqual("0,150", invalidForSome[0].Key);
            CollectionAssert.AreEqual(new[] { "a_left.png" }, invalidForSome[0].Value);
        }

        [TestMethod]
        public void ClassifyInvalidTrimHeightsは高さの分かる画像が無ければ書式の不正だけを全部の画像で不正とする()
        {
            List<String> invalidForAll;
            List<KeyValuePair<String, String[]>> invalidForSome;

            PictMerge.ClassifyInvalidTrimHeights(new[] { "0,99999", "abc" }, new List<KeyValuePair<String, int>>(),
                out invalidForAll, out invalidForSome);

            CollectionAssert.AreEqual(new[] { "abc" }, invalidForAll);
            Assert.AreEqual(0, invalidForSome.Count);
        }

        [TestMethod]
        public void BuildInvalidTrimHeightsMessageはどの画像で不正かを書く()
        {
            var heights = new List<KeyValuePair<String, int>>
            {
                new KeyValuePair<String, int>("a_left.png", 100),
                new KeyValuePair<String, int>("b_left.png", 200),
            };

            Assert.AreEqual(String.Empty, PictMerge.BuildInvalidTrimHeightsMessage(new[] { "0,100", "-,-" }, heights));

            String message = PictMerge.BuildInvalidTrimHeightsMessage(new[] { "0,150", "abc" }, heights);
            StringAssert.Contains(message, "全部の画像で不正: [abc]");
            StringAssert.Contains(message, "[0,150] a_left.png" + Environment.NewLine);
            Assert.IsFalse(message.Contains("b_left.png"), "高い画像では有効な行なので挙げない");
        }
    }
}
