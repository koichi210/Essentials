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

        // 再生で送る入力イベントの中身だけを確かめる(SendInputは呼ばない。InputSimulatorTestsと同じ方針)
        private const int MouseeventfMove = 0x0001;
        private const int MouseeventfXDown = 0x0080;
        private const int MouseeventfXUp = 0x0100;
        private const int MouseeventfWheel = 0x0800;
        private const int MouseeventfAbsoluteVirtualDesk = 0x8000 | 0x4000;
        private const int KeyeventfKeyUp = 0x0002;

        [TestMethod]
        public void ホイールの行はクリックと同じ座標で1ノッチ分のWHEELを送る()
        {
            InputSimulation.InputSimulator.Input click = EventRules.BuildPlaybackInputs("LEFT_DOWN", "100", "200", "").Single();
            InputSimulation.InputSimulator.Input wheelUp = EventRules.BuildPlaybackInputs("WHEEL_UP", "100", "200", "").Single();
            InputSimulation.InputSimulator.Input wheelDown = EventRules.BuildPlaybackInputs("WHEEL_DOWN", "100", "200", "").Single();

            foreach (InputSimulation.InputSimulator.Input input in new[] { wheelUp, wheelDown })
            {
                Assert.AreEqual(0, input.Type, "マウス入力は Type=0");
                Assert.AreEqual(MouseeventfMove | MouseeventfWheel | MouseeventfAbsoluteVirtualDesk, input.Mouse.Flags);
                Assert.AreEqual(click.Mouse.X, input.Mouse.X);
                Assert.AreEqual(click.Mouse.Y, input.Mouse.Y);
            }

            // 奥へ回す(WHEEL_UP)が+、手前へ回す(WHEEL_DOWN)が-。回転量の無い行(以前のファイル)は1ノッチ(WHEEL_DELTA=120)
            Assert.AreEqual(120, wheelUp.Mouse.Data);
            Assert.AreEqual(-120, wheelDown.Mouse.Data);
        }

        [TestMethod]
        public void ホイールの行は記録した回転量で送る()
        {
            // 高分解能ホイールは1ノッチより細かい量(例: 30)で何回も通知してくるので、1行=120で送ると回りすぎていた
            Assert.AreEqual(30, EventRules.BuildPlaybackInputs("WHEEL_UP", "100", "200", "", "30").Single().Mouse.Data);
            Assert.AreEqual(-30, EventRules.BuildPlaybackInputs("WHEEL_DOWN", "100", "200", "", "30").Single().Mouse.Data);
            Assert.AreEqual(240, EventRules.BuildPlaybackInputs("WHEEL_UP", "100", "200", "", "240").Single().Mouse.Data);

            // 回転量が空なら従来どおり1ノッチ
            Assert.AreEqual(-120, EventRules.BuildPlaybackInputs("WHEEL_DOWN", "100", "200", "", "").Single().Mouse.Data);

            // ホイール以外の行のmouseData(Xボタンの番号)は回転量で変えない
            Assert.AreEqual(2, EventRules.BuildPlaybackInputs("X2_DOWN", "100", "200", "", "30").Single().Mouse.Data);
        }

        [TestMethod]
        public void 記録時のmouseDataの上位ワードから回転量を正の数で取り出す()
        {
            Assert.AreEqual("120", EventRules.FormatWheelDelta(120u << 16));
            Assert.AreEqual("30", EventRules.FormatWheelDelta(30u << 16));
            // 手前へ回すと上位ワードは負(-120 = 0xFF88)。向きはWHEEL_DOWNで表すので量は正で残す
            Assert.AreEqual("120", EventRules.FormatWheelDelta(0xFF880000u));
            Assert.AreEqual("", EventRules.FormatWheelDelta(0u));
        }

        [TestMethod]
        public void ホイールの回転量はDetail列に表示し編集すると書き戻す()
        {
            EventRow row = EventRow.FromData("WHEEL_UP", "10", "20", "", "0", "", "240");
            Assert.AreEqual("X:10 Y:20 Delta:240", row.Detail);

            row.Detail = "X:10 Y:20 Delta:60";
            Assert.AreEqual("60", row.Delta);

            // Delta:を消せば回転量も空になり、1ノッチで送る
            row.Detail = "X:10 Y:20";
            Assert.AreEqual("", row.Delta);

            // 回転量の無い行(以前のファイル)の表示は今までどおり
            Assert.AreEqual("X:10 Y:20", EventRow.FromData("WHEEL_UP", "10", "20", "", "0", "").Detail);
        }

        [TestMethod]
        public void ホイールの回転量が正の数値でなければ再生前チェックでエラーにする()
        {
            String message;
            Assert.IsTrue(EventRow.FromData("WHEEL_UP", "10", "20", "", "0", "", "abc").IsInvalidForPlayback(out message));
            StringAssert.Contains(message, "回転量(abc)");
            Assert.IsTrue(EventRow.FromData("WHEEL_DOWN", "10", "20", "", "0", "", "-120").IsInvalidForPlayback(out message));
            Assert.IsTrue(EventRow.FromData("WHEEL_DOWN", "10", "20", "", "0", "", "0").IsInvalidForPlayback(out message));

            Assert.IsFalse(EventRow.FromData("WHEEL_DOWN", "10", "20", "", "0", "", "240").IsInvalidForPlayback(out message));
            Assert.IsFalse(EventRow.FromData("WHEEL_DOWN", "10", "20", "", "0", "", "").IsInvalidForPlayback(out message));
        }

        [TestMethod]
        public void X1とX2ボタンの行はクリックと同じ座標でXDOWNとXUPをボタン番号付きで送る()
        {
            InputSimulation.InputSimulator.Input click = EventRules.BuildPlaybackInputs("LEFT_DOWN", "-50", "30", "").Single();
            String[] types = { "X1_DOWN", "X1_UP", "X2_DOWN", "X2_UP" };
            int[] expectedButtonFlags = { MouseeventfXDown, MouseeventfXUp, MouseeventfXDown, MouseeventfXUp };
            int[] expectedData = { 1, 1, 2, 2 };   // XBUTTON1 / XBUTTON2

            for (int i = 0; i < types.Length; i++)
            {
                InputSimulation.InputSimulator.Input input = EventRules.BuildPlaybackInputs(types[i], "-50", "30", "").Single();

                Assert.AreEqual(0, input.Type, types[i]);
                Assert.AreEqual(MouseeventfMove | expectedButtonFlags[i] | MouseeventfAbsoluteVirtualDesk, input.Mouse.Flags, types[i]);
                Assert.AreEqual(expectedData[i], input.Mouse.Data, types[i]);
                Assert.AreEqual(click.Mouse.X, input.Mouse.X, types[i]);
                Assert.AreEqual(click.Mouse.Y, input.Mouse.Y, types[i]);
            }
        }

        [TestMethod]
        public void SYSKEYの行は普通のキーとして送りAlt付きの組み合わせもそのまま送れる()
        {
            // Alt+F4を記録した時の並び(Altを押している間のキーはSYSKEY_DOWN/UPになる)
            String[][] rows =
            {
                new[] { "SYSKEY_DOWN", "LMenu" },
                new[] { "SYSKEY_DOWN", "F4" },
                new[] { "SYSKEY_UP", "F4" },
                new[] { "KEY_UP", "LMenu" },
                new[] { "SYSKEY_DOWN", "F10" },   // F10単独もSYSKEY
                new[] { "SYSKEY_UP", "F10" },
            };
            Keys[] expectedKeys = { Keys.LMenu, Keys.F4, Keys.F4, Keys.LMenu, Keys.F10, Keys.F10 };
            Boolean[] expectedUp = { false, false, true, true, false, true };

            for (int i = 0; i < rows.Length; i++)
            {
                InputSimulation.InputSimulator.Input input = EventRules.BuildPlaybackInputs(rows[i][0], "", "", rows[i][1]).Single();

                Assert.AreEqual(1, input.Type, "キーボード入力は Type=1");
                Assert.AreEqual((short)expectedKeys[i], input.Keyboard.VirtualKey, rows[i][0] + ":" + rows[i][1]);
                Assert.AreEqual(expectedUp[i] ? KeyeventfKeyUp : 0, input.Keyboard.Flags & KeyeventfKeyUp, rows[i][0] + ":" + rows[i][1]);

                // 送る中身は普通のKEY_DOWN/KEY_UPの行と同じ
                String keyType = expectedUp[i] ? "KEY_UP" : "KEY_DOWN";
                InputSimulation.InputSimulator.Input asKey = EventRules.BuildPlaybackInputs(keyType, "", "", rows[i][1]).Single();
                Assert.AreEqual(asKey.Keyboard.Flags, input.Keyboard.Flags);
                Assert.AreEqual(asKey.Keyboard.ScanCode, input.Keyboard.ScanCode);
            }
        }

        [TestMethod]
        public void 再生しない行は何も送らない()
        {
            Assert.AreEqual(0, EventRules.BuildPlaybackInputs("WAIT_MS", "", "", "").Count);
            Assert.AreEqual(0, EventRules.BuildPlaybackInputs("TYPO", "1", "2", "A").Count);
            Assert.AreEqual(0, EventRules.BuildPlaybackInputs("SYSKEY_DOWN", "", "", "NoSuchKey").Count);
        }

        [TestMethod]
        public void ホイールとXボタンとSYSKEYの行も再生前チェックの対象になる()
        {
            // 再生するようになったので、値が読めなければクリックやキーの行と同じくエラーにする
            String message;
            Assert.IsTrue(EventRow.FromData("WHEEL_DOWN", "abc", "20", "", "0", "").IsInvalidForPlayback(out message));
            StringAssert.Contains(message, "X/Y(abc, 20)");
            Assert.IsTrue(EventRow.FromData("X2_UP", "", "", "", "0", "").IsInvalidForPlayback(out message));
            Assert.IsTrue(EventRow.FromData("SYSKEY_DOWN", "", "", "NoSuchKey", "0", "").IsInvalidForPlayback(out message));

            Assert.IsFalse(EventRow.FromData("WHEEL_UP", "10", "20", "", "0", "").IsInvalidForPlayback(out message));
            Assert.IsFalse(EventRow.FromData("SYSKEY_UP", "", "", "F4", "0", "").IsInvalidForPlayback(out message));

            // マウス行なのにKeyがある警告も同じ
            StringAssert.Contains(EventRow.FromData("X1_DOWN", "10", "20", "A", "0", "").DetailProblemText, "Key(A)は再生時には使われず無視される");
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
