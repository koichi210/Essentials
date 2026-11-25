using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Services
{
    /// <summary>
    /// JavaScript の JSON.stringify と同じ形の文字列を作る。
    /// 設定ファイル(launcher.json)は MagicLauncher(Node)と共有しているので、こちらが保存しても
    /// 向こうが保存したときと同じ見た目(インデント2・日本語や絵文字はそのまま)になるようにする。
    /// System.Text.Json の既定の書き出しは BMP 外の文字(🎮 等の絵文字)を \uXXXX に変えてしまうため自前で書く。
    /// </summary>
    public static class JsJson
    {
        /// <summary>JSON.stringify(node, null, 2) と同じ形</summary>
        public static string StringifyIndented(JsonNode node)
        {
            var sb = new StringBuilder();
            Write(sb, node, "", true);
            return sb.ToString();
        }

        /// <summary>JSON.stringify(node) と同じ形(改行・空白なし)</summary>
        public static string StringifyCompact(JsonNode node)
        {
            var sb = new StringBuilder();
            Write(sb, node, "", false);
            return sb.ToString();
        }

        /// <summary>
        /// MagicLauncher が id の無い項目に付ける「中身と位置から決まる id」と同じ値。
        /// server.js の itemId(): sha1(`${index}\n${JSON.stringify(item)}`) の先頭8桁に i を付けたもの。
        /// </summary>
        public static string ItemIdFor(JsonNode item, int index)
        {
            string source = index.ToString(CultureInfo.InvariantCulture) + "\n" + StringifyCompact(item);
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(source));
            return "i" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }

        /// <summary>
        /// JavaScript のオブジェクトのキーの順: 配列の添字に見えるキー("0" "1" "2024" など)が先に小さい順、残りは入れた順。
        /// JSON.parse も JSON.stringify もこの順になるので、タグ名が数字だけのときの並び・書き出しを MagicLauncher と揃えるのに使う。
        /// </summary>
        public static IEnumerable<KeyValuePair<string, JsonNode>> JsOrder(JsonObject obj)
        {
            var indexKeys = new List<(uint Index, KeyValuePair<string, JsonNode> Pair)>();
            var otherKeys = new List<KeyValuePair<string, JsonNode>>();
            foreach (var pair in obj)
            {
                if (IsArrayIndex(pair.Key, out uint index))
                {
                    indexKeys.Add((index, pair));
                }
                else
                {
                    otherKeys.Add(pair);
                }
            }
            return indexKeys.OrderBy(k => k.Index).Select(k => k.Pair).Concat(otherKeys);
        }

        // "0" か、0 で始まらない数字だけで 2^32-2 以下(JavaScript の配列の添字になるキー)
        private static bool IsArrayIndex(string key, out uint index)
        {
            index = 0;
            if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0') || !key.All(c => c >= '0' && c <= '9'))
            {
                return false;
            }
            return uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < uint.MaxValue;
        }

        private static void Write(StringBuilder sb, JsonNode node, string indent, bool pretty)
        {
            switch (node)
            {
                case null:
                    sb.Append("null");
                    return;
                case JsonObject obj:
                    WriteObject(sb, obj, indent, pretty);
                    return;
                case JsonArray array:
                    WriteArray(sb, array, indent, pretty);
                    return;
                case JsonValue value:
                    WriteValue(sb, value);
                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, JsonObject obj, string indent, bool pretty)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            string inner = indent + "  ";
            sb.Append('{');
            bool first = true;
            foreach (var pair in JsOrder(obj))
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                if (pretty)
                {
                    sb.Append('\n').Append(inner);
                }
                WriteString(sb, pair.Key);
                sb.Append(pretty ? ": " : ":");
                Write(sb, pair.Value, inner, pretty);
            }
            if (pretty)
            {
                sb.Append('\n').Append(indent);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, JsonArray array, string indent, bool pretty)
        {
            if (array.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            string inner = indent + "  ";
            sb.Append('[');
            for (int i = 0; i < array.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                if (pretty)
                {
                    sb.Append('\n').Append(inner);
                }
                Write(sb, array[i], inner, pretty);
            }
            if (pretty)
            {
                sb.Append('\n').Append(indent);
            }
            sb.Append(']');
        }

        private static void WriteValue(StringBuilder sb, JsonValue value)
        {
            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    WriteString(sb, value.GetValue<string>());
                    return;
                case JsonValueKind.True:
                    sb.Append("true");
                    return;
                case JsonValueKind.False:
                    sb.Append("false");
                    return;
                case JsonValueKind.Null:
                    sb.Append("null");
                    return;
                default:
                    // 数値は読んだときの表記のまま(手で書いた 2 や 1.5 をそのまま残す)
                    sb.Append(value.ToJsonString());
                    return;
            }
        }

        // JSON.stringify の文字列の書き方: " と \ と制御文字だけエスケープ、ペアになっていないサロゲートは \uXXXX
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (c < 0x20)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append(c).Append(s[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }
    }
}
