using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SecretaryGadget
{
    /// <summary>
    /// メンション判定。各文字列からメールアドレス(\S+@\S+)を取り除いたうえで、
    /// キーワードのどれかを大文字小文字無視で部分一致したらメンションとみなす。
    /// 自分のアドレスが見出しに出ることがあるため、アドレス除去は必須。
    /// </summary>
    internal static class MentionFilter
    {
        private static readonly Regex _address = new Regex(@"\S+@\S+");

        public static bool IsMention(IEnumerable<string?> texts, IReadOnlyList<string> keywords)
        {
            List<string> words = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
            if (words.Count == 0)
            {
                return false;
            }
            foreach (string? text in texts)
            {
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                string stripped = _address.Replace(text, string.Empty);
                if (words.Any(w => stripped.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ContainsAddress(string text)
        {
            return _address.IsMatch(text);
        }
    }
}
