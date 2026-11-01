using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SecretaryGadget
{
    /// <summary>Teams(ms-teams)の TeamsWebView 窓1つ分の情報。</summary>
    internal readonly record struct TeamsWindow(IntPtr Handle, NativeMethods.RECT Rect, bool IsIconic, string Title);

    /// <summary>
    /// 他アプリを前へ出す処理と、Teamsの窓探し(EnumWindows)。
    /// NOACTIVATE の窓から SetForegroundWindow してもタスクバーが点滅するだけなので、Altキーを押して離して前面化の許可を得る。
    /// </summary>
    internal static class WindowFocus
    {
        public const string TeamsClassName = "TeamsWebView";
        private const string _teamsProcessName = "ms-teams";

        /// <summary>最小化なら SW_RESTORE → Altを押して離す → SetForegroundWindow。</summary>
        public static void BringToFront(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeMethods.SetForegroundWindow(hwnd);
        }

        /// <summary>
        /// Teamsのメイン窓を前へ。可視・TeamsWebView・幅≥600(最小化中は幅不問)のうち、タイトルに「@」を含む窓を優先
        /// (会議中は会議窓も同じクラスなので)。無ければ msteams: を起動する。
        /// </summary>
        public static void FocusTeamsMain()
        {
            List<TeamsWindow> candidates = EnumerateTeamsWindows().Where(w => w.IsIconic || w.Rect.Width >= 600).ToList();
            if (candidates.Count == 0)
            {
                OpenUri("msteams:");
                return;
            }
            TeamsWindow target = candidates.FirstOrDefault(w => w.Title.Contains('@'));
            if (target.Handle == IntPtr.Zero)
            {
                target = candidates[0];
            }
            BringToFront(target.Handle);
        }

        /// <summary>URL や msteams: を既定の関連付けで開く。</summary>
        public static void OpenUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("関連付けで開けませんでした: " + ex.GetType().Name);
            }
        }

        /// <summary>可視の TeamsWebView 窓(プロセス ms-teams)を列挙する。</summary>
        public static List<TeamsWindow> EnumerateTeamsWindows()
        {
            var result = new List<TeamsWindow>();
            var processNames = new Dictionary<uint, string>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetClassNameOf(hwnd) != TeamsClassName)
                {
                    return true;
                }
                if (!IsTeamsProcess(hwnd, processNames))
                {
                    return true;
                }
                NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);
                result.Add(new TeamsWindow(hwnd, rect, NativeMethods.IsIconic(hwnd), NativeMethods.GetWindowTitle(hwnd)));
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>クラス名だけで TeamsWebView 窓を列挙する(不可視も含む。前回の残骸の修復用)。</summary>
        public static List<IntPtr> EnumerateTeamsClassWindows()
        {
            var result = new List<IntPtr>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (NativeMethods.GetClassNameOf(hwnd) == TeamsClassName)
                {
                    result.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static bool IsTeamsProcess(IntPtr hwnd, Dictionary<uint, string> cache)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!cache.TryGetValue(pid, out string? name))
            {
                try
                {
                    using Process p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                }
                catch
                {
                    name = string.Empty;
                }
                cache[pid] = name;
            }
            return string.Equals(name, _teamsProcessName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
