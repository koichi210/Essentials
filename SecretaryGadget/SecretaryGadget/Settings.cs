using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace SecretaryGadget
{
    /// <summary>
    /// settings.json の中身。起動時に1回だけ読み、メニュー操作のたびに全項目を書き戻す。
    /// 場所は2段構え: %LOCALAPPDATA%\SecGad\settings_location.json の SettingsDir(環境変数展開可)のフォルダ、
    /// メモが無い・読めない場合は %LOCALAPPDATA%\SecGad\settings.json。
    /// </summary>
    internal sealed class Settings
    {
        private const string _fileName = "settings.json";
        private const string _locationFileName = "settings_location.json";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 日本語を\uXXXXにエスケープせず、手で編集しやすいそのままの文字で書く
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public string UserName { get; set; } = "<名>さん";

        public string Skin { get; set; } = "brown";

        public double SkinScale { get; set; } = 1.0;

        public int DisplayHeight { get; set; } = 420;

        public int DisplaySeconds { get; set; } = 20;

        public int OutlookPollSeconds { get; set; } = 20;

        public List<string> OutlookWatchFolders { get; set; } = new List<string> { "受信トレイ" };

        public int TeamsPopupPollMilliseconds { get; set; } = 500;

        public int TeamsMentionCountSeconds { get; set; } = 60;

        public bool TeamsToastWatch { get; set; } = false;

        public int RestartMemoryMB { get; set; } = 1500;

        public bool NotifyOutlookMail { get; set; } = true;

        public bool NotifyMeetings { get; set; } = true;

        public bool NotifyTeams { get; set; } = true;

        public int MeetingLeadMinutes { get; set; } = 5;

        public bool MeetingIncludeOrganized { get; set; } = false;

        public string MarkReadSearchFolder { get; set; } = "未読";

        public bool TeamsMentionOnly { get; set; } = true;

        public bool TeamsHideMentionPopups { get; set; } = true;

        public List<string> TeamsMentionKeywords { get; set; } = new List<string> { "<姓・漢字>", "<姓・ひらがな>", "<姓・ローマ字>" };

        public int TeamsPollSeconds { get; set; } = 1;

        public bool Paused { get; set; } = false;

        /// <summary>PCごとのデータ置き場(ログ・追加スキン・設定の場所メモ)</summary>
        public static string LocalDir => LocalDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecGad");

        /// <summary>単体テストで本物の %LOCALAPPDATA%\SecGad を触らないための差し替え先</summary>
        internal static string? LocalDirOverride { get; set; }

        public static string LocationFilePath => Path.Combine(LocalDir, _locationFileName);

        /// <summary>いま使っている settings.json のフルパス(Loadで決まる)</summary>
        [JsonIgnore]
        public string FilePath { get; private set; } = Path.Combine(LocalDir, _fileName);

        /// <summary>settings_location.json を見て settings.json のフォルダを決める。</summary>
        public static string ResolveSettingsDir()
        {
            try
            {
                if (File.Exists(LocationFilePath))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LocationFilePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (doc.RootElement.TryGetProperty("SettingsDir", out JsonElement dir) && dir.ValueKind == JsonValueKind.String)
                    {
                        string expanded = Environment.ExpandEnvironmentVariables(dir.GetString() ?? string.Empty).Trim();
                        if (expanded.Length > 0)
                        {
                            return expanded;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("設定の場所メモが読めないため既定の場所を使います: " + ex.GetType().Name);
            }
            return LocalDir;
        }

        public static Settings Load()
        {
            string path = Path.Combine(ResolveSettingsDir(), _fileName);
            Settings settings = new Settings();
            try
            {
                if (File.Exists(path))
                {
                    settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), _jsonOptions) ?? new Settings();
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings.json が読めないため既定値で起動します: " + ex.GetType().Name);
                settings = new Settings();
            }
            settings.FilePath = path;
            settings.Normalize();
            return settings;
        }

        /// <summary>いま使っている settings.json のフォルダ</summary>
        [JsonIgnore]
        public string Dir => Path.GetDirectoryName(FilePath) ?? LocalDir;

        /// <summary>全項目を書き戻す。失敗してもアプリは止めない。</summary>
        public void Save()
        {
            TrySave(FilePath);
        }

        /// <summary>
        /// settings.json のフォルダを変える。選んだフォルダに settings.json が無ければ今の設定をそこへ保存し、
        /// あればそちらを読み込んで使う(共有フォルダで他のPCと設定を揃える用)。元の場所の settings.json は消さない
        /// (他のPCが同じ場所を見ているかもしれないため)。保存できなければ場所は変えず null を返す。
        /// </summary>
        public static Settings? ChangeDir(Settings current, string newDir)
        {
            string newPath = Path.Combine(newDir, _fileName);
            bool exists = File.Exists(newPath);
            if (!exists && !current.TrySave(newPath))
            {
                return null;
            }
            try
            {
                WriteLocation(newDir);
            }
            catch (Exception ex)
            {
                Log.Write("設定の場所メモを書けませんでした: " + ex.GetType().Name);
                return null;
            }
            Log.Write(exists ? "設定ファイルのフォルダを変更しました(既存の設定を読み込み)" : "設定ファイルのフォルダを変更しました(今の設定を保存)");
            if (!exists)
            {
                current.FilePath = newPath;
                return current;
            }
            return Load();
        }

        private bool TrySave(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("settings.json を保存できませんでした: " + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>settings_location.json を書き換えて、settings.json の場所を変える。</summary>
        public static void WriteLocation(string settingsDir)
        {
            Directory.CreateDirectory(LocalDir);
            string json = JsonSerializer.Serialize(new Dictionary<string, string> { ["SettingsDir"] = settingsDir }, _jsonOptions);
            File.WriteAllText(LocationFilePath, json, new UTF8Encoding(false));
        }

        /// <summary>手で編集されて null や範囲外になった値を使える値に戻す。</summary>
        private void Normalize()
        {
            UserName ??= "<名>さん";
            Skin ??= "brown";
            OutlookWatchFolders ??= new List<string> { "受信トレイ" };
            TeamsMentionKeywords ??= new List<string>();
            MarkReadSearchFolder ??= string.Empty;
            if (SkinScale <= 0)
            {
                SkinScale = 1.0;
            }
            if (DisplayHeight <= 0)
            {
                DisplayHeight = 420;
            }
            if (DisplaySeconds <= 0)
            {
                DisplaySeconds = 20;
            }
            OutlookPollSeconds = Math.Max(1, OutlookPollSeconds);
            TeamsPopupPollMilliseconds = Math.Max(100, TeamsPopupPollMilliseconds);
            TeamsMentionCountSeconds = Math.Max(5, TeamsMentionCountSeconds);
            TeamsPollSeconds = Math.Max(1, TeamsPollSeconds);
            MeetingLeadMinutes = Math.Max(0, MeetingLeadMinutes);
            RestartMemoryMB = Math.Max(0, RestartMemoryMB);
        }
    }

    /// <summary>
    /// log.txt(%LOCALAPPDATA%\SecGad)への記録。
    /// メールの件名・差出人・Teamsの本文・人名は書かない(件数・種類・固定見出しのみ)。
    /// </summary>
    internal static class Log
    {
        private const long _maxBytes = 1024 * 1024;
        private static readonly object _lock = new object();

        public static string FilePath => Path.Combine(Settings.LocalDir, "log.txt");

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Settings.LocalDir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > _maxBytes)
                    {
                        // 肥大化しないよう1世代だけ残して切り替える
                        File.Copy(FilePath, Path.Combine(Settings.LocalDir, "log.old.txt"), true);
                        File.Delete(FilePath);
                    }
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}", new UTF8Encoding(false));
                }
            }
            catch
            {
                // ログが書けなくても本体の動作は続ける
            }
        }
    }
}
