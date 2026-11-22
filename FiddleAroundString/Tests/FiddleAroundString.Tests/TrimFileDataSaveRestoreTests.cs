using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// 「ファイル抽出」タブ(TrimFileDataView)の設定保存/読み込み(WpfSaveRestore)のテスト。旧TrimFileDataのSaveRestoreTestsを移植したもの。
    /// WinForms版TrimFileDataで保存したJSONプロファイル(typo修正前の旧キーを含む)をそのまま読めることも確認する。
    /// データ保存先は一時フォルダに差し替え、本物の%LOCALAPPDATA%\FiddleAroundString・旧%LOCALAPPDATA%\TrimFileDataには触れない。
    /// </summary>
    [TestClass]
    public class TrimFileDataSaveRestoreTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "TrimFileDataSaveRestoreTests_" + Guid.NewGuid().ToString("N"));
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

        private static void WriteJson(string path, string valuesJson)
        {
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" + valuesJson + "\n  },\n" +
                "  \"Lists\": {},\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {}\n" +
                "}", new UTF8Encoding(false));
        }

        [TestMethod]
        public void 全項目が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                string path = Path.Combine(tempDirectory, "setting.json");

                var writer = new TrimFileDataView(tempDirectory);
                writer.textBox_ReferencePath.Text = @"C:\Work\reference.txt";
                writer.textBox_SearchCommonWord.Text = "error";
                writer.checkBox_FirstWordOnly.IsChecked = true;
                writer.checkBox_OrdinalCase.IsChecked = true;
                writer.checkBox_IgnoreWidth.IsChecked = true;
                writer.textBox_SearchWordList.Text = "apple banana\r\ncherry";
                writer.textBox_SearchResultList.Text = "◆apple banana\r\n結果";
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new TrimFileDataView(tempDirectory);
                Assert.IsTrue(reader.SaveRestore.Load(path));

                Assert.AreEqual(@"C:\Work\reference.txt", reader.textBox_ReferencePath.Text);
                Assert.AreEqual("error", reader.textBox_SearchCommonWord.Text);
                Assert.AreEqual(true, reader.checkBox_FirstWordOnly.IsChecked);
                Assert.AreEqual(true, reader.checkBox_OrdinalCase.IsChecked);
                Assert.AreEqual(true, reader.checkBox_IgnoreWidth.IsChecked);
                Assert.AreEqual("apple banana\r\ncherry", reader.textBox_SearchWordList.Text);
                Assert.AreEqual("◆apple banana\r\n結果", reader.textBox_SearchResultList.Text);
            });
        }

        [TestMethod]
        public void 保存キーはWinForms版と同じ()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                var keys = window.SaveRestore.BuildGenericProfile().Values.Keys;

                CollectionAssert.AreEquivalent(new[]
                {
                    "Common|textBox_ReferencePath",
                    "Common|textBox_SearchCommonWord",
                    "Common|checkBox_FirstWordOnly",
                    "Common|checkBox_OrdinalCase",
                    "Common|checkBox_IgnoreWidth",
                    "Common|textBox_SearchWordList",
                    "Common|textBox_SearchResultList",
                }, new System.Collections.Generic.List<string>(keys));
            });
        }

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            string path = Path.Combine(tempDirectory, "winforms.json");
            WriteJson(path,
                "    \"Common|textBox_ReferencePath\": \"C:\\\\refer.txt\",\n" +
                "    \"Common|textBox_SearchCommonWord\": \"common\",\n" +
                "    \"Common|checkBox_FirstWordOnly\": \"True\",\n" +
                "    \"Common|checkBox_OrdinalCase\": \"False\",\n" +
                "    \"Common|textBox_SearchWordList\": \"apple\\r\\nbanana\"");

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                window.textBox_SearchResultList.Text = "消える値";
                Assert.IsTrue(window.SaveRestore.Load(path));

                Assert.AreEqual(@"C:\refer.txt", window.textBox_ReferencePath.Text);
                Assert.AreEqual("common", window.textBox_SearchCommonWord.Text);
                Assert.AreEqual(true, window.checkBox_FirstWordOnly.IsChecked);
                Assert.AreEqual(false, window.checkBox_OrdinalCase.IsChecked);
                Assert.AreEqual("apple\r\nbanana", window.textBox_SearchWordList.Text);
                // ファイルに無い項目は既定値(空)に戻る(WinForms版と同じ)
                Assert.AreEqual("", window.textBox_SearchResultList.Text);
            });
        }

        [TestMethod]
        public void typo修正前の旧キーで保存された設定も読める()
        {
            string path = Path.Combine(tempDirectory, "legacy.json");
            WriteJson(path,
                "    \"Common|textBox_SerchCommonWord\": \"old common\",\n" +
                "    \"Common|textBox_SerchWordList\": \"old words\",\n" +
                "    \"Common|textBox_SerchResultList\": \"old result\"");

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                Assert.IsTrue(window.SaveRestore.Load(path));

                Assert.AreEqual("old common", window.textBox_SearchCommonWord.Text);
                Assert.AreEqual("old words", window.textBox_SearchWordList.Text);
                Assert.AreEqual("old result", window.textBox_SearchResultList.Text);
            });
        }

        [TestMethod]
        public void 起動時にデータ保存先の既定設定ファイルを読み込み一覧で選択する()
        {
            WriteJson(Path.Combine(tempDirectory, "another.json"),
                "    \"Common|textBox_ReferencePath\": \"another\"");
            WriteJson(Path.Combine(tempDirectory, "TrimFileData.json"),
                "    \"Common|textBox_ReferencePath\": \"default\"");

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);

                Assert.AreEqual("default", window.textBox_ReferencePath.Text);
                Assert.AreEqual("TrimFileData.json", window.comboBox_LoadSetting.SelectedItem);
                Assert.AreEqual(2, window.comboBox_LoadSetting.Items.Count);
            });
        }

        [TestMethod]
        public void 一覧でプロファイルを選ぶとその内容に置き換わる()
        {
            WriteJson(Path.Combine(tempDirectory, "another.json"),
                "    \"Common|textBox_ReferencePath\": \"another\"");
            WriteJson(Path.Combine(tempDirectory, "TrimFileData.json"),
                "    \"Common|textBox_ReferencePath\": \"default\"");

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                window.comboBox_LoadSetting.SelectedItem = "another.json";

                Assert.AreEqual("another", window.textBox_ReferencePath.Text);
            });
        }

        [TestMethod]
        public void 設定ファイルが無ければ既定値で起動する()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);

                Assert.AreEqual("", window.textBox_ReferencePath.Text);
                Assert.AreEqual(false, window.checkBox_FirstWordOnly.IsChecked);
                Assert.AreEqual(false, window.checkBox_OrdinalCase.IsChecked);
                Assert.AreEqual(0, window.comboBox_LoadSetting.Items.Count);
            });
        }
    }
}
