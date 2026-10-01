using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace othello.Tests
{
    /// <summary>
    /// MainWindow（WinForms版Form1相当の、オセロ盤面のウィンドウ）のテスト。
    ///
    /// ⚠️ button_ReStart_Click / 遊び方 / バージョン情報 / 棋譜の保存・読込ダイアログは
    /// MessageBoxやファイルダイアログを出すため、テスト対象から除外する
    /// (読込後の処理はLoadKihuTextで検証する)。
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
        public void 棋譜テキストを読み込むと盤面が再生される()
        {
            StaRunner.Run(() =>
            {
                var original = new GameMaster();
                original.Initialize();
                original.TryPut(2, 3);
                original.TryPut(2, 2);
                string text = Kihu.ToText(original.History);

                var window = new MainWindow();
                Assert.IsTrue(window.LoadKihuText(text, false));
                Assert.AreEqual(2, window.GameMaster.History.Count);
                Assert.AreEqual("黒の番 (黒:3 白:3)", window.label_Status.Text);
                Assert.IsTrue(window.menuItem_Undo.IsEnabled);

                Assert.IsFalse(window.LoadKihuText("でたらめ", false));
                window.Close();
            });
        }
    }
}
