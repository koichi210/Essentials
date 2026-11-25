using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SecretaryGadget
{
    /// <summary>動いている Claude Code の会話1件。Busy=返事を作っている途中(実行中)、false=返事を書き終えて次を待っている(処理完了)。</summary>
    internal sealed record ClaudeSession(string SessionId, string Title, string Project, DateTime Since, bool Busy);

    /// <summary>
    /// Claude Code の動いている会話と、その状態(実行中／処理完了)を読む。Claude Code 自身の在席ファイル(~/.claude/sessions/&lt;pid&gt;.json)を
    /// 読むだけで、外のツール(箱庭など)には頼らない。プロセスが生きていれば1件。状態が busy なら実行中、それ以外は処理完了。
    /// 箱庭は idle が 30 分を超えると「おやすみ」と分けるが、次を待っていることに変わりはないので、ここでは時間で分けない。
    /// </summary>
    internal static class ClaudeSessions
    {
        /// <summary>会話記録の末尾から題名を探す範囲。題名(ai-title)は何度も書き直されるので末尾にある</summary>
        private const int TitleTailBytes = 2 * 1024 * 1024;

        /// <summary>CLAUDE_CONFIG_DIR があればそれ、無ければ %USERPROFILE%\.claude。</summary>
        public static string ConfigDir()
        {
            string? env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }

        /// <summary>動いている会話を、処理完了(待たせている時間の長い順)→実行中(長く動いている順)で返す。読めないファイルは飛ばす。</summary>
        public static IReadOnlyList<ClaudeSession> ReadSessions(string? configDir = null, DateTime? now = null, Func<int, bool>? isAlive = null)
        {
            string dir = configDir ?? ConfigDir();
            DateTime at = now ?? DateTime.Now;
            isAlive ??= IsAlive;
            var list = new List<ClaudeSession>();
            string sessionsDir = Path.Combine(dir, "sessions");
            if (!Directory.Exists(sessionsDir))
            {
                return list;
            }
            foreach (string file in Directory.EnumerateFiles(sessionsDir, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                {
                    continue;
                }
                try
                {
                    ClaudeSession? s = ParseLive(File.ReadAllText(file, Encoding.UTF8), at, isAlive);
                    if (s != null)
                    {
                        list.Add(s with { Title = FindTitle(dir, s.SessionId) ?? s.Title });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // 書き込み途中などは次に呼んだとき
                }
            }
            return list.OrderBy(s => s.Busy).ThenBy(s => s.Since).ToList();
        }

        /// <summary>
        /// 在席ファイル1つを読み、プロセスが生きていれば返す(題名はまだ在席ファイルの名前)。生きていなければ null。
        /// </summary>
        public static ClaudeSession? ParseLive(string json, DateTime now, Func<int, bool> isAlive)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string? id = Str(root, "sessionId");
            if (string.IsNullOrEmpty(id) || !root.TryGetProperty("pid", out JsonElement pidEl) || !pidEl.TryGetInt32(out int pid) || !isAlive(pid))
            {
                return null;
            }
            bool busy = Str(root, "status") == "busy";
            long sinceMs = Num(root, "statusUpdatedAt") ?? Num(root, "updatedAt") ?? Num(root, "startedAt") ?? 0;
            DateTime since = sinceMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(sinceMs).LocalDateTime : now;
            string cwd = (Str(root, "cwd") ?? string.Empty).TrimEnd('\\', '/');
            string project = cwd.Length > 0 ? Path.GetFileName(cwd) : string.Empty;
            string name = Str(root, "name") ?? string.Empty;
            return new ClaudeSession(id, name.Length > 0 ? name : project, project, since, busy);
        }

        /// <summary>
        /// 会話記録(~/.claude/projects/*/&lt;id&gt;.jsonl)の末尾から、自分で付けた題名(custom-title)、無ければ自動の題名(ai-title)を探す。
        /// </summary>
        public static string? FindTitle(string configDir, string sessionId)
        {
            string projects = Path.Combine(configDir, "projects");
            if (!Directory.Exists(projects))
            {
                return null;
            }
            foreach (string ws in Directory.EnumerateDirectories(projects))
            {
                string file = Path.Combine(ws, sessionId + ".jsonl");
                if (File.Exists(file))
                {
                    return TitleFromTail(ReadTail(file, TitleTailBytes));
                }
            }
            return null;
        }

        /// <summary>記録の行の並びから題名を選ぶ。custom-title が最優先、次に最後の ai-title。</summary>
        public static string? TitleFromTail(string text)
        {
            string? custom = null;
            string? ai = null;
            foreach (string line in text.Split('\n'))
            {
                // 題名の行は短いので、種類を文字列で見てから JSON として読む(大きな行を全部読まない)
                bool isCustom = line.Contains("\"custom-title\"", StringComparison.Ordinal);
                bool isAi = !isCustom && line.Contains("\"ai-title\"", StringComparison.Ordinal);
                if (!isCustom && !isAi)
                {
                    continue;
                }
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    if (isCustom && Str(doc.RootElement, "type") == "custom-title")
                    {
                        custom = Str(doc.RootElement, "customTitle") ?? custom;
                    }
                    else if (isAi && Str(doc.RootElement, "type") == "ai-title")
                    {
                        ai = Str(doc.RootElement, "aiTitle") ?? ai;
                    }
                }
                catch (JsonException)
                {
                    // 末尾から読んだ最初の行は途中から始まるので読めない
                }
            }
            string? title = custom ?? ai;
            return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        }

        private static string ReadTail(string file, int maxBytes)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            return Encoding.UTF8.GetString(buf, 0, read);
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                using Process p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 権限が無いだけなら生きている
                return true;
            }
        }

        private static string? Str(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static long? Num(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
        }
    }
}
