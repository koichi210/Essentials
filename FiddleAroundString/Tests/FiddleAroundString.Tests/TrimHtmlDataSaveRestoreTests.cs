using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// 「HTML抽出」タブ(TrimHtmlDataView)の設定保存/読み込み(WpfSaveRestore)のテスト。旧TrimHtmlDataのSaveRestoreTestsを移植したもの。
    /// WinForms版TrimHtmlDataで保存したJSONプロファイルをそのまま読めることも確認する。
    /// データ保存先は一時フォルダに差し替え、本物の%LOCALAPPDATA%\FiddleAroundString・旧%LOCALAPPDATA%\TrimHtmlDataには触れない。
    /// </summary>
    [TestClass]
    public class TrimHtmlDataSaveRestoreTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "TrimHtmlDataSaveRestoreTests_" + Guid.NewGuid().ToString("N"));
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

                var writer = new TrimHtmlDataView(tempDirectory);
                writer.textBox_SourceList.Text = "https://example.com/a\r\nhttps://example.com/b";
                writer.textBox_DestList.Text = "◆https://example.com/a\r\n結果";
                writer.textBox_SearchWord.Text = "<title>";
                writer.textBox_DelimiterWord.Text = "</title>";
                writer.textBox_TrimLineNum.Text = "3";
                writer.checkBox_OrdinalCase.IsChecked = true;
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new TrimHtmlDataView(tempDirectory);
                Assert.IsTrue(reader.SaveRestore.Load(path));

                // 取得元・取得値はプロファイルに含めないので、読み込んだ側の欄は空のまま
                Assert.AreEqual("", reader.textBox_SourceList.Text);
                Assert.AreEqual("", reader.textBox_DestList.Text);
                Assert.AreEqual("<title>", reader.textBox_SearchWord.Text);
                Assert.AreEqual("</title>", reader.textBox_DelimiterWord.Text);
                Assert.AreEqual("3", reader.textBox_TrimLineNum.Text);
                Assert.AreEqual(true, reader.checkBox_OrdinalCase.IsChecked);
            });
        }

        [TestMethod]
        public void 保存キーはWinForms版と同じ()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var keys = window.SaveRestore.BuildGenericProfile().Values.Keys;

                // 「最初にヒットしたワードだけを抽出」はWinForms版でも保存していない。
                // 取得元・取得値は設定ではなくデータなので、両版とも保存対象から外した
                CollectionAssert.AreEquivalent(new[]
                {
                    "Common|textBox_SearchWord",
                    "Common|textBox_DelimiterWord",
                    "Common|textBox_TrimLineNum",
                    "Common|checkBox_OrdinalCase",
                }, new List<string>(keys));
            });
        }

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            string path = Path.Combine(tempDirectory, "winforms.json");
            WriteJson(path,
                "    \"Common|textBox_SourceList\": \"https://example.com/\\r\\nhttps://example.org/\",\n" +
                "    \"Common|textBox_SearchWord\": \"<h1>\",\n" +
                "    \"Common|textBox_TrimLineNum\": \"2\",\n" +
                "    \"Common|checkBox_OrdinalCase\": \"True\"");

            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                window.textBox_SourceList.Text = "入力中のURL";
                window.textBox_DestList.Text = "前回の結果";
                Assert.IsTrue(window.SaveRestore.Load(path));

                // 以前のプロファイルに残っている取得元の値は無視され、入力中の取得元・取得値はそのまま残る
                Assert.AreEqual("入力中のURL", window.textBox_SourceList.Text);
                Assert.AreEqual("前回の結果", window.textBox_DestList.Text);
                Assert.AreEqual("<h1>", window.textBox_SearchWord.Text);
                Assert.AreEqual("2", window.textBox_TrimLineNum.Text);
                Assert.AreEqual(true, window.checkBox_OrdinalCase.IsChecked);
            });
        }

        [TestMethod]
        public void 起動時にデータ保存先の既定設定ファイルを読み込み一覧で選択する()
        {
            WriteJson(Path.Combine(tempDirectory, "another.json"),
                "    \"Common|textBox_SearchWord\": \"another\"");
            WriteJson(Path.Combine(tempDirectory, "TrimHtmlData.json"),
                "    \"Common|textBox_SearchWord\": \"default\"");

            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);

                Assert.AreEqual("default", window.textBox_SearchWord.Text);
                Assert.AreEqual("TrimHtmlData.json", window.comboBox_LoadSetting.SelectedItem);

                // 別のプロファイルを選ぶとその内容に置き換わる
                window.comboBox_LoadSetting.SelectedItem = "another.json";
                Assert.AreEqual("another", window.textBox_SearchWord.Text);
            });
        }

        [TestMethod]
        public void 設定ファイルが無ければ既定値で起動する()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);

                Assert.AreEqual("", window.textBox_SourceList.Text);
                Assert.AreEqual("", window.textBox_TrimLineNum.Text);
                Assert.AreEqual(false, window.checkBox_OrdinalCase.IsChecked);
                Assert.AreEqual(0, window.comboBox_LoadSetting.Items.Count);
            });
        }
    }
}
