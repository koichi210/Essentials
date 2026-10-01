using System.Collections.Generic;
using InputSimulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorder.Tests
{
    /// <summary>
    /// InputSimulator のテスト。
    ///
    /// ⚠️ SendInput() は実際にこのマシンへキーボード・マウスの入力イベントを注入する
    /// （P/Invoke で user32.dll の SendInput を呼ぶ）。テスト実行中に他の操作へ実際の
    /// キー入力・クリックが送られてしまうため、絶対に呼び出してはいけない。
    ///
    /// テストするのは「入力イベントのリストを組み立てる」AddMouseInput / AddKeyboardInput
    /// だけ。これらは List に構造体を積むだけの純粋なデータ組み立てで、OSへの操作は行わない
    /// （AddKeyboardInput の Keys 版だけは MapVirtualKey という読み取り専用の問い合わせを
    /// 呼ぶが、これはキー配列からスキャンコードを引くだけで副作用は無い）。
    /// </summary>
    [TestClass]
    public class InputSimulatorTests
    {
        [TestMethod]
        public void AddMouseInputは種類と座標を積む()
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddMouseInput(ref inputs, InputSimulator.MouseStroke.LEFT_DOWN, 0, false, 10, 20);

            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual(0, inputs[0].Type, "マウス入力は Type=0");
            Assert.AreEqual((int)InputSimulator.MouseStroke.LEFT_DOWN, inputs[0].Mouse.Flags);
            Assert.AreEqual(10, inputs[0].Mouse.X);
            Assert.AreEqual(20, inputs[0].Mouse.Y);
        }

        [TestMethod]
        public void AddMouseInputは複数フラグをOR結合する()
        {
            var inputs = new List<InputSimulator.Input>();
            var flags = new List<InputSimulator.MouseStroke>
            {
                InputSimulator.MouseStroke.LEFT_DOWN,
                InputSimulator.MouseStroke.LEFT_UP,
            };

            InputSimulator.AddMouseInput(ref inputs, flags, 0, false, 0, 0);

            int expected = (int)InputSimulator.MouseStroke.LEFT_DOWN | (int)InputSimulator.MouseStroke.LEFT_UP;
            Assert.AreEqual(expected, inputs[0].Mouse.Flags);
        }

        [TestMethod]
        public void AddMouseInputはフラグがnullなら何も積まない()
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddMouseInput(ref inputs, (List<InputSimulator.MouseStroke>)null, 0, false, 0, 0);

            Assert.AreEqual(0, inputs.Count);
        }

        [TestMethod]
        public void AddMouseInputは絶対座標指定だと画面サイズに応じて座標を換算する()
        {
            // 65535 を画面の幅・高さで割った値を座標に掛けている。
            // 実行環境の画面解像度に依存するため、期待値も同じ計算式で求める
            // （固定値をハードコードすると解像度の違う環境で壊れるため）。
            var inputs = new List<InputSimulator.Input>();
            int screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            int screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

            InputSimulator.AddMouseInput(ref inputs, InputSimulator.MouseStroke.MOVE, 0, true, 100, 200);

            int expectedX = 100 * (65535 / screenWidth);
            int expectedY = 200 * (65535 / screenHeight);
            Assert.AreEqual(expectedX, inputs[0].Mouse.X);
            Assert.AreEqual(expectedY, inputs[0].Mouse.Y);

            // ABSOLUTE フラグ(0x8000)が立っているはず
            Assert.AreEqual(0x8000, inputs[0].Mouse.Flags & 0x8000);
        }

        [TestMethod]
        public void AddKeyboardInputは文字列1文字ごとに押す離すの2件を積む()
        {
            // KEYEVENTF_UNICODE 方式で送るため、VirtualKey は 0 固定で、文字コードは
            // ScanCode 側に入る（Win32 の SendInput 仕様どおり。仮想キーではなく
            // Unicode文字そのものとして入力を合成するときの標準的な使い方）。
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, "AB");

            Assert.AreEqual(4, inputs.Count, "2文字 x (押す+離す) で4件");
            foreach (InputSimulator.Input input in inputs)
            {
                Assert.AreEqual(1, input.Type, "キーボード入力は Type=1");
                Assert.AreEqual(0, input.Keyboard.VirtualKey, "UNICODE指定では仮想キーは0固定");
            }

            Assert.AreEqual((short)'A', inputs[0].Keyboard.ScanCode);
            Assert.AreEqual((short)'A', inputs[1].Keyboard.ScanCode);
            Assert.AreEqual((short)'B', inputs[2].Keyboard.ScanCode);
            Assert.AreEqual((short)'B', inputs[3].Keyboard.ScanCode);
        }

        [TestMethod]
        public void AddKeyboardInputは空文字やnullなら何も積まない()
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, "");
            InputSimulator.AddKeyboardInput(ref inputs, (string)null);

            Assert.AreEqual(0, inputs.Count);
        }

        [TestMethod]
        public void AddKeyboardInputはKeysを指定しても積める()
        {
            // MapVirtualKey は読み取り専用の問い合わせ(キー配列からスキャンコードを引くだけ)なので、
            // 呼び出しても実害は無い。返るスキャンコードの値はハードウェア・レイアウト依存なので
            // 具体的な値までは検証せず、正しい VirtualKey が積まれることだけ確認する。
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_DOWN, System.Windows.Forms.Keys.Enter);

            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual((short)System.Windows.Forms.Keys.Enter, inputs[0].Keyboard.VirtualKey);
        }
    }
}
