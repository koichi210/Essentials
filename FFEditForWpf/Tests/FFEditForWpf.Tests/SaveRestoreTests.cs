using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// MainWindowの設定保存/読み込み(WpfSaveRestore＋WinForms版SaveRestore.csのLoadProc/SaveSetting相当)のテスト。
    /// FFEdit の保存対象は、コンボボックスの入力履歴3つとテキストボックス1つだけとシンプル。
    /// WinForms版FFEditで保存したJSONをそのまま読めることも確認する。
    /// </summary>
    [TestClass]
    public class SaveRestoreTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditForWpfSaveRestoreTests_" + Guid.NewGuid().ToString("N"));
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
            return Path.Combine(tempDirectory, name + ".json");
        }

        [TestMethod]
        public void 拡張子の指定が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                var writer = new MainWindow();
                writer.textBox_Target_Extension.Text = "*.png";

                string path = PathFor("ext");
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));
                Assert.AreEqual("*.png", reader.textBox_Target_Extension.Text);
            });
        }

        [TestMethod]
        public void コンボボックスの入力履歴が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                var writer = new MainWindow();
                writer.comboBox_TargetDir.Items.Clear();
                writer.comboBox_TargetDir.Items.Add(@"C:\work");
                writer.comboBox_TargetDir.Items.Add(@"D:\data");
                writer.comboBox_String1.Items.Add("foo");
                writer.comboBox_String2.Items.Add("bar");

                string path = PathFor("history");
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));

                Assert.AreEqual(2, reader.comboBox_TargetDir.Items.Count);
                Assert.AreEqual(@"C:\work", reader.comboBox_TargetDir.Items[0]);
                Assert.AreEqual(@"D:\data", reader.comboBox_TargetDir.Items[1]);
                CollectionAssert.Contains(reader.comboBox_String1.Items, "foo");
                CollectionAssert.Contains(reader.comboBox_String2.Items, "bar");
            });
        }

        [TestMethod]
        public void 拡張子は未指定なら既定値のアスタリスクになる()
        {
            StaRunner.Run(() =>
            {
                // 既定値は "*"。何も設定せず保存・読み込みしても既定値のまま
                var writer = new MainWindow();
                string path = PathFor("default_ext");
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new MainWindow();
                reader.textBox_Target_Extension.Text = "書き換え";
                Assert.IsTrue(reader.LoadProc(path));

                Assert.AreEqual("*", reader.textBox_Target_Extension.Text);
            });
        }

        [TestMethod]
        public void LoadProcはファイル名が空なら何もせず失敗を返す()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_Target_Extension.Text = "そのまま";

                Assert.IsFalse(window.LoadProc(""));
                Assert.AreEqual("そのまま", window.textBox_Target_Extension.Text);
            });
        }

        [TestMethod]
        public void LoadProcはファイルが無ければ既定値に戻して失敗を返す()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_Target_Extension.Text = "*.txt";
                window.comboBox_String1.Items.Add("消える履歴");

                Assert.IsFalse(window.LoadProc(PathFor("nothing")));
                Assert.AreEqual("*", window.textBox_Target_Extension.Text);
                Assert.AreEqual(0, window.comboBox_String1.Items.Count);
            });
        }

        [TestMethod]
        public void SaveSettingは今入力中の文字列を履歴に追加してから重複を整理する()
        {
            // ModifyCombBoxList は Items を無条件に整理するわけではない。
            // ComboBox.Text（今まさに入力/選択されている値）が空ならそのまま何もせず戻り、
            // 空でなければ Text を Items に追加したうえで重複を取り除く、という動き。
            StaRunner.Run(() =>
            {
                var writer = new MainWindow();
                writer.comboBox_String1.Items.Add("dup");
                writer.comboBox_String1.Text = "dup"; // 今回も同じ値を使った、という体にする

                string path = PathFor("dedup");
                Assert.IsTrue(writer.SaveSetting(path));
                Assert.AreEqual("dup", writer.comboBox_String1.Text, "履歴を整理しても入力中の文字列は消えない");

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));
                Assert.AreEqual(1, reader.comboBox_String1.Items.Count, "Text分と重複するので1件に整理される");
            });
        }

        [TestMethod]
        public void SaveSettingは入力中の新しい文字列を履歴の末尾に追加する()
        {
            StaRunner.Run(() =>
            {
                var writer = new MainWindow();
                writer.comboBox_TargetDir.Items.Clear();
                writer.comboBox_TargetDir.Items.Add(@"C:\old");
                writer.comboBox_TargetDir.Text = @"C:\new";

                string path = PathFor("append");
                Assert.IsTrue(writer.SaveSetting(path));

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));
                Assert.AreEqual(2, reader.comboBox_TargetDir.Items.Count);
                Assert.AreEqual(@"C:\old", reader.comboBox_TargetDir.Items[0]);
                Assert.AreEqual(@"C:\new", reader.comboBox_TargetDir.Items[1]);
            });
        }

        [TestMethod]
        public void SaveSettingは入力欄が空だと履歴を整理しない()
        {
            StaRunner.Run(() =>
            {
                var writer = new MainWindow();
                writer.comboBox_String1.Items.Add("a");
                writer.comboBox_String1.Items.Add("a");
                // Text は空のまま（何も入力/選択していない）

                string path = PathFor("no_text");
                Assert.IsTrue(writer.SaveSetting(path));

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));
                Assert.AreEqual(2, reader.comboBox_String1.Items.Count,
                    "Text が空だと ModifyCombBoxList は即 return するので重複はそのまま残る");
            });
        }

        [TestMethod]
        public void SaveSettingはファイル名が空なら保存せず失敗を返す()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                Assert.IsFalse(window.SaveSetting(""));
            });
        }

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            string path = Path.Combine(tempDirectory, "FFEdit.json");
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" +
                "    \"textBox_Target_Extension|Value\": \"*.jpg\"\n" +
                "  },\n" +
                "  \"Lists\": {\n" +
                "    \"comboBox_TargetDir|Value_\": [\n" +
                "      \"C:\\\\photo\",\n" +
                "      \"D:\\\\写真\\\\2024\"\n" +
                "    ],\n" +
                "    \"comboBox_String1|Value_\": [\n" +
                "      \"IMG_\"\n" +
                "    ],\n" +
                "    \"comboBox_String2|Value_\": []\n" +
                "  },\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {}\n" +
                "}", new UTF8Encoding(false));

            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.comboBox_String2.Items.Add("消える履歴");
                Assert.IsTrue(window.LoadProc(path));

                Assert.AreEqual("*.jpg", window.textBox_Target_Extension.Text);
                Assert.AreEqual(2, window.comboBox_TargetDir.Items.Count);
                Assert.AreEqual(@"C:\photo", window.comboBox_TargetDir.Items[0]);
                Assert.AreEqual(@"D:\写真\2024", window.comboBox_TargetDir.Items[1]);
                Assert.AreEqual(1, window.comboBox_String1.Items.Count);
                Assert.AreEqual("IMG_", window.comboBox_String1.Items[0]);
                Assert.AreEqual(0, window.comboBox_String2.Items.Count);
            });
        }

        [TestMethod]
        public void 保存したJSONのキーはWinForms版と同じ()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_Target_Extension.Text = "*.txt";
                window.comboBox_TargetDir.Items.Clear();
                window.comboBox_TargetDir.Items.Add(@"C:\work");

                string path = PathFor("keys");
                Assert.IsTrue(window.SaveRestore.Save(path));

                string json = File.ReadAllText(path, Encoding.UTF8);
                StringAssert.Contains(json, "\"textBox_Target_Extension|Value\"");
                StringAssert.Contains(json, "\"comboBox_TargetDir|Value_\"");
                StringAssert.Contains(json, "\"comboBox_String1|Value_\"");
                StringAssert.Contains(json, "\"comboBox_String2|Value_\"");
            });
        }
    }
}
