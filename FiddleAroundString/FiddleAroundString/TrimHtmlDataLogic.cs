using System;
using System.Text;

namespace FiddleAroundString
{
    /// <summary>
    /// 「HTML抽出」タブ(旧TrimHtmlData)のロジック。CS_Wpf\TrimHtmlData\Logic.cs を中身ごと移植し、クラス名だけTrimHtmlDataLogicに変えた(4本分のLogicを1つのプロジェクトに入れるため)。
    ///
    /// HTMLソースから検索ワードにヒットする行(とそこから指定行数)を抽出するロジック。
    /// WinForms版(CS_Form\TrimHtmlData\Logic.cs)をそのまま持ってきたもの(挙動は変えていない)。
    /// 画面のコントロール参照(最初のヒットだけ等)は、呼び出し元(TrimHtmlDataView)で読み取った値を引数で渡す。
    ///
    /// trimLineNum が2以上でヒット行が末尾付近の場合は、末尾でクランプする(取得できる行数が足りなければ、ある分だけ返す)。
    /// 取得行数の読み取り(TryGetTrimLine)と、改行コードを問わない行分割(SplitLines)はWinForms版と同時に直した。
    /// </summary>
    internal static class TrimHtmlDataLogic
    {
        public const string TrimLineNumErrorMessage = "取得行数は0以上の数字で入力してください。";

        // 行の区切りとして扱う改行コード。以前はEnvironment.NewLine(CRLF)だけで分けていたため、
        // LFだけのテキストを貼り付けた取得元や、CRだけで改行しているHTMLが丸ごと1行扱いになっていた。
        // "\r\n"を先頭に置くのは、CRLFを"\r"と"\n"の2回の区切り(=間に空行)と見なさないため
        private static readonly string[] _newLines = { "\r\n", "\n", "\r" };

        // 改行コード(CRLF/LF/CR、混在も可)を問わず行に分ける。空行は捨てる
        public static string[] SplitLines(string text)
        {
            return text.Split(_newLines, StringSplitOptions.RemoveEmptyEntries);
        }

        // 取得行数欄の文字列を行数に変換する。空欄・0は1行扱い(従来どおり)。
        // 以前はint.Parseで直接変換していたため、数字以外を入れると実行時に例外ダイアログが出ていた。
        // 負の数は取り出す行が無くなるだけで意味が無いので、数字以外と同じく読めない扱いにする
        public static bool TryGetTrimLine(string trimLineNumText, out int trimLineNum)
        {
            trimLineNum = 1;
            if (string.IsNullOrWhiteSpace(trimLineNumText))
            {
                return true;
            }

            int value;
            if (!int.TryParse(trimLineNumText, out value) || value < 0)
            {
                trimLineNum = 0;
                return false;
            }

            if (value > 0)
            {
                trimLineNum = value;
            }
            return true;
        }

        public static string GetSearchString(string htmlSource, string searchWord, int trimLineNum, StringComparison comparison, bool firstWordOnly)
        {
            string[] lines = SplitLines(htmlSource);
            // String +=はヒット行数が多いほど文字列全体のコピーが積み上がって遅くなるため、StringBuilderを使う
            StringBuilder result = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(searchWord, comparison) != -1)
                {
                    // ヒットした行を含む指定行数分取得(末尾を超えないようクランプする)
                    for (int j = 0; j < trimLineNum && (i + j) < lines.Length; j++)
                    {
                        result.Append(lines[i + j]).Append(Environment.NewLine);
                    }

                    // 最初に見つかったワードのみ
                    if (firstWordOnly)
                    {
                        break;
                    }

                    // 次のワードとの境界
                    result.Append(Environment.NewLine);
                }
            }

            return result.Append(Environment.NewLine).ToString();
        }
    }
}
