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
        public void AddMouseInputは絶対座標指定だと仮想スクリーン基準で座標を換算する()
        {
            // 実行環境の画面構成に依存するため、期待値は ToAbsoluteCoordinate で求める
            // （固定値をハードコードすると解像度の違う環境で壊れるため）。
            // 換算そのものの正しさは下の ToAbsoluteCoordinate のテストで確かめる。
            // サブモニタも含めた仮想スクリーンの左上が原点になる(以前はメイン画面基準だった)
            var inputs = new List<InputSimulator.Input>();
            System.Drawing.Rectangle virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

            InputSimulator.AddMouseInput(ref inputs, InputSimulator.MouseStroke.MOVE, 0, true, 100, 200);

            Assert.AreEqual(InputSimulator.ToAbsoluteCoordinate(100 - virtualScreen.Left, virtualScreen.Width), inputs[0].Mouse.X);
            Assert.AreEqual(InputSimulator.ToAbsoluteCoordinate(200 - virtualScreen.Top, virtualScreen.Height), inputs[0].Mouse.Y);

            // ABSOLUTE フラグ(0x8000)と VIRTUALDESK フラグ(0x4000)が立っているはず
            Assert.AreEqual(0x8000, inputs[0].Mouse.Flags & 0x8000);
            Assert.AreEqual(0x4000, inputs[0].Mouse.Flags & 0x4000);
        }

        [TestMethod]
        public void AddMouseInputは仮想スクリーンの左上と右下の座標を端の値に換算する()
        {
            // メイン画面の左や上にサブモニタがあると左上はマイナス座標になる。
            // 以前はメイン画面基準だったため、マイナス座標はすべて0(メイン画面の端)に寄せられていた
            var inputs = new List<InputSimulator.Input>();
            System.Drawing.Rectangle virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

            InputSimulator.AddMouseInput(ref inputs, InputSimulator.MouseStroke.MOVE, 0, true, virtualScreen.Left, virtualScreen.Top);
            InputSimulator.AddMouseInput(ref inputs, InputSimulator.MouseStroke.MOVE, 0, true, virtualScreen.Right - 1, virtualScreen.Bottom - 1);

            Assert.AreEqual(0, inputs[0].Mouse.X);
            Assert.AreEqual(0, inputs[0].Mouse.Y);

            // Windowsは「n * 仮想スクリーンの幅 / 65536 + 左端」でピクセルに戻す
            Assert.AreEqual(virtualScreen.Right - 1, (int)((long)inputs[1].Mouse.X * virtualScreen.Width / 65536) + virtualScreen.Left);
            Assert.AreEqual(virtualScreen.Bottom - 1, (int)((long)inputs[1].Mouse.Y * virtualScreen.Height / 65536) + virtualScreen.Top);
        }

        [DataTestMethod]
        [DataRow(1920)]
        [DataRow(1080)]
        [DataRow(1366)]
        [DataRow(768)]
        [DataRow(2560)]
        [DataRow(3840)]
        public void ToAbsoluteCoordinateはWindowsがピクセルに戻すと元の座標になる(int screenSize)
        {
            // Windowsは正規化座標nを「n * 画面サイズ / 65536 (切り捨て)」でピクセルに戻す。
            // 以前の「pixel * (65535 / 画面サイズ)」では、幅1920の右端(1919)が1911に戻っていた
            for (int pixel = 0; pixel < screenSize; pixel++)
            {
                int normalized = InputSimulator.ToAbsoluteCoordinate(pixel, screenSize);

                Assert.IsTrue(normalized >= 0 && normalized <= 65535, "範囲外: pixel=" + pixel);
                Assert.AreEqual(pixel, (int)((long)normalized * screenSize / 65536), "pixel=" + pixel);
            }
        }

        [TestMethod]
        public void ToAbsoluteCoordinateは画面外の座標を0から65535に収める()
        {
            Assert.AreEqual(0, InputSimulator.ToAbsoluteCoordinate(-10, 1920));
            Assert.AreEqual(65535, InputSimulator.ToAbsoluteCoordinate(5000, 1920));
            Assert.AreEqual(0, InputSimulator.ToAbsoluteCoordinate(100, 0));
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

        [TestMethod]
        public void AddKeyboardInputはKeys指定ならUNICODEを付けずに仮想キーで積む()
        {
            // 仮想キーで送るときにKEYEVENTF_UNICODE(0x0004)が付いていると、
            // Alt+F4などの組み合わせが届かないことがある
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_DOWN, System.Windows.Forms.Keys.Menu);
            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_DOWN, System.Windows.Forms.Keys.F4);
            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_UP, System.Windows.Forms.Keys.F4);
            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_UP, System.Windows.Forms.Keys.Menu);

            Assert.AreEqual(4, inputs.Count);
            Assert.AreEqual((int)InputSimulator.KeyboardStroke.KEY_DOWN, inputs[0].Keyboard.Flags);
            Assert.AreEqual((int)InputSimulator.KeyboardStroke.KEY_DOWN, inputs[1].Keyboard.Flags);
            Assert.AreEqual((int)InputSimulator.KeyboardStroke.KEY_UP, inputs[2].Keyboard.Flags);
            Assert.AreEqual((int)InputSimulator.KeyboardStroke.KEY_UP, inputs[3].Keyboard.Flags);
            Assert.AreEqual((short)System.Windows.Forms.Keys.Menu, inputs[0].Keyboard.VirtualKey);
            Assert.AreEqual((short)System.Windows.Forms.Keys.F4, inputs[1].Keyboard.VirtualKey);
        }

        [TestMethod]
        [DataRow(System.Windows.Forms.Keys.Left)]
        [DataRow(System.Windows.Forms.Keys.Right)]
        [DataRow(System.Windows.Forms.Keys.Up)]
        [DataRow(System.Windows.Forms.Keys.Down)]
        [DataRow(System.Windows.Forms.Keys.Insert)]
        [DataRow(System.Windows.Forms.Keys.Delete)]
        [DataRow(System.Windows.Forms.Keys.Home)]
        [DataRow(System.Windows.Forms.Keys.End)]
        [DataRow(System.Windows.Forms.Keys.PageUp)]
        [DataRow(System.Windows.Forms.Keys.PageDown)]
        [DataRow(System.Windows.Forms.Keys.RControlKey)]
        [DataRow(System.Windows.Forms.Keys.RMenu)]
        [DataRow(System.Windows.Forms.Keys.LWin)]
        [DataRow(System.Windows.Forms.Keys.RWin)]
        [DataRow(System.Windows.Forms.Keys.Apps)]
        [DataRow(System.Windows.Forms.Keys.NumLock)]
        [DataRow(System.Windows.Forms.Keys.Divide)]
        [DataRow(System.Windows.Forms.Keys.PrintScreen)]
        public void AddKeyboardInputは拡張キーにEXTENDEDKEYを付ける(System.Windows.Forms.Keys key)
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_DOWN, key);
            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_UP, key);

            Assert.AreEqual(0x0001, inputs[0].Keyboard.Flags, "押す: EXTENDEDKEYだけ");
            Assert.AreEqual(0x0002 | 0x0001, inputs[1].Keyboard.Flags, "離す: KEYUP+EXTENDEDKEY");
            Assert.AreEqual((short)key, inputs[0].Keyboard.VirtualKey);
        }

        [TestMethod]
        [DataRow(System.Windows.Forms.Keys.A)]
        [DataRow(System.Windows.Forms.Keys.Enter)]
        [DataRow(System.Windows.Forms.Keys.LControlKey)]
        [DataRow(System.Windows.Forms.Keys.LMenu)]
        [DataRow(System.Windows.Forms.Keys.ShiftKey)]
        [DataRow(System.Windows.Forms.Keys.NumPad4)]
        [DataRow(System.Windows.Forms.Keys.F4)]
        public void AddKeyboardInputは拡張キーでなければEXTENDEDKEYを付けない(System.Windows.Forms.Keys key)
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, InputSimulator.KeyboardStroke.KEY_DOWN, key);

            Assert.AreEqual(0, inputs[0].Keyboard.Flags & 0x0001);
            Assert.AreEqual(0, inputs[0].Keyboard.Flags & 0x0004, "UNICODEも付かない");
        }

        [TestMethod]
        public void AddKeyboardInputは文字列ならUNICODEを付けて積む()
        {
            var inputs = new List<InputSimulator.Input>();

            InputSimulator.AddKeyboardInput(ref inputs, "あ");

            Assert.AreEqual(0x0004, inputs[0].Keyboard.Flags);
            Assert.AreEqual(0x0002 | 0x0004, inputs[1].Keyboard.Flags);
            Assert.AreEqual((short)'あ', inputs[0].Keyboard.ScanCode);
        }
    }
}
