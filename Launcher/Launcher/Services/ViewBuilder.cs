using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Launcher.Models;

namespace Launcher.Services
{
    /// <summary>
    /// 設定ファイルの中身から画面の「魔法陣 → セクション(タグ) → 項目」を組み立てる。
    /// 並べ方は MagicLauncher の画面(public/launcher.js の buildView)に合わせる:
    /// ・魔法陣は「最初の魔法陣」→ circles の順 → タグに書かれた順
    /// ・セクションは tags のキーの順。置く魔法陣(circle)が空欄なら最初の魔法陣
    /// ・項目は先頭のタグのセクションに並ぶ。タグの無い項目は最初の魔法陣の「タグなし」へ
    /// 違いは、まだ項目の無いタグもセクションとして出すこと(作ったばかりのセクションに項目を足せるように)。
    /// </summary>
    public static class ViewBuilder
    {
        /// <summary>タグなしの項目をまとめる仮のタグ(MagicLauncher の UNTAGGED と同じ)</summary>
        public const string UntaggedKey = "\u0000untagged";

        private static readonly Color _fallbackColor = Color.FromRgb(0x7f, 0xb0, 0xff);
        private static readonly Color _untaggedColor = Color.FromRgb(0x8a, 0xa4, 0xc8);

        public static List<CircleModel> Build(JsonObject source, Func<string, bool> isExpanded)
        {
            // 手で書き足したタグにも(ファイルには書かずに)色を付けて見せる
            JsonObject data = (JsonObject)source.DeepClone();
            ConfigService.EnsureTags(data);
            JsonObject tags = (JsonObject)data["tags"];
            JsonArray rawItems = data["items"] as JsonArray ?? new JsonArray();

            string first = ConfigService.FirstCircleName(data);
            var circleOrder = new List<string>();
            void AddCircle(string name)
            {
                if (!circleOrder.Contains(name))
                {
                    circleOrder.Add(name);
                }
            }
            AddCircle(first);
            ConfigService.CleanTags(data["circles"]).ForEach(AddCircle);

            var sections = new Dictionary<string, (SectionModel Section, string Circle)>();
            var sectionOrder = new List<string>();
            foreach (var pair in JsJson.JsOrder(tags))
            {
                string circle = ConfigService.Str(pair.Value?["circle"]).Trim();
                if (circle.Length == 0)
                {
                    circle = first;
                }
                AddCircle(circle);
                var section = new SectionModel
                {
                    Name = pair.Key,
                    Key = pair.Key,
                    ColorBrush = Freeze(ParseColor(ConfigService.Str(pair.Value?["color"])) ?? _fallbackColor),
                    IsExpanded = isExpanded(pair.Key),
                };
                sections[pair.Key] = (section, circle);
                sectionOrder.Add(pair.Key);
            }

            List<string> ids = ConfigService.ItemIds(rawItems);
            for (int i = 0; i < rawItems.Count; i++)
            {
                if (!ConfigService.IsValidItem(rawItems[i]))
                {
                    continue;
                }
                JsonObject raw = (JsonObject)rawItems[i];
                List<string> itemTags = ConfigService.CleanTags(raw["tags"]);
                string path = ConfigService.StripQuotes(ConfigService.Str(raw["path"]));
                string name = ConfigService.Str(raw["name"]);
                var item = new ItemModel
                {
                    Id = ids[i],
                    Name = name,
                    Path = path,
                    Tags = itemTags,
                    Emoji = EmojiFor(ConfigService.Str(raw["emoji"]), path),
                    Note = ConfigService.Str(raw["note"]),
                    Title = name.Trim().Length > 0 ? name.Trim() : DerivedName(path),
                };
                string key = itemTags.Count > 0 ? itemTags[0] : UntaggedKey;
                if (!sections.ContainsKey(key))
                {
                    var untagged = new SectionModel
                    {
                        Name = "タグなし",
                        Key = UntaggedKey,
                        IsUntagged = true,
                        ColorBrush = Freeze(_untaggedColor),
                        IsExpanded = isExpanded(UntaggedKey),
                    };
                    sections[key] = (untagged, first);
                    sectionOrder.Add(key);
                }
                sections[key].Section.Items.Add(item);
            }

            var circles = circleOrder.Select(n => new CircleModel { Name = n, IsFirst = n == first }).ToList();
            foreach (string key in sectionOrder)
            {
                var (section, circle) = sections[key];
                circles.First(c => c.Name == circle).Sections.Add(section);
            }
            // セクションの無い魔法陣は出さない(最初の魔法陣だけは、セクションを足せるよう残す)
            return circles.Where(c => c.IsFirst || c.Sections.Count > 0).ToList();
        }

        /// <summary>名前が空なら、URL はホスト名、パスは末尾の名前(MagicLauncher の derivedName と同じ)</summary>
        public static string DerivedName(string path)
        {
            string p = ConfigService.StripQuotes(path);
            if (p.Length == 0)
            {
                return "";
            }
            if (ConfigService.IsUrl(p))
            {
                if (Uri.TryCreate(p, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
                {
                    return uri.Host;
                }
                string rest = Regex.Replace(p, @"\A[a-zA-Z0-9+.-]+:/*", "").Split('/', '?', '#')[0];
                return rest.Length > 0 ? rest : p;
            }
            string trimmed = p.TrimEnd('\\', '/');
            string last = trimmed.Split('\\', '/').Last();
            return last.Length > 0 ? last : p;
        }

        // 絵文字が登録されていなければ、URL は種類の絵文字(ファイル・フォルダの区別はネットワークパスで固まらないよう調べない)
        private static string EmojiFor(string emoji, string path)
        {
            if (!string.IsNullOrWhiteSpace(emoji))
            {
                return emoji.Trim();
            }
            if (ConfigService.IsUrl(path))
            {
                return Regex.IsMatch(path, @"\Ahttps?:", RegexOptions.IgnoreCase) ? "🌐" : "🔗";
            }
            return "";
        }

        private static Color? ParseColor(string hex)
        {
            if (!ConfigService.IsHexColor(hex))
            {
                return null;
            }
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
