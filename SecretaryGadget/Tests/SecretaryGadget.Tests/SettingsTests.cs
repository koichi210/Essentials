using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>設定ファイルのフォルダ変更。本物の %LOCALAPPDATA%\SecGad を触らないよう一時フォルダへ差し替えて確かめる。</summary>
    [TestClass]
    [DoNotParallelize]
    public class SettingsTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "SecGadTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Settings.LocalDirOverride = Path.Combine(_root, "local");
        }

        [TestCleanup]
        public void TearDown()
        {
            Settings.LocalDirOverride = null;
            Directory.Delete(_root, true);
        }

        [TestMethod]
        public void CallMenu_DefaultsToBuiltins()
        {
            Settings s = Settings.Load();
            CollectionAssert.AreEqual(
                new[] { "@schedule", "@hakoniwa", "@stockradar", "@timer" },
                s.CallMenu.Select(e => e.Path).ToArray());
            Assert.AreEqual("timer", s.CallMenu[3].BuiltinName);
        }

        [TestMethod]
        public void ReadCallMenu_PicksUpAddedRowsAndDropsBlankOnes()
        {
            string path = Path.Combine(_root, "settings.json");
            File.WriteAllText(path, """
                {
                  // 手で足した行
                  "CallMenu": [
                    { "Label": "本日の予定", "Path": "@Schedule" },
                    { "Label": " メモ帳 ", "Path": "%WINDIR%\notepad.exe", "Arguments": "a.txt" },
                    { "Label": "", "Path": "x.bat" },
                    { "Label": "パスなし", "Path": " " },
                  ],
                }
                """);

            List<CallMenuEntry> menu = Settings.ReadCallMenu(path)!;

            Assert.AreEqual(2, menu.Count);
            Assert.AreEqual("schedule", menu[0].BuiltinName);
            Assert.AreEqual("メモ帳", menu[1].Label);
            Assert.IsFalse(menu[1].IsBuiltin);
            Assert.AreEqual("a.txt", menu[1].Arguments);
        }

        [TestMethod]
        public void ReadCallMenu_MissingKeyMeansDefaults_EmptyListMeansNone_BrokenFileMeansNull()
        {
            string path = Path.Combine(_root, "settings.json");
            File.WriteAllText(path, "{ \"UserName\": \"テストさん\" }");
            Assert.AreEqual(4, Settings.ReadCallMenu(path)!.Count);

            File.WriteAllText(path, "{ \"CallMenu\": [] }");
            Assert.AreEqual(0, Settings.ReadCallMenu(path)!.Count);

            File.WriteAllText(path, "{ 壊れた");
            Assert.IsNull(Settings.ReadCallMenu(path));
            Assert.IsNull(Settings.ReadCallMenu(Path.Combine(_root, "無い.json")));
        }

        [TestMethod]
        public void CallMenuEntry_Launch_MissingFile_ReturnsNotFoundMessage()
        {
            var entry = new CallMenuEntry { Label = "無いツール", Path = Path.Combine(_root, "無い.bat") };
            StringAssert.Contains(entry.Launch(), "起動先が見つかりません");
        }

        [TestMethod]
        public void ChangeDir_EmptyFolder_SavesCurrentAndLoadUsesIt()
        {
            Settings current = Settings.Load();
            current.UserName = "テストさん";
            string shared = Path.Combine(_root, "shared");

            Settings? changed = Settings.ChangeDir(current, shared);

            Assert.AreSame(current, changed);
            Assert.AreEqual(Path.Combine(shared, "settings.json"), changed!.FilePath);
            Assert.IsTrue(File.Exists(Path.Combine(shared, "settings.json")));
            // 次回の起動(Load)も新しいフォルダを使う
            Settings reloaded = Settings.Load();
            Assert.AreEqual(shared, reloaded.Dir);
            Assert.AreEqual("テストさん", reloaded.UserName);
        }

        [TestMethod]
        public void ChangeDir_FolderWithSettings_LoadsExistingAndKeepsOldFile()
        {
            Settings current = Settings.Load();
            current.UserName = "元のPC";
            current.Save();
            string oldFile = current.FilePath;

            string shared = Path.Combine(_root, "shared");
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(shared, "settings.json"), "{ \"UserName\": \"共有の設定\", \"SkinScale\": 0.5 }");

            Settings? changed = Settings.ChangeDir(current, shared);

            Assert.IsNotNull(changed);
            Assert.AreNotSame(current, changed);
            Assert.AreEqual("共有の設定", changed!.UserName);
            Assert.AreEqual(0.5, changed.SkinScale);
            Assert.AreEqual(shared, changed.Dir);
            // 元の場所の settings.json は消さない
            Assert.IsTrue(File.Exists(oldFile));
        }

        [TestMethod]
        public void ChangeDir_EnvironmentVariablesInLocationAreExpanded()
        {
            Environment.SetEnvironmentVariable("SECGAD_TEST_ROOT", _root);
            try
            {
                Settings.WriteLocation("%SECGAD_TEST_ROOT%\\viaenv");
                Assert.AreEqual(Path.Combine(_root, "viaenv"), Settings.ResolveSettingsDir());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SECGAD_TEST_ROOT", null);
            }
        }
    }
}
