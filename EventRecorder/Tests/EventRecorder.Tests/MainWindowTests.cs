using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;
using Keys = System.Windows.Forms.Keys;
using Stroke = GlobalHook.KeyboardHook.Stroke;

namespace EventRecorder.Tests
{
    /// <summary>
    /// MainWindowの編集操作(貼り付け・Deleteでのクリア・Undo/Redo・行削除)・記録のキー処理・プレイリスト操作のテスト。
    ///
    /// ⚠️ テスト用のコンストラクタ(MainWindow(データフォルダ))はグローバルフックを張らない。
    /// 記録開始(ToggleRecording)を呼んでもマウスフックは張られず、再生(SendInput)は一切呼ばない。
    /// 記録のキー処理はフックのコールバックの中身(HandleKeyboardStroke)を直接呼んで確かめる。
    /// </summary>
    [TestClass]
    public class MainWindowTests
    {
        private TempFolder _temp;

        [TestInitialize]
        public void SetUp()
        {
            _temp = new TempFolder();
        }

        [TestCleanup]
        public void TearDown()
        {
            _temp.Dispose();
        }

        [TestMethod]
        public void 貼り付けは表示列だけに入り行が足りなければ追加し1回のUndoで戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.EventRows.Add(EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", "元の備考"));

                // Event / Detail / 備考 / (はみ出る4列目は切り捨て)
                window.PasteText("KEY_DOWN\tKey:B\tメモ\tはみ出し\r\nLEFT_UP\tX:10 Y:20\r\n", 0, window.col_Type);

                Assert.AreEqual(2, window.EventRows.Count, "足りない行は追加される");
                Assert.AreEqual("KEY_DOWN", window.EventRows[0].Type);
                Assert.AreEqual("B", window.EventRows[0].Key, "Detail列に貼った値は非表示のKeyに書き戻される");
                Assert.AreEqual("", window.EventRows[0].X);
                Assert.AreEqual("メモ", window.EventRows[0].Remarks);
                Assert.AreEqual("LEFT_UP", window.EventRows[1].Type);
                Assert.AreEqual("10", window.EventRows[1].X);
                Assert.AreEqual("20", window.EventRows[1].Y);

                window.EventsUndo.Undo();
                Assert.AreEqual("LEFT_DOWN", window.EventRows[0].Type);
                Assert.AreEqual("1", window.EventRows[0].X);
                Assert.AreEqual("", window.EventRows[0].Key);
                Assert.AreEqual("元の備考", window.EventRows[0].Remarks);
                Assert.AreEqual("", window.EventRows[1].Type, "追加された行の値も戻る(行そのものは残る。WinForms版と同じ)");

                window.EventsUndo.Redo();
                Assert.AreEqual("KEY_DOWN", window.EventRows[0].Type);
                Assert.AreEqual("LEFT_UP", window.EventRows[1].Type);
            });
        }

        [TestMethod]
        public void 貼り付けは開始列から右へ入る()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.EventRows.Add(EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", ""));

                window.PasteText("X:5 Y:6\t備考だけ", 0, window.col_Detail);

                Assert.AreEqual("LEFT_DOWN", window.EventRows[0].Type, "開始列より左は変えない");
                Assert.AreEqual("5", window.EventRows[0].X);
                Assert.AreEqual("備考だけ", window.EventRows[0].Remarks);
            });
        }

        [TestMethod]
        public void Deleteキー相当で選択セルが空になり1回のUndoで戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.EventRows.Add(EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", "a"));
                window.EventRows.Add(EventRow.FromData("LEFT_UP", "1", "2", "", "0", "b"));

                window.ClearCells(new[]
                {
                    new DataGridCellInfo(window.EventRows[0], window.col_Remarks),
                    new DataGridCellInfo(window.EventRows[1], window.col_Detail),
                }, window.EventsUndo);

                Assert.AreEqual("", window.EventRows[0].Remarks);
                Assert.AreEqual("", window.EventRows[1].X, "Detailを空にすると非表示のX/Yも空になる");
                Assert.AreEqual("LEFT_DOWN", window.EventRows[0].Type, "選択していないセルは変えない");

                window.EventsUndo.Undo();
                Assert.AreEqual("a", window.EventRows[0].Remarks);
                Assert.AreEqual("1", window.EventRows[1].X);
                Assert.AreEqual("X:1 Y:2", window.EventRows[1].Detail);
            });
        }

        [TestMethod]
        public void プレイリストのDeleteキー相当ではファイル列はクリアしない()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                PlaylistRow row = window.PlaylistRows[0];
                row.FileName = "x.json";
                row.Enabled = true;

                window.ClearCells(new[]
                {
                    new DataGridCellInfo(row, window.col_PlaylistEnabled),
                    new DataGridCellInfo(row, window.col_PlaylistFile),
                    new DataGridCellInfo(row, window.col_PlaylistLoopCount),
                }, window.PlaylistUndo);

                Assert.IsFalse(row.Enabled);
                Assert.AreEqual("x.json", row.FileName);
                Assert.AreEqual("", row.LoopCount);
            });
        }

        [TestMethod]
        public void KeyUp行は表示したまま_KeyDown行と一緒に削除される()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.EventRows.Add(EventRow.FromData("KEY_DOWN", "", "", "A", "0", ""));
                window.EventRows.Add(EventRow.FromData("WAIT_MS", "", "", "", "50", ""));
                window.EventRows.Add(EventRow.FromData("KEY_UP", "", "", "A", "0", ""));
                window.EventRows.Add(EventRow.FromData("LEFT_DOWN", "1", "1", "", "0", ""));

                // KeyUp行もグリッドにそのまま出ている(隠していない)
                Assert.AreEqual(4, window.dataGrid_Events.Items.Count);
                Assert.IsTrue(window.dataGrid_Events.Items.Contains(window.EventRows[2]));

                window.DeleteEventRows(new List<int> { 0 });

                CollectionAssert.AreEqual(new[] { "WAIT_MS", "LEFT_DOWN" }, window.EventRows.Select(r => r.Type).ToArray());
                Assert.AreEqual(0, window.EventRows[0].RowNumber, "行番号は振り直される");
                Assert.AreEqual(1, window.EventRows[1].RowNumber);
            });
        }

        [TestMethod]
        public void 記録はホットキーで開始停止しキーリピートを抑制しホットキー自体は記録しない()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);

                // 記録していない時のキー操作は何も記録しない
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.Z, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.Z, Keys.None);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);   // 記録開始(既定のホットキー)
                Assert.AreEqual("EventRecorder - 記録中", window.Title);
                Assert.AreEqual("記録中…", window.button_Record.Content);
                Assert.IsFalse(GlobalHook.MouseHook.IsHooking, "テストではマウスフックを張らない");
                Assert.IsFalse(GlobalHook.KeyboardHook.IsHooking, "テストではキーボードフックを張らない");

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);   // F1のリピートは無視
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F1, Keys.None);     // ホットキーのUpは消費
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.A, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.A, Keys.None);    // OSのキーリピート
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.A, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.A, Keys.None);
                window.HandleKeyboardStroke(Stroke.SYSKEY_DOWN, Keys.Menu, Keys.Alt);
                window.HandleKeyboardStroke(Stroke.SYSKEY_UP, Keys.Menu, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F1, Keys.None);   // 記録停止
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F1, Keys.None);

                Assert.AreEqual("EventRecorder", window.Title);
                Assert.AreEqual("記録", window.button_Record.Content);

                String[] recorded = window.EventRows
                    .Where(r => r.Type != EventRules.WaitEventType)
                    .Select(r => r.Type + ":" + r.Key)
                    .ToArray();
                CollectionAssert.AreEqual(new[] { "KEY_DOWN:A", "KEY_UP:A", "SYSKEY_DOWN:Menu", "SYSKEY_UP:Menu" }, recorded);
                Assert.IsTrue(window.EventRows.Last().IsHighlighted, "記録した最新行はハイライトされる");
            });
        }

        [TestMethod]
        public void 修飾キー付きのホットキーは同じ組み合わせの時だけ反応する()
        {
            _temp.Write("EventRecorder.json", "{ \"RecordHotkey\": " + (int)(Keys.F5 | Keys.Control | Keys.Shift) + " }");

            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F5, Keys.Control);
                Assert.AreEqual("EventRecorder", window.Title, "Shiftが足りないので反応しない");
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F5, Keys.None);

                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F5, Keys.Control | Keys.Shift);
                Assert.AreEqual("EventRecorder - 記録中", window.Title);
                window.HandleKeyboardStroke(Stroke.KEY_UP, Keys.F5, Keys.None);
                window.HandleKeyboardStroke(Stroke.KEY_DOWN, Keys.F5, Keys.Control | Keys.Shift);
                Assert.AreEqual("EventRecorder", window.Title);
            });
        }

        [TestMethod]
        public void WAIT時間の一括変更は1回のUndoで戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.EventRows.Add(EventRow.FromData("WAIT_MS", "", "", "", "10", ""));
                window.EventRows.Add(EventRow.FromData("LEFT_UP", "1", "1", "", "0", ""));
                window.EventRows.Add(EventRow.FromData("WAIT_MS", "", "", "", "20", ""));
                window.EventRows.Add(EventRow.FromData("RIGHT_UP", "1", "1", "", "0", ""));

                window.BulkChangeEventWait("LEFT_UP", 300);
                Assert.AreEqual("300", window.EventRows[0].Wait);
                Assert.AreEqual("20", window.EventRows[2].Wait, "対象はLEFT_UPだけなので、RIGHT_UP直前のWAITは変わらない");

                window.EventsUndo.Undo();
                Assert.AreEqual("10", window.EventRows[0].Wait);
                Assert.AreEqual("20", window.EventRows[2].Wait);
                Assert.IsFalse(window.EventsUndo.CanUndo);
            });
        }

        [TestMethod]
        public void プレイリストで設定ファイルを選ぶとそのファイルのループ回数が入る()
        {
            _temp.Write("loop5.json", "{ \"LoopCount\": \"5\", \"Events\": [], \"Playlist\": [] }");

            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                // 起動時にloop5.jsonが読み込まれ、その中身(空のプレイリスト)なので空行2行が用意される
                Assert.AreEqual(2, window.PlaylistRows.Count);
                CollectionAssert.AreEqual(new[] { "loop5.json" }, window.PlaylistFileItems.ToArray());

                window.PlaylistRows[0].FileName = "loop5.json";
                Assert.AreEqual("5", window.PlaylistRows[0].LoopCount);

                // ファイル選択と、それに連動したループ回数の自動セットは1回のUndoでまとめて戻る
                window.PlaylistUndo.Undo();
                Assert.AreEqual("", window.PlaylistRows[0].FileName);
                Assert.AreEqual("1", window.PlaylistRows[0].LoopCount);
                Assert.IsFalse(window.PlaylistUndo.CanUndo);
            });
        }

        [TestMethod]
        public void プレイリストを更新すると増えたファイルだけ末尾に追加される()
        {
            _temp.Write("a.json", "{ \"LoopCount\": \"4\", \"Events\": [], \"Playlist\": [ { \"Enabled\": false, \"FileName\": \"a.json\", \"LoopCount\": \"9\" } ] }");
            _temp.Write("b.json", "{ \"LoopCount\": \"6\", \"Events\": [], \"Playlist\": [] }");

            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                Assert.AreEqual(1, window.PlaylistRows.Count);

                window.RefreshPlaylist();

                Assert.AreEqual(2, window.PlaylistRows.Count);
                Assert.AreEqual("a.json", window.PlaylistRows[0].FileName);
                Assert.AreEqual("9", window.PlaylistRows[0].LoopCount, "既存の行の設定はそのまま");
                Assert.IsFalse(window.PlaylistRows[0].Enabled);
                Assert.AreEqual("b.json", window.PlaylistRows[1].FileName);
                Assert.IsTrue(window.PlaylistRows[1].Enabled, "追加した行は実行チェックON");
                Assert.AreEqual("6", window.PlaylistRows[1].LoopCount, "ファイルに保存されたループ回数が入る");
            });
        }

        [TestMethod]
        public void プレイリストの実行対象はチェックONかつファイル指定ありの行だけ()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.PlaylistRows.Clear();
                window.PlaylistRows.Add(PlaylistRow.FromData(true, "a.json", "3"));
                window.PlaylistRows.Add(PlaylistRow.FromData(false, "b.json", "3"));
                window.PlaylistRows.Add(PlaylistRow.FromData(true, "", "3"));
                window.PlaylistRows.Add(PlaylistRow.FromData(true, "c.json", "0"));

                List<MainWindow.PlaylistEntry> entries = window.GetPlaylistEntries();

                CollectionAssert.AreEqual(new[] { "a.json", "c.json" }, entries.Select(e => e.FileName).ToArray());
                CollectionAssert.AreEqual(new[] { 3, 1 }, entries.Select(e => e.LoopCount).ToArray(), "0以下は1回扱い");
                CollectionAssert.AreEqual(new[] { 0, 3 }, entries.Select(e => e.RowIndex).ToArray());
            });
        }

        [TestMethod]
        public void プレイリストの行の並べ替え_フィルタ_ファイル不在表示()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow(_temp.Path);
                window.PlaylistRows.Clear();
                window.AddPlaylistRow(0, isEnabled: true);
                window.AddPlaylistRow(1, isEnabled: false);
                window.AddPlaylistRow(2, isEnabled: true);
                window.PlaylistRows[0].FileName = "first.json";
                window.PlaylistRows[1].FileName = "second.json";
                window.PlaylistRows[2].FileName = "third.json";

                // ドラッグ&ドロップ: 0行目を2行目の位置へ
                window.MovePlaylistRow(0, 2);
                CollectionAssert.AreEqual(new[] { "second.json", "third.json", "first.json" }, window.PlaylistRows.Select(r => r.FileName).ToArray());
                Assert.AreEqual(2, window.PlaylistRows[2].RowNumber);

                // チェックONのみ表示
                window.ShowOnlyCheckedPlaylistRows = true;
                Assert.IsFalse(window.PlaylistRows[0].IsVisible);
                Assert.IsTrue(window.PlaylistRows[1].IsVisible);
                window.PlaylistRows[0].Enabled = true;
                Assert.IsTrue(window.PlaylistRows[0].IsVisible, "チェックを付けたらその場で表示される");
                window.ShowOnlyCheckedPlaylistRows = false;

                // ファイル存在チェックの結果の反映
                window.ApplyPlaylistMissingFileHighlights(new Dictionary<String, Boolean>
                {
                    { "second.json", false },
                    { "third.json", true },
                    { "first.json", true },
                });
                Assert.IsTrue(window.PlaylistRows[0].IsFileMissing);
                StringAssert.Contains(window.PlaylistRows[0].MissingFileText, "second.json");
                Assert.IsFalse(window.PlaylistRows[1].IsFileMissing);

                Assert.AreEqual(1, MainWindow.StepLoopCount(0, -1), "ループ数は1未満にならない");
                Assert.AreEqual(3, MainWindow.StepLoopCount(2, 1));
            });
        }
    }
}
