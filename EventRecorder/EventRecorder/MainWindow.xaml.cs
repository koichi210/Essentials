using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using StandardTemplate;
using StandardTemplate.Wpf;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorder
{
    // WinForms版EventRecorder(Form1.cs)のWPF版。Form1と同じく、処理のまとまりごとにpartialファイルへ分けてある:
    //   MainWindow.xaml.cs        … 起動・終了・アプリ設定(ウィンドウサイズ/ホットキー)・システムメニュー・モード切替・トレイ
    //   MainWindow.Recording.cs   … 記録(グローバルフック)
    //   MainWindow.Playback.cs    … 再生(SendInput)・レコード表の編集操作
    //   MainWindow.Playlist.cs    … プレイリスト
    //   MainWindow.Profile.cs     … プロファイル(JSON)の保存/読込
    public partial class MainWindow : Window
    {
        private readonly StcUtils _util = new StcUtils();

        // 記録中/再生中フラグ。UIスレッド(ボタンクリック・グローバルホットキー)と
        // 再生用バックグラウンドスレッド(Task.Run側)の両方から読み書きされるため、volatileにしている
        private volatile Boolean _isRecording;
        private volatile Boolean _isPlaying;
        private volatile Boolean _stopPlayRequested;

        // 実行中の再生スレッド(単発再生/プレイリスト実行のTask.Run)。終了時に停止を伝えた後、
        // 本当に止まるまで待つために持っておく(止まる前に終了すると、再生スレッドが閉店後の店内で動き回る)
        private Task _playbackTask;

        // 終了処理(PrepareForExit)に入ったらtrue。以後は記録/再生を新しく始めない
        private Boolean _isExiting;

        // タイトルバーに表示する再生中のループ進捗(WinForms版と同じ。再生スレッドから書き込み、UIはUpdateTitleで読むだけ)
        private int _playbackOverallLoopNo;
        private int _playbackOverallLoopMax;
        private int _playbackInnerLoopNo;
        private int _playbackInnerLoopMax;

        // 直前のイベント時刻(記録の待機ms算出用)
        private int _lastEventTick;

        // 記録中/再生中にハイライトしている行のインデックス(-1ならハイライト無し)
        private int _highlightedEventRowIndex = -1;

        // プレイリスト実行中にハイライトしている行(dataGrid_Playlist側)のインデックス
        private int _highlightedPlaylistRowIndex = -1;

        // 再生開始時のマウスカーソル位置(再生終了後に戻すため)。WinForms版と同じくCursor.Position(DPI非対応の座標)を使う
        private System.Drawing.Point _cursorPositionBeforePlay;

        // 記録中に押下中のキー(OSのキーリピートによるKeyDown連発を抑制するため、
        // KeyUpが来るまで「押されている」とみなす)。記録セッションの開始のたびにクリアする
        private readonly HashSet<Keys> _pressedKeys = new HashSet<Keys>();

        // ホットキーの押下状態。記録セッションとは独立して管理する(_pressedKeysをClearしても消えないように)
        private readonly HashSet<Keys> _pressedHotkeys = new HashSet<Keys>();

        // タイトルバーの基本文字列。記録中/再生中はここに状態を追記する
        private const String _baseTitle = "EventRecorder";

        // 記録/再生の切り替えホットキー。設定画面([[HotkeySettingsWindow]]、システムメニューから開く)で変更でき、
        // 変更内容はEventRecorder.json(WinForms版と共通のアプリ設定ファイル)に保存される
        private Keys _hotkeyToggleRecord = HotkeyDefaults.Record;
        private Keys _hotkeyTogglePlay = HotkeyDefaults.Play;

        // キーバインド設定画面を開いている間だけtrueにする(テストで押したキーがホットキーとして誤発動しないように)
        private Boolean _isHotkeySettingsOpen;

        // 記録したがまだグリッドに反映していない行(フックのコールバックを描画待ちで塞がないよう、
        // 一旦ここに貯めてタイマーでまとめて反映する)
        private readonly List<String[]> _pendingRows = new List<String[]>();
        private readonly DispatcherTimer _gridFlushTimer;

        // マウスカーソル座標の常時表示用タイマー
        private readonly DispatcherTimer _mousePosTimer;

        // マクロ・プレイリストのユーザーデータ置き場。WinForms版と同じ%LOCALAPPDATA%\EventRecorder\(既定)を使うので、
        // WinForms版で保存したプロファイル・設定をそのまま読める([[_Common\UserDataLocation.cs]])
        private const String _appName = "EventRecorder";
        private readonly String _userDataFolder;

        // グローバルフック(キーボード/マウス)を張ってよいか。テスト用のコンストラクタではfalseにして、
        // このPCのキーボード/マウスに一切影響しないようにする
        private readonly Boolean _isHookEnabled;

        // レコード表・プレイリストの中身(1要素=1行)と、それぞれのUndo/Redo(WinForms版のDataGridViewEx相当)
        internal GridRowCollection<EventRow> EventRows { get; } = new GridRowCollection<EventRow>();
        internal GridRowCollection<PlaylistRow> PlaylistRows { get; } = new GridRowCollection<PlaylistRow>();
        internal GridUndoRedo<EventRow> EventsUndo { get; }
        internal GridUndoRedo<PlaylistRow> PlaylistUndo { get; }

        // プレイリストの設定ファイル列のプルダウンの選択肢(comboBox_Profileの一覧のコピー)
        internal ObservableCollection<String> PlaylistFileItems { get; } = new ObservableCollection<String>();

        // 起動時に復元する左右分割の境界線位置(レイアウトが確定するLoadedで適用する)
        private int _pendingSplitterDistance;

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(_appName), true)
        {
        }

        // internal: テストでは実際のユーザーデータフォルダではなく一時フォルダを渡す(グローバルフックも張らない)
        internal MainWindow(String dataFolder) : this(dataFolder, false)
        {
        }

        private MainWindow(String dataFolder, Boolean enableHooks)
        {
            InitializeComponent();
            _userDataFolder = dataFolder;
            _isHookEnabled = enableHooks;

            Resources["PlaylistFileItems"] = PlaylistFileItems;
            dataGrid_Events.ItemsSource = EventRows;
            dataGrid_Playlist.ItemsSource = PlaylistRows;
            EventsUndo = new GridUndoRedo<EventRow>(EventRows);
            PlaylistUndo = new GridUndoRedo<PlaylistRow>(PlaylistRows);
            PlaylistUndo.RowCellChanged += PlaylistRow_CellChanged;

            // ウィンドウサイズ+境界線位置+ホットキーを、前回終了時の状態(EventRecorder.json、無ければ既定値のまま)で復元する
            LoadAppSettings();

            _util.SetCurrentDirectory();

            _gridFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _gridFlushTimer.Tick += (s, e) => FlushPendingRows();

            // マウスカーソルの座標を起動中ずっと表示しておく(記録/再生の状態と関係なく常時更新)
            _mousePosTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _mousePosTimer.Tick += (s, e) =>
            {
                System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
                label_MousePos.Text = "Mouse: " + p.X + ", " + p.Y;
            };
            _mousePosTimer.Start();

            if (_isHookEnabled)
            {
                // キーボードフックはホットキー監視のためアプリ起動中ずっと張っておく。
                // マウスフックは記録中だけでよいのでToggleRecording側で開始/停止する
                GlobalHook.KeyboardHook.AddEvent(OnKeyboardEvent);
                GlobalHook.KeyboardHook.Start();

                // 画面ロック・サインイン(セッション切替)と、シャットダウン/再起動/サインアウトの通知。
                // テスト用のコンストラクタ(フック無し)では、テスト実行中のPCの状態に反応しないよう購読しない
                Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
                if (Application.Current != null)
                {
                    Application.Current.SessionEnding += Application_SessionEnding;
                }
            }

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
            StateChanged += (s, e) => UpdateTaskbarVisibility();

            // _userDataFolder配下のプロファイル一覧をコンボボックスに表示する。一覧の先頭が選ばれ、
            // そのまま読み込まれる(=起動時は一覧の先頭を読み込む。WinForms版と同じ)
            UpdateProfileListAll("");

            // プレイリストの設定ファイル列は、comboBox_Profileと全く同じ一覧を表示する
            SyncPlaylistFileItems();
            UpdatePlaylistMissingFileHighlights();

            // プレイリストが空のままだと使うたびに毎回「行の追加」を押す羽目になるため、起動時点で空行を2行用意しておく。
            // ただし、読み込んだプロファイルに既にプレイリストの中身が入っていた場合は、その内容を優先する
            if (PlaylistRows.Count == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    AddPlaylistRow(i, isEnabled: false);
                }
            }

            // 起動時のデフォルトモードは「レコード」。ただしプロファイルが読み込まれていれば、そのプロファイルの
            // IsRecordModeが既に反映済みなので上書きしない(プロファイルが1つも無い時だけ適用する)
            if (comboBox_Profile.Items.Count == 0)
            {
                radioButton_Record.IsChecked = true;
            }
            UpdateModeHighlight();

            // システムメニュー(タイトルバー右クリック/左上アイコン)に「データ保存先を変更」「キーバインドを設定」を足す
            WpfDataFolderMenu.Attach(this, ChangeDataFolder);
            SourceInitialized += (s, e) => AppendHotkeyMenu();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 境界線位置(左側パネルの幅)を復元する。WinForms版のSplitContainer(FixedPanel=None)と同じく、
            // ウィンドウのサイズ変更では左右が比率を保って伸び縮みするよう、比率(*)で指定する
            double total = grid_Split.ActualWidth - 4;
            if (_pendingSplitterDistance > 0 && total > 0)
            {
                double left = Math.Max(column_SplitLeft.MinWidth, Math.Min(_pendingSplitterDistance, total - column_SplitRight.MinWidth));
                if (left > 0 && total - left > 0)
                {
                    column_SplitLeft.Width = new GridLength(left, GridUnitType.Star);
                    column_SplitRight.Width = new GridLength(total - left, GridUnitType.Star);
                }
            }
        }

        // *******************************************************************************
        // モード切替(レコード/プレイバック)

        // 今選択中のモードのグループボックスだけ背景色をハイライトする。
        // ボタンをDisableにする方式は分かりにくいという理由でやめ、色分けだけにした(WinForms版と同じ)。
        // 実行中の行のハイライト(LightYellow)と被らないよう、別の色にしてある
        private static readonly Brush _modeHighlightBrush = CreateFrozenBrush(Color.FromRgb(205, 255, 230));

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void radioButton_Mode_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateModeHighlight();
        }

        private void UpdateModeHighlight()
        {
            panel_Playback.Background = radioButton_Playback.IsChecked == true ? _modeHighlightBrush : Brushes.Transparent;
            panel_Record.Background = radioButton_Record.IsChecked == true ? _modeHighlightBrush : Brushes.Transparent;
        }

        // グループボックス自体(枠・余白部分)や、グループボックス内のコントロールをクリックしたら、
        // そのグループボックスと同じ名前のラジオボタンをCheckedにする(WinForms版のSetupGroupBoxRadioSync)
        private void panel_Record_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            radioButton_Record.IsChecked = true;
        }

        private void panel_Playback_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            radioButton_Playback.IsChecked = true;
        }

        // *******************************************************************************
        // ショートカットキー(Ctrl+S=プロファイル保存、Ctrl+F=検索、Ctrl+H=置換)。
        // テキストボックス等にフォーカスがあっても拾えるよう、ウィンドウのPreviewKeyDownで処理する(WinForms版のProcessCmdKey)

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.S:
                    e.Handled = true;
                    SaveProfileWithPrompt();
                    break;
                case Key.F:
                    e.Handled = true;
                    ShowFindReplaceDialog(FindReplaceMode.Find);
                    break;
                case Key.H:
                    e.Handled = true;
                    ShowFindReplaceDialog(FindReplaceMode.Replace);
                    break;
            }
        }

        // Ctrl+F/Ctrl+Hのダイアログは、既に開いていれば作り直さずモードだけ切り替えて前面に出す(重複オープン防止)
        private FindReplaceWindow _findReplaceWindow;

        private void ShowFindReplaceDialog(FindReplaceMode mode)
        {
            if (_isRecording || _isPlaying)
            {
                return;
            }

            if (_findReplaceWindow == null)
            {
                String initialText = "";
                if (mode == FindReplaceMode.Find)
                {
                    EventRow currentRow = dataGrid_Events.CurrentCell.Item as EventRow;
                    if (currentRow != null && dataGrid_Events.CurrentCell.Column != null)
                    {
                        initialText = currentRow.GetCell(WpfGridHelper.GetColumnName(dataGrid_Events.CurrentCell.Column));
                    }
                }

                _findReplaceWindow = new FindReplaceWindow(dataGrid_Events, EventRows, EventsUndo, mode, initialText);
                _findReplaceWindow.Owner = this;
                _findReplaceWindow.Closed += (s, e) => _findReplaceWindow = null;
                _findReplaceWindow.Show();
            }
            else
            {
                _findReplaceWindow.SetMode(mode);
                _findReplaceWindow.Activate();
            }
        }

        // *******************************************************************************
        // システムメニュー(データ保存先の変更・キーバインド設定)

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, Boolean bRevert);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Boolean AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, String lpNewItem);

        private const uint MF_STRING = 0x0;
        private const int WM_SYSCOMMAND = 0x112;

        // 「データ保存先を変更」はWpfDataFolderMenu([[_Common/Wpf/WpfDataFolderMenu.cs]])が0x1000で追加する。
        // EventRecorder独自の項目のIDはWinForms版と同じ0x1010(16の倍数かつ0xF000未満)
        private const int _sysMenuIdChangeHotkeys = 0x1010;

        private void AppendHotkeyMenu()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            AppendMenu(GetSystemMenu(hWnd, false), MF_STRING, (UIntPtr)_sysMenuIdChangeHotkeys, "キーバインドを設定(&K)...");

            HwndSource.FromHwnd(hWnd).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref Boolean handled) =>
            {
                if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == _sysMenuIdChangeHotkeys)
                {
                    handled = true;
                    // システムメニューのメッセージ処理中にモーダルダイアログを開かないよう、処理が戻ってから開く
                    Dispatcher.BeginInvoke(new Action(ChangeHotkeys));
                }
                return IntPtr.Zero;
            });
        }

        // キーバインド設定画面を開き、「保存」で閉じられたら現在有効なホットキーとEventRecorder.jsonの両方を更新する
        private void ChangeHotkeys()
        {
            if (WarnIfBusy("EventRecorder - キーバインド設定"))
            {
                return;
            }

            // 設定画面を開いている間は、テキストボックスに試しに入力したキーがグローバルフック(OnKeyboardEvent)側にも
            // 同時に届いて、記録/再生のホットキーとして誤発動してしまう。その間はOnKeyboardEventの処理そのものを止める
            _isHotkeySettingsOpen = true;
            try
            {
                HotkeySettingsWindow window = new HotkeySettingsWindow(_hotkeyToggleRecord, _hotkeyTogglePlay);
                window.Owner = this;
                if (window.ShowDialog() != true)
                {
                    return;
                }

                _hotkeyToggleRecord = window.RecordHotkey;
                _hotkeyTogglePlay = window.PlayHotkey;

                if (!SaveAppSettings())
                {
                    MessageBox.Show(
                        this,
                        "キーバインド設定の保存に失敗したよ。今回のセッションだけは新しい設定のまま動くよ",
                        "EventRecorder - キーバインド設定",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                _isHotkeySettingsOpen = false;
            }
        }

        // 記録中/再生中なら設定を変更できない旨を警告してtrueを返す(システムメニューからの設定変更共通)
        private Boolean WarnIfBusy(String caption)
        {
            if (!_isRecording && !_isPlaying)
            {
                return false;
            }

            MessageBox.Show(
                this,
                "記録中/再生中は変更できないよ。停止してから試してね",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return true;
        }

        // 保存先フォルダを選び直し、exe直下のポインタファイル(DataFolder.txt)を書き換える。
        // 実行中の_userDataFolderはその場では切り替えない(変更は次回起動時から反映される、WinForms版と同じ)
        private void ChangeDataFolder()
        {
            if (WarnIfBusy(_appName + " - データ保存先の変更"))
            {
                return;
            }

            // EventRecorder.json(アプリの設定ファイル、プロファイルではない)は引っ越し対象から外す
            DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, _appName, IsNonProfileSettingFile));
        }

        // *******************************************************************************
        // 最小化時のタスクトレイ格納

        private System.Windows.Forms.NotifyIcon _notifyIconTray;

        // タスクトレイに格納するためにウィンドウを隠している最中か
        private Boolean _isHiddenToTray;

        // checkBox_MinimizeOnPlay(実行時にウィンドウを最小化する)がオンかつ最小化中の時だけ、
        // タスクバーから消してシステムトレイアイコンを表示する(WinForms版と同じ)。
        // WPFでは最小化中にShowInTaskbarを切り替えると画面左下に小さなタイトルバーが残ってしまうため、
        // タスクバーから消す代わりにウィンドウ自体を隠す(Hide)方式にしている
        private void UpdateTaskbarVisibility()
        {
            if (checkBox_MinimizeOnPlay.IsChecked == true && WindowState == WindowState.Minimized)
            {
                EnsureTrayIcon();
                _notifyIconTray.Visible = true;
                if (!_isHiddenToTray)
                {
                    _isHiddenToTray = true;
                    Hide();
                }
            }
            else
            {
                if (_notifyIconTray != null)
                {
                    _notifyIconTray.Visible = false;
                }
                if (_isHiddenToTray)
                {
                    _isHiddenToTray = false;
                    Show();
                }
            }
        }

        private void checkBox_MinimizeOnPlay_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateTaskbarVisibility();
        }

        private void EnsureTrayIcon()
        {
            if (_notifyIconTray != null)
            {
                return;
            }

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("元に戻す(&R)", null, (s, e) => RestoreFromTray());
            menu.Items.Add("終了(&X)", null, (s, e) => Close());

            _notifyIconTray = new System.Windows.Forms.NotifyIcon
            {
                Text = "EventRecorder",
                ContextMenuStrip = menu,
                Visible = false,
            };
            try
            {
                using (Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/EventRecorder.ico")).Stream)
                {
                    _notifyIconTray.Icon = new System.Drawing.Icon(stream);
                }
            }
            catch (IOException)
            {
                _notifyIconTray.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIconTray.DoubleClick += (s, e) => RestoreFromTray();
        }

        // トレイアイコンのダブルクリック/右クリックメニュー「元に戻す」共通の復元処理
        private void RestoreFromTray()
        {
            WindowState = WindowState.Normal;
            UpdateTaskbarVisibility();
            Activate();
        }

        // *******************************************************************************
        // 終了処理・アプリ設定(ウィンドウサイズ+境界線位置+ホットキー)

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            PrepareForExit();
        }

        // Windowsのシャットダウン/再起動/サインアウト。WPFはこの時Window.Closingを呼ばずに終了するため
        // (Window.Closingのドキュメントに明記されている仕様)、ここからも同じ終了処理を呼ぶ
        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            PrepareForExit();
        }

        // 終了処理。閉じるボタン・トレイの「終了」(Closing)と、シャットダウン等(SessionEnding)の両方から呼ばれる。
        // 以前は記録/再生を止めずに終了していたため、再生スレッドが終了後も入力を送り続けたり、
        // 止まったUIスレッドへDispatcher.Invokeしたりしていた。
        // 「店じまい」の順番: ①ホットキーを受け付けない ②記録を止める ③再生に停止を伝えて止まるまで待つ
        // ④設定を保存 ⑤タイマー・フックを片付ける。2回目以降の呼び出しは何もしない
        internal void PrepareForExit()
        {
            if (_isExiting)
            {
                return;
            }
            _isExiting = true;

            // ①止めている最中にホットキーで記録/再生が再開されないよう、先にキーボードフックを外す
            GlobalHook.KeyboardHook.Stop();

            // ②記録中なら、溜まっている行を表へ反映してから止める(マウスフックもここで外れる)
            if (_isRecording)
            {
                ToggleRecording();
            }

            // ③再生中なら停止を伝え、再生スレッドが後片付け(押しっぱなしのキーを離す等)を終えるまで待つ
            if (_isPlaying)
            {
                _stopPlayRequested = true;
                Task task = _playbackTask;
                if (task != null)
                {
                    SessionGuard.WaitWhilePumping(() => task.IsCompleted, PumpDispatcher, SessionGuard.ExitWaitTimeoutMs);
                }
            }

            // ④
            SaveAppSettings();

            // ⑤
            _gridFlushTimer.Stop();
            _mousePosTimer.Stop();
            GlobalHook.MouseHook.Stop();
            GlobalHook.KeyboardHook.Stop();
        }

        // UIスレッドに溜まっている処理(再生スレッドからのDispatcher.Invoke等)を、優先度の高いものから実行させる。
        // WinFormsのApplication.DoEvents相当
        private void PumpDispatcher()
        {
            try
            {
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            }
            catch (InvalidOperationException)
            {
                // Dispatcherの処理が一時停止中で回せない時は、メッセージを処理せずに待つだけにする(時間切れで抜ける)
            }
        }

        // *******************************************************************************
        // 画面ロック・サインイン(セッション切替)

        private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            SessionChange change = SessionGuard.Classify(e.Reason);
            if (_isExiting || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            // 通常はUIスレッドで通知されるが、念のため別スレッドから来た場合はUIスレッドへ回す
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => HandleSessionChange(change)));
                return;
            }

            HandleSessionChange(change);
        }

        internal void HandleSessionChange(SessionChange change)
        {
            if (_isExiting)
            {
                return;
            }

            switch (change)
            {
                case SessionChange.Suspend:
                    SuspendForSessionLock();
                    break;
                case SessionChange.Resume:
                    ResumeAfterSessionUnlock();
                    break;
            }
        }

        // 画面ロック等でこのセッションの画面から離れる時、記録/再生を止める(WinForms版と同じ)。
        // ・再生: ロック中のSendInputはロック画面(別デスクトップ)に届かず空振りする上、ロック解除した瞬間に
        //   途中の行から勝手に再開してしまうため、ここで停止する(押しっぱなしのキーは再生スレッドが離す)
        // ・記録: ロック中は入力がフックに来ず、ロック操作(Win+L等)のKeyUpも取りこぼして
        //   「押しっぱなし」扱いのキーが残るため、ここで記録を終える(記録済みの行はそのまま残る)
        private void SuspendForSessionLock()
        {
            if (_isRecording)
            {
                ToggleRecording();
            }

            if (_isPlaying)
            {
                _stopPlayRequested = true;
            }

            _pressedHotkeys.Clear();
        }

        // ロック解除・サインインでこのセッションの画面に戻ってきた時の立て直し(WinForms版と同じ)。
        // ・ロック直前に押したホットキーのKeyUpを取りこぼしていると、次の1回が「リピート」扱いで無視されるため押下状態を消す
        // ・低レベルキーボードフックは、コールバックが一定時間内に戻らないとWindowsに黙って外される
        //   (ロック解除直後は画面の再描画などでUIスレッドが詰まりやすい)。外れたかどうかは知る手段が無いので張り直す
        private void ResumeAfterSessionUnlock()
        {
            _pressedHotkeys.Clear();

            if (!_isHookEnabled)
            {
                return;
            }

            try
            {
                GlobalHook.KeyboardHook.Stop();
                GlobalHook.KeyboardHook.ClearEvent();
                GlobalHook.KeyboardHook.AddEvent(OnKeyboardEvent);
                GlobalHook.KeyboardHook.Start();
            }
            catch (Win32Exception)
            {
                // 張り直しに失敗してもエラー表示はしない(ロック解除のたびにダイアログが出るのを避ける)。
                // ホットキーは効かなくなるが、ボタン操作は使えるし、次のロック解除でもう一度張り直しを試みる
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // SystemEvents・Applicationのイベントは閉じた後も残るので、閉じたウィンドウへ通知が来ないよう解除する
            Microsoft.Win32.SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            if (Application.Current != null)
            {
                Application.Current.SessionEnding -= Application_SessionEnding;
            }

            // 残しておくと、終了後もタスクトレイにアイコンの抜け殻が残ってしまう
            if (_notifyIconTray != null)
            {
                _notifyIconTray.Visible = false;
                _notifyIconTray.Dispose();
                _notifyIconTray = null;
            }
        }

        // アプリ本体の設定をまとめて保存するファイル名(WinForms版と同じ)。プロファイル一覧には出さない
        private const String _appSettingsFileName = "EventRecorder.json";

        // _userDataFolder直下にプロファイルと混在して置かれる、アプリ自体の設定ファイルかどうか
        private static Boolean IsNonProfileSettingFile(String filePath)
        {
            return String.Equals(Path.GetFileName(filePath), _appSettingsFileName, StringComparison.OrdinalIgnoreCase);
        }

        // 起動時、前回終了時のウィンドウサイズ+境界線位置+ホットキーを復元する。保存ファイルが
        // 無い/壊れている場合は何もしない(XAML既定のサイズ・境界線位置、HotkeyDefaultsのままで動く)
        private void LoadAppSettings()
        {
            AppSettings settings = JsonFileStorage.Load<AppSettings>(Path.Combine(_userDataFolder, _appSettingsFileName));
            if (settings == null)
            {
                return;
            }

            if (settings.Width > 0 && settings.Height > 0)
            {
                // MinWidth/MinHeightより小さい値が保存されていてもWPF側で自動的に補正される
                Width = settings.Width;
                Height = settings.Height;
            }

            if (settings.SplitterDistance > 0)
            {
                _pendingSplitterDistance = settings.SplitterDistance;
            }

            if (settings.RecordHotkey != Keys.None)
            {
                _hotkeyToggleRecord = settings.RecordHotkey;
            }

            if (settings.PlayHotkey != Keys.None)
            {
                _hotkeyTogglePlay = settings.PlayHotkey;
            }
        }

        // ウィンドウサイズ+境界線位置+ホットキーをまとめて保存する。終了時と、キーバインド設定画面で「保存」した直後の両方から呼ばれる。
        // 終了処理中は落としたくないので失敗は握りつぶしてfalseを返す(設定画面からの保存では呼び出し側が失敗を知らせる)
        internal Boolean SaveAppSettings()
        {
            // 最大化・最小化中は今のサイズではなく、通常表示の時のサイズ(RestoreBounds)を保存する
            Size sizeToSave;
            if (WindowState != WindowState.Normal && !RestoreBounds.IsEmpty)
            {
                sizeToSave = RestoreBounds.Size;
            }
            else
            {
                sizeToSave = new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
            }

            int splitterDistance = column_SplitLeft.ActualWidth > 0
                ? (int)Math.Round(column_SplitLeft.ActualWidth)
                : _pendingSplitterDistance;

            AppSettings settings = new AppSettings
            {
                Width = (int)Math.Round(sizeToSave.Width),
                Height = (int)Math.Round(sizeToSave.Height),
                SplitterDistance = splitterDistance,
                RecordHotkey = _hotkeyToggleRecord,
                PlayHotkey = _hotkeyTogglePlay,
            };

            try
            {
                JsonFileStorage.Save(Path.Combine(_userDataFolder, _appSettingsFileName), settings);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
