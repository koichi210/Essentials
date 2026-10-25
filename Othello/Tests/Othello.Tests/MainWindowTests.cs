using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace Othello.Tests
{
    /// <summary>
    /// MainWindow（WinForms版Form1相当の、オセロ盤面のウィンドウ）のテスト。
    ///
    /// ⚠️ button_ReStart_Click / 遊び方 / バージョン情報 / 棋譜の保存・読込ダイアログは
    /// MessageBoxやファイルダイアログを出すため、テスト対象から除外する
    /// (読込後の処理はLoadKifuTextで検証する)。
    /// </summary>
    [TestClass]
    public class MainWindowTests
    {
        /// <summary>
        /// 溜まっているDispatcherの処理(レイアウト・Loaded等)を流す
        /// </summary>
        private static void DoEvents()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        }

        private static MainWindow ShowWindow()
        {
            var window = new MainWindow
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
            };
            window.Show();
            DoEvents();
            return window;
        }

        /// <summary>
        /// マス目(cellX, cellY)の中心の、盤面基準のDIP座標
        /// </summary>
        private static Point CellCenter(MainWindow window, int cellX, int cellY)
        {
            double w = window.boardHost.ActualWidth;
            double h = window.boardHost.ActualHeight;
            return new Point(w * (cellX + 0.5) / GameMaster.BoardSize, h * (cellY + 0.5) / GameMaster.BoardSize);
        }

        [TestMethod]
        public void コンストラクタで例外なく生成でき盤面が初期化される()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                Assert.IsNotNull(window);
                Assert.IsNotNull(window.BoardRenderer.Canvas);
                Assert.IsNotNull(window.pictureBoxField.Source);
                Assert.AreEqual("黒の番 (黒:2 白:2)", window.label_Status.Text);
                Assert.IsFalse(window.menuItem_Undo.IsEnabled);
                Assert.IsFalse(window.menuItem_Redo.IsEnabled);
                Assert.IsTrue(window.menuItem_PP.IsChecked);
                Assert.IsTrue(window.menuItem_ComLevel1.IsChecked);
                Assert.IsTrue(window.menuItem_TimeNone.IsChecked);
                Assert.AreEqual(Visibility.Hidden, window.label_Time.Visibility);
                window.Close();
            });
        }

        [TestMethod]
        public void 表示すると盤面が正方形で実ピクセルサイズで描画され最小サイズが設定される()
        {
            StaRunner.Run(() =>
            {
                var window = ShowWindow();
                try
                {
                    Assert.AreEqual(window.boardHost.ActualWidth, window.boardHost.ActualHeight, 1.0);
                    Assert.AreEqual(200.0, window.boardHost.ActualWidth, 1.0);

                    var source = PresentationSource.FromVisual(window);
                    double scale = source.CompositionTarget.TransformToDevice.M11;
                    Assert.AreEqual((int)Math.Round(window.boardHost.ActualWidth * scale), window.BoardRenderer.Width);

                    Assert.AreEqual(window.ActualWidth, window.MinWidth, 0.5);
                    Assert.AreEqual(window.ActualHeight, window.MinHeight, 0.5);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

        [TestMethod]
        public void ドラッグでリサイズすると盤面が正方形のまま拡大され描き直される()
        {
            StaRunner.Run(() =>
            {
                var window = ShowWindow();
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    Assert.IsTrue(GetWindowRect(hwnd, out RECT rect));
                    int oldBitmapWidth = window.BoardRenderer.Width;

                    // 右下(WMSZ_BOTTOMRIGHT=8)を掴んで幅を150px広げた時、高さはOSの提案のままにしておく
                    rect.Right += 150;
                    SendMessage(hwnd, 0x0214 /* WM_SIZING */, new IntPtr(8), ref rect);
                    MoveWindow(hwnd, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, true);
                    DoEvents();

                    Assert.IsTrue(window.boardHost.ActualWidth > 250, "盤面が広がっていない: " + window.boardHost.ActualWidth);
                    Assert.AreEqual(window.boardHost.ActualWidth, window.boardHost.ActualHeight, 2.0);
                    Assert.IsTrue(window.BoardRenderer.Width > oldBitmapWidth);
                    Assert.AreEqual(window.BoardRenderer.Width, ((BitmapSource)window.pictureBoxField.Source).PixelWidth);

                    // 広げた後も、クリック位置とマス目の対応がずれない
                    Assert.IsTrue(window.HandleBoardClick(CellCenter(window, 5, 4)));
                    Assert.AreEqual(StoneColor.Black, window.GameMaster.Table[4, 5]);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void 表示した盤面画像に初期配置の石が描かれている()
        {
            StaRunner.Run(() =>
            {
                var window = ShowWindow();
                try
                {
                    var bmp = (BitmapSource)window.pictureBoxField.Source;
                    Assert.AreEqual(window.BoardRenderer.Width, bmp.PixelWidth);

                    // Table[y,x]: (3,3)=白, (4,3)=黒
                    Assert.AreEqual(0xFFFFFFFFu, PixelAt(bmp, 3.5, 3.5));
                    Assert.AreEqual(0xFF000000u, PixelAt(bmp, 4.5, 3.5));
                    // 左上のマスは緑
                    Assert.AreEqual(0xFF008000u, PixelAt(bmp, 0.5, 0.5));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static uint PixelAt(BitmapSource bmp, double cellX, double cellY)
        {
            int x = (int)(bmp.PixelWidth * cellX / GameMaster.BoardSize);
            int y = (int)(bmp.PixelHeight * cellY / GameMaster.BoardSize);
            var pixel = new byte[4];
            bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return (uint)(pixel[3] << 24 | pixel[2] << 16 | pixel[1] << 8 | pixel[0]);
        }

        [TestMethod]
        public void 盤面クリックでその位置のマスに石が置かれ手番が変わる()
        {
            StaRunner.Run(() =>
            {
                var window = ShowWindow();
                try
                {
                    // 置けないマス(左上)は何も起きない
                    Assert.IsFalse(window.HandleBoardClick(CellCenter(window, 0, 0)));
                    Assert.AreEqual("黒の番 (黒:2 白:2)", window.label_Status.Text);

                    // (x=2,y=3)は黒が置ける
                    Assert.IsTrue(window.HandleBoardClick(CellCenter(window, 2, 3)));
                    Assert.AreEqual(StoneColor.Black, window.GameMaster.Table[3, 2]);
                    Assert.AreEqual("白の番 (黒:4 白:1)", window.label_Status.Text);
                    Assert.IsTrue(window.menuItem_Undo.IsEnabled);

                    // 置いた石が画像にも反映されている
                    var bmp = (BitmapSource)window.pictureBoxField.Source;
                    Assert.AreEqual(0xFF000000u, PixelAt(bmp, 2.5, 3.5));

                    // 同じマスにはもう置けない
                    Assert.IsFalse(window.HandleBoardClick(CellCenter(window, 2, 3)));

                    // 盤面の外は無視
                    Assert.IsFalse(window.HandleBoardClick(new Point(-1, -1)));
                    Assert.IsFalse(window.HandleBoardClick(new Point(window.boardHost.ActualWidth + 1, 0)));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void マス目の境界付近のクリックも罫線と同じマスとして判定される()
        {
            StaRunner.Run(() =>
            {
                var window = ShowWindow();
                try
                {
                    // (x=2,y=3)マスの右下ぎりぎり(境界の少し内側)
                    double w = window.boardHost.ActualWidth;
                    double h = window.boardHost.ActualHeight;
                    var p = new Point(w * 3 / 8 - 0.6, h * 4 / 8 - 0.6);
                    Assert.IsTrue(window.HandleBoardClick(p));
                    Assert.AreEqual(StoneColor.Black, window.GameMaster.Table[3, 2]);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void 人対COMではCOMの手番中はクリックを受け付けずCOMが打つと手番が戻る()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.menuItem_PlayMode_Click(window.menuItem_PC, null);
                Assert.IsTrue(window.menuItem_PC.IsChecked);
                Assert.IsFalse(window.menuItem_PP.IsChecked);
                Assert.IsFalse(window.IsComMovePending);

                Assert.IsTrue(window.GameMaster.TryPut(2, 3));
                // 白(COM)の番: クリックは受け付けない
                window.menuItem_ComLevel_Click(window.menuItem_ComLevel2, null);
                Assert.IsTrue(window.menuItem_ComLevel2.IsChecked);
                Assert.IsFalse(window.menuItem_ComLevel1.IsChecked);

                window.DoComMove();
                Assert.AreEqual(StoneColor.Black, window.GameMaster.CurrentTurn);
                Assert.AreEqual(2, window.GameMaster.History.Count);
                Assert.IsFalse(window.IsComMovePending);
                window.Close();
            });
        }

        [TestMethod]
        public void 持ち時間を選ぶと残り時間が表示され時間切れで負けになる()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.menuItem_TimeLimit_Click(window.menuItem_Time30s, null);

                Assert.IsTrue(window.menuItem_Time30s.IsChecked);
                Assert.IsFalse(window.menuItem_TimeNone.IsChecked);
                Assert.AreEqual(Visibility.Visible, window.label_Time.Visibility);
                Assert.AreEqual("残り時間 黒:30秒 白:30秒", window.label_Time.Text);
                Assert.IsTrue(window.IsCountdownRunning);

                window.CountDown(1000);
                Assert.AreEqual("残り時間 黒:29秒 白:30秒", window.label_Time.Text);

                window.CountDown(29000);
                Assert.IsFalse(window.IsCountdownRunning);
                Assert.AreEqual("時間切れ(黒) 白の勝ち 黒:2 白:2", window.label_Status.Text);

                window.menuItem_TimeLimit_Click(window.menuItem_TimeNone, null);
                Assert.AreEqual(Visibility.Hidden, window.label_Time.Visibility);
                Assert.IsFalse(window.IsCountdownRunning);
                Assert.AreEqual("黒の番 (黒:2 白:2)", window.label_Status.Text);
                window.Close();
            });
        }

        [TestMethod]
        public void 時間切れになると一手戻す一手進めるは押せない()
        {
            StaRunner.Run(() =>
            {
                var original = new GameMaster();
                original.Initialize();
                original.TryPut(2, 3);
                original.TryPut(2, 2);

                string text = Kifu.ToText(original.History);

                var window = new MainWindow();
                window.menuItem_TimeLimit_Click(window.menuItem_Time30s, null);
                Assert.IsTrue(window.LoadKifuText(text, false));
                Assert.IsTrue(window.menuItem_Undo.IsEnabled);

                // 読み込んだ後は「開始」を押してから時間が減る
                window.menuItem_Start_Click(null, null);
                window.CountDown(30000);
                Assert.IsFalse(window.menuItem_Undo.IsEnabled);
                Assert.IsFalse(window.menuItem_Redo.IsEnabled);

                // 棋譜を読み込み直すと時間切れが解除されるので、また戻せる
                Assert.IsTrue(window.LoadKifuText(text, false));
                Assert.IsTrue(window.menuItem_Undo.IsEnabled);
                window.Close();
            });
        }

        [TestMethod]
        public void 棋譜テキストを読み込むと盤面が再生される()
        {
            StaRunner.Run(() =>
            {
                var original = new GameMaster();
                original.Initialize();
                original.TryPut(2, 3);
                original.TryPut(2, 2);
                string text = Kifu.ToText(original.History);

                var window = new MainWindow();
                Assert.IsTrue(window.LoadKifuText(text, false));
                Assert.AreEqual(2, window.GameMaster.History.Count);
                Assert.AreEqual("黒の番 (黒:3 白:3) [「開始」で続きから]", window.label_Status.Text);
                Assert.IsTrue(window.menuItem_Undo.IsEnabled);

                Assert.IsFalse(window.LoadKifuText("でたらめ", false));
                window.Close();
            });
        }

        [TestMethod]
        public void 棋譜を読み込んだ後は開始を押すまでCOMも人も打たず時間も減らない()
        {
            StaRunner.Run(() =>
            {
                var original = new GameMaster();
                original.Initialize();
                original.TryPut(2, 3);
                original.TryPut(2, 2);
                string text = Kifu.ToText(original.History);

                var window = ShowWindow();
                try
                {
                    // COM vs 人(COMが黒)・持ち時間あり。読み込んだ局面は黒(COM)の番
                    window.menuItem_PlayMode_Click(window.menuItem_CP, null);
                    window.menuItem_TimeLimit_Click(window.menuItem_Time30s, null);
                    Assert.IsTrue(window.LoadKifuText(text, false));

                    // 以前は読み込んだ直後にCOMが打ち始めていた
                    Assert.IsFalse(window.IsComMovePending);
                    Assert.IsFalse(window.IsCountdownRunning);
                    window.DoComMove();
                    Assert.AreEqual(2, window.GameMaster.History.Count, "開始前はCOMが打たない");
                    Assert.AreEqual("残り時間 黒:30秒 白:30秒", window.label_Time.Text);

                    // 一手戻す/進めるで局面を見ても、開始待ちのまま
                    window.GameMaster.Undo();
                    window.menuItem_Start_Click(null, null);
                    Assert.AreEqual(1, window.GameMaster.TurnCount, "開始で新しい対局に上書きしない");
                    Assert.AreEqual(2, window.GameMaster.History.Count);

                    // 開始すると読み込んだ局面(から1手戻した所)の続きから。白(人)の番なのでCOMは待つ
                    Assert.AreEqual(StoneColor.White, window.GameMaster.CurrentTurn);
                    Assert.IsTrue(window.IsCountdownRunning);
                    Assert.AreEqual("白の番 (黒:4 白:1)", window.label_Status.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void 棋譜を読み込んだ後は開始を押すまで人のクリックも受け付けない()
        {
            StaRunner.Run(() =>
            {
                var original = new GameMaster();
                original.Initialize();
                original.TryPut(2, 3);
                string text = Kifu.ToText(original.History);

                var window = ShowWindow();
                try
                {
                    // 人 vs COM(人が黒)。読み込んだ局面は白(COM)の番
                    window.menuItem_PlayMode_Click(window.menuItem_PC, null);
                    Assert.IsTrue(window.LoadKifuText(text, false));
                    Assert.IsFalse(window.IsComMovePending);

                    // 開始すると白(COM)が読み込んだ局面の続きを打ち、黒(人)の番になる
                    window.menuItem_Start_Click(null, null);
                    Assert.IsTrue(window.IsComMovePending);
                    window.DoComMove();
                    Assert.AreEqual(2, window.GameMaster.History.Count);
                    Assert.AreEqual(StoneColor.Black, window.GameMaster.CurrentTurn);

                    // もう一度読み込むとまた開始待ち。人(黒)の番でもクリックでは打てない
                    original.TryPut(2, 2);
                    Assert.IsTrue(window.LoadKifuText(Kifu.ToText(original.History), false));
                    Assert.AreEqual(StoneColor.Black, window.GameMaster.CurrentTurn);
                    Point cell = FindValidCell(window);
                    Assert.IsFalse(window.HandleBoardClick(cell));
                    Assert.AreEqual(2, window.GameMaster.History.Count);

                    window.menuItem_Start_Click(null, null);
                    Assert.IsTrue(window.HandleBoardClick(cell));
                    Assert.AreEqual(3, window.GameMaster.History.Count);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void 終局した棋譜を読み込んだ後の開始は新しい対局になる()
        {
            StaRunner.Run(() =>
            {
                // COM同士で最後まで打った棋譜
                string text = Kifu.ToText(PlayToEnd().History);

                var window = new MainWindow();
                window.LoadKifuText(text, false);
                Assert.IsTrue(window.GameMaster.IsGameEnd);

                window.menuItem_Start_Click(null, null);
                Assert.AreEqual(0, window.GameMaster.History.Count);
                Assert.AreEqual("黒の番 (黒:2 白:2)", window.label_Status.Text);
                window.Close();
            });
        }

        /// <summary>
        /// COM(レベル1)同士で終局まで打った対局
        /// </summary>
        private static GameMaster PlayToEnd()
        {
            var gm = new GameMaster();
            gm.Initialize();
            for (int i = 0; i < 100 && !gm.IsGameEnd; i++)
            {
                Assert.IsTrue(ComPlayer.TryGetMove(gm, gm.CurrentTurn, 1, out int x, out int y));
                Assert.IsTrue(gm.TryPut(x, y));
            }
            Assert.IsTrue(gm.IsGameEnd);
            return gm;
        }

        /// <summary>
        /// 今の手番が置けるマスの1つ(の中心のDIP座標)
        /// </summary>
        private static Point FindValidCell(MainWindow window)
        {
            bool[,] valid = window.GameMaster.GetValidMoves(window.GameMaster.CurrentTurn);
            for (int y = 0; y < GameMaster.BoardSize; y++)
            {
                for (int x = 0; x < GameMaster.BoardSize; x++)
                {
                    if (valid[y, x])
                    {
                        return CellCenter(window, x, y);
                    }
                }
            }
            Assert.Fail("置けるマスが無い");
            return new Point();
        }
    }
}
