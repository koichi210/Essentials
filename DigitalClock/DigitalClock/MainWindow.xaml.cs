using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StandardTemplate;

namespace DigitalClock
{
    public partial class MainWindow : Window
    {
        private const String _appName = "DigitalClock";
        private const String _settingFileName = "DigitalClock.json";

        // WinForms版のOpacity=0.5相当(0〜255)
        private const byte _windowAlpha = 128;

        private const int GWL_EXSTYLE = -20;
        private const int WM_STYLECHANGING = 0x007C;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint LWA_ALPHA = 0x00000002;

        private const int GWLP_WNDPROC = -4;

        [StructLayout(LayoutKind.Sequential)]
        private struct StyleStruct
        {
            public int StyleOld;
            public int StyleNew;
        }

        private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        // GCに回収されるとウィンドウプロシージャが消えて落ちるので、フィールドで保持しておく
        private WndProc _wndProcDelegate;
        private IntPtr _originalWndProc;

        private readonly String _settingFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _appName, _settingFileName);

        private readonly DispatcherTimer _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        public MainWindow()
        {
            InitializeComponent();

            RestorePlacement();

            _clockTimer.Tick += (s, e) => UpdateTime();
            _clockTimer.Start();
            UpdateTime();
        }

        // WPFのWindow.Opacityは枠なし(AllowsTransparency=True)でしか効かないため、
        // WinForms版と同じくタイトルバー付きのまま半透明にするには、レイヤードウィンドウにして透明度を指定する。
        // ただしWPFは自前の透過を使わないウィンドウからWS_EX_LAYEREDを剥がしてしまう(HwndSourceのフックより手前で処理される)ので、
        // ウィンドウプロシージャを差し替え、WPFの処理の後でWS_EX_LAYEREDを付け直す
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _wndProcDelegate = KeepLayeredWndProc;
            _originalWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
            SetLayeredWindowAttributes(hwnd, 0, _windowAlpha, LWA_ALPHA);
        }

        private IntPtr KeepLayeredWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            IntPtr result = CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
            if (msg == WM_STYLECHANGING && wParam.ToInt32() == GWL_EXSTYLE)
            {
                StyleStruct style = (StyleStruct)Marshal.PtrToStructure(lParam, typeof(StyleStruct));
                style.StyleNew |= WS_EX_LAYERED;
                Marshal.StructureToPtr(style, lParam, false);
            }
            return result;
        }

        // 設定ファイルが無い・壊れている・画面外を指している場合は、XAMLの初期位置(100, 100)のままにする
        private void RestorePlacement()
        {
            WindowPlacement placement = JsonFileStorage.Load<WindowPlacement>(_settingFilePath);
            if (placement == null)
            {
                return;
            }
            if (!Logic.IsOnScreen(placement.Left, placement.Top,
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
            {
                return;
            }
            Left = placement.Left;
            Top = placement.Top;
        }

        // 保存に失敗しても終了は妨げない(WinForms版も保存失敗は無視していた)
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _clockTimer.Stop();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingFilePath));
                JsonFileStorage.Save(_settingFilePath, new WindowPlacement { Left = Left, Top = Top });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private void UpdateTime()
        {
            label_time.Text = Logic.FormatTime(DateTime.Now);
        }

        private void label_time_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
