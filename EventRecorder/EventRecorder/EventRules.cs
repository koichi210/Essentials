// WinForms版Form1.Playback.cs / Form1.Recording.cs にprivateメソッドとして埋め込まれていた、
// 記録データ(1行=1イベント)の解釈ルールを、画面(DataGrid)から切り離してテストできる形にまとめたもの。
// ロジックの中身はWinForms版と同じ(DataGridViewRowの代わりにEventRowを受け取るようにしただけ)。
using System;
using System.Collections.Generic;
using System.Linq;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorder
{
    internal static class EventRules
    {
        // Event列が"WAIT_MS"の行は、待機のためだけの行(実際の操作を伴わない)を表す
        public const String WaitEventType = "WAIT_MS";

        // 旧バージョンでは"WAIT"という表記で保存していたため、読込時だけはこちらも
        // 同じ意味として扱えるようにしておく
        public const String LegacyWaitEventType = "WAIT";

        public static Boolean IsWaitEventType(String type)
        {
            return type == WaitEventType || type == LegacyWaitEventType;
        }

        // マウス行は"X:123 Y:456"、キーボード行は"Key:A"の形式でDetail列に表示する
        public static String FormatDetail(String x, String y, String key)
        {
            if (!String.IsNullOrEmpty(key))
            {
                return "Key:" + key;
            }

            if (!String.IsNullOrEmpty(x) || !String.IsNullOrEmpty(y))
            {
                return "X:" + x + " Y:" + y;
            }

            return String.Empty;
        }

        // FormatDetailの逆変換。"X:123 Y:456"や"Key:A"の形式から値を取り出す
        public static void ParseDetail(String detail, out String x, out String y, out String key)
        {
            x = String.Empty;
            y = String.Empty;
            key = String.Empty;

            if (String.IsNullOrEmpty(detail))
            {
                return;
            }

            foreach (String token in detail.Split(' '))
            {
                if (token.StartsWith("X:"))
                {
                    x = token.Substring(2);
                }
                else if (token.StartsWith("Y:"))
                {
                    y = token.Substring(2);
                }
                else if (token.StartsWith("Key:"))
                {
                    key = token.Substring(4);
                }
            }
        }

        // WAIT_MS行のDetail表示(数値だけ、単位は付けない)
        public static String FormatWaitDetail(String waitMs)
        {
            return waitMs;
        }

        // FormatWaitDetailの逆変換。旧バージョンの"500ms"のような末尾"ms"付き表記が
        // 残っていた場合もそのまま数値として読めるように、その場合だけ末尾を取り除く
        public static Boolean TryParseWaitDetail(String detail, out String waitMs)
        {
            waitMs = String.Empty;

            if (String.IsNullOrEmpty(detail))
            {
                return false;
            }

            waitMs = detail.EndsWith("ms") ? detail.Substring(0, detail.Length - 2) : detail;
            return true;
        }

        // マウスイベントの行なのにKey列にも値が入っているか判定する。
        // (PlayOneEventはEvent列でマウス/キーボードを判定するので、この場合Key列は再生時に無視される)
        public static Boolean IsKeyIgnoredOnMouseRow(String eventType, String keyValue, out String message)
        {
            InputSimulation.InputSimulator.MouseStroke mouseStroke;
            Boolean isMouseRow = Enum.TryParse<InputSimulation.InputSimulator.MouseStroke>(eventType, out mouseStroke);

            if (isMouseRow && !String.IsNullOrEmpty(keyValue))
            {
                message = "マウスイベント(" + eventType + ")の行なので、Key(" + keyValue + ")は再生時には使われず無視されるよ";
                return true;
            }

            message = "";
            return false;
        }

        // 再生時にint.Parse/Enum.Parse相当が失敗して、値が読めないまま無言で再生が止まって
        // しまう行を検出する(実際に例外の原因になりうる値だけをチェックする)。
        // 認識できないEvent種別の行(例: 打ち間違い)は、再生時に静かにスキップされるだけで
        // 例外は起きないため、ここではチェック対象にしない
        public static Boolean IsRowInvalidForPlayback(String type, String x, String y, String key, String wait, out String message)
        {
            if (IsWaitEventType(type))
            {
                int waitValue;
                if (!int.TryParse(wait, out waitValue) || waitValue < 0)
                {
                    message = "WAIT_MS行の待機時間(" + wait + ")が数値として読み取れないよ";
                    return true;
                }

                message = "";
                return false;
            }

            InputSimulation.InputSimulator.MouseStroke mouseStroke;
            if (Enum.TryParse<InputSimulation.InputSimulator.MouseStroke>(type, out mouseStroke))
            {
                int xValue, yValue;
                if (!int.TryParse(x, out xValue) || !int.TryParse(y, out yValue))
                {
                    message = "マウスイベント(" + type + ")のX/Y(" + x + ", " + y + ")が数値として読み取れないよ";
                    return true;
                }

                message = "";
                return false;
            }

            InputSimulation.InputSimulator.KeyboardStroke keyStroke;
            if (Enum.TryParse<InputSimulation.InputSimulator.KeyboardStroke>(type, out keyStroke))
            {
                Keys keyCode;
                if (!Enum.TryParse<Keys>(key, out keyCode))
                {
                    message = "キーボードイベント(" + type + ")のKey(" + key + ")が認識できないよ";
                    return true;
                }

                message = "";
                return false;
            }

            message = "";
            return false;
        }

        // 旧バージョンの保存形式(各行が自分自身の待機時間をWait列に持つ)を、
        // 新形式(待機を独立したWAIT_MS行として挿入する形式)に変換する。
        // 併せて、Event列の表記が旧版の"WAIT"のままの行があれば"WAIT_MS"に揃える。
        // すでに新形式(各行のWaitが0)の場合は何もしない、何度呼んでも安全な処理
        public static void MigrateWaitColumnToRows(IList<EventRow> rows)
        {
            // 後ろから処理すれば、Insertしても未処理の行のインデックスに影響しない
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                EventRow row = rows[i];
                String type = row.Type;

                if (IsWaitEventType(type))
                {
                    // 旧バージョンの表記("WAIT")が残っていたら、ここで新表記に揃えておく
                    if (type == LegacyWaitEventType)
                    {
                        row.Type = WaitEventType;
                    }
                    continue;
                }

                // WinForms版はutil.GetInteger(int.Parse)だったため数値以外で例外になっていた。
                // 読込処理で落ちないよう、数値として読めない値は「待機なし」とみなす
                int wait;
                if (!int.TryParse(row.Wait, out wait) || wait <= 0)
                {
                    continue;
                }

                EventRow waitRow = new EventRow();
                waitRow.Type = WaitEventType;
                waitRow.Wait = wait.ToString();
                rows.Insert(i, waitRow);

                // 元の行のWaitは移し替えたので0にしておく(でないと再生時に二重に待ってしまう)
                row.Wait = "0";
            }
        }

        // downRowIndexがKeyDown/SysKeyDownの行なら、それより後ろで最初に見つかる
        // 「同じKeyの対応するKeyUp/SysKeyUp行」のインデックスを返す。見つからなければ-1
        public static int FindPairedKeyUpRowIndex(IList<EventRow> rows, int downRowIndex)
        {
            EventRow downRow = rows[downRowIndex];
            String downType = downRow.Type;
            String key = downRow.Key;

            String upType;
            if (downType == GlobalHook.KeyboardHook.Stroke.KEY_DOWN.ToString())
            {
                upType = GlobalHook.KeyboardHook.Stroke.KEY_UP.ToString();
            }
            else if (downType == GlobalHook.KeyboardHook.Stroke.SYSKEY_DOWN.ToString())
            {
                upType = GlobalHook.KeyboardHook.Stroke.SYSKEY_UP.ToString();
            }
            else
            {
                return -1;
            }

            for (int i = downRowIndex + 1; i < rows.Count; i++)
            {
                EventRow row = rows[i];
                if (row.Type == upType && row.Key == key)
                {
                    return i;
                }
            }

            return -1;
        }

        // 削除対象の行(targetIndexes)に、KeyDown/SysKeyDown行とペアになるKeyUp/SysKeyUp行を足して、
        // 後ろから順に並べて返す(後ろから消せば前方のインデックスがずれない)
        public static List<int> CollectRowsToDelete(IList<EventRow> rows, IEnumerable<int> targetIndexes)
        {
            HashSet<int> indexesToRemove = new HashSet<int>();
            foreach (int idx in targetIndexes)
            {
                if (idx < 0 || idx >= rows.Count)
                {
                    continue;
                }

                indexesToRemove.Add(idx);
                int pairedUpRowIndex = FindPairedKeyUpRowIndex(rows, idx);
                if (pairedUpRowIndex >= 0)
                {
                    indexesToRemove.Add(pairedUpRowIndex);
                }
            }

            return indexesToRemove.OrderByDescending(x => x).ToList();
        }

        // eventTypeに指定したイベントの各行について、直前がWAIT_MS行ならその待機時間をまとめて指定値に変更する。
        // 直前がWAIT_MS行でない(=待機無しで連続している)行はSKIPする
        public static void BulkChangeEventWait(IList<EventRow> rows, String eventType, int waitMs)
        {
            String waitText = waitMs.ToString();

            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].Type != eventType)
                {
                    continue;
                }

                EventRow prevRow = rows[i - 1];
                if (!IsWaitEventType(prevRow.Type))
                {
                    continue;
                }

                prevRow.Wait = waitText;
            }
        }
    }
}
