using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileArranger.Tests
{
    /// <summary>
    /// FileSorter（フォルダ内ファイルを連番リネームする機能）のテスト。
    /// Form1.cs の SortFile private メソッド＋pmf フィールドをそのまま切り出したもの。
    /// 実際に一時ファイルを操作し、ディスク上の結果で確認する。
    /// </summary>
    [TestClass]
    public class FileSorterTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FileArrangerSorterTests_" + Guid.NewGuid().ToString("N"));
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

        private void CreateFile(string name)
        {
            File.WriteAllText(Path.Combine(tempDirectory, name), "dummy");
        }

        [TestMethod]
        public void SortFolder_ファイルが連番にリネームされる()
        {
            CreateFile("banana.txt");
            CreateFile("apple.txt");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);

            // Directory.GetFiles は既定でアルファベット順に近い順序で返る（apple → banana）
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "000.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "001.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "apple.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "banana.txt")));
        }

        [TestMethod]
        public void SortFolder_空フォルダを渡しても例外にならない()
        {
            // ファイルが1件も無い場合、Directory.GetFiles は空配列を返し、
            // リネーム対象が無いので何も起きない。
            var sorter = new FileSorter();

            sorter.SortFolder(tempDirectory);

            Assert.AreEqual(0, Directory.GetFiles(tempDirectory).Length);
        }

        [TestMethod]
        public void SortFolder_空フォルダのあとCommitしてもRestoreはtrueを返す()
        {
            // 記録するものが何も無いので、実際に元へ戻す対象は無い。それでも
            // DecrementSerialNumber 自体は成功する（CommitBatchでCurrentIdxを1つ
            // 進めているので）ため、Restore() の戻り値は true になる。
            // 「戻せた」ではなく「巻き戻しの帳尻は合った」という意味の true であることに注意。
            // FFEdit の Function.Copy 後の Restore と同じ仕様（呼び出し元は判断できない）。
            var sorter = new FileSorter();

            sorter.SortFolder(tempDirectory);
            sorter.CommitBatch();

            Assert.IsTrue(sorter.Restore());
        }

        [TestMethod]
        public void SortFolder_拡張子は維持される()
        {
            CreateFile("a.jpg");
            CreateFile("b.png");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);

            string[] files = Directory.GetFiles(tempDirectory);
            Assert.IsTrue(Array.Exists(files, f => f.EndsWith(".jpg")));
            Assert.IsTrue(Array.Exists(files, f => f.EndsWith(".png")));
        }

        [TestMethod]
        public void SortFolder_名前順に番号を振る()
        {
            // GetFilesの返す順はFAT/exFATでは名前順にならないため、名前順(大文字小文字は区別しない)に並べてから振る
            File.WriteAllText(Path.Combine(tempDirectory, "c.txt"), "c");
            File.WriteAllText(Path.Combine(tempDirectory, "B.txt"), "B");
            File.WriteAllText(Path.Combine(tempDirectory, "a.txt"), "a");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);

            Assert.AreEqual("a", File.ReadAllText(Path.Combine(tempDirectory, "000.txt")));
            Assert.AreEqual("B", File.ReadAllText(Path.Combine(tempDirectory, "001.txt")));
            Assert.AreEqual("c", File.ReadAllText(Path.Combine(tempDirectory, "002.txt")));
        }

        [TestMethod]
        public void RestoreでSortFolderの結果を元に戻せる()
        {
            CreateFile("banana.txt");
            CreateFile("apple.txt");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);
            sorter.CommitBatch();

            Assert.IsTrue(sorter.Restore());

            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "apple.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "banana.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "000.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "001.txt")));
        }

        [TestMethod]
        public void 複数フォルダをまとめてCommitすると1回のRestoreで両方戻る()
        {
            string dirA = Path.Combine(tempDirectory, "a");
            string dirB = Path.Combine(tempDirectory, "b");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            File.WriteAllText(Path.Combine(dirA, "x.txt"), "dummy");
            File.WriteAllText(Path.Combine(dirB, "y.txt"), "dummy");

            var sorter = new FileSorter();
            sorter.SortFolder(dirA);
            sorter.SortFolder(dirB);
            sorter.CommitBatch(); // 2フォルダ分をまとめて1回の実行として確定

            Assert.IsTrue(sorter.Restore(), "1回のRestoreで両方戻るはず");

            Assert.IsTrue(File.Exists(Path.Combine(dirA, "x.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(dirB, "y.txt")));
        }

        [TestMethod]
        public void SortFoldersが途中で失敗しても_そこまでの分はRestoreで戻せる()
        {
            string dirA = Path.Combine(tempDirectory, "a");
            string dirB = Path.Combine(tempDirectory, "b");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            File.WriteAllText(Path.Combine(dirA, "x.txt"), "dummy");
            // 2つ目のフォルダのファイルを開いたままにして、リネームを失敗させる
            // (以前は既存の"000.txt"とぶつけて失敗させていたが、2段階リネームにしたのでぶつからなくなった)
            File.WriteAllText(Path.Combine(dirB, "y.txt"), "locked");

            var sorter = new FileSorter();
            using (new FileStream(Path.Combine(dirB, "y.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsException<IOException>(() => sorter.SortFolders(new[] { dirA, dirB }));
            }
            // 前提: 1つ目のフォルダはリネーム済み。2つ目は元のまま
            Assert.IsTrue(File.Exists(Path.Combine(dirA, "000.txt")));
            Assert.AreEqual("locked", File.ReadAllText(Path.Combine(dirB, "y.txt")));

            Assert.IsTrue(sorter.Restore());

            Assert.IsTrue(File.Exists(Path.Combine(dirA, "x.txt")), "失敗する前にリネームした分は戻る");
            Assert.IsFalse(File.Exists(Path.Combine(dirA, "000.txt")));
        }

        [TestMethod]
        public void SortFolder_リネーム先と同じ名前のファイルが先にあってもぶつからない()
        {
            // "(1).txt"は"000.txt"より先に並ぶので、直接リネームすると既存の"000.txt"とぶつかって例外になっていた
            File.WriteAllText(Path.Combine(tempDirectory, "(1).txt"), "first");
            File.WriteAllText(Path.Combine(tempDirectory, "000.txt"), "second");
            File.WriteAllText(Path.Combine(tempDirectory, "001.txt"), "third");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);
            sorter.CommitBatch();

            Assert.AreEqual(3, Directory.GetFiles(tempDirectory).Length, "一時名のファイルが残らない");
            Assert.AreEqual("first", File.ReadAllText(Path.Combine(tempDirectory, "000.txt")));
            Assert.AreEqual("second", File.ReadAllText(Path.Combine(tempDirectory, "001.txt")));
            Assert.AreEqual("third", File.ReadAllText(Path.Combine(tempDirectory, "002.txt")));

            // 元に戻すと、元の名前と中身の組み合わせに戻る
            Assert.IsTrue(sorter.Restore());

            Assert.AreEqual(3, Directory.GetFiles(tempDirectory).Length);
            Assert.AreEqual("first", File.ReadAllText(Path.Combine(tempDirectory, "(1).txt")));
            Assert.AreEqual("second", File.ReadAllText(Path.Combine(tempDirectory, "000.txt")));
            Assert.AreEqual("third", File.ReadAllText(Path.Combine(tempDirectory, "001.txt")));
        }

        [TestMethod]
        public void CommitBatchしていない状態でRestoreすると失敗する()
        {
            var sorter = new FileSorter();

            Assert.IsFalse(sorter.Restore());
        }

        [TestMethod]
        public void Restoreは1回分ずつしか戻せない()
        {
            CreateFile("1回目.txt");

            var sorter = new FileSorter();
            sorter.SortFolder(tempDirectory);
            sorter.CommitBatch();

            Assert.IsTrue(sorter.Restore());
            Assert.IsFalse(sorter.Restore(), "戻せるのは1回分だけ");
        }
    }
}
