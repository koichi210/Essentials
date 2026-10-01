using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using StandardTemplate.Wpf.Tests;

namespace EventRecorderForWpf.Tests
{
    /// <summary>
    /// WinForms版EventRecorderとのデータ互換性のテスト。
    /// WinForms版が書いたプロファイル(*.json)・アプリ設定(EventRecorder.json)をWPF版がそのまま読めること、
    /// WPF版が書いたものも同じキー名・同じ形になることを確認する。
    /// すべて一時フォルダ(%TEMP%)の中だけで行い、本物のユーザーデータフォルダには触れない。
    /// </summary>
    [TestClass]
    public class ProfileCompatibilityTests
    {
        // WinForms版(JsonFileStorage.Save = Newtonsoft.Json・インデント付き)が実際に書き出す形そのまま
        private const String WinFormsProfileJson =
            "{\n" +
            "  \"LoopCount\": \"3\",\n" +
            "  \"Events\": [\n" +
            "    { \"Type\": \"WAIT_MS\", \"X\": \"\", \"Y\": \"\", \"Key\": \"\", \"Wait\": \"250\", \"Remarks\": \"\" },\n" +
            "    { \"Type\": \"LEFT_DOWN\", \"X\": \"638\", \"Y\": \"503\", \"Key\": \"\", \"Wait\": \"0\", \"Remarks\": \"ボタン\" },\n" +
            "    { \"Type\": \"LEFT_UP\", \"X\": \"638\", \"Y\": \"503\", \"Key\": \"\", \"Wait\": \"0\", \"Remarks\": null },\n" +
            "    { \"Type\": \"KEY_DOWN\", \"X\": \"\", \"Y\": \"\", \"Key\": \"A\", \"Wait\": \"0\", \"Remarks\": \"\" },\n" +
            "    { \"Type\": \"KEY_UP\", \"X\": \"\", \"Y\": \"\", \"Key\": \"A\", \"Wait\": \"0\", \"Remarks\": \"\" }\n" +
            "  ],\n" +
            "  \"Playlist\": [\n" +
            "    { \"Enabled\": true, \"FileName\": \"sub\\\\child.json\", \"LoopCount\": \"2\" },\n" +
            "    { \"Enabled\": false, \"FileName\": \"deleted.json\", \"LoopCount\": \"5\" }\n" +
            "  ],\n" +
            "  \"IsRecordMode\": false,\n" +
            "  \"MinimizeOnPlay\": true\n" +
            "}";

        [TestMethod]
        public void WinForms版で保存したプロファイルを起動時に読み込める()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("a_macro.json", WinFormsProfileJson);
                temp.Write(@"sub\child.json", "{ \"LoopCount\": \"7\", \"Events\": [], \"Playlist\": [] }");

