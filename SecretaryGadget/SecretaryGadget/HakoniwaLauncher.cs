using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SecretaryGadget
{
    /// <summary>
    /// 箱庭(Claude Office、127.0.0.1:8765 のローカル Web ダッシュボード)を開く。
    /// 起動済みならブラウザで開くだけ、未起動なら ClaudeOffice\autostart.ps1 で窓なしに起動してから開く。
    /// </summary>
    internal static class HakoniwaLauncher
    {
        public const string Url = "http://127.0.0.1:8765/";
        private const int _port = 8765;
        private const string _folderName = "ClaudeOffice";
        private const string _startScript = "autostart.ps1";
        private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(30);

        /// <summary>開けたら null、開けなければ理由(吹き出しに出す文面)。</summary>
        public static async Task<string?> OpenAsync()
        {
            if (await IsListeningAsync())
            {
                WindowFocus.OpenUri(Url);
                return null;
            }
            string? folder = FindFolder();
            if (folder == null)
            {
                Log.Write("箱庭のフォルダが見つかりません");
                return "箱庭(ClaudeOffice フォルダ)が見つかりません。";
            }
            try
            {
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = folder,
                };
                foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", Path.Combine(folder, _startScript) })
                {
                    psi.ArgumentList.Add(a);
                }
                using Process? p = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Write("箱庭を起動できませんでした: " + ex.GetType().Name);
                return "箱庭を起動できませんでした。";
            }
            // 初回は会話記録を全部読むので、待ち受けまで数秒かかる
            DateTime limit = DateTime.Now + _startTimeout;
            while (DateTime.Now < limit)
            {
                await Task.Delay(500);
                if (await IsListeningAsync())
                {
                    Log.Write("箱庭を起動しました");
                    WindowFocus.OpenUri(Url);
                    return null;
                }
            }
            Log.Write("箱庭の起動を待ちきれませんでした");
            return "箱庭が起動しませんでした(Node.js が入っているか確認してください)。";
        }

        private static async Task<bool> IsListeningAsync()
        {
            try
            {
                using var client = new TcpClient();
                Task connect = client.ConnectAsync("127.0.0.1", _port);
                return await Task.WhenAny(connect, Task.Delay(500)) == connect && client.Connected;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>exe のフォルダから上へたどり、ClaudeOffice\autostart.ps1 がある兄弟フォルダを探す(同じ Essentials の中にある)。</summary>
        private static string? FindFolder()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, _folderName);
                if (File.Exists(Path.Combine(candidate, _startScript)))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
