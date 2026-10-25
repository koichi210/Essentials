using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Keys = System.Windows.Forms.Keys;

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

        private static String[] TypeAndKeys(List<EventRow> rows)
        {
            return rows.Select(r => r.Type + ":" + r.Key).ToArray();
        }

        [TestMethod]
        public void 記録停止時_末尾に残った修飾キーのDownだけの行は消す()
        {
            // Ctrl+Shift+F5で記録を止めた時: CtrlとShiftのDownだけが記録され、Upは停止後なので記録されない
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("KEY_DOWN", "", "", "A", "0", ""),
                EventRow.FromData("KEY_UP", "", "", "A", "0", ""),
                EventRow.FromData("WAIT_MS", "", "", "", "300", ""),
                EventRow.FromData("KEY_DOWN", "", "", "LControlKey", "0", ""),
                EventRow.FromData("WAIT_MS", "", "", "", "100", ""),
                EventRow.FromData("KEY_DOWN", "", "", "LShiftKey", "0", ""),
            };

            Assert.IsTrue(EventRules.ReleaseUnpairedModifierKeys(rows, new[] { Keys.LControlKey, Keys.LShiftKey }));

            CollectionAssert.AreEqual(new[] { "KEY_DOWN:A", "KEY_UP:A", "WAIT_MS:", "WAIT_MS:" }, TypeAndKeys(rows));
        }

        [TestMethod]
        public void 記録停止時_修飾キーを押したまま他の操作をしていたら末尾にUpを足す()
        {
            // Ctrlを押したままクリックしてから、Ctrl+F5で止めた時: Ctrl+クリックの意味を残す
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("KEY_DOWN", "", "", "LControlKey", "0", ""),
                EventRow.FromData("LEFT_DOWN", "1", "1", "", "0", ""),
                EventRow.FromData("LEFT_UP", "1", "1", "", "0", ""),
                EventRow.FromData("SYSKEY_DOWN", "", "", "LMenu", "0", ""),
                EventRow.FromData("RIGHT_DOWN", "1", "1", "", "0", ""),
            };

            Assert.IsTrue(EventRules.ReleaseUnpairedModifierKeys(rows, new[] { Keys.LControlKey, Keys.LMenu }));

            Assert.AreEqual(7, rows.Count);
            CollectionAssert.AreEquivalent(new[] { "KEY_UP:LControlKey", "SYSKEY_UP:LMenu" }, TypeAndKeys(rows).Skip(5).ToArray());
            Assert.AreEqual("0", rows[6].Wait);
        }

        [TestMethod]
        public void 記録停止時_修飾キー以外や押されていないキーは触らない()
        {
            List<EventRow> rows = new List<EventRow>
            {
                EventRow.FromData("KEY_DOWN", "", "", "A", "0", ""),
                EventRow.FromData("KEY_DOWN", "", "", "LControlKey", "0", ""),
                EventRow.FromData("KEY_UP", "", "", "LControlKey", "0", ""),
            };

            // Aは押したままだが修飾キーではない。Ctrlは離してあるので押されているキーに入っていない
            Assert.IsFalse(EventRules.ReleaseUnpairedModifierKeys(rows, new[] { Keys.A }));
            Assert.IsFalse(EventRules.ReleaseUnpairedModifierKeys(rows, new Keys[0]));
            Assert.AreEqual(3, rows.Count);
        }

        [TestMethod]
        public void 再生時に飛ばされる行は種類ごとに行番号付きでまとめる()
        {
            List<String> types = new List<String> { "LEFT_DOWN", "SYSKEY_DOWN", "WAIT_MS", "SYSKEY_UP", "KEY_DOWN", "X1_DOWN" };
            for (int i = 0; i < 7; i++)
            {
                types.Add("WHEEL_DOWN");
            }

            List<String> lines = EventRules.SummarizeUnsupportedRowsForPlayback(types);

            CollectionAssert.AreEqual(new[]
            {
                "SYSKEY_DOWN: 1行(行1)",
                "SYSKEY_UP: 1行(行3)",
                "X1_DOWN: 1行(行5)",
                "WHEEL_DOWN: 7行(行6, 行7, 行8, 行9, 行10, …)",
            }, lines);
        }

        [TestMethod]
        public void 再生できる行だけなら飛ばされる行のまとめは空()
        {
            List<String> types = new List<String> { "LEFT_DOWN", "LEFT_UP", "KEY_DOWN", "KEY_UP", "WAIT_MS", "MIDDLE_DOWN" };

            Assert.AreEqual(0, EventRules.SummarizeUnsupportedRowsForPlayback(types).Count);
        }

        [TestMethod]
        public void WAIT時間の一括変更は指定イベントの直前がWAIT_MS行の時だけ変える()
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

            EventRules.BulkChangeEventWait(rows, "RIGHT_UP", 77);

            Assert.AreEqual("10", rows[0].Wait, "LEFT_UPは対象外");
            Assert.AreEqual("20", rows[2].Wait, "直後がLEFT_DOWNなので変えない");
            Assert.AreEqual("77", rows[5].Wait);
            Assert.AreEqual("77", rows[5].Detail, "Detail表示も追従する");

            EventRules.BulkChangeEventWait(rows, "LEFT_DOWN", 5);
            Assert.AreEqual("5", rows[2].Wait, "LEFT_DOWN直前のWAIT_MSも指定すれば変えられる");
        }

        [TestMethod]
        public void 一括変更の対象イベント名は大文字小文字を問わず正規化し不正な名前は弾く()
        {
            String name;
            Assert.IsTrue(EventWaitBulkChangeWindow.TryNormalizeEventName(" left_up ", out name));
            Assert.AreEqual("LEFT_UP", name);
            Assert.IsTrue(EventWaitBulkChangeWindow.TryNormalizeEventName("KEY_DOWN", out name));
            Assert.AreEqual("KEY_DOWN", name);
            Assert.IsFalse(EventWaitBulkChangeWindow.TryNormalizeEventName("WAIT_MS", out name));
            Assert.IsFalse(EventWaitBulkChangeWindow.TryNormalizeEventName("UNKNOWN", out name));
            Assert.IsFalse(EventWaitBulkChangeWindow.TryNormalizeEventName("", out name));
        }
    }
}
