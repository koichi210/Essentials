using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FFEdit
{
    // 名称変換のうち、名前の文字列だけで結果が決まる変換(ファイルを触らないのでテストしやすい)。
    // WinForms版(CS_Form/FFEdit)とWPF版(Essentials/FFEdit)で同じ内容にしてある
    static class NameConverter
    {
        private const String _kanjiDigits = "〇一二三四五六七八九";
        private const String _smallUnits = "十百千";
        private const String _largeUnits = "万億兆";

        // 単独で出てきたときは数字として扱わない文字(「千葉」「百合」「万歳」などの熟語を壊さないため)。
        // 「十」は「第十話」のように単独でもよく使うので変換する
        private const String _standaloneSkipChars = "百千万億兆";

        private static readonly Regex _numberRegex = new Regex("[0-9]+");

        // 名前に含まれる漢数字を算用数字に置き換える。
        //   位取りだけの並び: 二〇二四 → 2024、〇〇七 → 007
        //   十・百・千・万・億・兆を含む並び: 第十二話 → 第12話、百二十三 → 123、三万五千 → 35000
        // 「一緒」「統一」のような熟語の中の漢数字も置き換わる点に注意(名前だけでは見分けられない)
        public static String ConvertKanjiNumerals(String text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return text;
            }

            StringBuilder result = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                if (!IsKanjiNumeral(text[index]))
                {
                    result.Append(text[index]);
                    index++;
                    continue;
                }

                int start = index;
                while (index < text.Length && IsKanjiNumeral(text[index]))
                {
                    index++;
                }
                result.Append(ConvertKanjiNumeralRun(text.Substring(start, index - start)));
            }
            return result.ToString();
        }

        private static Boolean IsKanjiNumeral(Char c)
        {
            return GetKanjiDigit(c) >= 0 || _smallUnits.IndexOf(c) >= 0 || _largeUnits.IndexOf(c) >= 0;
        }

        // 0～9の漢数字なら値を、それ以外は-1を返す(「零」も0として扱う)
        private static int GetKanjiDigit(Char c)
        {
            if (c == '零')
            {
                return 0;
            }
            return _kanjiDigits.IndexOf(c);
        }

        // 漢数字だけが続いた部分(run)を算用数字にする。変換できなければ元のまま返す
        private static String ConvertKanjiNumeralRun(String run)
        {
            if (run.Length == 1 && _standaloneSkipChars.IndexOf(run[0]) >= 0)
            {
                return run;
            }

            Boolean hasUnit = false;
            foreach (Char c in run)
            {
                if (GetKanjiDigit(c) < 0)
                {
                    hasUnit = true;
                    break;
                }
            }

            if (!hasUnit)
            {
                // 位取り(1文字ずつ数字に置き換える)。先頭の〇も残す
                StringBuilder digits = new StringBuilder(run.Length);
                foreach (Char c in run)
                {
                    digits.Append((Char)('0' + GetKanjiDigit(c)));
                }
                return digits.ToString();
            }

            try
            {
                return ParseKanjiNumberWithUnits(run).ToString();
            }
            catch (OverflowException)
            {
                return run;
            }
        }

        // 十・百・千(小さい単位)は4桁ごとのかたまり(section)に足し込み、
        // 万・億・兆(大きい単位)でかたまりを確定させて合計に足す。
        // 単位の前の数字が無ければ1とみなす(十二 → 12、千九百 → 1900)
        private static long ParseKanjiNumberWithUnits(String run)
        {
            checked
            {
                long total = 0;
                long section = 0;
                long current = -1;   // 単位の前に並んでいる数字(位取りで読む)。-1は数字無し

                foreach (Char c in run)
                {
                    int digit = GetKanjiDigit(c);
                    if (digit >= 0)
                    {
                        current = (current < 0 ? 0 : current) * 10 + digit;
                        continue;
                    }

                    int smallIndex = _smallUnits.IndexOf(c);
                    if (smallIndex >= 0)
                    {
                        long unit = (long)Math.Pow(10, smallIndex + 1);
                        section += (current < 0 ? 1 : current) * unit;
                        current = -1;
                        continue;
                    }

                    int largeIndex = _largeUnits.IndexOf(c);
                    long largeUnit = (long)Math.Pow(10, (largeIndex + 1) * 4);
                    long sectionValue = section + (current < 0 ? 0 : current);
                    if (sectionValue == 0)
                    {
                        sectionValue = 1;
                    }
                    total += sectionValue * largeUnit;
                    section = 0;
                    current = -1;
                }

                return total + section + (current < 0 ? 0 : current);
            }
        }

        // 名前に含まれる数字(半角0～9の並び)のうち、position番目のものをdigits桁に0埋めする。
        //   position: 1以上=先頭から数えてN番目、-1以下=末尾から数えてN番目、0=すべての数字
        //   該当する数字が無い・既にdigits桁以上ある場合はそのまま
        // 例: PadNumber("AAA1BBB", 1, 2) → "AAA01BBB"
        public static String PadNumber(String text, int position, int digits)
        {
            if (String.IsNullOrEmpty(text) || digits <= 0)
            {
                return text;
            }

            MatchCollection matches = _numberRegex.Matches(text);
            if (matches.Count == 0)
            {
                return text;
            }

            List<Match> targets = new List<Match>();
            if (position == 0)
            {
                foreach (Match match in matches)
                {
                    targets.Add(match);
                }
            }
            else
            {
                int matchIndex = (position > 0) ? position - 1 : matches.Count + position;
                if (matchIndex < 0 || matchIndex >= matches.Count)
                {
                    return text;
                }
                targets.Add(matches[matchIndex]);
            }

            // 後ろから置き換えると、前の数字の位置がずれない
            StringBuilder result = new StringBuilder(text);
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Match target = targets[i];
                result.Remove(target.Index, target.Length);
                result.Insert(target.Index, target.Value.PadLeft(digits, '0'));
            }
            return result.ToString();
        }
    }
}
