using System;
using System.IO;
using System.Text;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// MainWindow（4つのツールをタブで切り替える画面）のテスト。
    /// 各タブの中身の動きは ToyingFileViewTests などで確かめるので、ここではタブの並び・データ保存先の割り振り・
    /// タイトルへの進み具合の表示だけを見る。
    /// データ保存先は一時フォルダに差し替え、旧アプリの設定の取り込み(importLegacySettings)もしない
    /// (本物の%LOCALAPPDATA%に触れないため)。Close()も呼ばない。
    /// </summary>
    [TestClass]
    public class MainWindowTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FiddleAroundStringTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }

        private static void WriteJson(string path, string valuesJson)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" + valuesJson + "\n  },\n" +
                "  \"Lists\": {},\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {}\n" +
                "}", new UTF8Encoding(false));
        }

        [TestMethod]
        public void タイトルと4つのタブが元のツールの順に並ぶ()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow(_tempDirectory);

                Assert.AreEqual("FiddleAroundString", window.Title);
                Assert.AreEqual(4, window.tabControl_Menu.Items.Count);

                TabItem[] tabs =
                {
                    window.tabItem_ToyingFile, window.tabItem_ToyingData, window.tabItem_TrimFileData, window.tabItem_TrimHtmlData,
                };
                CollectionAssert.AreEqual(new object[] { "ファイル加工", "データ加工", "ファイル抽出", "HTML抽出" },
                    Array.ConvertAll(tabs, tab => tab.Header));
                for (int i = 0; i < tabs.Length; i++)
                {
                    Assert.AreSame(tabs[i], window.tabControl_Menu.Items[i], "タブの並び順");
                }

                Assert.IsInstanceOfType(window.tabItem_ToyingFile.Content, typeof(ToyingFileView));
                Assert.IsInstanceOfType(window.tabItem_ToyingData.Content, typeof(ToyingDataView));
                Assert.IsInstanceOfType(window.tabItem_TrimFileData.Content, typeof(TrimFileDataView));
                Assert.IsInstanceOfType(window.tabItem_TrimHtmlData.Content, typeof(TrimHtmlDataView));

                // タブのツールチップで、どの旧ツールの機能か分かるようにしてある
                StringAssert.Contains((string)window.tabItem_ToyingFile.ToolTip, "旧ToyingFile");
                StringAssert.Contains((string)window.tabItem_ToyingData.ToolTip, "旧ToyingData");
                StringAssert.Contains((string)window.tabItem_TrimFileData.ToolTip, "旧TrimFileData");
                StringAssert.Contains((string)window.tabItem_TrimHtmlData.ToolTip, "旧TrimHtmlData");
            });
        }

        [TestMethod]
        public void 起動直後は最初のタブが選ばれている()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow(_tempDirectory);

                Assert.AreEqual(0, window.tabControl_Menu.SelectedIndex);
            });
        }

        [TestMethod]
        public void 設定を持つタブはデータ保存先の下のそれぞれのフォルダから既定の設定を読む()
        {
            WriteJson(Path.Combine(_tempDirectory, "TrimFileData", "TrimFileData.json"),
                "    \"Common|textBox_ReferencePath\": \"file-default\"");
            WriteJson(Path.Combine(_tempDirectory, "TrimFileData", "another.json"),
                "    \"Common|textBox_ReferencePath\": \"file-another\"");
            WriteJson(Path.Combine(_tempDirectory, "TrimHtmlData", "TrimHtmlData.json"),
                "    \"Common|textBox_SearchWord\": \"html-default\"");

            StaRunner.Run(() =>
            {
                var window = new MainWindow(_tempDirectory);

                Assert.AreEqual("file-default", window.trimFileDataView.textBox_ReferencePath.Text);
                Assert.AreEqual("TrimFileData.json", window.trimFileDataView.comboBox_LoadSetting.SelectedItem);
                Assert.AreEqual("html-default", window.trimHtmlDataView.textBox_SearchWord.Text);
                Assert.AreEqual("TrimHtmlData.json", window.trimHtmlDataView.comboBox_LoadSetting.SelectedItem);

                // プロファイル一覧はタブごとに分かれていて、もう片方のタブの設定ファイルは出てこない
                Assert.AreEqual(2, window.trimFileDataView.comboBox_LoadSetting.Items.Count);
                Assert.AreEqual(1, window.trimHtmlDataView.comboBox_LoadSetting.Items.Count);
            });
        }

        [TestMethod]
        public void タブごとの設定フォルダが無ければ作る()
        {
            StaRunner.Run(() =>
            {
                new MainWindow(_tempDirectory);
            });

            Assert.IsTrue(Directory.Exists(Path.Combine(_tempDirectory, MainWindow.TrimFileDataFolderName)));
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempDirectory, MainWindow.TrimHtmlDataFolderName)));
        }

        [TestMethod]
        public void HTML抽出タブの取得中の進み具合がタイトルに出て終わると戻る()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow(_tempDirectory);

                // HTML抽出タブのShowStatusがMainWindowのタイトルにつながっていること
                window.trimHtmlDataView.ShowStatus("HTML取得中 (1/3)");
                Assert.AreEqual("FiddleAroundString - HTML取得中 (1/3)", window.Title);

                window.trimHtmlDataView.ShowStatus(null);
                Assert.AreEqual("FiddleAroundString", window.Title);
            });
        }
    }
}
