using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace EventRecorder
{
    // EventRecorderのポップアップダイアログ(設定画面・検索/置換など)共通の基底クラス(WinForms版DialogFormBaseのWPF版)。
    // CancelButtonの有無に関わらず、Escapeキーで閉じる動作を一律で持たせる。
    // あわせてWinForms版の ShowIcon = false / ShowInTaskbar = false と同じ見た目にする
    public class DialogWindowBase : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern Boolean SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_DLGMODALFRAME = 0x0001;
        private const int WM_SETICON = 0x0080;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public DialogWindowBase()
        {
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SourceInitialized += (s, e) => HideIcon();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Close();
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        // タイトルバー左端のアイコンを消す(WinForms版のShowIcon = false相当)
        private void HideIcon()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle | WS_EX_DLGMODALFRAME);
            SendMessage(hWnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);
            SendMessage(hWnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
    }
}
