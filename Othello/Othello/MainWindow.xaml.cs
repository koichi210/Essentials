using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Othello
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 対戦モード。C++版 PLAYER_PLAYER/PLAYER_COM/COM_PLAYER/COM_COM 相当。
        /// PC/CPは「先に書かれている方が黒(先手)」という命名。
        /// </summary>
        private enum PlayMode
        {
            PlayerPlayer,
            PlayerCom,
            ComPlayer,
            ComCom,
        }

        private const int _countdownIntervalMs = 100;
        private const string _kifuFileFilter = "棋譜ファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*";

        internal BoardRenderer BoardRenderer { get; } = new BoardRenderer();
        internal GameMaster GameMaster { get; } = new GameMaster();
        private PlayMode _playMode = PlayMode.PlayerPlayer;
        private int _comLevel = 1;

        // 持ち時間(秒)。-1は「なし」。C++版 time_limit 相当。
        private int _timeLimitSeconds = -1;
        private int _blackTimeMs;
        private int _whiteTimeMs;
        // 時間切れで終局したかどうか(GameMaster.IsGameEndとは別に管理する)
        private bool _isTimedOut;

        // COMの手を少し間を置いてから打つためのタイマー(人間の手と同じ速さで即打つと
        // 何が起きたか分かりにくいため)。
        private readonly DispatcherTimer _comMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

        // 持ち時間のカウントダウン用タイマー。C++版 COUNT_DOWN_CYC(100ms)相当。
        // 常に「今の手番」の残り時間を減らす(手番が変わればおのずと対象も切り替わる)。
        private readonly DispatcherTimer _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_countdownIntervalMs) };

        private bool _isLayoutFixed;

        // MouseClick相当(同じボタンで押して離した時だけ反応させる)のための押下中ボタン
        private MouseButton? _pressedButton;

        public MainWindow()
        {
            InitializeComponent();

            _comMoveTimer.Tick += ComMoveTimer_Tick;
            _countdownTimer.Tick += CountdownTimer_Tick;

            ResetGame();

            // 表示前はDPIが分からないので、まずはデザインサイズ(200x200)で描いておく。
            // 表示後はboardHost_SizeChangedで実際のピクセルサイズに作り直す。
            BoardRenderer.SetDrawArea((int)boardHost.Width, (int)boardHost.Height);
            RedrawBoard();

            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += (s, e) =>
            {
                _comMoveTimer.Stop();
                _countdownTimer.Stop();
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初期デザインのサイズ(SizeToContentで決まった大きさ)を下限にする(C++版のWIN_MIN_SIZE相当)。
            // これより小さくはリサイズできないようにし、盤面が0サイズになる事態を防ぐ。
            UpdateLayout();
            SizeToContent = SizeToContent.Manual;
            MinWidth = ActualWidth;
            MinHeight = ActualHeight;
            Width = ActualWidth;
            Height = ActualHeight;

            // ここからは盤面をウィンドウのサイズに追従させる(WinForms版のAnchor=上下左右相当)
            boardHost.ClearValue(WidthProperty);
            boardHost.ClearValue(HeightProperty);
            menuStrip1.ClearValue(WidthProperty);
            _isLayoutFixed = true;
        }

        #region 盤面を正方形に保つリサイズ(WM_SIZING)

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        private const int WM_SIZING = 0x0214;
        private const int WMSZ_TOP = 3;
        private const int WMSZ_TOPLEFT = 4;
        private const int WMSZ_TOPRIGHT = 5;

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        /// <summary>
        /// ドラッグでのリサイズ中(WM_SIZING)に、幅から高さを逆算して盤面が
        /// 常に正方形になるよう、OSがこれから適用しようとしているウィンドウ矩形を書き換える。
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SIZING && _isLayoutFixed)
            {
                AdjustResizingRectForSquareBoard(hwnd, wParam, lParam);
            }

            return IntPtr.Zero;
        }

        private void AdjustResizingRectForSquareBoard(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
        {
            if (!GetWindowRect(hwnd, out RECT window) || !GetClientRect(hwnd, out RECT client))
            {
                return;
            }

            GetDpiScale(out double scaleX, out double scaleY);

            RECT rect = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));

            // WM_SIZINGの矩形は物理ピクセル。WPFの寸法(DIP)はDPI倍率を掛けて合わせる
            int borderWidth = (window.Right - window.Left) - (client.Right - client.Left);
            int borderHeight = (window.Bottom - window.Top) - (client.Bottom - client.Top);

            int outerWidth = rect.Right - rect.Left;
            int clientWidth = outerWidth - borderWidth;

            // 盤面(boardHost)の「上下の余白の合計」と「左右の余白の合計」の差(DIP)。
            // WPFのMenuは幅によって折り返し段数が変わるので、WinForms版のように起動時に固定せず、
            // 「これからなる幅」でのMenuの高さを測って毎回求める
            double newClientWidthDip = clientWidth / scaleX;
            menuStrip1.Measure(new Size(newClientWidthDip, double.PositiveInfinity));
            double menuHeightDelta = menuStrip1.DesiredSize.Height - menuStrip1.ActualHeight;
            double boardHeightOffset = (rootPanel.ActualHeight + menuHeightDelta - boardHost.ActualHeight) - (rootPanel.ActualWidth - boardHost.ActualWidth);
            int desiredClientHeight = (int)Math.Round(clientWidth / scaleX * scaleY + boardHeightOffset * scaleY);
            int desiredOuterHeight = desiredClientHeight + borderHeight;

            // 最小サイズより小さくはしない
            desiredOuterHeight = Math.Max(desiredOuterHeight, (int)Math.Ceiling(MinHeight * scaleY));

            int edge = wParam.ToInt32();
            bool draggingTopEdge = edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT;

            if (draggingTopEdge)
            {
                // 上端をドラッグしている時は下端を固定し、上端の位置を合わせる
                rect.Top = rect.Bottom - desiredOuterHeight;
            }
            else
            {
                // それ以外(左右・下・左下・右下)は上端を固定し、下端の位置を合わせる
                rect.Bottom = rect.Top + desiredOuterHeight;
            }

            Marshal.StructureToPtr(rect, lParam, true);
        }

        #endregion

        /// <summary>
        /// 画面のDPI倍率(96dpi=1.0)。表示前(PresentationSourceが無い間)は1.0を返す。
        /// </summary>
        private void GetDpiScale(out double scaleX, out double scaleY)
        {
            scaleX = 1.0;
            scaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix m = source.CompositionTarget.TransformToDevice;
                scaleX = m.M11;
                scaleY = m.M22;
            }
        }

        /// <summary>
        /// 新規対局を始める(盤面・手番・持ち時間・時間切れ状態を初期化する)。
        /// </summary>
        private void ResetGame()
        {
            _comMoveTimer.Stop();
            _countdownTimer.Stop();

            GameMaster.Initialize();
            ResetTimeLimit();
        }

        /// <summary>
        /// 時間切れ状態を解除し、両者の残り時間を持ち時間いっぱいに戻す。
        /// </summary>
        private void ResetTimeLimit()
        {
            _isTimedOut = false;
            int limitMs = _timeLimitSeconds < 0 ? 0 : _timeLimitSeconds * 1000;
            _blackTimeMs = limitMs;
            _whiteTimeMs = limitMs;
        }

        /// <summary>
        /// メニュー「ゲーム」→「一手戻す」。C++版 VersProc/OnMenuitemVers 相当
        /// (このプロジェクトではUndoに相当する側)。
        /// </summary>
        private void menuItem_Undo_Click(object sender, RoutedEventArgs e)
        {
            _comMoveTimer.Stop();
            if (GameMaster.Undo())
            {
                _isTimedOut = false;
                RedrawBoard();
            }
        }

        /// <summary>
        /// メニュー「ゲーム」→「一手進める」。Undoで戻した手をやり直す(Redo)。
        /// C++版 ReVersProc/OnMenuitemRevers 相当。
        /// </summary>
        private void menuItem_Redo_Click(object sender, RoutedEventArgs e)
        {
            _comMoveTimer.Stop();
            if (GameMaster.Redo())
            {
                _isTimedOut = false;
                RedrawBoard();
            }
        }

        private void button_ReStart_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult dlgResult = MessageBox.Show(
                this,
                "初期画面にもどります。よろしいですか？",
                "Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (dlgResult == MessageBoxResult.Yes)
            {
                ResetGame();
                RedrawBoard();
            }
        }

        /// <summary>
        /// メニュー「ゲーム」→「開始」。確認なしで新規対局を始める。
        /// </summary>
        private void menuItem_Start_Click(object sender, RoutedEventArgs e)
        {
            ResetGame();
            RedrawBoard();
        }

        private void menuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void menuItem_HowToPlay_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "・黒が先手です。\n" +
                "・自分の石で相手の石を挟める場所をクリックすると石を置けます。\n" +
                "・挟まれた相手の石は自分の色にひっくり返ります。\n" +
                "・置ける場所が無い場合は自動的にパスされます。\n" +
                "・双方とも置ける場所が無くなったら終局、石数の多い方が勝ちです。",
                "遊び方",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void menuItem_Version_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "Othello (C#/WPF版)\nC++版(MFC)からの移植",
                "バージョン情報",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// メニュー「対戦モード」。選び直したら新規対局から始める。
        /// </summary>
        internal void menuItem_PlayMode_Click(object sender, RoutedEventArgs e)
        {
            var clicked = (MenuItem)sender;

            if (clicked == menuItem_PP)
            {
                _playMode = PlayMode.PlayerPlayer;
            }
            else if (clicked == menuItem_PC)
            {
                _playMode = PlayMode.PlayerCom;
            }
            else if (clicked == menuItem_CP)
            {
                _playMode = PlayMode.ComPlayer;
            }
            else if (clicked == menuItem_CC)
            {
                _playMode = PlayMode.ComCom;
            }

            CheckOnly(clicked, menuItem_PP, menuItem_PC, menuItem_CP, menuItem_CC);

            ResetGame();
            RedrawBoard();
        }

        /// <summary>
        /// IsCheckable=Trueのメニューはクリックで一旦反転するので、itemsのうちclickedだけをチェック状態に揃え直す。
        /// </summary>
        private static void CheckOnly(MenuItem clicked, params MenuItem[] items)
        {
            foreach (MenuItem item in items)
            {
                item.IsChecked = item == clicked;
            }
        }

        /// <summary>
        /// メニュー「COMレベル」。
        /// </summary>
        internal void menuItem_ComLevel_Click(object sender, RoutedEventArgs e)
        {
            var clicked = (MenuItem)sender;

            if (clicked == menuItem_ComLevel1)
            {
                _comLevel = 1;
            }
            else if (clicked == menuItem_ComLevel2)
            {
                _comLevel = 2;
            }
            else if (clicked == menuItem_ComLevel3)
            {
                _comLevel = 3;
            }

            CheckOnly(clicked, menuItem_ComLevel1, menuItem_ComLevel2, menuItem_ComLevel3);
        }

        /// <summary>
        /// メニュー「持ち時間」。選び直したら新規対局から始める。
        /// </summary>
        internal void menuItem_TimeLimit_Click(object sender, RoutedEventArgs e)
        {
            var clicked = (MenuItem)sender;

            if (clicked == menuItem_TimeNone)
            {
                _timeLimitSeconds = -1;
            }
            else if (clicked == menuItem_Time30s)
            {
                _timeLimitSeconds = 30;
            }
            else if (clicked == menuItem_Time1m)
            {
                _timeLimitSeconds = 60;
            }
            else if (clicked == menuItem_Time2m)
            {
                _timeLimitSeconds = 120;
            }
            else if (clicked == menuItem_Time3m)
            {
                _timeLimitSeconds = 180;
            }
            else if (clicked == menuItem_Time5m)
            {
                _timeLimitSeconds = 300;
            }
            else if (clicked == menuItem_Time10m)
            {
                _timeLimitSeconds = 600;
            }
            else if (clicked == menuItem_Time15m)
            {
                _timeLimitSeconds = 900;
            }

            CheckOnly(clicked, menuItem_TimeNone, menuItem_Time30s, menuItem_Time1m, menuItem_Time2m,
                menuItem_Time3m, menuItem_Time5m, menuItem_Time10m, menuItem_Time15m);

            // WinForms版のVisible=falseと同じく、非表示でも場所は空けておく(Hidden)
            label_Time.Visibility = _timeLimitSeconds >= 0 ? Visibility.Visible : Visibility.Hidden;

            ResetGame();
            RedrawBoard();
        }

        /// <summary>
        /// メニュー「棋譜」→「表示」。ここまでの手順をメッセージボックスに表示する。
        /// </summary>
        private void menuItem_KifuShow_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, Kifu.ToText(GameMaster.History), "棋譜", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// メニュー「棋譜」→「保存」。ここまでの手順をテキストファイルに保存する。
        /// </summary>
        private void menuItem_KifuSave_Click(object sender, RoutedEventArgs e)
        {
            if (GameMaster.History.Count == 0)
            {
                MessageBox.Show(this, "まだ1手も打たれていないよ。", "棋譜の保存", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = _kifuFileFilter,
                FileName = "Othello_kifu.txt",
            };

            if (dialog.ShowDialog(this) == true)
            {
                System.IO.File.WriteAllText(dialog.FileName, Kifu.ToText(GameMaster.History));
            }
        }

        /// <summary>
        /// メニュー「棋譜」→「読込」。テキストファイルから手順を読み込み、最初から再生する。
        /// </summary>
        private void menuItem_KifuLoad_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = _kifuFileFilter,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string text;
            try
            {
                text = System.IO.File.ReadAllText(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "棋譜ファイルを読み込めなかったよ。\n" + ex.Message, "棋譜の読込", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadKifuText(text);
        }

        /// <summary>
        /// 棋譜テキストを最初から再生する(「棋譜」→「読込」のファイル読込以降の処理)。
        /// 失敗した場合はメッセージを表示する。
        /// </summary>
        internal bool LoadKifuText(string text, bool showError = true)
        {
            _comMoveTimer.Stop();
            _countdownTimer.Stop();

            bool ok = Kifu.TryReplay(text, GameMaster, out string errorMessage);
            ResetTimeLimit();

            RedrawBoard();

            if (!ok && showError)
            {
                MessageBox.Show(this, errorMessage ?? "棋譜を読み込めなかったよ。", "棋譜の読込", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return ok;
        }

        /// <summary>
        /// 現在の対戦モードで、colorがCOM操作かどうか。
        /// </summary>
        private bool IsComTurn(StoneColor color)
        {
            switch (_playMode)
            {
                case PlayMode.PlayerCom:
                    return color == StoneColor.White;
                case PlayMode.ComPlayer:
                    return color == StoneColor.Black;
                case PlayMode.ComCom:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 現在の手番がCOMなら、少し間を置いてからCOMに一手打たせる。
        /// </summary>
        private void MaybeTriggerComMove()
        {
            if (!GameMaster.IsGameEnd && IsComTurn(GameMaster.CurrentTurn))
            {
                _comMoveTimer.Start();
            }
        }

        internal bool IsComMovePending => _comMoveTimer.IsEnabled;

        internal bool IsCountdownRunning => _countdownTimer.IsEnabled;

        private void ComMoveTimer_Tick(object sender, EventArgs e)
        {
            DoComMove();
        }

        /// <summary>
        /// COMに一手打たせる(comMoveTimerのTick処理本体)。
        /// </summary>
        internal void DoComMove()
        {
            _comMoveTimer.Stop();

            if (_isTimedOut || GameMaster.IsGameEnd || !IsComTurn(GameMaster.CurrentTurn))
            {
                return;
            }

            if (ComPlayer.TryGetMove(GameMaster, GameMaster.CurrentTurn, _comLevel, out int x, out int y))
            {
                GameMaster.TryPut(x, y);
            }

            RedrawBoard();
        }

        /// <summary>
        /// 持ち時間のカウントダウン。今の手番の残り時間を100msずつ減らし、
        /// 0になったらその手番の時間切れ負けとして対局を止める(C++版 OnTimer/TimeOutProc)。
        /// </summary>
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            CountDown(_countdownIntervalMs);
        }

        internal void CountDown(int elapsedMs)
        {
            if (GameMaster.CurrentTurn == StoneColor.Black)
            {
                _blackTimeMs -= elapsedMs;
            }
            else
            {
                _whiteTimeMs -= elapsedMs;
            }

            UpdateTimeLabel();

            if (_blackTimeMs <= 0 || _whiteTimeMs <= 0)
            {
                _countdownTimer.Stop();
                _comMoveTimer.Stop();
                _isTimedOut = true;
                UpdateStatusLabel();
            }
        }

        private void boardHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _pressedButton = e.ChangedButton;
            boardHost.CaptureMouse();
        }

        private void boardHost_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // WinForms版のMouseClick相当: 盤面上で押したボタンと同じボタンを盤面上で離した時だけ反応する
            bool isClick = _pressedButton == e.ChangedButton;
            _pressedButton = null;
            if (boardHost.IsMouseCaptured)
            {
                boardHost.ReleaseMouseCapture();
            }
            if (!isClick)
            {
                return;
            }

            HandleBoardClick(e.GetPosition(boardHost));
        }

        /// <summary>
        /// 盤面上の位置(boardHost基準のDIP座標)がクリックされた時の処理。
        /// 置けたらtrue。
        /// </summary>
        internal bool HandleBoardClick(Point position)
        {
            if (_isTimedOut || GameMaster.IsGameEnd)
            {
                return false;
            }

            // COMの手番中(タイマー待ち)は人間のクリックを受け付けない
            if (IsComTurn(GameMaster.CurrentTurn))
            {
                return false;
            }

            double boardWidth = boardHost.ActualWidth;
            double boardHeight = boardHost.ActualHeight;
            if (boardWidth <= 0 || boardHeight <= 0 || BoardRenderer.Width <= 0 || BoardRenderer.Height <= 0)
            {
                return false;
            }

            // 盤面の画像(BoardRenderer.Canvas)はboardHost全体に引き伸ばして表示しているので、
            // DIP座標を画像のピクセル座標に直してから、BoardRenderer側の罫線と同じ計算式でマス目を求める
            // (DPI倍率はここでの比率に含まれる)。
            int pixelX = (int)Math.Floor(position.X * BoardRenderer.Width / boardWidth);
            int pixelY = (int)Math.Floor(position.Y * BoardRenderer.Height / boardHeight);
            if (!BoardRenderer.TryGetCell(pixelX, pixelY, BoardRenderer.Width, BoardRenderer.Height, out int x, out int y))
            {
                return false;
            }

            if (GameMaster.TryPut(x, y))
            {
                RedrawBoard();
                return true;
            }
            return false;
        }

        private void boardHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (boardHost.ActualWidth <= 0 || boardHost.ActualHeight <= 0)
            {
                return;
            }

            // 盤面は実際の画面ピクセル数で描く(高DPIでもぼやけないように)
            GetDpiScale(out double scaleX, out double scaleY);
            int pixelWidth = (int)Math.Round(boardHost.ActualWidth * scaleX);
            int pixelHeight = (int)Math.Round(boardHost.ActualHeight * scaleY);
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                return;
            }

            if (pixelWidth != BoardRenderer.Width || pixelHeight != BoardRenderer.Height || BoardRenderer.Canvas == null)
            {
                BoardRenderer.SetDrawArea(pixelWidth, pixelHeight);
                RedrawBoard();
            }
        }

        /// <summary>
        /// 盤面・置ける場所のマーク・手番表示をまとめて再描画し、COMの手番なら次の一手を予約する。
        /// 終局している場合や、これから打つのがCOMの場合は置ける場所のマークは出さない
        /// (マークは「あなたがここに置けるよ」という案内なので)。
        /// </summary>
        private void RedrawBoard()
        {
            bool showNotice = !_isTimedOut && !GameMaster.IsGameEnd && !IsComTurn(GameMaster.CurrentTurn);
            bool[,] validMoves = showNotice ? GameMaster.GetValidMoves(GameMaster.CurrentTurn) : null;
            BoardRenderer.DrawField(GameMaster.Table, validMoves);
            GetDpiScale(out double scaleX, out double scaleY);
            pictureBoxField.Source = BoardImage.ToBitmapSource(BoardRenderer.Canvas, 96.0 * scaleX, 96.0 * scaleY);
            UpdateStatusLabel();
            UpdateTimeLabel();

            menuItem_Undo.IsEnabled = GameMaster.CanUndo;
            menuItem_Redo.IsEnabled = GameMaster.CanRedo;

            if (!_isTimedOut && !GameMaster.IsGameEnd && _timeLimitSeconds >= 0)
            {
                _countdownTimer.Start();
            }
            else
            {
                _countdownTimer.Stop();
            }

            MaybeTriggerComMove();
        }

        /// <summary>
        /// 持ち時間の残りをlabel_Timeに表示する(持ち時間「なし」なら何も表示しない)。
        /// </summary>
        private void UpdateTimeLabel()
        {
            if (_timeLimitSeconds < 0)
            {
                return;
            }

            int blackSec = Math.Max(0, _blackTimeMs) / 1000;
            int whiteSec = Math.Max(0, _whiteTimeMs) / 1000;
            label_Time.Text = string.Format("残り時間 黒:{0}秒 白:{1}秒", blackSec, whiteSec);
        }

        /// <summary>
        /// 手番・石数・終局結果をlabel_Statusに表示する
        /// </summary>
        private void UpdateStatusLabel()
        {
            GameMaster.CountStones(out int blackCount, out int whiteCount);

            if (_isTimedOut)
            {
                string loserName = _blackTimeMs <= 0 ? "黒" : "白";
                string winnerName = _blackTimeMs <= 0 ? "白" : "黒";
                label_Status.Text = string.Format("時間切れ({0}) {1}の勝ち 黒:{2} 白:{3}", loserName, winnerName, blackCount, whiteCount);
                return;
            }

            if (GameMaster.IsGameEnd)
            {
                string winner;
                if (blackCount > whiteCount)
                {
                    winner = "黒の勝ち";
                }
                else if (whiteCount > blackCount)
                {
                    winner = "白の勝ち";
                }
                else
                {
                    winner = "引き分け";
                }

                label_Status.Text = string.Format("終局 黒:{0} 白:{1} {2}", blackCount, whiteCount, winner);
            }
            else
            {
                string turnName = GameMaster.CurrentTurn == StoneColor.Black ? "黒" : "白";
                label_Status.Text = string.Format("{0}の番 (黒:{1} 白:{2})", turnName, blackCount, whiteCount);
            }
        }
    }
}
