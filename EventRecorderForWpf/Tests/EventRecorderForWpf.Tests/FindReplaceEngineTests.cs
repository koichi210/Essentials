using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorderForWpf.Tests
{
    /// <summary>
    /// 検索/置換(FindReplaceEngine)のテスト。WinForms版FindReplaceFormのロジックを画面なしで確認する
    /// </summary>
    [TestClass]
    public class FindReplaceEngineTests
    {
        private static readonly String[] VisibleColumns = { EventRow.ColType, EventRow.ColDetail, EventRow.ColRemarks };

        private GridRowCollection<EventRow> rows;
        private GridUndoRedo<EventRow> undo;
        private FindReplaceEngine engine;

        [TestInitialize]
        public void SetUp()
        {
            rows = new GridRowCollection<EventRow>
            {
                EventRow.FromData("LEFT_DOWN", "10", "20", "", "0", "ログイン"),   // 0
                EventRow.FromData("WAIT_MS", "", "", "", "100", ""),             // 1
                EventRow.FromData("KEY_DOWN", "", "", "A", "0", "left hand"),     // 2
                EventRow.FromData("LEFT_UP", "10", "20", "", "0", ""),            // 3
            };
            undo = new GridUndoRedo<EventRow>(rows);
            engine = new FindReplaceEngine(
                () => rows.Cast<IEditableGridRow>().ToList(),
                () => VisibleColumns,
                undo.BeginUndoBatch,
                undo.EndUndoBatch);
        }

        [TestMethod]
        public void 検索は大文字小文字を区別せずヒットした行と列を返す()
        {
            List<FindHit> hits = engine.Search("left");

            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, hits.Select(h => h.TargetRowIndex).ToArray());
            CollectionAssert.AreEqual(new[] { 0 }, hits[0].HitVisibleColumnIndexes);
            CollectionAssert.AreEqual(new[] { 2 }, hits[1].HitVisibleColumnIndexes, "備考列でヒット");
            Assert.AreEqual("X:10 Y:20", hits[0].Values[1], "表示列の値(Detail)も一覧に出す");
        }

        [TestMethod]
        public void 大文字小文字を区別する設定ならヒットしない()
        {
            engine.MatchCase = true;
            CollectionAssert.AreEqual(new[] { 2 }, engine.Search("left").Select(h => h.TargetRowIndex).ToArray());
        }

        [TestMethod]
        public void 次を検索は続きから探して末尾で先頭に戻る()
        {
            String status;
            Assert.IsTrue(engine.FindNext("LEFT", out status));
            Assert.AreEqual(0, engine.LastFoundRowIndex);
            Assert.IsTrue(engine.FindNext("LEFT", out status));
            Assert.AreEqual(2, engine.LastFoundRowIndex);
            Assert.AreEqual(2, engine.LastFoundColumnIndex);
            Assert.IsTrue(engine.FindNext("LEFT", out status));
            Assert.AreEqual(3, engine.LastFoundRowIndex);
            Assert.IsTrue(engine.FindNext("LEFT", out status));
            Assert.AreEqual(0, engine.LastFoundRowIndex, "末尾まで行ったら先頭に戻る");

            Assert.IsFalse(engine.FindNext("存在しない", out status));
            Assert.AreEqual("見つからなかったよ", status);
            Assert.IsFalse(engine.FindNext("", out status));
            Assert.AreEqual("検索文字列を入力してね", status);
        }

        [TestMethod]
        public void 置換は直前に見つけたセルだけを置換する()
        {
            String status;
            engine.FindNext("LEFT", out status);
            engine.ReplaceCurrent("LEFT", "RIGHT");

            Assert.AreEqual("RIGHT_DOWN", rows[0].Type);
            Assert.AreEqual("LEFT_UP", rows[3].Type);
        }

        [TestMethod]
        public void すべて置換は件数を返し1回のUndoで全部戻る()
        {
            int count = engine.ReplaceAll("left", "RIGHT");

            Assert.AreEqual(3, count);
            Assert.AreEqual("RIGHT_DOWN", rows[0].Type);
            Assert.AreEqual("RIGHT hand", rows[2].Remarks);
            Assert.AreEqual("RIGHT_UP", rows[3].Type);

            undo.Undo();
            Assert.AreEqual("LEFT_DOWN", rows[0].Type);
            Assert.AreEqual("left hand", rows[2].Remarks);
            Assert.AreEqual("LEFT_UP", rows[3].Type);
            Assert.IsFalse(undo.CanUndo);
        }

        [TestMethod]
        public void Detail列の置換は非表示の実データにも反映される()
        {
            engine.ReplaceAll("X:10", "X:99");

            Assert.AreEqual("99", rows[0].X);
            Assert.AreEqual("20", rows[0].Y);
        }

        [TestMethod]
        public void 全置換の比較方法は設定に従う()
        {
            Assert.AreEqual("xBxb", FindReplaceEngine.ReplaceAllOccurrences("aBab", "a", "x", StringComparison.Ordinal));
            Assert.AreEqual("axax", FindReplaceEngine.ReplaceAllOccurrences("aBab", "b", "x", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("aBax", FindReplaceEngine.ReplaceAllOccurrences("aBab", "b", "x", StringComparison.Ordinal));
        }
    }
}
