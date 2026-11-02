using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SecretaryGadget
{
    /// <summary>
    /// 同じ Essentials にある Node のローカル Web ツール(箱庭 = ClaudeOffice、株レーダー = StockRadar)を開く。
    /// 起動済みならブラウザで開くだけ、未起動ならツールのフォルダの autostart.ps1 で窓なしに起動してから開く。
    /// </summary>
    internal sealed class LocalWebLauncher
    {
        // 箱庭は初回に会話記録を全部読むので、待ち受けまで数秒かかる
        public static readonly LocalWebLauncher Hakoniwa = new("箱庭", "ClaudeOffice", 8765, TimeSpan.FromSeconds(30));
        public static readonly LocalWebLauncher StockRadar = new("株レーダー", "StockRadar", 8766, TimeSpan.FromSeconds(15));

        private const string _startScript = "autostart.ps1";

        private readonly string _title;
        private readonly string _folderName;
        private readonly int _port;
        private readonly TimeSpan _startTimeout;

        private LocalWebLauncher(string title, string folderName, int port, TimeSpan startTimeout)
        {
            _title = title;
            _folderName = folderName;
            _port = port;
            _startTimeout = startTimeout;
        }

        public string Title => _title;

        public string Url => $"http://127.0.0.1:{_port}/";

        /// <summary>開けたら null、開けなければ理由(吹き出しに出す文面)。</summary>
        public async Task<string?> OpenAsync()
        {
            if (await IsListeningAsync())
            {
                WindowFocus.OpenUri(Url);
                return null;
            }
            string? folder = FindFolder();
            if (folder == null)
            {
                Log.Write($"{_title}のフォルダが見つかりません");
                return $"{_title}({_folderName} フォルダ)が見つかりません。";
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
                Log.Write($"{_title}を起動できませんでした: " + ex.GetType().Name);
                return $"{_title}を起動できませんでした。";
            }
            DateTime limit = DateTime.Now + _startTimeout;
            while (DateTime.Now < limit)
            {
                await Task.Delay(500);
                if (await IsListeningAsync())
                {
                    Log.Write($"{_title}を起動しました");
                    WindowFocus.OpenUri(Url);
                    return null;
                }
            }
            Log.Write($"{_title}の起動を待ちきれませんでした");
            return $"{_title}が起動しませんでした(Node.js が入っているか確認してください)。";
        }

        private async Task<bool> IsListeningAsync()
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

        /// <summary>exe のフォルダから上へたどり、ツールのフォルダ(autostart.ps1 がある兄弟フォルダ)を探す。</summary>
        private string? FindFolder()
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
