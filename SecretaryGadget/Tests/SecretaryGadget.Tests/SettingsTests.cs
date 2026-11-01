using System;
using System.IO;
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
