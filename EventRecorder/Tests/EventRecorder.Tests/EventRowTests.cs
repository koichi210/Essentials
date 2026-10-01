using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorder.Tests
{
    /// <summary>
    /// レコード表の1行(EventRow)と記録データの解釈ルール(EventRules)のテスト。
    /// WinForms版ではDataGridViewのCellValueChanged/CellFormattingでやっていた処理
    /// (Detail列と非表示のX/Y/Key/Waitの同期・警告表示)が、WPF版でも同じ結果になることを確認する。
    /// </summary>
    [TestClass]
    public class EventRowTests
    {
        [TestMethod]
        public void キーボード行はDetailがKey表記になる()
        {
            EventRow row = EventRow.FromData("KEY_DOWN", "", "", "A", "0", "");
            Assert.AreEqual("Key:A", row.Detail);
            Assert.IsFalse(row.HasDetailProblem);
        }

        [TestMethod]
        public void WAIT_MS行はDetailが待機ms()
        {
            EventRow row = EventRow.FromData("WAIT_MS", "", "", "", "250", "");
            Assert.AreEqual("250", row.Detail);
        }

        [TestMethod]
        public void Detailを書き換えるとX_Y_Keyに書き戻される()
        {
            EventRow row = EventRow.FromData("LEFT_DOWN", "1", "2", "", "0", "");

            row.Detail = "X:100 Y:200";

            Assert.AreEqual("100", row.X);
            Assert.AreEqual("200", row.Y);
            Assert.AreEqual("", row.Key);
        }

        [TestMethod]
        public void WAIT_MS行のDetailを書き換えるとWaitに書き戻され旧表記のmsも読める()
        {
            EventRow row = EventRow.FromData("WAIT_MS", "", "", "", "100", "");

            row.Detail = "500ms";

            Assert.AreEqual("500", row.Wait);
        }

        [TestMethod]
        public void Typeを書き換えるとDetailが作り直される()
        {
            EventRow row = EventRow.FromData("LEFT_DOWN", "10", "20", "", "300", "");
            Assert.AreEqual("X:10 Y:20", row.Detail);

            row.Type = "WAIT_MS";

            Assert.AreEqual("300", row.Detail);
        }

        [TestMethod]
        public void マウス行にKeyがあると警告になる()
        {
            EventRow row = EventRow.FromData("LEFT_DOWN", "10", "20", "A", "0", "");

            Assert.IsTrue(row.HasDetailProblem);
            StringAssert.Contains(row.DetailProblemText, "Key(A)は再生時には使われず無視される");
        }

        [TestMethod]
        public void 再生できない値はエラーになる()
        {
            String message;
            Assert.IsTrue(EventRow.FromData("LEFT_DOWN", "abc", "20", "", "0", "").IsInvalidForPlayback(out message));
            StringAssert.Contains(message, "X/Y(abc, 20)");

            Assert.IsTrue(EventRow.FromData("WAIT_MS", "", "", "", "-5", "").IsInvalidForPlayback(out message));
            Assert.IsTrue(EventRow.FromData("KEY_DOWN", "", "", "NoSuchKey", "0", "").IsInvalidForPlayback(out message));

            // 認識できないEvent種別は再生時に静かにスキップされるだけなので、エラーにはしない(WinForms版と同じ)
            Assert.IsFalse(EventRow.FromData("TYPO", "", "", "", "", "").IsInvalidForPlayback(out message));
        }

        [TestMethod]
        public void 旧形式のWait列はWAIT_MS行に切り出され旧表記WAITはWAIT_MSに揃う()
        {
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("LEFT_DOWN", "1", "2", "", "150", ""),
                EventRow.FromData("WAIT", "", "", "", "300", ""),
                EventRow.FromData("LEFT_UP", "1", "2", "", "0", ""),
            };

            EventRules.MigrateWaitColumnToRows(rows);

            CollectionAssert.AreEqual(new[] { "WAIT_MS", "LEFT_DOWN", "WAIT_MS", "LEFT_UP" }, rows.Select(r => r.Type).ToArray());
            Assert.AreEqual("150", rows[0].Wait);
            Assert.AreEqual("0", rows[1].Wait, "移し替えた元の行のWaitは0になる(二重に待たないように)");
            Assert.AreEqual("300", rows[2].Wait);

            // 何度呼んでも変わらない
            EventRules.MigrateWaitColumnToRows(rows);
            Assert.AreEqual(4, rows.Count);
        }

        [TestMethod]
        public void KeyDown行を消すと対応するKeyUp行も一緒に消える()
        {
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("KEY_DOWN", "", "", "A", "0", ""),   // 0
                EventRow.FromData("KEY_DOWN", "", "", "B", "0", ""),   // 1
                EventRow.FromData("KEY_UP", "", "", "B", "0", ""),     // 2
                EventRow.FromData("KEY_UP", "", "", "A", "0", ""),     // 3
                EventRow.FromData("SYSKEY_DOWN", "", "", "F4", "0", ""), // 4
                EventRow.FromData("SYSKEY_UP", "", "", "F4", "0", ""),   // 5
            };

            CollectionAssert.AreEqual(new[] { 3, 0 }, EventRules.CollectRowsToDelete(rows, new[] { 0 }));
            CollectionAssert.AreEqual(new[] { 5, 4 }, EventRules.CollectRowsToDelete(rows, new[] { 4 }));
            // KeyUp行だけを消す場合は、ペアのKeyDown行は残す
            CollectionAssert.AreEqual(new[] { 2 }, EventRules.CollectRowsToDelete(rows, new[] { 2 }));
        }

        [TestMethod]
        public void MOUSE_UP時間の一括変更は直前がWAIT_MS行の時だけ変える()
        {
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("WAIT_MS", "", "", "", "10", ""),
                EventRow.FromData("LEFT_UP", "1", "1", "", "0", ""),
                EventRow.FromData("WAIT_MS", "", "", "", "20", ""),
                EventRow.FromData("LEFT_DOWN", "1", "1", "", "0", ""),
                EventRow.FromData("RIGHT_UP", "1", "1", "", "0", ""),
                EventRow.FromData("WAIT_MS", "", "", "", "30", ""),
                EventRow.FromData("RIGHT_UP", "1", "1", "", "0", ""),
            };

            EventRules.BulkChangeMouseUpWait(rows, 77);

            Assert.AreEqual("77", rows[0].Wait);
            Assert.AreEqual("20", rows[2].Wait, "直後がLEFT_DOWNなので変えない");
            Assert.AreEqual("77", rows[5].Wait);
            Assert.AreEqual("77", rows[5].Detail, "Detail表示も追従する");
        }
    }
}
