using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Launcher.Services
{
    /// <summary>設定ファイルを読めない・項目が見つからない等、画面にそのまま出す理由付きの失敗</summary>
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message)
        {
        }
    }

    /// <summary>設定ファイルの場所</summary>
    public sealed class ConfigLocation
    {
        /// <summary>settings.json に書かれている値(%VAR% 展開前)。既定のときは空</summary>
        public string Raw { get; init; }

        /// <summary>実際に読み書きするファイルのフルパス</summary>
        public string FullPath { get; init; }

        /// <summary>settings.json に場所が書かれておらず、このアプリの既定の場所を使っているか</summary>
        public bool IsDefault { get; init; }
    }

    /// <summary>ある時点で読んだ設定ファイルの中身</summary>
    public sealed class ConfigSnapshot
    {
        public ConfigLocation Location { get; init; }

        public bool Exists { get; init; }

        /// <summary>読めなかった理由。null なら読めている(この間は保存しない)</summary>
        public string Error { get; init; }

        /// <summary>版数(更新時刻ms-サイズ)。変更チェックに使う</summary>
        public string Version { get; init; }

        /// <summary>中身。Error があるときは null</summary>
        public JsonObject Data { get; init; }
    }

    /// <summary>
    /// 設定ファイル(launcher.json)の読み書き。MagicLauncher(魔法陣ランチャー)と同じファイル・同じ形式を使う。
    /// 形式と読み書きのルールは MagicLauncher の server.js / README に合わせている(向こうが正、こちらは合わせる側):
    /// ・ファイルの場所は %LOCALAPPDATA%\MagicLauncher\settings.json の configPath(%VAR% 可)。
    ///   環境変数 MAGIC_LAUNCHER_DATA があればその下の settings.json(確認用)。
    /// ・書き込みのたびにディスクの内容を読み直して差分だけ当てる(画面の古い一覧で上書きしない)。直前の内容は .bak に1世代。
    /// ・読めない・一番外が {} でない・items が配列でない・フォルダを指定 → 書き込みを拒否。
    /// ・emoji / note / circleColors / idle / columns など、こちらで使わない値もそのまま残す。
    /// </summary>
    public class ConfigService
    {
        // MagicLauncher が新しいタグに順に割り当てる 12 色(server.js の PALETTE と同じ)
        private static readonly string[] _palette =
        {
            "#3f7fe0", "#5fd0ff", "#5b5ff0", "#1fc7a0", "#b06cf0", "#e0b43a",
            "#ff7a6a", "#ff5fd2", "#9be15d", "#ff9f43", "#7b9cff", "#e05a8a",
        };

        // JavaScript の /^#[0-9a-f]{6}$/i と同じ(.NET の $ は末尾の改行の前にも当たるので \z)
        private static readonly Regex _hexColor = new Regex(@"\A#[0-9a-fA-F]{6}\z");
        private static readonly Regex _envVar = new Regex(@"%([^%\\/:*?""<>|\r\n]+)%");
        private static readonly Regex _urlScheme = new Regex(@"\A[a-zA-Z][a-zA-Z0-9+.-]+:");

        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        private readonly string _dataDir;

        public ConfigService() : this(null, null)
        {
        }

        /// <param name="dataDir">settings.json の置き場(null なら MAGIC_LAUNCHER_DATA か %LOCALAPPDATA%\MagicLauncher)</param>
        /// <param name="defaultConfigPath">settings.json に場所が無いときに使うファイル(null なら %LOCALAPPDATA%\Launcher\launcher.json)</param>
        public ConfigService(string dataDir, string defaultConfigPath)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string envDataDir = Environment.GetEnvironmentVariable("MAGIC_LAUNCHER_DATA");
            _dataDir = dataDir ?? (string.IsNullOrEmpty(envDataDir) ? Path.Combine(localAppData, "MagicLauncher") : envDataDir);
            DefaultConfigPath = defaultConfigPath ?? Path.Combine(localAppData, "Launcher", "launcher.json");
        }

        /// <summary>MagicLauncher と共有している「設定ファイルの場所」のファイル</summary>
        public string SettingsFilePath => Path.Combine(_dataDir, "settings.json");

        /// <summary>settings.json に場所が書かれていないときに使うファイル</summary>
        public string DefaultConfigPath { get; }

        // ------------------------------------------------------------ 設定ファイルの場所

        public ConfigLocation GetLocation()
        {
            string raw = Str(ReadSettings()["configPath"]).Trim();
            if (raw.Length == 0)
            {
                return new ConfigLocation { Raw = "", FullPath = DefaultConfigPath, IsDefault = true };
            }
            string full;
            try
            {
                full = Path.GetFullPath(ExpandEnv(raw));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                full = raw; // 読むときに「確認できません」になる
            }
            return new ConfigLocation { Raw = raw, FullPath = full, IsDefault = false };
        }

        /// <summary>
        /// 設定ファイルの場所を settings.json に書く(MagicLauncher の「設定ファイルの場所」と同じ値なので、向こうも同じファイルを使うようになる)。
        /// 空なら場所の指定を消す。
        /// </summary>
        public void SetConfigPath(string path)
        {
            string raw = StripQuotes(path);
            JsonObject settings = ReadSettings();
            if (raw.Length == 0)
            {
                settings.Remove("configPath");
                WriteSettings(settings);
                return;
            }
            string full = ExpandEnv(raw);
            if (!Path.IsPathFullyQualified(full))
            {
                throw new ConfigException("フルパスで指定してください(%USERPROFILE% などは使えます)");
            }
            if (Directory.Exists(full))
            {
                throw new ConfigException("フォルダは指定できません(launcher.json などのファイル名まで入れてください)");
            }
            if (!File.Exists(full) && !Directory.Exists(Path.GetDirectoryName(full)))
            {
                throw new ConfigException("そのフォルダがありません");
            }
            settings["configPath"] = raw;
            WriteSettings(settings);
        }

        private JsonObject ReadSettings()
        {
            try
            {
                string text = File.ReadAllText(SettingsFilePath, Encoding.UTF8).TrimStart('﻿');
                return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch (Exception)
            {
                // 無い・壊れている → 場所の指定なし(MagicLauncher と同じ扱い)
                return new JsonObject();
            }
        }

        private void WriteSettings(JsonObject settings)
        {
            Directory.CreateDirectory(_dataDir);
            File.WriteAllText(SettingsFilePath, JsJson.StringifyIndented(settings) + "\n", _utf8NoBom);
        }

        // ------------------------------------------------------------ 読み込み

        /// <summary>今の版数(場所＋更新時刻＋サイズ)。中身は読まないので頻繁に呼んでよい</summary>
        public string GetVersionToken()
        {
            ConfigLocation location = GetLocation();
            return location.FullPath + "|" + StatVersion(location.FullPath);
        }

        private static string StatVersion(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return Directory.Exists(path) ? "dir" : "0-0";
                }
                long ms = (long)Math.Floor((info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalMilliseconds);
                return ms + "-" + info.Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return "err";
            }
        }

        /// <summary>毎回ディスクから読み直す。壊れていれば Error を付けて返す</summary>
        public ConfigSnapshot Load()
        {
            ConfigLocation location = GetLocation();
            string path = location.FullPath;
            try
            {
                if (Directory.Exists(path))
                {
                    return Broken(location, true, StatVersion(path), "フォルダが指定されています(JSON ファイルを指定してください)");
                }
                if (!File.Exists(path))
                {
                    // 無ければ空(最初に保存したときに作る)
                    return new ConfigSnapshot { Location = location, Exists = false, Version = "0-0", Data = EmptyData() };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Broken(location, false, "0-0", $"設定ファイルを確認できません({ex.Message})");
            }

            string version = StatVersion(path);
            JsonNode node;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
                node = JsonNode.Parse(text);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Broken(location, true, version, $"設定ファイルを読めません({ex.Message})");
            }
            catch (Exception ex)
            {
                return Broken(location, true, version, $"JSON として読めません({ex.Message})");
            }

            if (node is not JsonObject data)
            {
                return Broken(location, true, version, "一番外が {} ではありません");
            }
            if (data.TryGetPropertyValue("items", out JsonNode items) && items is not JsonArray)
            {
                return Broken(location, true, version, "items が配列ではありません");
            }
            return new ConfigSnapshot { Location = location, Exists = true, Version = version, Data = data };
        }

        private static ConfigSnapshot Broken(ConfigLocation location, bool exists, string version, string error)
        {
            return new ConfigSnapshot { Location = location, Exists = exists, Version = version, Error = error, Data = null };
        }

        private static JsonObject EmptyData()
        {
            return new JsonObject { ["version"] = 1, ["title"] = "Launcher", ["items"] = new JsonArray() };
        }

        // ------------------------------------------------------------ 書き込み

        /// <summary>
        /// ディスクの内容を読み直し、fn で差分を当てて保存する(server.js の mutate と同じ手順)。
        /// 読めない設定ファイルには書かない。
        /// </summary>
        public T Mutate<T>(Func<JsonObject, T> fn)
        {
            ConfigSnapshot snapshot = Load();
            if (snapshot.Error != null)
            {
                throw new ConfigException($"設定ファイルを読めないため保存しません: {snapshot.Error}");
            }
            JsonObject data = snapshot.Data;
            if (data["items"] is not JsonArray items)
            {
                items = new JsonArray();
                data["items"] = items;
            }
            // id の無い項目には、読んだときと同じ id を書き込んでおく(以降は位置が変わっても同じ id)
            List<string> ids = ItemIds(items);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is JsonObject item && !(item["id"] is JsonValue idValue && idValue.GetValueKind() == JsonValueKind.String && idValue.GetValue<string>() == ids[i]))
                {
                    var withId = new JsonObject { ["id"] = ids[i] };
                    foreach (var pair in item)
                    {
                        if (pair.Key != "id")
                        {
                            withId[pair.Key] = pair.Value?.DeepClone();
                        }
                    }
                    items[i] = withId;
                }
            }
            EnsureTags(data);
            T result = fn(data);
            EnsureTags(data);
            WriteConfig(snapshot.Location.FullPath, data, snapshot.Exists);
            return result;
        }

        private static void WriteConfig(string file, JsonObject data, bool existed)
        {
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (existed)
            {
                File.Copy(file, file + ".bak", true); // 直前の内容を1世代残す
            }
            File.WriteAllText(file, JsJson.StringifyIndented(data) + "\n", _utf8NoBom);
        }

        // ------------------------------------------------------------ 画面からの操作

        /// <summary>セクション(=タグ)を追加し、circle の魔法陣に置く</summary>
        public void AddTag(string name, string circle)
        {
            name = (name ?? "").Trim();
            circle = (circle ?? "").Trim();
            if (name.Length == 0)
            {
                throw new ConfigException("セクションの名前がありません");
            }
            Mutate(data =>
            {
                JsonObject tags = (JsonObject)data["tags"];
                if (tags[name] is JsonObject)
                {
                    throw new ConfigException($"「{name}」というセクション(タグ)はもうあります");
                }
                var used = tags.Select(t => Str(t.Value?["color"])).Where(IsHexColor);
                var tag = new JsonObject { ["color"] = NextPaletteColor(used) };
                tags[name] = tag;
                if (circle.Length > 0)
                {
                    // 新しい魔法陣の名前なら並びの末尾へ(並びが空なら、いまの最初の魔法陣を先頭に固定してから)。server.js の POST /api/tag と同じ
                    List<string> circles = CleanTags(data["circles"]);
                    if (circles.Count == 0)
                    {
                        circles.Add(FirstCircleName(data));
                    }
                    if (!circles.Contains(circle))
                    {
                        circles.Add(circle);
                    }
                    data["circles"] = ToJsonArray(circles);
                    tag["circle"] = circle;
                }
                return 0;
            });
        }

        /// <summary>セクション(=タグ)の名前を変える。並び・色・置く魔法陣はそのままで、付いている項目のタグも書き換える</summary>
        public void RenameTag(string from, string to)
        {
            from = (from ?? "").Trim();
            to = (to ?? "").Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                throw new ConfigException("変更前と変更後のセクションの名前を指定してください");
            }
            Mutate(data =>
            {
                JsonObject tags = (JsonObject)data["tags"];
                JsonArray items = (JsonArray)data["items"];
                bool used = items.Any(it => IsValidItem(it) && CleanTags(it["tags"]).Contains(from));
                if (tags[from] is not JsonObject && !used)
                {
                    throw new ConfigException("セクションが見つかりません(ほかの PC で変更されたかも)");
                }
                if (from == to)
                {
                    return 0;
                }
                if (tags[to] is JsonObject)
                {
                    throw new ConfigException($"「{to}」というセクション(タグ)はもうあります");
                }
                data["tags"] = RenameKey(tags, from, to);
                foreach (JsonObject item in items.OfType<JsonObject>())
                {
                    if (item["tags"] is JsonArray itemTags)
                    {
                        item["tags"] = ToJsonArray(CleanStrings(itemTags.Select(t => Str(t).Trim() == from ? to : Str(t))));
                    }
                }
                return 0;
            });
        }

        /// <summary>
        /// セクション(=タグ)を消す。そのタグだけが付いていた項目は削除し、ほかのタグも付いている項目からはタグを外すだけ(残りのタグのセクションへ移る)。
        /// 戻り値は(削除した項目数, タグを外しただけの項目数)。
        /// </summary>
        public (int Deleted, int Moved) DeleteTag(string name)
        {
            name = (name ?? "").Trim();
            return Mutate(data =>
            {
                JsonObject tags = (JsonObject)data["tags"];
                JsonArray items = (JsonArray)data["items"];
                bool used = items.Any(it => IsValidItem(it) && CleanTags(it["tags"]).Contains(name));
                if (name.Length == 0 || (tags[name] is not JsonObject && !used))
                {
                    throw new ConfigException("セクションが見つかりません(ほかの PC で変更されたかも)");
                }
                int deleted = 0;
                int moved = 0;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i] is not JsonObject item || item["tags"] is not JsonArray)
                    {
                        continue;
                    }
                    List<string> itemTags = CleanTags(item["tags"]);
                    if (!itemTags.Contains(name))
                    {
                        continue;
                    }
                    itemTags.Remove(name);
                    if (itemTags.Count == 0 && IsValidItem(item))
                    {
                        items.RemoveAt(i);
                        deleted++;
                    }
                    else
                    {
                        item["tags"] = ToJsonArray(itemTags);
                        moved++;
                    }
                }
                tags.Remove(name);
                return (deleted, moved);
            });
        }

        /// <summary>項目を追加する。tag が null ならタグなし。戻り値は新しい id</summary>
        public string AddItem(string tag, string name, string path)
        {
            string p = StripQuotes(path);
            if (p.Length == 0)
            {
                throw new ConfigException("リンク先が空です");
            }
            var tagList = new List<string>();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tagList.Add(tag.Trim());
            }
            return Mutate(data =>
            {
                JsonArray items = (JsonArray)data["items"];
                string id = NewId(items);
                // 項目の形は server.js の POST /api/item と同じ並び
                items.Add(new JsonObject
                {
                    ["id"] = id,
                    ["name"] = (name ?? "").Trim(),
                    ["path"] = p,
                    ["tags"] = ToJsonArray(tagList),
                    ["emoji"] = "",
                    ["note"] = "",
                });
                return id;
            });
        }

        /// <summary>項目の名前とリンク先を変える(タグ・絵文字・メモなどはそのまま)</summary>
        public void UpdateItem(string id, string name, string path)
        {
            string p = StripQuotes(path);
            if (p.Length == 0)
            {
                throw new ConfigException("リンク先が空です");
            }
            Mutate(data =>
            {
                JsonObject item = FindItem(data, id);
                item["name"] = (name ?? "").Trim();
                item["path"] = p;
                return 0;
            });
        }

        public void DeleteItem(string id)
        {
            Mutate(data =>
            {
                JsonArray items = (JsonArray)data["items"];
                int index = ItemIds(items).IndexOf(id ?? "");
                if (string.IsNullOrEmpty(id) || index < 0)
                {
                    throw new ConfigException("この項目が見つかりません(ほかの PC で削除されたかも)");
                }
                items.RemoveAt(index);
                return 0;
            });
        }

        private static JsonObject FindItem(JsonObject data, string id)
        {
            JsonArray items = (JsonArray)data["items"];
            int index = ItemIds(items).IndexOf(id ?? "");
            if (string.IsNullOrEmpty(id) || index < 0 || items[index] is not JsonObject item)
            {
                throw new ConfigException("この項目が見つかりません(ほかの PC で削除されたかも)");
            }
            return item;
        }

        // ------------------------------------------------------------ server.js と同じ小物

        /// <summary>JavaScript の str(v): 文字列はそのまま、null は空、それ以外は文字列にしたもの</summary>
        public static string Str(JsonNode node)
        {
            if (node == null)
            {
                return "";
            }
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                return value.GetValue<string>();
            }
            return node.ToJsonString();
        }

        /// <summary>%VAR% を環境変数で展開する(無い変数はそのまま残す)</summary>
        public static string ExpandEnv(string s)
        {
            return _envVar.Replace(s ?? "", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
        }

        /// <summary>「英字 + :」で始まり、スキームが 2 文字以上なら URL(C: はドライブなのでパス)</summary>
        public static bool IsUrl(string s)
        {
            return _urlScheme.IsMatch((s ?? "").Trim());
        }

        /// <summary>エクスプローラーの「パスのコピー」で付く前後の "..." を外す</summary>
        public static string StripQuotes(string s)
        {
            string t = (s ?? "").Trim();
            while (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
            {
                t = t.Substring(1, t.Length - 2).Trim();
            }
            return t;
        }

        public static bool IsHexColor(string s)
        {
            return _hexColor.IsMatch(s ?? "");
        }

        /// <summary>タグの一覧を「空白を除いた・空でない・重複なし」にする</summary>
        public static List<string> CleanTags(JsonNode tags)
        {
            return tags is JsonArray array ? CleanStrings(array.Select(Str)) : new List<string>();
        }

        private static List<string> CleanStrings(IEnumerable<string> values)
        {
            var result = new List<string>();
            foreach (string value in values)
            {
                string name = (value ?? "").Trim();
                if (name.Length > 0 && !result.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        public static bool IsValidItem(JsonNode item)
        {
            return item is JsonObject obj
                && obj["path"] is JsonValue path
                && path.GetValueKind() == JsonValueKind.String
                && path.GetValue<string>().Trim().Length > 0;
        }

        /// <summary>各項目の id(書かれていなければ中身と位置から決める。重複していたら後ろは決めた値)</summary>
        public static List<string> ItemIds(JsonArray items)
        {
            var seen = new HashSet<string>();
            var ids = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                string id = items[i] is JsonObject obj && obj["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
                if (id.Length == 0 || seen.Contains(id))
                {
                    id = JsJson.ItemIdFor(items[i], i);
                }
                seen.Add(id);
                ids.Add(id);
            }
            return ids;
        }

        /// <summary>「最初の魔法陣」の名前: circles の先頭、無ければ title、それも無ければ Launcher</summary>
        public static string FirstCircleName(JsonObject data)
        {
            if (data["circles"] is JsonArray circles)
            {
                string first = circles.Select(c => Str(c).Trim()).FirstOrDefault(c => c.Length > 0);
                if (first != null)
                {
                    return first;
                }
            }
            string title = Str(data["title"]).Trim();
            return title.Length > 0 ? title : "Launcher";
        }

        /// <summary>tags を「使われているタグ・色の無いタグにも色が付いた」形にする(data を書き換える)</summary>
        public static void EnsureTags(JsonObject data)
        {
            if (data["tags"] is not JsonObject tags)
            {
                tags = new JsonObject();
                data["tags"] = tags;
            }
            if (data["items"] is JsonArray items)
            {
                foreach (JsonNode item in items)
                {
                    if (!IsValidItem(item))
                    {
                        continue;
                    }
                    foreach (string t in CleanTags(item["tags"]))
                    {
                        if (tags[t] is not JsonObject)
                        {
                            tags[t] = new JsonObject();
                        }
                    }
                }
            }
            foreach (string key in tags.Select(p => p.Key).ToList())
            {
                if (tags[key] is not JsonObject)
                {
                    tags[key] = new JsonObject();
                }
            }
            // 色を割り当てる順も JavaScript のキーの順(数字だけのタグ名が先)に合わせる
            foreach (JsonObject tag in JsJson.JsOrder(tags).Select(p => (JsonObject)p.Value).ToList())
            {
                if (!IsHexColor(Str(tag["color"])))
                {
                    var used = tags.Select(p => Str(p.Value?["color"])).Where(IsHexColor).ToList();
                    tag["color"] = NextPaletteColor(used);
                }
            }
        }

        private static string NextPaletteColor(IEnumerable<string> usedColors)
        {
            var used = new HashSet<string>(usedColors.Select(c => c.ToLowerInvariant()));
            string free = _palette.FirstOrDefault(c => !used.Contains(c));
            return free ?? _palette[used.Count % _palette.Length];
        }

        private static string NewId(JsonArray items)
        {
            var used = new HashSet<string>(items.OfType<JsonObject>().Select(it => Str(it["id"])));
            while (true)
            {
                string id = "i" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                if (!used.Contains(id))
                {
                    return id;
                }
            }
        }

        private static JsonArray ToJsonArray(IEnumerable<string> values)
        {
            return new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
        }

        // キーの並びを保ったまま名前だけ変える
        private static JsonObject RenameKey(JsonObject obj, string from, string to)
        {
            var next = new JsonObject();
            foreach (var pair in obj.ToList())
            {
                next[pair.Key == from ? to : pair.Key] = pair.Value?.DeepClone();
            }
            return next;
        }
    }
}
