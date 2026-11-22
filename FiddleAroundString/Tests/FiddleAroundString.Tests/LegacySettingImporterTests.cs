using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// LegacySettingImporter（旧TrimFileData・旧TrimHtmlDataの設定を初回だけコピーする処理）のテスト。
    /// 旧アプリの保存先・新しい保存先とも一時フォルダを使い、本物の%LOCALAPPDATA%には触れない。
    /// </summary>
    [TestClass]
    public class LegacySettingImporterTests
    {
        private string _tempDirectory;
        private string _legacyFolder;
        private string _newFolder;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FiddleAroundStringImportTests_" + Guid.NewGuid().ToString("N"));
            _legacyFolder = Path.Combine(_tempDirectory, "legacy");
            _newFolder = Path.Combine(_tempDirectory, "new", "TrimFileData");
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

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        [TestMethod]
        public void 新しいフォルダが無ければ旧フォルダのJSONをサブフォルダごとコピーする()
        {
            WriteFile(Path.Combine(_legacyFolder, "TrimFileData.json"), "default");
            WriteFile(Path.Combine(_legacyFolder, "sub", "work.json"), "sub");
            WriteFile(Path.Combine(_legacyFolder, "old.xml"), "xml");

            int copied = LegacySettingImporter.ImportIfFirstRun(_legacyFolder, _newFolder);

            Assert.AreEqual(2, copied);
            Assert.AreEqual("default", File.ReadAllText(Path.Combine(_newFolder, "TrimFileData.json")));
            Assert.AreEqual("sub", File.ReadAllText(Path.Combine(_newFolder, "sub", "work.json")));
            Assert.IsFalse(File.Exists(Path.Combine(_newFolder, "old.xml")), "WPF版で読めない旧XMLはコピーしない");
            // 移動ではなくコピーなので、旧アプリ側はそのまま
            Assert.IsTrue(File.Exists(Path.Combine(_legacyFolder, "TrimFileData.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_legacyFolder, "sub", "work.json")));
        }

        [TestMethod]
        public void 新しいフォルダが既にあれば何もしない()
        {
            WriteFile(Path.Combine(_legacyFolder, "TrimFileData.json"), "legacy");
            WriteFile(Path.Combine(_newFolder, "TrimFileData.json"), "current");

            int copied = LegacySettingImporter.ImportIfFirstRun(_legacyFolder, _newFolder);

            Assert.AreEqual(0, copied);
            Assert.AreEqual("current", File.ReadAllText(Path.Combine(_newFolder, "TrimFileData.json")), "今の設定を上書きしない");
        }

        [TestMethod]
        public void 新しいフォルダが空でも既にあれば2回目以降はコピーしない()
        {
            Directory.CreateDirectory(_newFolder);
            WriteFile(Path.Combine(_legacyFolder, "TrimFileData.json"), "legacy");

            Assert.AreEqual(0, LegacySettingImporter.ImportIfFirstRun(_legacyFolder, _newFolder));
            Assert.IsFalse(File.Exists(Path.Combine(_newFolder, "TrimFileData.json")));
        }

        [TestMethod]
        public void 旧フォルダが無ければ何もせず新しいフォルダも作らない()
        {
            int copied = LegacySettingImporter.ImportIfFirstRun(_legacyFolder, _newFolder);

            Assert.AreEqual(0, copied);
            Assert.IsFalse(Directory.Exists(_newFolder));
        }

        [TestMethod]
        public void 旧アプリの既定の保存先はLocalAppDataの下のアプリ名フォルダ()
        {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrimHtmlData");

            Assert.AreEqual(expected, LegacySettingImporter.GetLegacyDefaultFolder("TrimHtmlData"));
        }

        [TestMethod]
        public void コピーした旧アプリの既定の設定をタブがそのまま読める()
        {
            // 旧TrimFileDataで保存したプロファイル(保存キーは同じ)
            WriteFile(Path.Combine(_legacyFolder, "TrimFileData.json"),
                "{\n  \"Values\": {\n    \"Common|textBox_ReferencePath\": \"C:\\\\legacy.txt\",\n    \"Common|checkBox_IgnoreWidth\": \"True\"\n  },\n" +
                "  \"Lists\": {},\n  \"CheckedStates\": {},\n  \"Grids\": {}\n}");

            LegacySettingImporter.ImportIfFirstRun(_legacyFolder, _newFolder);

            StaRunner.Run(() =>
            {
                var view = new TrimFileDataView(_newFolder);

                Assert.AreEqual(@"C:\legacy.txt", view.textBox_ReferencePath.Text);
                Assert.AreEqual(true, view.checkBox_IgnoreWidth.IsChecked);
                Assert.AreEqual("TrimFileData.json", view.comboBox_LoadSetting.SelectedItem);
            });
        }
    }
}
