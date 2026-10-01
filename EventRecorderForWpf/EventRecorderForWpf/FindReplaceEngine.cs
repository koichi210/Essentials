// 検索/置換ダイアログ([[FindReplaceWindow]])の中身のうち、画面に依存しない部分。
// WinForms版FindReplaceForm.csのDoSearch / FindNext / ReplaceCurrent / ReplaceAll のロジックをそのまま移し、
// DataGridViewのセルではなく「行(IEditableGridRow)×表示列名」で値を読み書きするようにした(テストしやすくするため)。
using System;
using System.Collections.Generic;
using System.Text;

namespace EventRecorderForWpf
{
    // Ctrl+F/Ctrl+Hで開くダイアログのモード。同じダイアログ内のラジオボタンでも切り替えられる
    internal enum FindReplaceMode
    {
        Find,
        Replace,
    }

    // 検索結果一覧の1行分(レコード表のどの行か・表示列ごとの値・どの表示列でヒットしたか)
    internal sealed class FindHit
    {
        public int TargetRowIndex;
        public String[] Values;
        public List<int> HitVisibleColumnIndexes;
    }

    internal sealed class FindReplaceEngine
    {
        private readonly Func<IList<IEditableGridRow>> getRows;
        private readonly Func<IList<String>> getVisibleColumns;
        private readonly Action beginUndoBatch;
        private readonly Action endUndoBatch;

        // 「次を検索」で最後に見つけたセル位置(行インデックス・表示列インデックス)。次回はこの続き(次のセル)から探す
        public int LastFoundRow { get; private set; } = -1;
        public int LastFoundColumn { get; private set; } = -1;

        public Boolean MatchCase { get; set; }

        public FindReplaceEngine(Func<IList<IEditableGridRow>> getRows, Func<IList<String>> getVisibleColumns,
                                 Action beginUndoBatch, Action endUndoBatch)
        {
            this.getRows = getRows;
            this.getVisibleColumns = getVisibleColumns;
            this.beginUndoBatch = beginUndoBatch;
            this.endUndoBatch = endUndoBatch;
        }

        private StringComparison Comparison
        {
            get { return MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; }
        }

        private Boolean Matches(String text, String keyword)
        {
            return !String.IsNullOrEmpty(text) && text.IndexOf(keyword, Comparison) >= 0;
        }

        // レコード表と同じ列構成(可視列)で、キーワードを含むセルがある行を一覧にする
        public List<FindHit> Search(String keyword)
        {
            List<FindHit> hits = new List<FindHit>();
            if (String.IsNullOrEmpty(keyword))
            {
                return hits;
            }

            IList<IEditableGridRow> rows = getRows();
            IList<String> columns = getVisibleColumns();

            for (int r = 0; r < rows.Count; r++)
            {
                List<int> hitIndexes = new List<int>();
                String[] values = new String[columns.Count];
                for (int vi = 0; vi < columns.Count; vi++)
                {
                    values[vi] = rows[r].GetCell(columns[vi]);
                    if (Matches(values[vi], keyword))
                    {
                        hitIndexes.Add(vi);
                    }
                }

                if (hitIndexes.Count == 0)
                {
                    continue;
                }

                hits.Add(new FindHit { TargetRowIndex = r, Values = values, HitVisibleColumnIndexes = hitIndexes });
            }

            return hits;
        }

        // LastFoundRow/LastFoundColumnの次のセルから、キーワードを含む次のセルを探す(見つかったらtrue)。
        // 末尾まで探して見つからなければ先頭に戻ってもう一周する(現在位置自体は含めない)。
        // statusは画面下部に出すメッセージ(nullなら表示を変えない)
        public Boolean FindNext(String keyword, out String status)
        {
            status = null;
            if (String.IsNullOrEmpty(keyword))
            {
                status = "検索文字列を入力してね";
                return false;
            }

            IList<IEditableGridRow> rows = getRows();
            IList<String> columns = getVisibleColumns();
            int rowCount = rows.Count;
            int colCount = columns.Count;
            if (rowCount == 0 || colCount == 0)
            {
                return false;
            }

            int r = LastFoundRow < 0 ? 0 : LastFoundRow;
            int c = LastFoundRow < 0 ? -1 : LastFoundColumn;
            if (r >= rowCount)
            {
                // 前回見つけた行が削除されて無くなっていたら、先頭から探し直す
                r = 0;
                c = -1;
            }

            for (int steps = 0; steps < rowCount * colCount; steps++)
            {
                c++;
                if (c >= colCount)
                {
                    c = 0;
                    r++;
                    if (r >= rowCount)
                    {
                        r = 0;
                    }
                }

                if (Matches(rows[r].GetCell(columns[c]), keyword))
                {
                    LastFoundRow = r;
                    LastFoundColumn = c;
                    status = "";
                    return true;
                }
            }

            status = "見つからなかったよ";
            return false;
        }

        // 直前の「次を検索」で見つけたセルが検索文字列にマッチしていれば置換する(続けての次を検索は呼び出し側)
        public void ReplaceCurrent(String keyword, String replacement)
        {
            if (String.IsNullOrEmpty(keyword))
            {
                return;
            }

            IList<IEditableGridRow> rows = getRows();
            IList<String> columns = getVisibleColumns();
            if (LastFoundRow >= 0 && LastFoundRow < rows.Count && LastFoundColumn >= 0 && LastFoundColumn < columns.Count)
            {
                IEditableGridRow row = rows[LastFoundRow];
                String text = row.GetCell(columns[LastFoundColumn]);
                if (Matches(text, keyword))
                {
                    row.SetCell(columns[LastFoundColumn], ReplaceAllOccurrences(text, keyword, replacement ?? "", Comparison));
                }
            }
        }

        // すべての表示セルを対象に一括置換し、置換したセル数を返す。複数セルの変更を1回のCtrl+Zで
        // まとめて戻せるよう、Undoバッチで囲む
        public int ReplaceAll(String keyword, String replacement)
        {
            if (String.IsNullOrEmpty(keyword))
            {
                return 0;
            }

            IList<IEditableGridRow> rows = getRows();
            IList<String> columns = getVisibleColumns();
            int replacedCount = 0;

            beginUndoBatch();
            try
            {
                foreach (IEditableGridRow row in rows)
                {
                    foreach (String column in columns)
                    {
                        String text = row.GetCell(column);
                        if (!Matches(text, keyword))
                        {
                            continue;
                        }

                        row.SetCell(column, ReplaceAllOccurrences(text, keyword, replacement ?? "", Comparison));
                        replacedCount++;
                    }
                }
            }
            finally
            {
                endUndoBatch();
            }

            return replacedCount;
        }

        // String.Replaceは比較方法(大文字/小文字を区別するか)を指定できないため、自前で全置換する
        public static String ReplaceAllOccurrences(String source, String oldValue, String newValue, StringComparison comparison)
        {
            StringBuilder sb = new StringBuilder();
            int pos = 0;
            while (true)
            {
                int idx = source.IndexOf(oldValue, pos, comparison);
                if (idx < 0)
                {
                    sb.Append(source, pos, source.Length - pos);
                    break;
                }

                sb.Append(source, pos, idx - pos);
                sb.Append(newValue);
                pos = idx + oldValue.Length;
            }

            return sb.ToString();
        }
    }
}
