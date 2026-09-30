using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FileArrangerForWpf.Tests
{
    /// <summary>
    /// MainWindowの設定保存/読み込み(WpfSaveRestore + SaveProfile/LoadProfile)のテスト。
    /// WinForms版FileArrangerのSaveRestoreTests(XML/JSON)をJSON(WPF版)で書き直したもの。
    /// WinForms版で保存したJSONプロファイルをそのまま読めることも確認する。
    ///
    /// MainWindowはテスト用コンストラクタでデータ保存先を一時フォルダに差し替えて作るので、
    /// ユーザーの実データフォルダには触れない。
    /// </summary>
    [TestClass]
    public class SaveRestoreTests
    {
        private string tempDirectory;
        private string dataFolder;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FileArrangerForWpfSaveRestoreTests_" + Guid.NewGuid().ToString("N"));
            dataFolder = Path.Combine(tempDirectory, "data");
            Directory.CreateDirectory(dataFolder);
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

        private MainWindow NewWindow()
        {
            return new MainWindow(dataFolder);
        }

        private string PathFor(string name)
        {
            return Path.Combine(tempDirectory, name + ".json");
        }

        [TestMethod]
        public void 共通タブの入力値が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cmn_textBox_Reference.Text = @"D:\source";
                writer.cmn_textBox_AddList.Text = "add1\r\nadd2";
                writer.cmn_textBox_AddListSuffix.Text = "_suffix";

                string path = PathFor("common");
                Assert.IsTrue(writer.sr.Save(path));

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.sr.Load(path));

                Assert.AreEqual(@"D:\source", reader.cmn_textBox_Reference.Text);
                Assert.AreEqual("add1\r\nadd2", reader.cmn_textBox_AddList.Text);
                Assert.AreEqual("_suffix", reader.cmn_textBox_AddListSuffix.Text);
            });
        }

        [TestMethod]
        public void フォルダ移動タブの設定が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.md_textBox_SourceDir.Text = @"D:\move_src";
                writer.md_comboBox_TargetDir.Text = @"D:\move_dst";

                string path = PathFor("movedir");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\move_src", reader.md_textBox_SourceDir.Text);
                Assert.AreEqual(@"D:\move_dst", reader.md_comboBox_TargetDir.Text);
            });
        }

        [TestMethod]
        public void リネームタブの設定が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.rd_textBox_ExistItemDir.Text = @"D:\rename";
                writer.rd_comboBox_MergeWord.Text = "merge";
                writer.rd_checkBox_FileOpen.IsChecked = true;
                writer.rd_textBox_SplitWord3.Text = "_";
                writer.rd_textBox_AddTitlePreWord.Text = "pre_";
                writer.rd_textBox_SearchTitleLine.Text = "0";
                writer.rd_textBox_SearchTitleLength.Text = "3";
                writer.rd_comboBox_AddTitlePostWord.Text = "_post";

                string path = PathFor("rename");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\rename", reader.rd_textBox_ExistItemDir.Text);
                Assert.AreEqual("merge", reader.rd_comboBox_MergeWord.Text);
                Assert.AreEqual(true, reader.rd_checkBox_FileOpen.IsChecked);
                Assert.AreEqual("_", reader.rd_textBox_SplitWord3.Text);
                Assert.AreEqual("pre_", reader.rd_textBox_AddTitlePreWord.Text);
                Assert.AreEqual("0", reader.rd_textBox_SearchTitleLine.Text);
                Assert.AreEqual("3", reader.rd_textBox_SearchTitleLength.Text);
                Assert.AreEqual("_post", reader.rd_comboBox_AddTitlePostWord.Text);
            });
        }

        [TestMethod]
        public void 振り分けタブの設定が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.pf_textBox_TargetFile.Text = @"D:\pf_target";
                writer.pf_textBox_ReferenceFile.Text = @"D:\pf_reference";
                writer.pf_textBox_TargetSeparator.Text = "-";
                writer.pf_textBox_SearchTitleLine.Text = "1";
                writer.pf_textBox_SearchTitleLength.Text = "2";
                writer.pf_checkBox_CreateNewDir.IsChecked = true;

                string path = PathFor("partition");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\pf_target", reader.pf_textBox_TargetFile.Text);
                Assert.AreEqual(@"D:\pf_reference", reader.pf_textBox_ReferenceFile.Text);
                Assert.AreEqual("-", reader.pf_textBox_TargetSeparator.Text);
                Assert.AreEqual("1", reader.pf_textBox_SearchTitleLine.Text);
                Assert.AreEqual("2", reader.pf_textBox_SearchTitleLength.Text);
                Assert.AreEqual(true, reader.pf_checkBox_CreateNewDir.IsChecked);
            });
        }

        [TestMethod]
        public void ソートタブとファイル移動タブの設定が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.sf_textBox_TargetFile.Text = @"D:\sf_target";
                writer.mf_textBox_SourceDir.Text = @"D:\mf_src";
                writer.mf_textBox_TargetDir.Text = @"D:\mf_dst";

                string path = PathFor("sort_move");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\sf_target", reader.sf_textBox_TargetFile.Text);
                Assert.AreEqual(@"D:\mf_src", reader.mf_textBox_SourceDir.Text);
                Assert.AreEqual(@"D:\mf_dst", reader.mf_textBox_TargetDir.Text);
            });
        }

        [TestMethod]
        public void コンボボックスの入力履歴が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.md_comboBox_TargetDir.Items.Add(@"D:\a");
                writer.md_comboBox_TargetDir.Items.Add(@"D:\b");
                writer.rd_comboBox_RenameDir.Items.Add("rename1");
                writer.rd_comboBox_AddTitlePostWord.Items.Add("post1");

                string path = PathFor("history");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(2, reader.md_comboBox_TargetDir.Items.Count);
                Assert.AreEqual(@"D:\a", reader.md_comboBox_TargetDir.Items[0]);
                Assert.AreEqual(@"D:\b", reader.md_comboBox_TargetDir.Items[1]);
                CollectionAssert.Contains(reader.rd_comboBox_RenameDir.Items.Cast<object>().ToList(), "rename1");
                CollectionAssert.Contains(reader.rd_comboBox_AddTitlePostWord.Items.Cast<object>().ToList(), "post1");
            });
        }

        [TestMethod]
        public void コンボボックスは入力履歴と入力中の文字の両方が戻る()
        {
            // 履歴(Lists)と入力値(Values)を同じコンボに登録しているので、履歴の入れ替えで入力値が消えないことを確認する
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.md_comboBox_TargetDir.Items.Add(@"D:\a");
                writer.md_comboBox_TargetDir.Items.Add(@"D:\b");
                writer.md_comboBox_TargetDir.Text = @"D:\b";

                string path = PathFor("history_and_text");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(2, reader.md_comboBox_TargetDir.Items.Count);
                Assert.AreEqual(@"D:\b", reader.md_comboBox_TargetDir.Text);
            });
        }

        [TestMethod]
        public void 保存時にコンボボックスの入力値が履歴へ追加される()
        {
            // WinForms版SaveJsonFileのModifyCombBoxList(入力値をプルダウンに追加・重複は除く)と同じ
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.md_comboBox_TargetDir.Items.Add(@"D:\a");
                writer.md_comboBox_TargetDir.Text = @"D:\new";
                writer.rd_comboBox_RenameDir.Text = "rename";
                writer.rd_comboBox_AddTitlePostWord.Items.Add("post");
                writer.rd_comboBox_AddTitlePostWord.Text = "post";

                string path = PathFor("modify");
                Assert.IsTrue(writer.SaveProfile(path));

                CollectionAssert.AreEqual(new object[] { @"D:\a", @"D:\new" }, writer.md_comboBox_TargetDir.Items.Cast<object>().ToList());
                Assert.AreEqual(@"D:\new", writer.md_comboBox_TargetDir.Text);

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.LoadProfile(path));
                CollectionAssert.AreEqual(new object[] { @"D:\a", @"D:\new" }, reader.md_comboBox_TargetDir.Items.Cast<object>().ToList());
                CollectionAssert.AreEqual(new object[] { "rename" }, reader.rd_comboBox_RenameDir.Items.Cast<object>().ToList());
                CollectionAssert.AreEqual(new object[] { "post" }, reader.rd_comboBox_AddTitlePostWord.Items.Cast<object>().ToList(), "重複は追加されない");
            });
        }

        [TestMethod]
        public void LoadProfileは参照候補フォルダとリストを読み込んで反映する()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.ReferenceCandidateFolders = new[] { @"D:\ref\a", @"D:\ref\b" };

                string path = PathFor("reference");
                Assert.IsTrue(writer.SaveProfile(path));

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.LoadProfile(path));
                CollectionAssert.AreEqual(new[] { @"D:\ref\a", @"D:\ref\b" }, reader.ReferenceCandidateFolders);
            });
        }

        [TestMethod]
        public void LoadProfileは参照候補フォルダからコンボボックスの候補を作り直す()
        {
            // WinForms版LoadJsonFileの後処理(UpdateRenameComboBox/UpdateMoveDestDirComboBox)
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cmn_textBox_Reference.Text = @"D:\ref";
                writer.rd_textBox_SplitWord3.Text = "_";
                writer.ReferenceCandidateFolders = new[] { @"D:\ref\beta_01", @"D:\ref\alpha_02" };

                string path = PathFor("reference_combo");
                Assert.IsTrue(writer.SaveProfile(path));

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.LoadProfile(path));

                // フォルダ格納先を取り除いた名前が、昇順(Sorted)で並ぶ
                CollectionAssert.AreEqual(new object[] { "alpha_02", "beta_01" },
                    reader.pf_comboBox_MoveDestDirName.Items.Cast<object>().ToList());
                // 結合文字は末尾の区切り文字より前だけ
                CollectionAssert.AreEqual(new object[] { "alpha", "beta" },
                    reader.rd_comboBox_MergeWord.Items.Cast<object>().ToList());
            });
        }

        [TestMethod]
        public void LoadProfileは読み込んだあとリストをクリアする()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.ReferenceCandidateFolders = new string[0];
                string path = PathFor("clearlist");
                writer.SaveProfile(path);

                MainWindow reader = NewWindow();
                reader.sf_listBox_Target.Items.Add("残っててはいけない項目");
                reader.rd_listView_Target.Items.Add(new ListViewRow("残っててはいけない項目", ""));
                reader.pf_listView_Target.Items.Add(new ListViewRow("残っててはいけない項目", "", ""));

                reader.LoadProfile(path);

                Assert.AreEqual(0, reader.sf_listBox_Target.Items.Count);
                Assert.AreEqual(0, reader.rd_listView_Target.Items.Count);
                Assert.AreEqual(0, reader.pf_listView_Target.Items.Count);
            });
        }

        [TestMethod]
        public void 存在しないファイルを読んでも例外にならず失敗を返す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.md_textBox_SourceDir.Text = "そのまま";

                Assert.IsFalse(window.sr.Load(PathFor("nothing")));
                Assert.AreEqual("そのまま", window.md_textBox_SourceDir.Text);
            });
        }

        [TestMethod]
        public void JSON保存で共通タブの入力値と参照候補フォルダが保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cmn_textBox_Reference.Text = @"D:\ref";
                writer.cmn_textBox_AddList.Text = "add";
                writer.cmn_textBox_AddListSuffix.Text = "_suffix";
                writer.ReferenceCandidateFolders = new[] { @"D:\ref\a", @"D:\ref\b" };

                string path = Path.Combine(tempDirectory, "common.json");
                Assert.IsTrue(writer.SaveProfile(path), "JSON保存に成功するはず");

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.LoadProfile(path), "JSON読み込みに成功するはず");

                Assert.AreEqual(@"D:\ref", reader.cmn_textBox_Reference.Text);
                Assert.AreEqual("add", reader.cmn_textBox_AddList.Text);
                Assert.AreEqual("_suffix", reader.cmn_textBox_AddListSuffix.Text);
                CollectionAssert.AreEqual(new[] { @"D:\ref\a", @"D:\ref\b" }, reader.ReferenceCandidateFolders);
            });
        }

        [TestMethod]
        public void JSONファイルが無ければ読み込みは失敗を返す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                Assert.IsFalse(window.LoadProfile(Path.Combine(tempDirectory, "nothing.json")));
            });
        }

        [TestMethod]
        public void 共通タブのリファレンスフォルダを変えると格納先にも反映される()
        {
            // WinForms版cmn_textBox_Reference_TextChanged
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.cmn_textBox_Reference.Text = @"D:\ref";

                Assert.AreEqual(@"D:\ref", window.rd_textBox_ExistItemDir.Text);
                Assert.AreEqual(@"D:\ref", window.pf_textBox_ReferenceFile.Text);
            });
        }

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            // WinForms版FileArranger(StcSaveRestore.BuildGenericProfile + SaveJsonFile)が書き出す形式。
            // 旧キー名(pf_textBox_RefrenceFile / pf_textBox_TargetSeprator)の読み替えも確認する
            string path = Path.Combine(tempDirectory, "winforms.json");
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" +
                "    \"Common|cmn_textBox_Reference\": \"D:\\\\ref\",\n" +
                "    \"Common|cmn_textBox_AddListSuffix\": \"_01\",\n" +
                "    \"MoveDir|md_textBox_SourceDir\": \"D:\\\\src\",\n" +
                "    \"MoveDir|md_comboBox_TargetDir\": \"D:\\\\dst2\",\n" +
                "    \"MoveDir|rd_comboBox_RenameDir\": \"D:\\\\rename\",\n" +
                "    \"RenameDir|rd_textBox_ExistItemDir\": \"D:\\\\ref\",\n" +
                "    \"RenameDir|rd_checkBox_FileOpen\": \"True\",\n" +
                "    \"RenameDir|rd_textBox_SplitWord3\": \"_\",\n" +
                "    \"RenameDir|rd_comboBox_AddTitlePostWord\": \"巻\",\n" +
                "    \"PartitionFile|pf_textBox_RefrenceFile\": \"D:\\\\pf_ref\",\n" +
                "    \"PartitionFile|pf_textBox_TargetSeprator\": \"-\",\n" +
                "    \"PartitionFile|pf_checkBox_CreateNewDir\": \"True\"\n" +
                "  },\n" +
                "  \"Lists\": {\n" +
                "    \"MoveDir|md_comboBox_TargetDir\": [\"D:\\\\dst1\", \"D:\\\\dst2\"],\n" +
                "    \"MoveDir|rd_comboBox_RenameDir\": [\"D:\\\\rename\"],\n" +
                "    \"RenameDir|rd_comboBox_AddTitlePostWord\": [\"話\", \"巻\"],\n" +
                "    \"ReferenceCandidate|Value_\": [\"D:\\\\ref\\\\作品A_01\", \"D:\\\\ref\\\\作品B_02\"]\n" +
                "  },\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {}\n" +
                "}", new UTF8Encoding(false));

            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.mf_textBox_SourceDir.Text = "消える値";
                Assert.IsTrue(window.LoadProfile(path));

                // 旧キー名(typoのまま保存されたもの)でも読める
                Assert.AreEqual(@"D:\pf_ref", window.pf_textBox_ReferenceFile.Text);
                Assert.AreEqual("-", window.pf_textBox_TargetSeparator.Text);
                Assert.AreEqual(@"D:\ref", window.cmn_textBox_Reference.Text);
                Assert.AreEqual("_01", window.cmn_textBox_AddListSuffix.Text);
                Assert.AreEqual(@"D:\src", window.md_textBox_SourceDir.Text);
                Assert.AreEqual(true, window.rd_checkBox_FileOpen.IsChecked);
                Assert.AreEqual(true, window.pf_checkBox_CreateNewDir.IsChecked);

                // コンボは履歴(Lists)と入力値(Values)の両方
                CollectionAssert.AreEqual(new object[] { @"D:\dst1", @"D:\dst2" }, window.md_comboBox_TargetDir.Items.Cast<object>().ToList());
                Assert.AreEqual(@"D:\dst2", window.md_comboBox_TargetDir.Text);
                CollectionAssert.AreEqual(new object[] { @"D:\rename" }, window.rd_comboBox_RenameDir.Items.Cast<object>().ToList());
                Assert.AreEqual(@"D:\rename", window.rd_comboBox_RenameDir.Text);
                Assert.AreEqual(2, window.rd_comboBox_AddTitlePostWord.Items.Count);
                Assert.AreEqual("巻", window.rd_comboBox_AddTitlePostWord.Text);

                // 参照候補フォルダと、そこから作られるコンボの候補
                CollectionAssert.AreEqual(new[] { @"D:\ref\作品A_01", @"D:\ref\作品B_02" }, window.ReferenceCandidateFolders);
                CollectionAssert.AreEqual(new object[] { "作品A", "作品B" }, window.rd_comboBox_MergeWord.Items.Cast<object>().ToList());

                // ファイルに無い項目は既定値(空)に戻る(WinForms版と同じ)
                Assert.AreEqual("", window.mf_textBox_SourceDir.Text);
            });
        }

        [TestMethod]
        public void 起動時に既定の設定ファイルを読み込みプロファイル一覧の先頭も読み込む()
        {
            // WinForms版と同じく、FileArranger.jsonを読んだあと、プロファイル一覧の先頭が選ばれて読み込まれる
            File.WriteAllText(Path.Combine(dataFolder, "FileArranger.json"),
                "{ \"Values\": { \"MoveFile|mf_textBox_SourceDir\": \"既定\" }, \"Lists\": {}, \"CheckedStates\": {}, \"Grids\": {} }",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dataFolder, "A_first.json"),
                "{ \"Values\": { \"MoveFile|mf_textBox_SourceDir\": \"先頭のプロファイル\" }, \"Lists\": {}, \"CheckedStates\": {}, \"Grids\": {} }",
                new UTF8Encoding(false));
            // 旧XMLは一覧に出さない(JSONのみ対応)
            File.WriteAllText(Path.Combine(dataFolder, "0_old.xml"), "<Setting />");

            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();

                CollectionAssert.AreEqual(new object[] { "A_first.json", "FileArranger.json" },
                    window.comboBox_LoadSetting.Items.Cast<object>().ToList());
                Assert.AreEqual("A_first.json", window.comboBox_LoadSetting.SelectedItem);
                Assert.AreEqual("先頭のプロファイル", window.mf_textBox_SourceDir.Text);
            });
        }
    }
}
