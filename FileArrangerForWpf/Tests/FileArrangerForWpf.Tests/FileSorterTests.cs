using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileArrangerForWpf.Tests
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
            // DecrementRegistNumber 自体は成功する（CommitBatchでCurrentIdxを1つ
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
