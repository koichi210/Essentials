using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorderForWpf.Tests
{
    /// <summary>
    /// GridUndoRedo(WinForms版DataGridViewExのCtrl+Z/Ctrl+Y相当)のテスト。画面は使わず行データだけで確認する。
    /// </summary>
    [TestClass]
    public class GridUndoRedoTests
    {
        [TestMethod]
        public void 値の変更を元に戻してやり直せる()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", "") };
            GridUndoRedo<EventRow> undo = new GridUndoRedo<EventRow>(rows);

            rows[0].Remarks = "メモ";
            Assert.IsTrue(undo.CanUndo);

            undo.Undo();
            Assert.AreEqual("", rows[0].Remarks);
            Assert.IsTrue(undo.CanRedo);

            undo.Redo();
            Assert.AreEqual("メモ", rows[0].Remarks);
        }

        [TestMethod]
        public void Detailの編集で連動して変わった非表示の列も1回で元に戻る()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", "") };
            GridUndoRedo<EventRow> undo = new GridUndoRedo<EventRow>(rows);

            rows[0].Detail = "X:30 Y:40";
            Assert.AreEqual("30", rows[0].X);

            undo.Undo();

            Assert.AreEqual("X:1 Y:2", rows[0].Detail);
            Assert.AreEqual("1", rows[0].X);
            Assert.AreEqual("2", rows[0].Y);
            Assert.IsFalse(undo.CanUndo, "1回のUndoでまとめて戻るはず");
        }

        [TestMethod]
        public void バッチでまとめた複数の変更は1回で戻る()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { new EventRow(), new EventRow() };
            GridUndoRedo<EventRow> undo = new GridUndoRedo<EventRow>(rows);

            undo.BeginUndoBatch();
            rows[0].Remarks = "a";
            rows[1].Remarks = "b";
            undo.EndUndoBatch();

            undo.Undo();
            Assert.AreEqual("", rows[0].Remarks);
            Assert.AreEqual("", rows[1].Remarks);
            Assert.IsFalse(undo.CanUndo);
        }

        [TestMethod]
        public void 新しい変更が入るとRedo履歴は消える()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { new EventRow() };
            GridUndoRedo<EventRow> undo = new GridUndoRedo<EventRow>(rows);

            rows[0].Remarks = "a";
            undo.Undo();
            rows[0].Remarks = "b";

            Assert.IsFalse(undo.CanRedo);
        }

        [TestMethod]
        public void 行を削除すると履歴はクリアされる_追加ではクリアされない()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { new EventRow(), new EventRow() };
            GridUndoRedo<EventRow> undo = new GridUndoRedo<EventRow>(rows);

            rows[0].Remarks = "a";
            rows.Add(new EventRow());
            Assert.IsTrue(undo.CanUndo, "行の追加では履歴を消さない(DataGridViewExと同じ)");

            rows.RemoveAt(1);
            Assert.IsFalse(undo.CanUndo, "行の削除で履歴はクリアされる(DataGridViewExと同じ)");
        }

        [TestMethod]
        public void 行番号は挿入や削除のたびに振り直される()
        {
            GridRowCollection<EventRow> rows = new GridRowCollection<EventRow> { new EventRow(), new EventRow(), new EventRow() };
            EventRow inserted = new EventRow();

            rows.Insert(1, inserted);
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual(i, rows[i].RowNumber);
            }

            rows.RemoveAt(0);
            Assert.AreEqual(0, inserted.RowNumber);
            Assert.AreEqual(2, rows[2].RowNumber);
        }

        [TestMethod]
        public void プレイリストの行もUndoできチェック列は文字列でも読み書きできる()
        {
            GridRowCollection<PlaylistRow> rows = new GridRowCollection<PlaylistRow> { PlaylistRow.FromData(true, "a.json", "3") };
            GridUndoRedo<PlaylistRow> undo = new GridUndoRedo<PlaylistRow>(rows);

            // Deleteキーでのクリア(null)はチェックOFF扱い
            rows[0].SetCell(PlaylistRow.ColEnabled, null);
            rows[0].SetCell(PlaylistRow.ColLoopCount, null);
            Assert.IsFalse(rows[0].Enabled);
            Assert.AreEqual("", rows[0].LoopCount);

            undo.Undo();
            Assert.AreEqual("3", rows[0].LoopCount);
            Assert.IsFalse(rows[0].Enabled, "Undoは1回に1つずつ(バッチにしていないので)");
            undo.Undo();
            Assert.IsTrue(rows[0].Enabled);
            Assert.AreEqual("True", rows[0].GetCell(PlaylistRow.ColEnabled));
        }
    }
}
