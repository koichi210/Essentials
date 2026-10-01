using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace othello
{
    /// <summary>
    /// 棋譜のテキスト形式への変換・読み込み。C++版 CManager::GetKihu/GetKihuFile 相当。
    ///
    /// 形式: 1手を1行、"手数 : 列.行 色" の形で表す(例: " 1 : C.4 黒")。
    /// 列はA〜H(0-indexedのxをA起点の文字に変換)、行は1〜8(0-indexedのyを+1)。
    /// </summary>
    static class Kihu
    {
        private const string Header = "***** 棋譜 *****";

        /// <summary>
        /// 手の履歴をテキスト形式(保存/表示用)に変換する。
        /// </summary>
        public static string ToText(IList<KihuMove> history)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);

            for (int i = 0; i < history.Count; i++)
            {
                KihuMove move = history[i];
                char column = (char)('A' + move.X);
                int row = move.Y + 1;
                string colorName = move.Color == StoneColor.Black ? "黒" : "白";
                sb.AppendLine(string.Format("{0,2} : {1}.{2} {3}", i + 1, column, row, colorName));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 棋譜テキストを読み、gmを初期化してから記載順に1手ずつ再生する。
        /// 打てない/解釈できない行があればそこで打ち切る(それまでに反映された手は残る)。
        /// C++版 CManager::GetKihuFile 相当。
        /// </summary>
        /// <returns>1手以上を正常に反映できればtrue</returns>
        public static bool TryReplay(string text, GameMaster gm, out string errorMessage)
        {
            gm.Initialize();
            errorMessage = null;

            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            int appliedCount = 0;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    // 見出し行や空行は読み飛ばす
                    continue;
                }

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex < 0)
                {
                    continue;
                }

                // ":" より後ろが「列.行 色」の部分(例: "C.4 黒")
                string movePart = trimmed.Substring(colonIndex + 1).Trim();
                string[] tokens = movePart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 1)
                {
                    continue;
                }

                string[] posTokens = tokens[0].Split('.');
                if (posTokens.Length != 2 || posTokens[0].Length != 1)
                {
                    continue;
                }

                char columnChar = char.ToUpperInvariant(posTokens[0][0]);
                if (columnChar < 'A' || columnChar > 'A' + GameMaster.BoardSize - 1)
                {
                    continue;
                }

                if (!int.TryParse(posTokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int row) ||
                    row < 1 || row > GameMaster.BoardSize)
                {
                    continue;
                }

                int x = columnChar - 'A';
                int y = row - 1;

                if (!gm.TryPut(x, y))
                {
                    errorMessage = string.Format(
                        "{0}手目({1}.{2})で読み込みを中断したよ(その場所には置けないみたい)",
                        appliedCount + 1, columnChar, row);
                    return appliedCount > 0;
                }

                appliedCount++;
            }

            if (appliedCount == 0)
            {
                errorMessage = "有効な手が1つも読み取れなかったよ";
                return false;
            }

            return true;
        }
    }
}
