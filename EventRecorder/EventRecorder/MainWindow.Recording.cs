using System;
using System.Windows;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorder
{
    public partial class MainWindow
    {
        // *******************************************************************************
        // 記録

        private void button_Record_Click(object sender, RoutedEventArgs e)
        {
            ToggleRecording();
        }

        private void button_Clear_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording || _isPlaying)
            {
                return;
            }

            EventRows.Clear();
            _highlightedEventRowIndex = -1;
        }

        // 現在の記録中/再生中の状態をタイトルバーに反映する
        private void UpdateTitle()
        {
            if (_isRecording)
            {
                Title = _baseTitle + " - 記録中";
            }
            else if (_isPlaying)
            {
                // (表示例)プレイバック中：2 / 5：34/100
                // 「2 / 5」=全体ループ(プレイリストの全体周回。単発再生では常に1/1)の今の実行数/最大数、
                // 「34/100」=ループ(PlayRows呼び出し1回あたりの繰り返し)の今の実行回数/最大数
                Title = _baseTitle + " - プレイバック中："
                    + _playbackOverallLoopNo + " / " + _playbackOverallLoopMax + "："
                    + _playbackInnerLoopNo + "/" + _playbackInnerLoopMax;
            }
            else
            {
                Title = _baseTitle;
            }
        }

        // 単発再生とプレイリスト実行は1つのbutton_Play(表示名「再生」)に統合したので、
        // _isPlayingが変わるたびにその表示を同期させるだけでよい
        private void UpdatePlayButton()
        {
            button_Play.Content = _isPlaying ? "停止" : "再生";
        }

        // 記録開始/停止を切り替える。ボタンクリックからもホットキーからも呼ばれる
        private void ToggleRecording()
        {
            if (_isRecording)
            {
                GlobalHook.MouseHook.Stop();
                _gridFlushTimer.Stop();
                FlushPendingRows();
                _isRecording = false;
                button_Record.Content = "記録";
                UpdateTitle();
                return;
            }

            // 終了処理中は新しく記録を始めない
            if (_isPlaying || _isExiting)
            {
                return;
            }

            // 記録開始時に既存の行はクリアしない(既存のマクロに追記で記録したい場合があるため)
            _pendingRows.Clear();
            _pressedKeys.Clear();
            _lastEventTick = Environment.TickCount;

            if (_isHookEnabled)
            {
                GlobalHook.MouseHook.AddEvent(OnMouseEvent);
                GlobalHook.MouseHook.Start();
            }

            _gridFlushTimer.Start();
            _isRecording = true;
            button_Record.Content = "記録中…";
            UpdateTitle();
        }

        private void OnMouseEvent(ref GlobalHook.MouseHook.StateMouse s)
        {
            // カーソル移動そのものは記録しない(HiMacroExと同様、クリック単位のみ記録)
            if (s.Stroke == GlobalHook.MouseHook.Stroke.MOVE || s.Stroke == GlobalHook.MouseHook.Stroke.UNKNOWN)
            {
                return;
            }

            QueueRow(s.Stroke.ToString(), s.X.ToString(), s.Y.ToString(), "", TakeElapsedMsAndReset());
        }

        private void OnKeyboardEvent(ref GlobalHook.KeyboardHook.StateKeyboard s)
        {
            if (s.Stroke == GlobalHook.KeyboardHook.Stroke.UNKNOWN)
            {
                return;
            }

            // キーバインド設定画面が開いている間は、テスト入力したキーがホットキーとして
            // 誤発動しないよう、記録データへの取り込みも含めてここで丸ごと処理を止める
            if (_isHotkeySettingsOpen)
            {
                return;
            }

            HandleKeyboardStroke(s.Stroke, s.Key, System.Windows.Forms.Control.ModifierKeys);
        }

        // フックから来た1回のキー操作を処理する(ホットキー判定→記録)。
        // heldModifiersは押した瞬間のCtrl/Alt/Shiftの状態(フックのKeyは修飾キーを含まない生のキーなので別に渡す)
        internal void HandleKeyboardStroke(GlobalHook.KeyboardHook.Stroke stroke, Keys key, Keys heldModifiers)
        {
            Boolean isDown = stroke == GlobalHook.KeyboardHook.Stroke.KEY_DOWN || stroke == GlobalHook.KeyboardHook.Stroke.SYSKEY_DOWN;
            Boolean isUp = stroke == GlobalHook.KeyboardHook.Stroke.KEY_UP || stroke == GlobalHook.KeyboardHook.Stroke.SYSKEY_UP;

            // ホットキーはアプリ操作用の予約キーなので、マクロの記録データには含めない
            if (isDown)
            {
                Keys combo = key | (heldModifiers & (Keys.Control | Keys.Alt | Keys.Shift));
                Boolean isRecordHotkey = combo == _hotkeyToggleRecord;
                Boolean isPlayHotkey = combo == _hotkeyTogglePlay;

                if (isRecordHotkey || isPlayHotkey)
                {
                    // リピート抑制。対応するKeyUpが来るまでは連続トグルさせない
                    if (!_pressedHotkeys.Add(key))
                    {
                        return;
                    }

                    if (isRecordHotkey)
                    {
                        ToggleRecording();
                    }
                    else
                    {
                        PlayOrStop();
                    }

                    return;
                }
            }
            else if (isUp)
            {
                // Down時にホットキーとして処理したキーのUpは、マクロ記録に含めず消費する
                if (_pressedHotkeys.Remove(key))
                {
                    return;
                }
            }

            if (!_isRecording)
            {
                return;
            }

            if (isDown)
            {
                // すでに押されている状態なら、OSのキーリピートによるKeyDown連発なので無視する
                if (!_pressedKeys.Add(key))
                {
                    return;
                }
            }
            else if (isUp)
            {
                _pressedKeys.Remove(key);
            }

            QueueRow(stroke.ToString(), "", "", key.ToString(), TakeElapsedMsAndReset());
        }

        // 直前のイベント(前回この関数を呼んだ時点)からの経過msを返し、基準時刻(_lastEventTick)を今に更新する。
        // 記録開始直後の1件目は「記録開始からの待機」になる
        private int TakeElapsedMsAndReset()
        {
            int now = Environment.TickCount;
            int wait = now - _lastEventTick;
            _lastEventTick = now;
            return wait < 0 ? 0 : wait;
        }

        // 記録した1件をバッファに貯めるだけ(フックのコールバックを描画待ちで塞がないため)。
        // waitが正の場合、待機だけを表す独立したWAIT_MS行を先に積んでから、実際のイベント行を積む
        private void QueueRow(String type, String x, String y, String key, int wait)
        {
            if (wait > 0)
            {
                _pendingRows.Add(new String[] { EventRules.WaitEventType, "", "", "", wait.ToString() });
            }

            _pendingRows.Add(new String[] { type, x, y, key, "0" });
        }

        // 貯まった行をまとめてグリッドに反映する
        internal void FlushPendingRows()
        {
            if (_pendingRows.Count == 0)
            {
                return;
            }

            foreach (String[] values in _pendingRows)
            {
                EventRow row = new EventRow();
                EventRowMapper.ApplyToRow(row, values);
                EventRows.Add(row);
            }

            _pendingRows.Clear();

            // 記録中も、今追加された最新行を薄い黄色でハイライト+自動スクロールする
            HighlightEventRow(EventRows.Count - 1);
        }
    }
}
