using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
        private readonly StcUtils util = new StcUtils();

        // 記録中/再生中フラグ。UIスレッド(ボタンクリック・グローバルホットキー)と
        // 再生用バックグラウンドスレッド(Task.Run側)の両方から読み書きされるため、volatileにしている
        private volatile Boolean isRecording = false;
        private volatile Boolean isPlaying = false;
        private volatile Boolean stopPlayRequested = false;

        // タイトルバーに表示する再生中のループ進捗(WinForms版と同じ。再生スレッドから書き込み、UIはUpdateTitleで読むだけ)
        private int playbackOverallLoopNo = 0;
        private int playbackOverallLoopMax = 0;
        private int playbackInnerLoopNo = 0;
        private int playbackInnerLoopMax = 0;

        // 直前のイベント時刻(記録の待機ms算出用)
        private int lastEventTick = 0;

        // 記録中/再生中にハイライトしている行のインデックス(-1ならハイライト無し)
        private int highlightedEventRowIndex = -1;

        // プレイリスト実行中にハイライトしている行(dataGrid_Playlist側)のインデックス
        private int highlightedPlaylistRowIndex = -1;

        // 再生開始時のマウスカーソル位置(再生終了後に戻すため)。WinForms版と同じくCursor.Position(DPI非対応の座標)を使う
        private System.Drawing.Point cursorPositionBeforePlay;

        // 記録中に押下中のキー(OSのキーリピートによるKeyDown連発を抑制するため、
        // KeyUpが来るまで「押されている」とみなす)。記録セッションの開始のたびにクリアする
        private readonly HashSet<Keys> pressedKeys = new HashSet<Keys>();

        // ホットキーの押下状態。記録セッションとは独立して管理する(pressedKeysをClearしても消えないように)
        private readonly HashSet<Keys> pressedHotkeys = new HashSet<Keys>();

        // タイトルバーの基本文字列。記録中/再生中はここに状態を追記する
        private const String BaseTitle = "EventRecorder";

        // 記録/再生の切り替えホットキー。設定画面([[HotkeySettingsWindow]]、システムメニューから開く)で変更でき、
        // 変更内容はEventRecorder.json(WinForms版と共通のアプリ設定ファイル)に保存される
        private Keys hotkeyToggleRecord = HotkeyDefaults.Record;
        private Keys hotkeyTogglePlay = HotkeyDefaults.Play;

        // キーバインド設定画面を開いている間だけtrueにする(テストで押したキーがホットキーとして誤発動しないように)
        private Boolean isHotkeySettingsOpen = false;

        // 記録したがまだグリッドに反映していない行(フックのコールバックを描画待ちで塞がないよう、
        // 一旦ここに貯めてタイマーでまとめて反映する)
        private readonly List<String[]> pendingRows = new List<String[]>();
        private readonly DispatcherTimer gridFlushTimer;

        // マウスカーソル座標の常時表示用タイマー
        private readonly DispatcherTimer mousePosTimer;

        // マクロ・プレイリストのユーザーデータ置き場。WinForms版と同じ%LOCALAPPDATA%\EventRecorder\(既定)を使うので、
        // WinForms版で保存したプロファイル・設定をそのまま読める([[_Common\UserDataLocation.cs]])
        private const String AppName = "EventRecorder";
        private readonly String userDataFolder;

        // グローバルフック(キーボード/マウス)を張ってよいか。テスト用のコンストラクタではfalseにして、
        // このPCのキーボード/マウスに一切影響しないようにする
        private readonly Boolean isHookEnabled;

        // レコード表・プレイリストの中身(1要素=1行)と、それぞれのUndo/Redo(WinForms版のDataGridViewEx相当)
        internal readonly GridRowCollection<EventRow> eventRows = new GridRowCollection<EventRow>();
        internal readonly GridRowCollection<PlaylistRow> playlistRows = new GridRowCollection<PlaylistRow>();
        internal readonly GridUndoRedo<EventRow> eventsUndo;
        internal readonly GridUndoRedo<PlaylistRow> playlistUndo;

        // プレイリストの設定ファイル列のプルダウンの選択肢(comboBox_Profileの一覧のコピー)
        internal readonly ObservableCollection<String> playlistFileItems = new ObservableCollection<String>();

        // 起動時に復元する左右分割の境界線位置(レイアウトが確定するLoadedで適用する)
        private int pendingSplitterDistance = 0;

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(AppName), true)
        {
        }

        // internal: テストでは実際のユーザーデータフォルダではなく一時フォルダを渡す(グローバルフックも張らない)
        internal MainWindow(String dataFolder) : this(dataFolder, false)
        {
        }

        private MainWindow(String dataFolder, Boolean enableHooks)
        {
            InitializeComponent();
            userDataFolder = dataFolder;
            isHookEnabled = enableHooks;

            Resources["PlaylistFileItems"] = playlistFileItems;
            dataGrid_Events.ItemsSource = eventRows;
            dataGrid_Playlist.ItemsSource = playlistRows;
            eventsUndo = new GridUndoRedo<EventRow>(eventRows);
            playlistUndo = new GridUndoRedo<PlaylistRow>(playlistRows);
            playlistUndo.RowCellChanged += PlaylistRow_CellChanged;

            // ウィンドウサイズ+境界線位置+ホットキーを、前回終了時の状態(EventRecorder.json、無ければ既定値のまま)で復元する
            LoadAppSettings();

            util.SetCurrentDirectory();

            gridFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            gridFlushTimer.Tick += (s, e) => FlushPendingRows();

            // マウスカーソルの座標を起動中ずっと表示しておく(記録/再生の状態と関係なく常時更新)
            mousePosTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            mousePosTimer.Tick += (s, e) =>
            {
                System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
                label_MousePos.Text = "Mouse: " + p.X + ", " + p.Y;
            };
            mousePosTimer.Start();

            // キーボードフックはホットキー監視のためアプリ起動中ずっと張っておく。
            // マウスフックは記録中だけでよいのでToggleRecording側で開始/停止する
            if (isHookEnabled)
            {
                GlobalHook.KeyboardHook.AddEvent(OnKeyboardEvent);
                GlobalHook.KeyboardHook.Start();
            }

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
            StateChanged += (s, e) => UpdateTaskbarVisibility();

            // userDataFolder配下のプロファイル一覧をコンボボックスに表示する。一覧の先頭が選ばれ、
            // そのまま読み込まれる(=起動時は一覧の先頭を読み込む。WinForms版と同じ)
            UpdateProfileListAll("");

            // プレイリストの設定ファイル列は、comboBox_Profileと全く同じ一覧を表示する
            SyncPlaylistFileItems();
            UpdatePlaylistMissingFileHighlights();

            // プレイリストが空のままだと使うたびに毎回「行の追加」を押す羽目になるため、起動時点で空行を2行用意しておく。
            // ただし、読み込んだプロファイルに既にプレイリストの中身が入っていた場合は、その内容を優先する
            if (playlistRows.Count == 0)
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
            if (pendingSplitterDistance > 0 && total > 0)
            {
                double left = Math.Max(column_SplitLeft.MinWidth, Math.Min(pendingSplitterDistance, total - column_SplitRight.MinWidth));
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
        private static readonly Brush ModeHighlightBrush = CreateFrozenBrush(Color.FromRgb(205, 255, 230));

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
            panel_Playback.Background = radioButton_Playback.IsChecked == true ? ModeHighlightBrush : Brushes.Transparent;
            panel_Record.Background = radioButton_Record.IsChecked == true ? ModeHighlightBrush : Brushes.Transparent;
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
        private FindReplaceWindow findReplaceWindow;

        private void ShowFindReplaceDialog(FindReplaceMode mode)
        {
            if (isRecording || isPlaying)
            {
                return;
            }

            if (findReplaceWindow == null)
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

                findReplaceWindow = new FindReplaceWindow(dataGrid_Events, eventRows, eventsUndo, mode, initialText);
                findReplaceWindow.Owner = this;
                findReplaceWindow.Closed += (s, e) => findReplaceWindow = null;
                findReplaceWindow.Show();
            }
            else
            {
                findReplaceWindow.SetMode(mode);
                findReplaceWindow.Activate();
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
        private const int SysMenuId_ChangeHotkeys = 0x1010;

        private void AppendHotkeyMenu()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            AppendMenu(GetSystemMenu(hWnd, false), MF_STRING, (UIntPtr)SysMenuId_ChangeHotkeys, "キーバインドを設定(&K)...");

            HwndSource.FromHwnd(hWnd).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref Boolean handled) =>
            {
                if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SysMenuId_ChangeHotkeys)
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
            if (isRecording || isPlaying)
            {
                MessageBox.Show(
                    this,
                    "記録中/再生中は変更できないよ。停止してから試してね",
                    "EventRecorder - キーバインド設定",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 設定画面を開いている間は、テキストボックスに試しに入力したキーがグローバルフック(OnKeyboardEvent)側にも
            // 同時に届いて、記録/再生のホットキーとして誤発動してしまう。その間はOnKeyboardEventの処理そのものを止める
            isHotkeySettingsOpen = true;
            try
            {
                HotkeySettingsWindow window = new HotkeySettingsWindow(hotkeyToggleRecord, hotkeyTogglePlay);
                window.Owner = this;
                if (window.ShowDialog() != true)
                {
                    return;
                }

                hotkeyToggleRecord = window.RecordHotkey;
                hotkeyTogglePlay = window.PlayHotkey;

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
                isHotkeySettingsOpen = false;
            }
        }

        // 保存先フォルダを選び直し、exe直下のポインタファイル(DataFolder.txt)を書き換える。
        // 実行中のuserDataFolderはその場では切り替えない(変更は次回起動時から反映される、WinForms版と同じ)
        private void ChangeDataFolder()
        {
            if (isRecording || isPlaying)
            {
                MessageBox.Show(
                    this,
                    "記録中/再生中は変更できないよ。停止してから試してね",
                    AppName + " - データ保存先の変更",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // EventRecorder.json(アプリの設定ファイル、プロファイルではない)は引っ越し対象から外す
            DataFolderMenu.ChangeDataFolder(AppName, userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, AppName, IsNonProfileSettingFile));
        }

        // *******************************************************************************
        // 最小化時のタスクトレイ格納

        private System.Windows.Forms.NotifyIcon notifyIcon_Tray;

        // タスクトレイに格納するためにウィンドウを隠している最中か
        private Boolean isHiddenToTray = false;

        // checkBox_MinimizeOnPlay(実行時にウィンドウを最小化する)がオンかつ最小化中の時だけ、
        // タスクバーから消してシステムトレイアイコンを表示する(WinForms版と同じ)。
        // WPFでは最小化中にShowInTaskbarを切り替えると画面左下に小さなタイトルバーが残ってしまうため、
        // タスクバーから消す代わりにウィンドウ自体を隠す(Hide)方式にしている
        private void UpdateTaskbarVisibility()
        {
            Boolean shouldHideFromTaskbar = checkBox_MinimizeOnPlay.IsChecked == true
                && WindowState == WindowState.Minimized;

            if (shouldHideFromTaskbar)
            {
                EnsureTrayIcon();
                notifyIcon_Tray.Visible = true;
                if (!isHiddenToTray)
                {
                    isHiddenToTray = true;
                    Hide();
                }
            }
            else
            {
                if (notifyIcon_Tray != null)
                {
                    notifyIcon_Tray.Visible = false;
                }
                if (isHiddenToTray)
                {
                    isHiddenToTray = false;
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
            if (notifyIcon_Tray != null)
            {
                return;
            }

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("元に戻す(&R)", null, (s, e) => RestoreFromTray());
            menu.Items.Add("終了(&X)", null, (s, e) => Close());

            notifyIcon_Tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "EventRecorder",
                ContextMenuStrip = menu,
                Visible = false,
            };
            try
            {
                using (Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/EventRecorder.ico")).Stream)
                {
                    notifyIcon_Tray.Icon = new System.Drawing.Icon(stream);
                }
            }
            catch (IOException)
            {
                notifyIcon_Tray.Icon = System.Drawing.SystemIcons.Application;
            }
            notifyIcon_Tray.DoubleClick += (s, e) => RestoreFromTray();
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
            SaveAppSettings();

            gridFlushTimer.Stop();
            mousePosTimer.Stop();
            GlobalHook.MouseHook.Stop();
            GlobalHook.KeyboardHook.Stop();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // 残しておくと、終了後もタスクトレイにアイコンの抜け殻が残ってしまう
            if (notifyIcon_Tray != null)
            {
                notifyIcon_Tray.Visible = false;
                notifyIcon_Tray.Dispose();
                notifyIcon_Tray = null;
            }
        }

        // アプリ本体の設定をまとめて保存するファイル名(WinForms版と同じ)。プロファイル一覧には出さない
        private const String AppSettingsFileName = "EventRecorder.json";

        // userDataFolder直下にプロファイルと混在して置かれる、アプリ自体の設定ファイルかどうか
        private static Boolean IsNonProfileSettingFile(String filePath)
        {
            String fileName = Path.GetFileName(filePath);
            return String.Equals(fileName, AppSettingsFileName, StringComparison.OrdinalIgnoreCase);
        }

        // 起動時、前回終了時のウィンドウサイズ+境界線位置+ホットキーを復元する。保存ファイルが
        // 無い/壊れている場合は何もしない(XAML既定のサイズ・境界線位置、HotkeyDefaultsのままで動く)
        private void LoadAppSettings()
        {
            AppSettings settings = JsonFileStorage.Load<AppSettings>(Path.Combine(userDataFolder, AppSettingsFileName));
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
                pendingSplitterDistance = settings.SplitterDistance;
            }

            if (settings.RecordHotkey != Keys.None)
            {
                hotkeyToggleRecord = settings.RecordHotkey;
            }

            if (settings.PlayHotkey != Keys.None)
            {
                hotkeyTogglePlay = settings.PlayHotkey;
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
                : pendingSplitterDistance;

            AppSettings settings = new AppSettings
            {
                Width = (int)Math.Round(sizeToSave.Width),
                Height = (int)Math.Round(sizeToSave.Height),
                SplitterDistance = splitterDistance,
                RecordHotkey = hotkeyToggleRecord,
                PlayHotkey = hotkeyTogglePlay,
            };

            try
            {
                JsonFileStorage.Save(Path.Combine(userDataFolder, AppSettingsFileName), settings);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
