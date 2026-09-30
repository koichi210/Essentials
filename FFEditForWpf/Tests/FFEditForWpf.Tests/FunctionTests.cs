using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// Function（コピー・移動・空フォルダ削除）のテスト。
    ///
    /// DelEmptyDir は内部で一時バッチファイルを作って cmd.exe をバックグラウンド起動する
    /// (StcUtils.ExecuteProcess は完了を待たない) ため、削除が完了するタイミングが不定。
    /// このテストではポーリングして許容時間内に消えることだけを確認する。
    /// </summary>
    [TestClass]
    public class FunctionTests
    {
        private string tempDirectory;
        private string targetDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditFunctionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            targetDirectory = Path.Combine(tempDirectory, "dest");
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
        public void Move_指定フォルダへファイルを移動できる()
        {
            CreateFile("a.txt");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _target_dir = targetDirectory,
                _file_list = new List<string> { "a.txt" },
                _function_type = Function.FunctionType.Move,
            };

            string errors = fs.Execute();

            Assert.AreEqual(string.Empty, errors);
            Assert.IsTrue(File.Exists(Path.Combine(targetDirectory, "a.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDirectory, "a.txt")));
        }

        [TestMethod]
        public void Move_移動先フォルダが無ければ自動で作られる()
        {
            CreateFile("a.txt");
            Assert.IsFalse(Directory.Exists(targetDirectory), "テスト開始時点では無いはず");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _target_dir = targetDirectory,
                _file_list = new List<string> { "a.txt" },
                _function_type = Function.FunctionType.Move,
            };
            fs.Execute();

            Assert.IsTrue(Directory.Exists(targetDirectory));
        }

        [TestMethod]
        public void Copy_元のファイルを残したままコピーできる()
        {
            CreateFile("a.txt");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _target_dir = targetDirectory,
                _file_list = new List<string> { "a.txt" },
                _function_type = Function.FunctionType.Copy,
            };

            string errors = fs.Execute();

            Assert.AreEqual(string.Empty, errors);
            Assert.IsTrue(File.Exists(Path.Combine(targetDirectory, "a.txt")), "コピー先にできる");
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "a.txt")), "元のファイルは残る");
        }

        [TestMethod]
        public void Moveの後にRestoreすると元の場所へ戻る()
        {
            CreateFile("a.txt");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _target_dir = targetDirectory,
                _file_list = new List<string> { "a.txt" },
                _function_type = Function.FunctionType.Move,
            };
            fs.Execute();

            Assert.IsTrue(fs.Restore());

            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, "a.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(targetDirectory, "a.txt")));
        }

        [TestMethod]
        public void Copyの後にRestoreを呼んでも複製されたファイルは残る()
        {
            // Function.Execute の Copy ケースは「コピーのときは処理を覚えない」とコメントされており、
            // 復元リストに何も登録していない。ただし Execute() は種類に関わらず末尾で
            // IncrementRegistNumber を呼ぶため、Restore() の DecrementRegistNumber 自体は成功し、
            // Restore() の戻り値は true になる（実際に戻す対象が無くても、という点は現状の仕様）。
            CreateFile("a.txt");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _target_dir = targetDirectory,
                _file_list = new List<string> { "a.txt" },
                _function_type = Function.FunctionType.Copy,
            };
            fs.Execute();

            Assert.IsTrue(fs.Restore(), "Increment/Decrementの帳尻自体は合うので true が返る");
            Assert.IsTrue(File.Exists(Path.Combine(targetDirectory, "a.txt")), "実際には何も戻されず複製はそのまま残る");
        }

        [TestMethod]
        public void DelEmptyDir_指定フォルダ自身が空でも消えない()
        {
            // DeleteBlankDir が組み立てるバッチは "dir 指定パス /ad /b /s" で
            // "指定パスの中にあるサブフォルダ" を列挙して rd するもので、
            // 指定パス自体は列挙対象に含まれない。そのため指定フォルダ自身が空でも、
            // そのフォルダ自体は削除されない（サブフォルダを持たない限り何も起きない）。
            string emptyDir = Path.Combine(tempDirectory, "empty_sub");
            Directory.CreateDirectory(emptyDir);

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "empty_sub" },
                _function_type = Function.FunctionType.DelEmptyDir,
            };
            fs.Execute();

            // 削除されない想定なので、バックグラウンド処理が走る時間を置いてから確認する
            System.Threading.Thread.Sleep(1000);
            Assert.IsTrue(Directory.Exists(emptyDir), "指定フォルダ自体は消えない");
        }

        [TestMethod]
        public void DelEmptyDir_指定フォルダの中にある空フォルダは削除される()
        {
            string root = Path.Combine(tempDirectory, "root");
            string nestedEmpty = Path.Combine(root, "nested_empty");
            Directory.CreateDirectory(nestedEmpty);

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "root" },
                _function_type = Function.FunctionType.DelEmptyDir,
            };
            fs.Execute();

            // StcUtils.ExecuteProcess は完了を待たずに返る（バックグラウンド実行）ため、
            // バッチ処理が終わるまでポーリングで待つ
            Assert.IsTrue(WaitUntil(() => !Directory.Exists(nestedEmpty), TimeSpan.FromSeconds(5)),
                "バックグラウンドのバッチ処理が完了するまで待っても削除されなかった");
            Assert.IsTrue(Directory.Exists(root), "root自体は消えずに残る");
        }

        [TestMethod]
        public void DelEmptyDir_中身があるフォルダは削除されない()
        {
            string root = Path.Combine(tempDirectory, "root2");
            string dirWithFile = Path.Combine(root, "has_file");
            Directory.CreateDirectory(dirWithFile);
            File.WriteAllText(Path.Combine(dirWithFile, "keep.txt"), "dummy");

            var fs = new Function
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "root2" },
                _function_type = Function.FunctionType.DelEmptyDir,
            };
            fs.Execute();

            // 削除されない想定なので、少し待ってから「消えていないこと」を確認する
            System.Threading.Thread.Sleep(1000);
            Assert.IsTrue(Directory.Exists(dirWithFile), "中身があるフォルダは残るはず");
        }

        private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                System.Threading.Thread.Sleep(100);
            }
            return condition();
        }
    }
}
