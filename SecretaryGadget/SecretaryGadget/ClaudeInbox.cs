using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryGadget
{
    /// <summary>Claude Code のフックから届いた知らせ1件(Stop=返事を書き終えた、Notification=承認待ちなど)。</summary>
    internal sealed record ClaudeHookEvent(string Event, string SessionId, string Cwd, string? Message, string? NotificationType);

    /// <summary>
    /// Claude Code のフック(~/.claude/settings.json の hooks)が置いていくファイルを受け取る。
    /// フックは標準入力の JSON をそのまま %LOCALAPPDATA%\SecGad\claude-inbox\&lt;時刻&gt;.json に書く(書き終えてから .tmp を .json へ改名)。
    /// ポートも外のツールも使わない。秘書さんが止まっている間に溜まった古いものは知らせずに消す。
    /// </summary>
    internal static class ClaudeInbox
    {
        /// <summary>これより古い知らせは、秘書さんが止まっていた間のものなので出さない</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        /// <summary>返事の本文が長いと大きくなるので上限を決めておく(超えたら捨てる)</summary>
        private const long _maxFileBytes = 8 * 1024 * 1024;

        private static readonly Regex _guid = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

        public static string Dir => Path.Combine(Settings.LocalDir, "claude-inbox");

        /// <summary>届いている知らせを古い順に読み、読んだファイルは消す。古すぎるもの・読めないものも消す。</summary>
        public static IReadOnlyList<ClaudeHookEvent> Drain(string? dir = null, DateTime? now = null)
        {
            string inbox = dir ?? Dir;
            DateTime at = now ?? DateTime.Now;
            var list = new List<ClaudeHookEvent>();
            if (!Directory.Exists(inbox))
            {
                return list;
            }
            var files = new DirectoryInfo(inbox).EnumerateFiles().OrderBy(f => f.LastWriteTime).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            foreach (FileInfo file in files)
            {
                bool isJson = file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
                bool old = at - file.LastWriteTime > MaxAge;
                // 書いている途中の .tmp は、古くなる(フックが途中で死んだ)まで触らない
                if (!isJson && !old)
                {
                    continue;
                }
                try
                {
                    if (isJson && !old && file.Length <= _maxFileBytes)
                    {
                        ClaudeHookEvent? e = Parse(File.ReadAllText(file.FullName, Encoding.UTF8));
                        if (e != null)
                        {
                            list.Add(e);
                        }
                    }
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 消せなかったものは次の回に
                }
            }
            return list;
        }

        /// <summary>フックの標準入力の JSON を読む。会話 ID が無い・形が違う・知らない種類なら null。</summary>
        public static ClaudeHookEvent? Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                string? ev = Str(root, "hook_event_name");
                string? id = Str(root, "session_id");
                if ((ev != "Stop" && ev != "Notification") || id == null || !_guid.IsMatch(id))
                {
                    return null;
                }
                string? message = ev == "Stop" ? Str(root, "last_assistant_message") : Str(root, "message");
                return new ClaudeHookEvent(ev, id, Str(root, "cwd") ?? string.Empty, message, Str(root, "notification_type"));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Str(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }
}
