using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Launcher.Tests
{
    /// <summary>
    /// 設定ファイル(launcher.json)を MagicLauncher と共通で読み書きできるかのテスト。
    /// 正解は MagicLauncher の server.js が実際に書いたもの(TestData/messy.expected.json)。
    /// </summary>
    [TestClass]
    public class ConfigServiceTests
    {
        private static readonly string _testData = Path.Combine(AppContext.BaseDirectory, "TestData");

        private string _dir;
        private string _dataDir;
        private string _configPath;

        [TestInitialize]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "LauncherTests_" + Guid.NewGuid().ToString("N"));
            _dataDir = Path.Combine(_dir, "MagicLauncher");
            _configPath = Path.Combine(_dir, "shared", "launcher.json");
            Directory.CreateDirectory(_dataDir);
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
        }

        [TestCleanup]
        public void TearDown()
        {
            Directory.Delete(_dir, true);
        }

        // settings.json に場所を書いた ConfigService(MagicLauncher と同じ見つけ方)
        private ConfigService CreateService(string fixture = "messy.json")
        {
            if (fixture != null)
            {
                File.Copy(Path.Combine(_testData, fixture), _configPath, true);
            }
            var settings = new JsonObject { ["configPath"] = _configPath };
            File.WriteAllText(Path.Combine(_dataDir, "settings.json"), settings.ToJsonString());
            return new ConfigService(_dataDir, Path.Combine(_dir, "default", "launcher.json"));
        }

        private JsonObject ReadConfig() => (JsonObject)JsonNode.Parse(File.ReadAllText(_configPath));

        // ------------------------------------------------------------ MagicLauncher と同じ形で書くか

        [TestMethod]
        public void NoOpSave_WritesExactlyWhatMagicLauncherWrites()
        {
            ConfigService service = CreateService();

            service.Mutate(data => 0);

            Assert.AreEqual(File.ReadAllText(Path.Combine(_testData, "messy.expected.json")), File.ReadAllText(_configPath));
            Assert.AreEqual(File.ReadAllText(Path.Combine(_testData, "messy.json")), File.ReadAllText(_configPath + ".bak"), "直前の内容が .bak に残る");
        }

        [TestMethod]
        public void NoOpSave_OnFileWrittenByMagicLauncher_DoesNotChangeABit()
        {
            ConfigService service = CreateService("messy.expected.json");
            byte[] before = File.ReadAllBytes(_configPath);

            service.Mutate(data => 0);

            CollectionAssert.AreEqual(before, File.ReadAllBytes(_configPath));
        }

        [TestMethod]
        public void ItemIds_AreTheSameAsMagicLauncher()
        {
            // server.js の GET /api/state が返した id(id の無い項目は中身と位置から決まる、重複は後ろを作り直す)
            ConfigService service = CreateService();
            JsonArray items = (JsonArray)service.Load().Data["items"];

            List<string> ids = ConfigService.ItemIds(items);

            Assert.AreEqual("i811029a7", ids[0]);
            Assert.AreEqual("iaaaa0001", ids[1]);
            Assert.AreEqual("i0238bbc2", ids[2]);
            Assert.AreEqual("i5bb6d83c", ids[3]);
            Assert.AreEqual("ifb079ade", ids[4]);
            Assert.AreEqual("if755ea91", ids[7]);
        }

        [TestMethod]
        public void Stringify_KeepsEmojiAndJapaneseAsIs_AndEscapesLikeJavaScript()
        {
            var node = new JsonObject { ["s"] = "🎮日本語\"\\\n\t\u0001\u2028", ["lone"] = "\ud83c" };

            string json = JsJson.StringifyCompact(node);

            Assert.AreEqual("{\"s\":\"🎮日本語\\\"\\\\\\n\\t\\u0001\u2028\",\"lone\":\"\\ud83c\"}", json);
        }

        [TestMethod]
        public void Stringify_PutsNumberLikeKeysFirst_LikeJavaScript()
        {
            var node = new JsonObject { ["b"] = 1, ["10"] = 2, ["a"] = 3, ["2"] = 4, ["01"] = 5 };

            Assert.AreEqual("{\"2\":4,\"10\":2,\"b\":1,\"a\":3,\"01\":5}", JsJson.StringifyCompact(node));
        }

        // ------------------------------------------------------------ 場所

        [TestMethod]
        public void Location_IsReadFromMagicLauncherSettings_WithEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("LAUNCHER_TEST_DIR", Path.GetDirectoryName(_configPath));
            File.WriteAllText(Path.Combine(_dataDir, "settings.json"), "\uFEFF{\"configPath\": \"%LAUNCHER_TEST_DIR%\\\\launcher.json\"}");
            var service = new ConfigService(_dataDir, Path.Combine(_dir, "default", "launcher.json"));

            ConfigLocation location = service.GetLocation();

            Assert.IsFalse(location.IsDefault);
            Assert.AreEqual(_configPath, location.FullPath);
        }

        [TestMethod]
        public void Location_WithoutSettings_IsTheDefault()
        {
            string defaultPath = Path.Combine(_dir, "default", "launcher.json");
            var service = new ConfigService(_dataDir, defaultPath);

            Assert.IsTrue(service.GetLocation().IsDefault);
            Assert.AreEqual(defaultPath, service.GetLocation().FullPath);
        }

        [TestMethod]
        public void SetConfigPath_KeepsOtherSettings_AndRejectsFolders()
        {
            File.WriteAllText(Path.Combine(_dataDir, "settings.json"), "{\"other\": 1}");
            var service = new ConfigService(_dataDir, null);

            service.SetConfigPath("\"" + _configPath + "\"");

            Assert.AreEqual("{\n  \"other\": 1,\n  \"configPath\": " + JsJson.StringifyCompact(JsonValue.Create(_configPath)) + "\n}\n",
                File.ReadAllText(Path.Combine(_dataDir, "settings.json")));
            Assert.ThrowsException<ConfigException>(() => service.SetConfigPath(_dir));
            Assert.ThrowsException<ConfigException>(() => service.SetConfigPath(Path.Combine(_dir, "no-such-folder", "launcher.json")));
            Assert.ThrowsException<ConfigException>(() => service.SetConfigPath("relative\\launcher.json"));
        }

        // ------------------------------------------------------------ 壊れているときは書かない

        [DataTestMethod]
        [DataRow("{ \"items\": [ ")]
        [DataRow("[]")]
        [DataRow("{ \"items\": {} }")]
        [DataRow("{ \"items\": null }")]
        public void BrokenFile_IsReportedAndNeverOverwritten(string content)
        {
            ConfigService service = CreateService(null);
            File.WriteAllText(_configPath, content);

            Assert.IsNotNull(service.Load().Error);
            Assert.ThrowsException<ConfigException>(() => service.AddItem("ツール", "x", "C:\\x"));
            Assert.AreEqual(content, File.ReadAllText(_configPath));
            Assert.IsFalse(File.Exists(_configPath + ".bak"));
        }

        [TestMethod]
        public void MissingFile_IsEmpty_AndCreatedOnFirstChange()
        {
            ConfigService service = CreateService(null);

            ConfigSnapshot snapshot = service.Load();
            Assert.IsFalse(snapshot.Exists);
            Assert.IsNull(snapshot.Error);

            string id = service.AddItem(null, "メモ帳", "C:\\Windows\\notepad.exe");

            JsonObject data = ReadConfig();
            Assert.AreEqual(1, (int)data["version"]);
            Assert.AreEqual(id, (string)data["items"][0]["id"]);
            Assert.IsFalse(File.Exists(_configPath + ".bak"));
        }

        // ------------------------------------------------------------ 画面からの操作

        [TestMethod]
        public void AddItem_WritesTheSameShapeAsMagicLauncher()
        {
            ConfigService service = CreateService("messy.expected.json");

            string id = service.AddItem("ツール", " 電卓 ", "\"C:\\Windows\\System32\\calc.exe\"");

            JsonObject item = (JsonObject)ReadConfig()["items"].AsArray().Last();
            CollectionAssert.AreEqual(new[] { "id", "name", "path", "tags", "emoji", "note" }, item.Select(p => p.Key).ToArray());
            Assert.AreEqual(id, (string)item["id"]);
            Assert.AreEqual("電卓", (string)item["name"]);
            Assert.AreEqual("C:\\Windows\\System32\\calc.exe", (string)item["path"]);
            Assert.AreEqual("[\"ツール\"]", JsJson.StringifyCompact(item["tags"]));
        }

        [TestMethod]
        public void UpdateItem_ChangesOnlyNameAndPath()
        {
            ConfigService service = CreateService("messy.expected.json");

            service.UpdateItem("i811029a7", "メモ帳2", "C:\\new.exe");

            JsonObject item = (JsonObject)ReadConfig()["items"][0];
            Assert.AreEqual("メモ帳2", (string)item["name"]);
            Assert.AreEqual("C:\\new.exe", (string)item["path"]);
            Assert.AreEqual("📝", (string)item["emoji"]);
            Assert.AreEqual("改行\nあり \"引用\" タブ\t", (string)item["note"]);
        }

        [TestMethod]
        public void UpdateOrDelete_UnknownId_Throws()
        {
            ConfigService service = CreateService("messy.expected.json");

            Assert.ThrowsException<ConfigException>(() => service.UpdateItem("inope", "x", "C:\\x"));
            Assert.ThrowsException<ConfigException>(() => service.DeleteItem("inope"));
        }

        [TestMethod]
        public void DeleteItem_RemovesOnlyThatItem()
        {
            ConfigService service = CreateService("messy.expected.json");
            int before = ReadConfig()["items"].AsArray().Count;

            service.DeleteItem("iaaaa0001");

            JsonArray items = ReadConfig()["items"].AsArray();
            Assert.AreEqual(before - 1, items.Count);
            Assert.IsFalse(items.OfType<JsonObject>().Any(i => (string)i["id"] == "iaaaa0001"));
        }

        [TestMethod]
        public void AddTag_GetsTheNextPaletteColor_AndItsCircle()
        {
            ConfigService service = CreateService("messy.expected.json");

            service.AddTag("新しい", "Games");

            JsonObject tag = (JsonObject)ReadConfig()["tags"]["新しい"];
            Assert.AreEqual("#ff7a6a", (string)tag["color"]); // 使われていない一番前の色
            Assert.AreEqual("Games", (string)tag["circle"]);
            Assert.ThrowsException<ConfigException>(() => service.AddTag("新しい", "Main"));
        }

        [TestMethod]
        public void AddTag_ToANewCircle_PinsTheFirstCircleBeforeAppending()
        {
            ConfigService service = CreateService(null);

            service.AddTag("A", "別");

            JsonObject data = ReadConfig();
            Assert.AreEqual("[\"Launcher\",\"別\"]", JsJson.StringifyCompact(data["circles"]));
        }

        [TestMethod]
        public void RenameTag_KeepsOrderAndColor_AndRenamesItemTags()
        {
            ConfigService service = CreateService("messy.expected.json");
            var keysBefore = ((JsonObject)ReadConfig()["tags"]).Select(p => p.Key).ToList();

            service.RenameTag("ツール", "道具");

            JsonObject data = ReadConfig();
            var keysAfter = ((JsonObject)data["tags"]).Select(p => p.Key).ToList();
            CollectionAssert.AreEqual(keysBefore.Select(k => k == "ツール" ? "道具" : k).ToList(), keysAfter);
            Assert.AreEqual("#3f7fe0", (string)data["tags"]["道具"]["color"]);
            Assert.AreEqual("[\"Web\",\"道具\"]", JsJson.StringifyCompact(data["items"][1]["tags"]));
            Assert.ThrowsException<ConfigException>(() => service.RenameTag("道具", "Web"));
        }

        [TestMethod]
        public void DeleteTag_DeletesItemsOnlyInThatTag_AndMovesTheOthers()
        {
            ConfigService service = CreateService("messy.expected.json");

            var (deleted, moved) = service.DeleteTag("ツール");

            JsonObject data = ReadConfig();
            Assert.AreEqual(1, deleted); // メモ帳(ツールだけ)
            Assert.AreEqual(2, moved); // example.com(Web にも付いている)と、パスの無い項目(項目として数えないので消さない)
            Assert.IsNull(data["tags"]["ツール"]);
            Assert.IsFalse(data["items"].AsArray().OfType<JsonObject>().Any(i => (string)i["name"] == "メモ帳"));
            Assert.AreEqual("[\"Web\"]", JsJson.StringifyCompact(data["items"].AsArray().OfType<JsonObject>().First(i => (string)i["id"] == "iaaaa0001")["tags"]));
        }

        [TestMethod]
        public void VersionToken_ChangesWhenTheFileChanges()
        {
            ConfigService service = CreateService("messy.expected.json");
            string before = service.GetVersionToken();

            service.AddItem("ツール", "x", "C:\\x");

            Assert.AreNotEqual(before, service.GetVersionToken());
        }
    }
}
