using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// FileMng（ファイルの移動・コピー・空フォルダ削除、ProcessMemory を継承）のテスト。
    ///
    /// Move / Copy の成功・失敗パスは、Rename / Function のテストを通じてすでに
    /// 間接的に踏んでいるが、ここでは FileMng 単体として直接確認する。
    ///
    /// ⚠️ 絶対に踏んではいけない分岐がある: Copy() はコピー元がフォルダだった場合、
    /// IsErrorPopup の値に関わらず必ず MessageBox.Show を呼ぶ（呼び出し元が失敗を
    /// 拾って処理を続けられるようにする仕組みが無い）。自動テストでこれを踏むと、
    /// 誰もクリックできないダイアログでテスト実行がハングする。そのため
    /// 「コピー元がフォルダのケース」は絶対にテストしない。
    ///
    /// IsErrorPopup=true を指定した状態で例外を起こすケースも同様に MessageBox が出るため、
    /// すべてのテストで IsErrorPopup は既定値(false)のまま使う。
    /// </summary>
    [TestClass]
    public class FileMngTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditFileMngTests_" + Guid.NewGuid().ToString("N"));
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

        private string PathFor(string name)
        {
            return Path.Combine(tempDirectory, name);
        }

        // ------------------------------------------------------------------
        // Move
        // ------------------------------------------------------------------

        [TestMethod]
        public void Move_ファイルを移動できる()
        {
            string src = PathFor("src.txt");
            string dest = PathFor("dest.txt");
            File.WriteAllText(src, "dummy");

            var fm = new FileMng();
            bool result = fm.Move(src, dest);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(dest));
            Assert.IsFalse(File.Exists(src));
        }

        [TestMethod]
        public void Move_フォルダを移動できる()
        {
            string src = PathFor("src_dir");
            string dest = PathFor("dest_dir");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "dummy");

            var fm = new FileMng();
            bool result = fm.Move(src, dest);

            Assert.IsTrue(result);
            Assert.IsTrue(Directory.Exists(dest));
            Assert.IsTrue(File.Exists(Path.Combine(dest, "a.txt")));
            Assert.IsFalse(Directory.Exists(src));
        }

        [TestMethod]
        public void Move_存在しないパスならfalseを返す()
        {
            var fm = new FileMng();

            bool result = fm.Move(PathFor("no_such_file.txt"), PathFor("dest.txt"));

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Move_移動先が既に存在すると例外を吸収してfalseを返す()
        {
            // IsErrorPopup は既定値(false)のまま。ポップアップを出さずに失敗させる。
            string src = PathFor("src.txt");
            string dest = PathFor("dest.txt");
            File.WriteAllText(src, "dummy");
            File.WriteAllText(dest, "既にある");

            var fm = new FileMng();
            bool result = fm.Move(src, dest);

            Assert.IsFalse(result, "File.Move は移動先に同名ファイルがあると例外を投げ、falseになるはず");
            Assert.IsTrue(File.Exists(src), "失敗したので元のファイルは残る");
        }

        // ------------------------------------------------------------------
        // Copy（コピー元がフォルダのケースは意図的にテストしない。上記コメント参照）
        // ------------------------------------------------------------------

        [TestMethod]
        public void Copy_ファイルをコピーできる()
        {
            string src = PathFor("src.txt");
            string dest = PathFor("dest.txt");
            File.WriteAllText(src, "dummy");

            var fm = new FileMng();
            bool result = fm.Copy(src, dest);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(dest));
            Assert.IsTrue(File.Exists(src), "コピーなので元のファイルは残る");
        }

        [TestMethod]
        public void Copy_存在しないパスならfalseを返す()
        {
            var fm = new FileMng();

            bool result = fm.Copy(PathFor("no_such_file.txt"), PathFor("dest.txt"));

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Copy_コピー先が既に存在すると例外を吸収してfalseを返す()
        {
            string src = PathFor("src.txt");
            string dest = PathFor("dest.txt");
            File.WriteAllText(src, "dummy");
            File.WriteAllText(dest, "既にある");

            var fm = new FileMng();
            bool result = fm.Copy(src, dest);

            Assert.IsFalse(result);
            Assert.IsTrue(File.Exists(src), "コピーが失敗しても元のファイルは残る");
        }

        // ------------------------------------------------------------------
        // DeleteBlankDir（FileMng単体からの直接呼び出し）
        // ------------------------------------------------------------------

        [TestMethod]
        public void DeleteBlankDir_中にある空フォルダを削除する()
        {
            string root = PathFor("root");
            string nestedEmpty = Path.Combine(root, "nested_empty");
            Directory.CreateDirectory(nestedEmpty);

            var fm = new FileMng();
            fm.DeleteBlankDir(root);

            // StcUtils.ExecuteProcess は完了を待たずに返る（バックグラウンド実行）ため、
            // バッチ処理が終わるまでポーリングで待つ
            Assert.IsTrue(WaitUntil(() => !Directory.Exists(nestedEmpty), TimeSpan.FromSeconds(5)),
                "バックグラウンドのバッチ処理が完了するまで待っても削除されなかった");
            Assert.IsTrue(Directory.Exists(root), "root自体は消えずに残る");
        }

        [TestMethod]
        public void DeleteBlankDir_指定フォルダ自身が空でも消えない()
        {
            // FFEdit\Tests\FunctionTests.cs で確認済みの仕様と同じ（DeleteBlankDir は
            // 指定パスの中にあるサブフォルダを対象にする。指定パス自身は対象外）。
            string emptyDir = PathFor("empty_sub");
            Directory.CreateDirectory(emptyDir);

            var fm = new FileMng();
            fm.DeleteBlankDir(emptyDir);

            System.Threading.Thread.Sleep(1000);
            Assert.IsTrue(Directory.Exists(emptyDir));
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
