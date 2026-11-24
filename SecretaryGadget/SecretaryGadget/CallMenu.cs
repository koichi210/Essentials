using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SecretaryGadget
{
    /// <summary>
    /// 「秘書さんを呼ぶ」の吹き出しに並べる選択肢1行分(settings.json の CallMenu の1要素)。
    /// Path は起動するファイル(exe・bat・ps1・ショートカット等。環境変数可)。
    /// 外のファイルを持たない秘書さん自身の機能(本日の予定・タイマー)だけは「@」で始まる名前で呼ぶ。
    /// 外のツールの場所は秘書さんに持たせず、必ずここに書く。
    /// </summary>
    internal sealed class CallMenuEntry
    {
        /// <summary>秘書さん自身の機能の名前の頭に付ける印</summary>
        public const string BuiltinPrefix = "@";

        public string Label { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        /// <summary>起動するファイルに渡す引数(秘書さん自身の機能では使わない)</summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>秘書さん自身の機能(@ を除いた名前, 説明)。設定画面の候補とチェックに使う。</summary>
        public static IReadOnlyList<(string Name, string Title)> Builtins { get; } = new[]
        {
            ("schedule", "本日の予定"),
            ("timer", "タイマー"),
        };

        [JsonIgnore]
        public bool IsBuiltin => Path.StartsWith(BuiltinPrefix, StringComparison.Ordinal);

        /// <summary>秘書さん自身の機能の名前(@ を除いたもの、小文字)。そうでなければ空。</summary>
        [JsonIgnore]
        public string BuiltinName => IsBuiltin ? Path.Substring(BuiltinPrefix.Length).Trim().ToLowerInvariant() : string.Empty;

        /// <summary>既定の並び。外のツールは場所を知らないので入れない(使う人が settings.json に書き足す)。</summary>
        public static List<CallMenuEntry> Defaults()
        {
            return new List<CallMenuEntry>
            {
                new CallMenuEntry { Label = "本日の予定", Path = "@schedule" },
                new CallMenuEntry { Label = "タイマー", Path = "@timer" },
            };
        }

        /// <summary>手で編集されたリストを整える。null なら既定の並び、表示名か Path が空の行は捨てる。</summary>
        public static List<CallMenuEntry> Normalize(List<CallMenuEntry>? entries)
        {
            if (entries == null)
            {
                return Defaults();
            }
            return entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Label) && !string.IsNullOrWhiteSpace(e.Path))
                .Select(e => new CallMenuEntry { Label = e.Label.Trim(), Path = e.Path.Trim(), Arguments = e.Arguments?.Trim() ?? string.Empty })
                .ToList();
        }

        /// <summary>
        /// 設定画面で保存する前のチェック。問題が無ければ null、あれば (何行目か(0始まり), 理由)。
        /// 表示名・起動先の空欄と、知らない「@」の機能名を見る(ファイルの有無は見ない: 別のPC用の行もありうるため)。
        /// </summary>
        public static (int Index, string Message)? FindProblem(IReadOnlyList<CallMenuEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CallMenuEntry e = entries[i];
                string row = $"{i + 1}行目";
                if (string.IsNullOrWhiteSpace(e.Label))
                {
                    return (i, $"{row}の表示名が空です。");
                }
                if (string.IsNullOrWhiteSpace(e.Path))
                {
                    return (i, $"{row}（{e.Label.Trim()}）の起動するものが空です。");
                }
                CallMenuEntry trimmed = new CallMenuEntry { Path = e.Path.Trim() };
                if (trimmed.IsBuiltin && !Builtins.Any(b => b.Name == trimmed.BuiltinName))
                {
                    return (i, $"{row}（{e.Label.Trim()}）の「{trimmed.Path}」という機能はありません。");
                }
            }
            return null;
        }

        /// <summary>
        /// ファイルを起動する。起動できたら null、できなければ理由(吹き出しに出す文面)。
        /// ps1 は既定の関連付けだとメモ帳で開いてしまうので、PowerShell で窓なしに実行して終わるまで待ち、
        /// 終了コードが 0 以外ならスクリプトが最後に出力した行を理由にする。
        /// それ以外(exe・bat・ショートカット・フォルダ・URL)は関連付けで開くだけで、終わりは待たない。
        /// </summary>
        public async Task<string?> LaunchAsync()
        {
            string target = Environment.ExpandEnvironmentVariables(Path);
            string arguments = Environment.ExpandEnvironmentVariables(Arguments ?? string.Empty);
            bool isFile = File.Exists(target);
            if (string.Equals(System.IO.Path.GetExtension(target), ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                return isFile ? await RunScriptAsync(target, arguments) : NotFound();
            }
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = arguments,
            };
            if (isFile)
            {
                // bat が自分のフォルダのファイルを相対パスで読めるように
                psi.WorkingDirectory = FolderOf(target);
            }
            try
            {
                using Process? p = Process.Start(psi);
                return null;
            }
            catch (Win32Exception ex)
            {
                Log.Write($"メニュー「{Label}」を起動できませんでした: Win32 {ex.NativeErrorCode}");
                return isFile || Directory.Exists(target) ? $"「{Label}」を起動できませんでした。" : NotFound();
            }
            catch (Exception ex)
            {
                Log.Write($"メニュー「{Label}」を起動できませんでした: " + ex.GetType().Name);
                return $"「{Label}」を起動できませんでした。";
            }
        }

        private async Task<string?> RunScriptAsync(string script, string arguments)
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = FolderOf(script),
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" {arguments}".TrimEnd(),
            };
            try
            {
                using Process p = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe が起動しませんでした");
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode == 0)
                {
                    return null;
                }
                Log.Write($"メニュー「{Label}」のスクリプトが終了コード {p.ExitCode} で終わりました");
                string? last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
                return last ?? $"「{Label}」を開けませんでした(終了コード {p.ExitCode})。";
            }
            catch (Exception ex)
            {
                Log.Write($"メニュー「{Label}」のスクリプトを実行できませんでした: " + ex.GetType().Name);
                return $"「{Label}」を起動できませんでした。";
            }
        }

        private string NotFound() => $"「{Label}」の起動先が見つかりません。settings.json の CallMenu を確認してください。";

        private static string FolderOf(string file) => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file)) ?? string.Empty;
    }
}
