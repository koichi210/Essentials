using System;
using System.Collections.Generic;
using System.Text;
using StandardTemplate;

namespace FiddleAroundString
{
    /// <summary>
    /// 「ファイル抽出」タブ(旧TrimFileData)のロジック。CS_Wpf\TrimFileData\Logic.cs を中身ごと移植し、クラス名だけTrimFileDataLogicに変えた(4本分のLogicを1つのプロジェクトに入れるため)。
    ///
    /// 検索ワードリストとリファレンスデータから該当行を抽出するロジック。
    /// WinForms版(CS_Form\TrimFileData\Logic.cs)をそのまま持ってきたもの(挙動は変えていない)。
    /// 画面のコントロール参照(大文字小文字の区別等)は、呼び出し元(TrimFileDataView)で読み取った値を引数で渡す。
    /// </summary>
    internal static class TrimFileDataLogic
    {
        // 行の区切りとして扱う改行コード(TrimHtmlDataLogic.SplitLinesと同じ)。以前はEnvironment.NewLine(CRLF)だけで分けていたため、
        // LFだけのリファレンスファイルが丸ごと1行扱いになり、検索ワードが1つでもヒットするとファイル全体が結果に出ていた。
        // "\r\n"を先頭に置くのは、CRLFを"\r"と"\n"の2回の区切り(=間に空行)と見なさないため
        private static readonly string[] _newLines = { "\r\n", "\n", "\r" };

        // 改行コード(CRLF/LF/CR、混在も可)を問わず行に分ける。空行は捨てる
        public static string[] SplitLines(string text)
        {
            return text.Split(_newLines, StringSplitOptions.RemoveEmptyEntries);
        }

        // 検索ワード欄の1行から検索ワードを取り出す。
        // 先頭は行の一番先頭から。終端は、行にスペース(半角)があれば「後ろから見ていって最初に出てきたスペース」の手前まで、
        // スペースが無ければ行の一番後ろまで(例: "apple pie 3" → "apple pie"、"apple" → "apple")。
        // 以前は行をスペースで区切った各単語をそれぞれ検索ワードにしていたが、スペースを含む語句で探せず、
        // 行末に付けたメモ(個数など)まで検索ワードになっていた。
        // 区切りのスペースの手前に余分なスペースが続いていたら落とす("apple pie   3" → "apple pie")。
        // 取り出した結果が空(スペースで始まり他にスペースが無い行など)なら空文字を返し、呼び出し元は何も探さない
        public static string ExtractSearchWord(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            int lastSpace = line.LastIndexOf(' ');
            string word = (lastSpace < 0) ? line : line.Substring(0, lastSpace);
            return word.TrimEnd(' ');
        }

        // 全角の英数字・記号(！〜～、U+FF01〜U+FF5E)と全角スペース(U+3000)を半角にする。
        // 「全角半角を区別しない」がONのとき、検索ワードと検索対象の両方をこれでそろえてから比べる。
        // 変換対象はToyingDataの全角→半角変換(数字・英大文字・英小文字・スペース)に、同じ並びの記号を足したもの。
        // 1文字を1文字に置き換えるだけなので、変換後も文字数は変わらない
        public static string ToNarrow(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '！' && c <= '～')
                {
                    chars[i] = (char)(c - 0xFEE0);
                }
                else if (c == '　')
                {
                    chars[i] = ' ';
                }
            }
            return new string(chars);
        }

        public static string GetSearchData(string[] searchWordLines, string[] referLines, bool ordinalCase, bool firstWordOnly, string searchCommonWord)
        {
            return GetSearchData(searchWordLines, referLines, ordinalCase, firstWordOnly, searchCommonWord, false);
        }

        // ignoreWidth: trueなら全角半角を区別しない(検索ワード・共通ワード・検索対象をToNarrowでそろえてから比べる)。
        // 結果に出すのは検索対象の元の行(半角にそろえた後の行ではない)
        public static string GetSearchData(string[] searchWordLines, string[] referLines, bool ordinalCase, bool firstWordOnly, string searchCommonWord, bool ignoreWidth)
        {
            StringComparison comparison = ordinalCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            StcUtils util = new StcUtils();

            // String +=はループの度に文字列全体をコピーし直すため、行数が多いと遅くなる。
            // StringBuilderに溜めてから最後に1回だけToString()する。
            StringBuilder resultBuilder = new StringBuilder();
            for (int i = 0; i < searchWordLines.Length; i++)
            {
                resultBuilder.Append("◆").Append(searchWordLines[i]).Append(Environment.NewLine);

                // 全角半角を区別しないときは、全角スペースも区切りのスペースとして扱えるよう、半角にそろえてから取り出す
                string searchWordLine = ignoreWidth ? ToNarrow(searchWordLines[i]) : searchWordLines[i];
                string searchWord = ExtractSearchWord(searchWordLine);
                string[] searchWords = (searchWord.Length == 0) ? new string[0] : new[] { searchWord };
                string hitLines = GetHitLines(searchWords, referLines, comparison, firstWordOnly, searchCommonWord, ignoreWidth);
                resultBuilder.Append(util.TrimDuplication(hitLines, Environment.NewLine));
                resultBuilder.Append(Environment.NewLine).Append(Environment.NewLine);
            }

            return resultBuilder.ToString();
        }

        public static string GetHitLines(string[] searchWords, string[] referLines, StringComparison comparison, bool firstWordOnly, string searchCommonWord)
        {
            return GetHitLines(searchWords, referLines, comparison, firstWordOnly, searchCommonWord, false);
        }

        // ignoreWidth: trueなら検索ワード・共通ワード・検索対象の行をToNarrowで半角にそろえてから比べる
        // (searchWordsは呼び出し元で半角にそろえてあってもよい。2回変換しても結果は同じ)
        public static string GetHitLines(string[] searchWords, string[] referLines, StringComparison comparison, bool firstWordOnly, string searchCommonWord, bool ignoreWidth)
        {
            if (ignoreWidth)
            {
                searchWords = Array.ConvertAll(searchWords, ToNarrow);
                searchCommonWord = ToNarrow(searchCommonWord);
            }

            // searchCommonWordによる絞り込みはsearchWordsのどの単語(j)でも結果が変わらないため、
            // 以前は単語数(j)×参照行数(k)回、毎回同じIndexOf判定を繰り返していた。
            // 単語ループに入る前に1回だけreferLinesを絞り込んでおけば、絞り込み自体はO(k)で済む。
            // 比べるのは半角にそろえた行(compareLines)、結果に出すのは元の行(filteredLines)
            bool hasCommonWord = !string.IsNullOrEmpty(searchCommonWord);
            List<string> filteredLines = new List<string>(referLines.Length);
            List<string> compareLines = new List<string>(referLines.Length);
            foreach (string line in referLines)
            {
                string compareLine = ignoreWidth ? ToNarrow(line) : line;
                if (!hasCommonWord || compareLine.IndexOf(searchCommonWord, comparison) != -1)
                {
                    filteredLines.Add(line);
                    compareLines.Add(compareLine);
                }
            }

            StringBuilder resultBuilder = new StringBuilder();

            for (int j = 0; j < searchWords.Length; j++)
            {
                for (int k = 0; k < filteredLines.Count; k++)
                {
                    if (compareLines[k].IndexOf(searchWords[j], comparison) != -1)
                    {
                        //Fileから抽出
                        resultBuilder.Append(filteredLines[k]).Append(Environment.NewLine);

                        // 最初に見つかった項目のみ抽出
                        if (firstWordOnly)
                        {
                            break;
                        }
                    }
                }
            }

            return resultBuilder.ToString();
        }
    }
}
