using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// Rename（ファイル名変更ロジック）のテスト。
    ///
    /// GetChangedName 等の名前生成ロジックは private のため、Execute() を通して
    /// 実際にファイルをリネームさせ、ディスク上の結果で確認する。実ファイルを動かす分、
    /// 純粋関数のテストより重いが、実際の呼び出し経路（FileMng.Move の成否判定を含む）を
    /// まるごと検証できる。
    /// </summary>
    [TestClass]
    public class RenameTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditRenameTests_" + Guid.NewGuid().ToString("N"));
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

        private bool Exists(string name)
        {
            return File.Exists(Path.Combine(tempDirectory, name));
        }

        [TestMethod]
        public void Number_連番と元の名前を付けてリネームできる()
        {
            CreateFile("a.txt");
            CreateFile("b.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "a.txt", "b.txt" },
                _change_type = Rename.ChangeType.Number,
                _first_number = 1,
                _pad_number = 3,
                _keep_org_name = true,
            };

            string errors = rename.Execute();

            Assert.AreEqual(string.Empty, errors, "エラーは出ないはず");
            Assert.IsTrue(Exists("001a.txt"), "1件目は連番001+元の名前になる");
            Assert.IsTrue(Exists("002b.txt"), "2件目は連番002+元の名前になる");
            Assert.IsFalse(Exists("a.txt"));
            Assert.IsFalse(Exists("b.txt"));
        }

        [TestMethod]
        public void Number_元の名前を残さない場合は連番と拡張子だけになる()
        {
            CreateFile("photo.jpg");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "photo.jpg" },
                _change_type = Rename.ChangeType.Number,
                _first_number = 10,
                _pad_number = 2,
                _keep_org_name = false,
            };

            rename.Execute();

            Assert.IsTrue(Exists("10.jpg"));
        }

        [TestMethod]
        public void DelNum_先頭と末尾から指定文字数を削れる()
        {
            CreateFile("abcdefgh.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "abcdefgh.txt" },
                _change_type = Rename.ChangeType.DelNum,
                _param1 = "2", // 先頭2文字(ab)を削る
                _param2 = "4", // (拡張子を除いた)末尾4文字(efgh)を削る
            };

            rename.Execute();

            // "abcdefgh.txt" → 先頭2文字削除 → "cdefgh.txt" → 拡張子抜きの末尾4文字削除 → "cd" + ".txt"
            Assert.IsTrue(Exists("cd.txt"));
        }

        [TestMethod]
        public void Add_先頭と末尾に文字列を追加できる()
        {
            CreateFile("name.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "name.txt" },
                _change_type = Rename.ChangeType.Add,
                _param1 = "PRE_",
                _param2 = "_SUF",
            };

            rename.Execute();

            Assert.IsTrue(Exists("PRE_name_SUF.txt"));
        }

        [TestMethod]
        public void Delete_指定文字列を削除できる()
        {
            CreateFile("fooBar_foo.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "fooBar_foo.txt" },
                _change_type = Rename.ChangeType.Delete,
                _param1 = "foo",
            };

            rename.Execute();

            // Replace は最初に見つかった1件だけでなく該当箇所すべてを消す
            Assert.IsTrue(Exists("Bar_.txt"));
        }

        [TestMethod]
        public void Replace_指定文字列を置換できる()
        {
            CreateFile("fooBar.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "fooBar.txt" },
                _change_type = Rename.ChangeType.Replace,
                _param1 = "foo",
                _param2 = "baz",
            };

            rename.Execute();

            Assert.IsTrue(Exists("bazBar.txt"));
        }

        [TestMethod]
        public void OnlyExt_拡張子だけを変更できる()
        {
            CreateFile("photo.jpg");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "photo.jpg" },
                _change_type = Rename.ChangeType.OnlyExt,
                _param1 = "png",
            };

            rename.Execute();

            Assert.IsTrue(Exists("photo.png"));
        }

        [TestMethod]
        public void 変更後の名前が同じなら何もしない()
        {
            CreateFile("same.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "same.txt" },
                _change_type = Rename.ChangeType.Delete,
                _param1 = "見つからない文字列",
            };

            string errors = rename.Execute();

            Assert.AreEqual(string.Empty, errors);
            Assert.IsTrue(Exists("same.txt"), "変化が無いのでファイルはそのまま");
        }

        [TestMethod]
        public void Restoreで直前の実行を元に戻せる()
        {
            CreateFile("original.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "original.txt" },
                _change_type = Rename.ChangeType.Add,
                _param1 = "renamed_",
            };

            rename.Execute();
            Assert.IsTrue(Exists("renamed_original.txt"));
            Assert.IsFalse(Exists("original.txt"));

            Assert.IsTrue(rename.Restore());

            Assert.IsTrue(Exists("original.txt"), "元の名前に戻るはず");
            Assert.IsFalse(Exists("renamed_original.txt"));
        }

        [TestMethod]
        public void 一度もExecuteしていない状態でRestoreすると失敗する()
        {
            var rename = new Rename();

            Assert.IsFalse(rename.Restore());
        }

        [TestMethod]
        public void 存在しないファイルを指定するとエラー一覧に載る()
        {
            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "no_such_file.txt" },
                _change_type = Rename.ChangeType.Add,
                _param1 = "x_",
            };

            string errors = rename.Execute();

            StringAssert.Contains(errors, "no_such_file.txt");
        }

        [TestMethod]
        public void AddDirName_フラットなファイル名だけを指定すると変化しない()
        {
            // GetChangedName の AddDirName ケースは "\" を "_" に置換するが、
            // _file_list の要素が "a.txt" のようにサブフォルダを含まない場合、
            // 置換対象の "\" が無いので名前は変わらない。結果、変更前後で名前が同じになり、
            // Execute() の「同一だったら処理しない」ロジックでスキップされる。
            CreateFile("a.txt");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { "a.txt" },
                _change_type = Rename.ChangeType.AddDirName,
            };

            string errors = rename.Execute();

            Assert.AreEqual(string.Empty, errors);
            Assert.IsTrue(Exists("a.txt"), "名前が変わらないので何も起きない");
        }

        [TestMethod]
        public void AddDirName_サブフォルダ付きの指定だとフォルダ名がファイル名に埋め込まれる()
        {
            // _file_list の要素が "sub\a.txt" のようにサブフォルダを含む場合、
            // "\" が "_" に置換された文字列がそのサブフォルダ内に新しいファイル名として
            // 書き戻される。結果、同じサブフォルダの中で "sub_a.txt" にリネームされる
            // （フォルダ名をファイル名の一部に埋め込む、という機能として成立している）。
            Directory.CreateDirectory(Path.Combine(tempDirectory, "sub"));
            File.WriteAllText(Path.Combine(tempDirectory, "sub", "a.txt"), "dummy");

            var rename = new Rename
            {
                _base_dir = tempDirectory,
                _file_list = new List<string> { @"sub\a.txt" },
                _change_type = Rename.ChangeType.AddDirName,
            };

            string errors = rename.Execute();

            Assert.AreEqual(string.Empty, errors);
            Assert.IsTrue(Exists(@"sub\sub_a.txt"), "サブフォルダ名がファイル名の先頭に付くはず");
            Assert.IsFalse(Exists(@"sub\a.txt"));
        }
    }
}
