// WinForms版Form1.Playback.cs / Form1.Recording.cs にprivateメソッドとして埋め込まれていた、
// 記録データ(1行=1イベント)の解釈ルールを、画面(DataGrid)から切り離してテストできる形にまとめたもの。
// ロジックの中身はWinForms版と同じ(DataGridViewRowの代わりにEventRowを受け取るようにしただけ)。
using System;
using System.Collections.Generic;
using System.Linq;
using InputSim = InputSimulation.InputSimulator;
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

        // マウス行は"X:123 Y:456"、キーボード行は"Key:A"の形式でDetail列に表示する。
        // ホイール行で回転量(delta)を記録してあれば、"X:123 Y:456 Delta:240"のように後ろに付ける
        public static String FormatDetail(String x, String y, String key, String delta = "")
        {
            if (!String.IsNullOrEmpty(key))
            {
                return "Key:" + key;
            }

            if (!String.IsNullOrEmpty(x) || !String.IsNullOrEmpty(y))
            {
                String detail = "X:" + x + " Y:" + y;
                if (!String.IsNullOrEmpty(delta))
                {
                    detail += " Delta:" + delta;
                }
                return detail;
            }

            return String.Empty;
        }

        // FormatDetailの逆変換。"X:123 Y:456"や"Key:A"の形式から値を取り出す
        public static void ParseDetail(String detail, out String x, out String y, out String key)
        {
            String delta;
            ParseDetail(detail, out x, out y, out key, out delta);
        }

        // FormatDetailの逆変換(ホイールの回転量"Delta:240"も取り出す)
        public static void ParseDetail(String detail, out String x, out String y, out String key, out String delta)
        {
            x = String.Empty;
            y = String.Empty;
            key = String.Empty;
            delta = String.Empty;

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
                else if (token.StartsWith("Delta:"))
                {
                    delta = token.Substring(6);
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
            InputSim.MouseStroke mouseStroke;
            int mouseData;
            Boolean isMouseRow = TryGetPlaybackMouseStroke(eventType, out mouseStroke, out mouseData);

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
            return IsRowInvalidForPlayback(type, x, y, key, wait, "", out message);
        }

        // ホイール行の回転量(delta)も含めてチェックする版
        public static Boolean IsRowInvalidForPlayback(String type, String x, String y, String key, String wait, String delta, out String message)
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

            InputSim.MouseStroke mouseStroke;
            int mouseData;
            if (TryGetPlaybackMouseStroke(type, out mouseStroke, out mouseData))
            {
                int xValue, yValue;
                if (!int.TryParse(x, out xValue) || !int.TryParse(y, out yValue))
                {
                    message = "マウスイベント(" + type + ")のX/Y(" + x + ", " + y + ")が数値として読み取れないよ";
                    return true;
                }

                int deltaValue;
                if (mouseStroke == InputSim.MouseStroke.WHEEL && !String.IsNullOrEmpty(delta) && !TryParseWheelDelta(delta, out deltaValue))
                {
                    message = "ホイールの回転量(" + delta + ")が正の数値として読み取れないよ";
                    return true;
                }

                message = "";
                return false;
            }

            InputSim.KeyboardStroke keyStroke;
            if (TryGetPlaybackKeyboardStroke(type, out keyStroke))
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

        // ホイール1ノッチ分の回転量(Win32のWHEEL_DELTA)
        public const int WheelDelta = 120;

        // Win32のXBUTTON1/XBUTTON2(MOUSEEVENTF_XDOWN/XUPのmouseDataに入れる、どちらのサイドボタンか)
        public const int XButton1 = 0x0001;
        public const int XButton2 = 0x0002;

        // 記録したEvent列の名前がInputSimulatorのMouseStrokeと違うマウス操作(ホイール・X1/X2(サイド)ボタン)の、
        // 送るときのMOUSEEVENTF_*とmouseData。ホイールはここでは1ノッチ(WHEEL_DELTA)の向きだけを決め、
        // 記録した回転量(Delta)があればBuildPlaybackInputsでその量に置き換える(無い行は1行=1ノッチ)
        private static readonly Dictionary<String, KeyValuePair<InputSim.MouseStroke, int>> _renamedMouseStrokes
            = new Dictionary<String, KeyValuePair<InputSim.MouseStroke, int>>
        {
            { GlobalHook.MouseHook.Stroke.WHEEL_UP.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.WHEEL, WheelDelta) },
            { GlobalHook.MouseHook.Stroke.WHEEL_DOWN.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.WHEEL, -WheelDelta) },
            { GlobalHook.MouseHook.Stroke.X1_DOWN.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.X_DOWN, XButton1) },
            { GlobalHook.MouseHook.Stroke.X1_UP.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.X_UP, XButton1) },
            { GlobalHook.MouseHook.Stroke.X2_DOWN.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.X_DOWN, XButton2) },
            { GlobalHook.MouseHook.Stroke.X2_UP.ToString(), new KeyValuePair<InputSim.MouseStroke, int>(InputSim.MouseStroke.X_UP, XButton2) },
        };

        // 再生でマウス操作として送る行なら、送るMOUSEEVENTF_*(stroke)とmouseData(ホイールの回転量・どのXボタンか)を返す。
        // 以前はホイールとX1/X2ボタンの行を再生で飛ばしていた
        public static Boolean TryGetPlaybackMouseStroke(String type, out InputSim.MouseStroke stroke, out int data)
        {
            KeyValuePair<InputSim.MouseStroke, int> renamed;
            if (type != null && _renamedMouseStrokes.TryGetValue(type, out renamed))
            {
                stroke = renamed.Key;
                data = renamed.Value;
                return true;
            }

            data = 0;
            return Enum.TryParse<InputSim.MouseStroke>(type, out stroke);
        }

        // 再生でキー操作として送る行なら、送るKEY_DOWN/KEY_UPを返す。
        // SYSKEY(Altを押しながらのキー・F10)も普通のキーとして送る(Altの押下も行として記録されているので、
        // Alt+F4などは送った側でも元どおりSYSKEYになる)。以前はSYSKEYの行を再生で飛ばしていた
        public static Boolean TryGetPlaybackKeyboardStroke(String type, out InputSim.KeyboardStroke stroke)
        {
            if (type == GlobalHook.KeyboardHook.Stroke.SYSKEY_DOWN.ToString())
            {
                stroke = InputSim.KeyboardStroke.KEY_DOWN;
                return true;
            }
            if (type == GlobalHook.KeyboardHook.Stroke.SYSKEY_UP.ToString())
            {
                stroke = InputSim.KeyboardStroke.KEY_UP;
                return true;
            }

            return Enum.TryParse<InputSim.KeyboardStroke>(type, out stroke);
        }

        // 記録した1行分を、SendInputに渡す入力イベントに組み立てる(送るのは呼び出し側。再生しない行なら空のリスト)。
        // マウス操作は、クリックと同じく記録した座標(絶対座標)へ移動させてから行う。
        // 値が読めない行は再生前チェックで止めている前提(X/Yが数値でなければ例外になる)
        public static List<InputSim.Input> BuildPlaybackInputs(String type, String x, String y, String key)
        {
            return BuildPlaybackInputs(type, x, y, key, "");
        }

        // ホイール行は、記録した回転量(delta)があればその量で送る。以前は回転量を記録しておらず、常に1行=1ノッチ(120)で
        // 送っていたため、1ノッチより細かく何回も通知してくる高分解能ホイールだと記録より大きく回りすぎていた。
        // deltaが空(回転量の無い以前のファイル)なら従来どおり±120で送る
        public static List<InputSim.Input> BuildPlaybackInputs(String type, String x, String y, String key, String delta)
        {
            List<InputSim.Input> inputs = new List<InputSim.Input>();

            InputSim.MouseStroke mouseStroke;
            int mouseData;
            if (TryGetPlaybackMouseStroke(type, out mouseStroke, out mouseData))
            {
                int deltaValue;
                if (mouseStroke == InputSim.MouseStroke.WHEEL && TryParseWheelDelta(delta, out deltaValue))
                {
                    mouseData = Math.Sign(mouseData) * deltaValue;
                }

                List<InputSim.MouseStroke> flags = new List<InputSim.MouseStroke> { InputSim.MouseStroke.MOVE, mouseStroke };
                InputSim.AddMouseInput(ref inputs, flags, mouseData, true, int.Parse(x), int.Parse(y));
                return inputs;
            }

            InputSim.KeyboardStroke keyStroke;
            Keys keyCode;
            if (TryGetPlaybackKeyboardStroke(type, out keyStroke) && Enum.TryParse<Keys>(key, out keyCode))
            {
                InputSim.AddKeyboardInput(ref inputs, keyStroke, keyCode);
            }

            return inputs;
        }

        // ホイールの回転量(Delta)の文字列を読む。向きはWHEEL_UP/WHEEL_DOWNで表すので、正の数だけを受け付ける
        public static Boolean TryParseWheelDelta(String delta, out int value)
        {
            return int.TryParse(delta, out value) && value > 0;
        }

        // 記録時のフックのmouseData(上位ワードが符号付きの回転量)から、保存する回転量(正の数)の文字列を作る
        public static String FormatWheelDelta(uint mouseData)
        {
            int delta = Math.Abs((int)(short)((mouseData >> 16) & 0xffff));
            return delta > 0 ? delta.ToString() : "";
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

        // Ctrl/Shift/Alt(左右の区別ありも含む)
        private static readonly HashSet<Keys> _modifierKeys = new HashSet<Keys>
        {
            Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
            Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
            Keys.Menu, Keys.LMenu, Keys.RMenu,
        };

        // 記録停止時に、対になるUpが記録されていない修飾キー(Ctrl/Shift/Alt)のDown行を片付ける。行を変えたらtrue。
        // Ctrl+F5のような修飾キー付きのホットキーで記録を止めると、CtrlのDownだけが記録され(Upは停止後なので記録されない)、
        // 再生後もCtrlが押されたままになっていた。pressedKeysは記録中にDownしてまだUpしていないキー。
        // ・後ろにWAIT_MS行(と同じく片付け対象の修飾キーのDown行)しか無い: ホットキーのために押しただけなのでDown行を消す
        // ・後ろに他の操作がある(Ctrlを押したままクリックした等): Ctrl+クリックの意味を残すため、末尾に対のUp行を足す
        public static Boolean ReleaseUnpairedModifierKeys(IList<EventRow> rows, IEnumerable<Keys> pressedKeys)
        {
            HashSet<String> unpairedKeys = new HashSet<String>(pressedKeys.Where(k => _modifierKeys.Contains(k)).Select(k => k.ToString()));
            if (unpairedKeys.Count == 0)
            {
                return false;
            }

            Boolean changed = false;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                EventRow row = rows[i];
                if (IsWaitEventType(row.Type))
                {
                    continue;
                }

                if (GetPairedKeyUpType(row.Type) == null || !unpairedKeys.Remove(row.Key))
                {
                    break;
                }

                rows.RemoveAt(i);
                changed = true;
            }

            foreach (String key in unpairedKeys)
            {
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    String upType = GetPairedKeyUpType(rows[i].Type);
                    if (upType != null && rows[i].Key == key)
                    {
                        rows.Add(EventRow.FromData(upType, "", "", key, "0", ""));
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        // KeyDown/SysKeyDownの行の種類なら、対になるKeyUp/SysKeyUpの種類を返す。それ以外はnull
        private static String GetPairedKeyUpType(String downType)
        {
            if (downType == GlobalHook.KeyboardHook.Stroke.KEY_DOWN.ToString())
            {
                return GlobalHook.KeyboardHook.Stroke.KEY_UP.ToString();
            }
            if (downType == GlobalHook.KeyboardHook.Stroke.SYSKEY_DOWN.ToString())
            {
                return GlobalHook.KeyboardHook.Stroke.SYSKEY_UP.ToString();
            }
            return null;
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
