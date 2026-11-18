// MagicLauncher の「開く係」。
// サーバ(node)は窓を持たない裏方のプロセスなので、そこから直接開くと Windows のフォアグラウンド制限で
// 開いた窓がブラウザの後ろに隠れる。この exe がいったん見えない手前の窓になってから開くことで、
// 開いた窓を手前に出す(手前のプロセスが開いたものは手前に出られる)。
//
// ビルド(サーバ起動時に自動。opener.cs の方が新しいときだけ):
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize /codepage:65001
//     /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:opener.exe opener.cs
// 使い方: opener.exe <パス または URL>
//   成功: 標準出力に "ok <対象>" を 1 行(UTF-8)出し、開いた窓が手前に来るまで最長 3 秒居残ってから 0 で終わる
//   失敗: 標準エラーに理由(UTF-8)を出して 0 以外で終わる
// csc v4.0 は C# 5 までなので、文字列補間などの新しい書き方は使わない。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MagicLauncher
{
    internal static class Opener
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VkMenu = 0x12;
        private const uint KeyEventFKeyUp = 0x0002;
        private const int LingerMs = 3000;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1 || string.IsNullOrEmpty(args[0]))
            {
                WriteLine(Console.OpenStandardError(), "usage: opener.exe <path-or-url>");
                return 2;
            }
            string target = args[0];

            using (Form form = CreateHiddenForm())
            {
                form.Show();
                Application.DoEvents();
                BringToFront(form.Handle);

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(target);
                    psi.UseShellExecute = true; // cmd を通さないので & や % を含むパスもそのまま渡せる
                    Process p = Process.Start(psi); // 既存のアプリに渡されて null が返っても成功扱い
                    if (p != null)
                    {
                        p.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    WriteLine(Console.OpenStandardError(), ex.Message);
                    return 1;
                }

                WriteLine(Console.OpenStandardOutput(), "ok " + target);

                // 先に終わると手前の座がブラウザへ戻るので、開いた窓が手前に来るまで居残る
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < LingerMs)
                {
                    Application.DoEvents();
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && fg != form.Handle && sw.ElapsedMilliseconds > 300)
                    {
                        break;
                    }
                    Thread.Sleep(50);
                }
                form.Close();
            }
            return 0;
        }

        // 見えない窓(枠なし・画面の外・透明・タスクバーに出さない)
        private static Form CreateHiddenForm()
        {
            Form form = new Form();
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Size = new Size(1, 1);
            form.Opacity = 0.01;
            form.Text = "MagicLauncher opener";
            return form;
        }

        // SetForegroundWindow → 手前の窓のスレッドと AttachThreadInput して再試行 → Alt を押した状態で再試行
        private static void BringToFront(IntPtr hwnd)
        {
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd)
            {
                return;
            }

            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            if (fg != IntPtr.Zero && fgThread != myThread)
            {
                AttachThreadInput(myThread, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                AttachThreadInput(myThread, fgThread, false);
            }
            if (GetForegroundWindow() == hwnd)
            {
                return;
            }

            keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
            keybd_event(VkMenu, 0, KeyEventFKeyUp, UIntPtr.Zero);
        }

        private static void WriteLine(Stream stream, string text)
        {
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (IOException)
            {
                // 出力先が無いとき(ダブルクリックで起動など)は捨てる
            }
        }
    }
}