                StaRunner.Run(() =>
                {
                    MainWindow window = new MainWindow(temp.Path);

                    // 一覧の先頭(名前順)が起動時に読み込まれる
                    Assert.AreEqual("a_macro.json", window.comboBox_Profile.SelectedItem);
                    Assert.AreEqual("3", window.textBox_Loop.Text);

                    Assert.AreEqual(5, window.eventRows.Count);
                    Assert.AreEqual("250", window.eventRows[0].Detail);
                    Assert.AreEqual("X:638 Y:503", window.eventRows[1].Detail);
                    Assert.AreEqual("ボタン", window.eventRows[1].Remarks);
                    Assert.AreEqual("", window.eventRows[2].Remarks, "nullは空文字として読む(WinForms版のセルと同じ)");
                    Assert.AreEqual("Key:A", window.eventRows[3].Detail);
                    Assert.AreEqual(5, window.dataGrid_Events.Items.Count, "KeyUp行も隠さず表示する(WinForms版 2026-09-28の修正と同じ)");

                    Assert.AreEqual(2, window.playlistRows.Count);
                    Assert.IsTrue(window.playlistRows[0].Enabled);
                    Assert.AreEqual(@"sub\child.json", window.playlistRows[0].FileName);
                    Assert.AreEqual("2", window.playlistRows[0].LoopCount, "読込時はファイル側のループ回数で上書きしない");
                    Assert.AreEqual("5", window.playlistRows[1].LoopCount);
                    CollectionAssert.Contains(window.playlistFileItems, "deleted.json", "消えたファイルも選択肢に残す");
                    CollectionAssert.Contains(window.playlistFileItems, @"sub\child.json");

                    // プロファイルのモードが起動後もそのまま残る(WinForms版 2026-09-28のモード復元の修正と同じ)
                    Assert.IsTrue(window.radioButton_Playback.IsChecked == true);
                    Assert.IsFalse(window.radioButton_Record.IsChecked == true);
                    Assert.IsTrue(window.checkBox_MinimizeOnPlay.IsChecked == true);

                    // 読込直後はCtrl+Zで読込前に戻せない
                    Assert.IsFalse(window.eventsUndo.CanUndo);
                    Assert.IsFalse(window.playlistUndo.CanUndo);
                });
            }
        }

        [TestMethod]
        public void 保存して読み直すと同じ内容に戻り_キー名はWinForms版と同じ()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("source.json", WinFormsProfileJson);
                String savedPath = temp.Combine("saved.json");

                StaRunner.Run(() =>
                {
                    MainWindow writer = new MainWindow(temp.Path);
                    writer.eventRows[1].Remarks = "書き換えた";
                    writer.eventRows.Add(EventRow.FromData("RIGHT_DOWN", "1", "2", "", "0", "追加"));
                    writer.playlistRows[1].Enabled = true;
                    writer.textBox_Loop.Text = "9";
                    writer.radioButton_Record.IsChecked = true;
                    String errorMessage;
                    Assert.IsTrue(writer.SaveProfile(savedPath, out errorMessage), errorMessage);

                    String expected = Newtonsoft.Json.JsonConvert.SerializeObject(writer.BuildProfileFromGrids());

                    MainWindow reader = new MainWindow(temp.Path);
                    reader.LoadProfile(savedPath);
                    String actual = Newtonsoft.Json.JsonConvert.SerializeObject(reader.BuildProfileFromGrids());

                    Assert.AreEqual(expected, actual);
                    Assert.AreEqual("書き換えた", reader.eventRows[1].Remarks);
                    Assert.AreEqual(6, reader.eventRows.Count);
                    Assert.IsTrue(reader.radioButton_Record.IsChecked == true);
                });

                // 保存されたJSONのキー名・型がWinForms版(EventRecorderProfile)と同じであること
                JObject json = JObject.Parse(File.ReadAllText(savedPath));
                CollectionAssert.AreEquivalent(
                    new[] { "LoopCount", "Events", "Playlist", "IsRecordMode", "MinimizeOnPlay" },
                    json.Properties().Select(p => p.Name).ToArray());
                CollectionAssert.AreEquivalent(
                    new[] { "Type", "X", "Y", "Key", "Wait", "Remarks" },
                    ((JObject)json["Events"][0]).Properties().Select(p => p.Name).ToArray());
                CollectionAssert.AreEquivalent(
                    new[] { "Enabled", "FileName", "LoopCount" },
                    ((JObject)json["Playlist"][0]).Properties().Select(p => p.Name).ToArray());
                Assert.AreEqual(JTokenType.String, json["LoopCount"].Type, "ループ回数は文字列で保存(WinForms版と同じ)");
                Assert.AreEqual(JTokenType.Boolean, json["Playlist"][0]["Enabled"].Type);
                Assert.AreEqual("9", (String)json["LoopCount"]);
            }
        }

        [TestMethod]
        public void 旧形式の待機列を持つプロファイルはWAIT_MS行に変換して読む()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("legacy.json",
                    "{ \"LoopCount\": \"1\", \"Events\": [ { \"Type\": \"LEFT_DOWN\", \"X\": \"1\", \"Y\": \"2\", \"Key\": \"\", \"Wait\": \"400\" } ] }");

                StaRunner.Run(() =>
                {
                    MainWindow window = new MainWindow(temp.Path);
                    Assert.AreEqual(2, window.eventRows.Count);
                    Assert.AreEqual("WAIT_MS", window.eventRows[0].Type);
                    Assert.AreEqual("400", window.eventRows[0].Wait);
                    Assert.AreEqual("0", window.eventRows[1].Wait);
                    // プロファイルにIsRecordModeが無ければ既定値(レコード)
                    Assert.IsTrue(window.radioButton_Record.IsChecked == true);
                });
            }
        }

        [TestMethod]
        public void WinForms版のアプリ設定を読み_同じ形で書き戻す()
        {
            using (TempFolder temp = new TempFolder())
            {
                // Keys.F2 = 113、Keys.F3 | Keys.Control = 114 | 0x20000
                temp.Write("EventRecorder.json",
                    "{ \"Width\": 700, \"Height\": 520, \"SplitterDistance\": 250, \"RecordHotkey\": 113, \"PlayHotkey\": " + (114 | 0x20000) + " }");

                StaRunner.Run(() =>
                {
                    MainWindow window = new MainWindow(temp.Path);
                    Assert.AreEqual(700, window.Width);
                    Assert.AreEqual(520, window.Height);
                    Assert.AreEqual(0, window.comboBox_Profile.Items.Count, "EventRecorder.jsonはプロファイル一覧に出さない");

                    // 起動時のホットキーが設定ファイルの値になっている(F2で記録が始まる)
                    window.HandleKeyboardStroke(GlobalHook.KeyboardHook.Stroke.KEY_DOWN, System.Windows.Forms.Keys.F2, System.Windows.Forms.Keys.None);
                    Assert.AreEqual("EventRecorder - 記録中", window.Title);
                    window.HandleKeyboardStroke(GlobalHook.KeyboardHook.Stroke.KEY_UP, System.Windows.Forms.Keys.F2, System.Windows.Forms.Keys.None);
                    window.HandleKeyboardStroke(GlobalHook.KeyboardHook.Stroke.KEY_DOWN, System.Windows.Forms.Keys.F2, System.Windows.Forms.Keys.None);
                    Assert.AreEqual("EventRecorder", window.Title);

                    Assert.IsTrue(window.SaveAppSettings());
                });

                JObject json = JObject.Parse(File.ReadAllText(temp.Combine("EventRecorder.json")));
                CollectionAssert.AreEquivalent(
                    new[] { "Width", "Height", "SplitterDistance", "RecordHotkey", "PlayHotkey" },
                    json.Properties().Select(p => p.Name).ToArray());
                Assert.AreEqual(700, (int)json["Width"]);
                Assert.AreEqual(520, (int)json["Height"]);
                Assert.AreEqual(250, (int)json["SplitterDistance"], "画面を表示していない時は読んだ値をそのまま書き戻す");
                Assert.AreEqual(113, (int)json["RecordHotkey"], "ホットキーは数値(Keys)のまま保存する");
                Assert.AreEqual(114 | 0x20000, (int)json["PlayHotkey"]);
            }
        }

        [TestMethod]
        public void プロファイル一覧は名前順でサブフォルダも含みアプリ設定とXMLは除く()
        {
            using (TempFolder temp = new TempFolder())
            {
                temp.Write("b.json", "{}");
                temp.Write("A.json", "{}");
                temp.Write(@"sub\c.json", "{}");
                temp.Write("EventRecorder.json", "{}");
                temp.Write("old.xml", "<x/>");

                List<String> names = MainWindow.ListProfileNames(temp.Path);

                CollectionAssert.AreEqual(new[] { "A.json", "b.json", @"sub\c.json" }, names);
            }
        }

        [TestMethod]
        public void プロファイルが無い時はレコードモードで空のプレイリスト2行から始まる()
        {
            using (TempFolder temp = new TempFolder())
            {
                StaRunner.Run(() =>
                {
                    MainWindow window = new MainWindow(temp.Path);
                    Assert.IsTrue(window.radioButton_Record.IsChecked == true);
                    Assert.AreEqual(0, window.eventRows.Count);
                    Assert.AreEqual(2, window.playlistRows.Count);
                    Assert.IsFalse(window.playlistRows[0].Enabled);
                    Assert.AreEqual("1", window.playlistRows[0].LoopCount);
                    Assert.AreEqual("EventRecorder", window.Title);
                });
            }
        }
    }
}
