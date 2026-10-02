using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using StandardTemplate;
using StandardTemplate.Wpf.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileArranger.Tests
{
    /// <summary>
    /// FileArranger.Utils（StcUtils を継承した独自ユーティリティ、internal）のテスト。
    /// WinForms版FileArrangerのUtilsTestsを移植したもの(FindSelectedRowIndexはWPFのListViewで確認する)。
    ///
    /// Form1.cs（1,284行）はロジックがイベントハンドラに埋め込まれていて、テストするには
    /// private メソッドの切り出し（本体コードの書き換え）が要る。今回はそこまで踏み込まず、
    /// もともと独立したクラスとして分離されている Utils.cs だけを対象にする。
    /// Form1.cs は一切変更していない。
    /// </summary>
    [TestClass]
    public class UtilsTests
    {
        private string tempDirectory;
        private Utils util;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FileArrangerUtilsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            util = new Utils();
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

        // ------------------------------------------------------------------
        // AvoidFolderNameConflict
        // ------------------------------------------------------------------

        [TestMethod]
        public void AvoidFolderNameConflict_フォルダが存在しなければ変更しない()
        {
            string path = Path.Combine(tempDirectory, "not_exist");

            util.AvoidFolderNameConflict(ref path, 1);

            Assert.AreEqual(Path.Combine(tempDirectory, "not_exist"), path);
        }

        [TestMethod]
        public void AvoidFolderNameConflict_フォルダが存在すれば連番付きの名前にする()
        {
            string original = Path.Combine(tempDirectory, "exists");
            Directory.CreateDirectory(original);
            string path = original;

            util.AvoidFolderNameConflict(ref path, 3);

            Assert.AreNotEqual(original, path);
            StringAssert.StartsWith(path, original + "_Cnt3_");
        }

        [TestMethod]
        public void AvoidFolderNameConflict_LoopIdxが0でも連番0として埋め込む()
        {
            string original = Path.Combine(tempDirectory, "exists_zero");
            Directory.CreateDirectory(original);
            string path = original;

            util.AvoidFolderNameConflict(ref path, 0);

            StringAssert.StartsWith(path, original + "_Cnt0_");
        }

        // ------------------------------------------------------------------
        // AvoidFileNameConflict
        // ------------------------------------------------------------------

        [TestMethod]
        public void AvoidFileNameConflict_何も無ければtrueを返し名前も変えない()
        {
            string path = Path.Combine(tempDirectory, "new.txt");

            bool result = util.AvoidFileNameConflict(ref path, 1);

            Assert.IsTrue(result);
            Assert.AreEqual(Path.Combine(tempDirectory, "new.txt"), path);
        }

        [TestMethod]
        public void AvoidFileNameConflict_ファイルが存在すればfalseを返し連番を付ける()
        {
            string original = Path.Combine(tempDirectory, "dup.txt");
            File.WriteAllText(original, "dummy");
            string path = original;

            bool result = util.AvoidFileNameConflict(ref path, 2);

            Assert.IsFalse(result);
            Assert.AreNotEqual(original, path);
            StringAssert.StartsWith(path, original + "_Cnt2_");
        }

        [TestMethod]
        public void AvoidFileNameConflict_LoopIdxが0でも連番0として埋め込む()
        {
            string original = Path.Combine(tempDirectory, "dup_zero.txt");
            File.WriteAllText(original, "dummy");
            string path = original;

            util.AvoidFileNameConflict(ref path, 0);

            StringAssert.StartsWith(path, original + "_Cnt0_");
        }

        [TestMethod]
        public void AvoidFileNameConflict_同名のフォルダがあってもfalseを返す()
        {
            // ファイルではなくフォルダとの重複も検知する
            string original = Path.Combine(tempDirectory, "dup_dir");
            Directory.CreateDirectory(original);
            string path = original;

            bool result = util.AvoidFileNameConflict(ref path, 1);

            Assert.IsFalse(result);
        }

        // ------------------------------------------------------------------
        // MoveFileタブの移動処理（MainWindow.MoveFile.cs bgWorkerMove_DoWorkと同じ手順）
        //
        // バグ: 元のコードはAvoidFolderNameConflict（Directory.Existsしか見ない）を使っていたため、
        // 移動先に「同名のファイル」があるケースを重複として検知できず、
        // StcFileInputOutput.MoveDirectory内のDirectory.Moveが失敗して移動自体が失敗していた。
        // File.Exists/Directory.Exists両方を見るAvoidFileNameConflictに差し替えて、
        // MoveDirタブと同じ"_CntN_日時"を付けるリネームで衝突を回避するように修正した。
        // ------------------------------------------------------------------

        [TestMethod]
        public void MoveFile_移動先に同名ファイルがあればリネームして両方残す()
        {
            string sourceDir = Path.Combine(tempDirectory, "src");
            string targetDir = Path.Combine(tempDirectory, "dst");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(targetDir);

            string fileName = "dup.txt";
            string sourcePath = Path.Combine(sourceDir, fileName);
            string originalTargetFilePath = Path.Combine(targetDir, fileName);
            File.WriteAllText(sourcePath, "source content");
            File.WriteAllText(originalTargetFilePath, "dest content");

            string targetPath = originalTargetFilePath;

            // bgWorkerMove_DoWork と同じ手順(重複回避→移動)
            util.AvoidFileNameConflict(ref targetPath, 0);
            var fio = new StcFileInputOutput();
            fio.MoveDirectory(sourcePath, targetPath);

            // 移動元からは無くなっている
            Assert.IsFalse(File.Exists(sourcePath));

            // 移動先には「元からあったファイル」と「リネームされた移動後のファイル」の両方が残る
            string[] filesInTarget = Directory.GetFiles(targetDir);
            Assert.AreEqual(2, filesInTarget.Length, "同名ファイルが失われず両方残っているはず");

            Assert.IsTrue(File.Exists(originalTargetFilePath));
            Assert.AreEqual("dest content", File.ReadAllText(originalTargetFilePath));

            string movedPath = filesInTarget.First(f => f != originalTargetFilePath);
            Assert.AreNotEqual(originalTargetFilePath, movedPath);
            StringAssert.Contains(movedPath, "_Cnt0_");
            Assert.AreEqual("source content", File.ReadAllText(movedPath));
        }

        // ------------------------------------------------------------------
        // CreateNewFolderName
        // ------------------------------------------------------------------

        [TestMethod]
        public void CreateNewFolderName_区切り文字未指定なら元の名前のまま()
        {
            string result = util.CreateNewFolderName("abc_def_ghi");

            Assert.AreEqual("abc_def_ghi", result);
        }

        [TestMethod]
        public void CreateNewFolderName_最初に見つかった区切りより前を切り出す()
        {
            string result = util.CreateNewFolderName("abc_def_ghi", "_");

            Assert.AreEqual("abc", result);
        }

        [TestMethod]
        public void CreateNewFolderName_Reverse指定で最後に見つかった区切りより前を切り出す()
        {
            string result = util.CreateNewFolderName("abc_def_ghi", "_", true);

            Assert.AreEqual("abc_def", result);
        }

        [TestMethod]
        public void CreateNewFolderName_区切り文字が見つからなければ元の名前のまま()
        {
            string result = util.CreateNewFolderName("abcdefghi", "_");

            Assert.AreEqual("abcdefghi", result);
        }

        [TestMethod]
        public void CreateNewFolderName_区切り文字が先頭にあれば空文字になる()
        {
            string result = util.CreateNewFolderName("_abc", "_");

            Assert.AreEqual("", result);
        }

        // ------------------------------------------------------------------
        // FindSelectedRowIndex
        // ------------------------------------------------------------------

        // WPFのListView(項目はListViewRow)を作る。WPFのコントロールはSTAスレッドでしか作れないので、
        // テスト本体はStaRunner.Runの中で実行する
        private static ListView NewListViewWithItems(params string[] texts)
        {
            var lv = new ListView { SelectionMode = SelectionMode.Extended };
            foreach (string t in texts)
            {
                lv.Items.Add(new ListViewRow(t));
            }
            return lv;
        }

        private static void Select(ListView lv, int index)
        {
            lv.SelectedItems.Add(lv.Items[index]);
        }

        [TestMethod]
        public void FindSelectedRowIndex_選択項目の中から部分一致するものを探す()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("apple_1.txt", "banana_2.txt", "cherry_3.txt");
                Select(lv, 0);
                Select(lv, 2);

                int idx = new Utils().FindSelectedRowIndex(lv, 0, "cherry");

                Assert.AreEqual(2, idx);
            });
        }

        [TestMethod]
        public void FindSelectedRowIndex_選択されていない項目はヒットしない()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("apple_1.txt", "banana_2.txt");
                Select(lv, 0);
                // banana は選択していない

                int idx = new Utils().FindSelectedRowIndex(lv, 0, "banana");

                Assert.AreEqual(-1, idx);
            });
        }

        [TestMethod]
        public void FindSelectedRowIndex_見つからなければマイナス1()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("apple_1.txt");
                Select(lv, 0);

                int idx = new Utils().FindSelectedRowIndex(lv, 0, "not_found");

                Assert.AreEqual(-1, idx);
            });
        }

        [TestMethod]
        public void FindSelectedRowIndex_TrimNameで区切ってから比較する()
        {
            // 検索対象の文字列側を区切ってから、リストの項目に部分一致するか見る
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("report", "summary");
                Select(lv, 0);
                Select(lv, 1);

                int idx = new Utils().FindSelectedRowIndex(lv, 0, "report_v2.txt", "_");

                Assert.AreEqual(0, idx);
            });
        }

        [TestMethod]
        public void FindSelectedRowIndex_選択した順ではなく並び順で先に一致したものを返す()
        {
            // WinForms版のSelectedItemsはインデックス順だった。WPFのSelectedItemsは選択した順なので、並び順に直していることを確認する
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("photo_a", "photo_b");
                Select(lv, 1);
                Select(lv, 0);

                int idx = new Utils().FindSelectedRowIndex(lv, 0, "photo");

                Assert.AreEqual(0, idx);
            });
        }
    }
}
