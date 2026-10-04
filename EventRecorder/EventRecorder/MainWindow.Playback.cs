using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InputSim = InputSimulation.InputSimulator;
using Keys = System.Windows.Forms.Keys;

namespace EventRecorder
{
    public partial class MainWindow
    {
        // *******************************************************************************
        // 再生

        private void button_Play_Click(object sender, RoutedEventArgs e)
        {
            PlayOrStop();
        }

        // 単発再生とプレイリスト実行を1つのbutton_Playに統合したので、開始時にどちらを動かすかは
        // ラジオボタンで選択中のモードで振り分ける。停止は共通。ボタンクリックからも再生ホットキーからも呼ばれる
        private void PlayOrStop()
        {
            if (_isPlaying)
            {
                _stopPlayRequested = true;
                return;
            }

            // 終了処理中は新しく再生を始めない
            if (_isRecording || _isExiting)
            {
                return;
            }

            // 編集途中のセルがあれば確定させてから再生する(WinForms版はセル編集中でもボタン押下で値が確定していた)
            dataGrid_Events.CommitEdit(DataGridEditingUnit.Row, true);
            dataGrid_Playlist.CommitEdit(DataGridEditingUnit.Row, true);

            if (radioButton_Playback.IsChecked == true)
            {
                StartPlaylistRun();
            }
            else
            {
                StartSinglePlayback();
            }
        }

        // レコードグループの記録データを単発再生する
        private void StartSinglePlayback()
        {
            if (!ValidateEventsForPlayback())
            {
                return;
            }

            int loopCount = ParseLoopCount(textBox_Loop.Text);

            List<String[]> rows = SnapshotRows();
            if (rows.Count == 0)
            {
                return;
            }

            // 単発再生には「全体ループ」の概念が無いので常に1/1固定にする
            _playbackOverallLoopNo = 1;
            _playbackOverallLoopMax = 1;
            _playbackInnerLoopNo = 0;
            _playbackInnerLoopMax = loopCount;

            BeginPlayback(() => PlayRows(rows, loopCount));
        }

        // 単発再生・プレイリスト実行共通の再生開始処理。ループ進捗(_playback*Loop*)は呼び出し元で設定済みの前提で、
        // 「再生中」状態にしてからplayBodyを再生スレッドで実行する
        private void BeginPlayback(Action playBody)
        {
            // 再生終了後にカーソルを戻せるよう、今の位置を覚えておく
            _cursorPositionBeforePlay = System.Windows.Forms.Cursor.Position;

            _isPlaying = true;
            _stopPlayRequested = false;
            UpdatePlayButton();
            UpdateTitle();
            MinimizeIfRequested();

            _playbackTask = Task.Run(() => RunPlayback(playBody));
        }

        // ループ回数の入力値を数値にする(0以下・数値以外は1回扱い)。単発再生・プレイリストの全体ループ/各行で共通
        private int ParseLoopCount(String text)
        {
            int loopCount = _util.GetInteger(text);
            return loopCount <= 0 ? 1 : loopCount;
        }

        // checkBox_MinimizeOnPlayがチェックされていたら、再生開始と同時にウィンドウを最小化する
        private void MinimizeIfRequested()
        {
            if (checkBox_MinimizeOnPlay.IsChecked == true)
            {
                WindowState = WindowState.Minimized;
            }
        }

        // MinimizeIfRequestedで最小化した場合、再生終了時(最後まで再生/停止/エラーのいずれでも)に
        // 標準サイズへ戻し、再生対象アプリの裏に隠れないよう前面に出す
        private void RestoreIfMinimizedByPlay()
        {
            if (checkBox_MinimizeOnPlay.IsChecked == true && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                UpdateTaskbarVisibility();
                BringWindowToFront();
            }
        }

        internal List<String[]> SnapshotRows()
        {
            // PlayRows/PlayOneEventは[Type, X, Y, Key, Wait]の順を前提にしている
            return EventRows.Select(EventRowMapper.ReadFromRow).ToList();
        }

        // 再生スレッド側の本体。playBodyの実行後、例外の有無に関わらず「再生中」状態を解除して画面を元に戻す
        // (プレイリストの状態表示・行ハイライトは単発再生では元々空なので、共通で消して問題ない)
        private void RunPlayback(Action playBody)
        {
            try
            {
                playBody();

                // マウスカーソルを再生開始前の位置に戻す
                System.Windows.Forms.Cursor.Position = _cursorPositionBeforePlay;
            }
            catch (Exception ex)
            {
                ReportPlaybackError(ex);
            }
            finally
            {
                // 途中で例外が起きても、必ず「再生中」状態を解除する(_isPlayingがtrueのまま固まらないように)
                _isPlaying = false;
                _stopPlayRequested = false;
                InvokeOnUi(() =>
                {
                    UpdatePlayButton();
                    label_PlaylistStatus.Text = "";
                    UpdateTitle();
                    HighlightEventRow(-1);
                    HighlightPlaylistRow(-1);
                    RestoreIfMinimizedByPlay();
                });
            }
        }

        // 再生スレッド(Task.Run)内の例外はどこにも通知されずに消えるため、UIスレッドへ投げ直して共通のエラー通知に乗せる
        private void ReportPlaybackError(Exception ex)
        {
            if (Dispatcher.HasShutdownStarted)
            {
                // アプリの終了と入れ違いの場合は知らせる先が無いので諦める
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => { throw new InvalidOperationException("再生中にエラーが発生したよ", ex); }));
        }

        // 再生スレッドからUIスレッドの処理を呼ぶ。シャットダウン等でUIスレッドが先に終了していた場合は、
        // 例外にせず何もしないで、再生そのものも止める(画面が無いのに入力だけ送り続けないように)。
        // action自体が投げた例外はそのまま呼び出し元へ伝える
        internal void InvokeOnUi(Action action)
        {
            if (Dispatcher.HasShutdownStarted)
            {
                _stopPlayRequested = true;
                return;
            }

            try
            {
                Dispatcher.Invoke(action);
            }
            catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted)
            {
                _stopPlayRequested = true;
            }
        }

        // rowsをloopCount回再生する処理そのもの(前後の状態管理は呼び出し元の責務)。単発再生・プレイリスト再生の両方から使う
        private void PlayRows(List<String[]> rows, int loopCount)
        {
            try
            {
                PlayRowsCore(rows, loopCount);
            }
            finally
            {
                // 中断された場合、Downのままのキー/マウスボタンを全部Upに戻す
                if (_stopPlayRequested)
                {
                    ReleaseAllPressedInputs();
                }
            }
        }

        private void PlayRowsCore(List<String[]> rows, int loopCount)
        {
            for (int i = 0; i < loopCount && !_stopPlayRequested; i++)
            {
                _playbackInnerLoopNo = i + 1;
                _playbackInnerLoopMax = loopCount;

                for (int idx = 0; idx < rows.Count && !_stopPlayRequested; idx++)
                {
                    String[] values = rows[idx];

                    int rowIndex = idx;
                    InvokeOnUi(() =>
                    {
                        HighlightEventRow(rowIndex);
                        UpdateTitle();
                    });

                    if (_stopPlayRequested)
                    {
                        return;
                    }

                    int wait = _util.GetInteger(values[4]);
                    if (wait > 0)
                    {
                        InterruptibleSleep(wait);
                    }

                    if (_stopPlayRequested)
                    {
                        return;
                    }

                    PlayOneEvent(values[0], values[1], values[2], values[3]);
                }
            }
        }

        // 待機を短い間隔に分割し、都度_stopPlayRequestedを見て早期に抜けられるようにする
        // (長いWAIT_MS行の待機中でも停止ボタンにすぐ反応させるため)
        private const int _sleepPollIntervalMs = 50;

        private void InterruptibleSleep(int totalMs)
        {
            int remaining = totalMs;
            while (remaining > 0 && !_stopPlayRequested)
            {
                int step = Math.Min(_sleepPollIntervalMs, remaining);
                Thread.Sleep(step);
                remaining -= step;
            }
        }

        // 指定したグリッドのidx行を薄い黄色でハイライトし、一番上に見えるようにスクロールする。idxに-1を渡すとハイライトを消す。
        // 記録中の最新行・単発再生中の実行中行・プレイリスト実行中の実行中行、どれも共通のこの処理を使う
        private static void HighlightRow<TRow>(DataGrid grid, IList<TRow> rows, int idx, ref int highlightedIndexField, Action<TRow, Boolean> setHighlight)
        {
            if (highlightedIndexField >= 0 && highlightedIndexField < rows.Count)
            {
                setHighlight(rows[highlightedIndexField], false);
            }

            if (idx >= 0 && idx < rows.Count)
            {
                setHighlight(rows[idx], true);

                if (grid.IsVisible)
                {
                    WpfGridHelper.ScrollRowToTop(grid, idx);
                }
            }

            highlightedIndexField = idx;
        }

        // 記録中の最新行・単発再生中の実行中行のハイライト(dataGrid_Events側)
        private void HighlightEventRow(int idx)
        {
            HighlightRow(dataGrid_Events, EventRows, idx, ref _highlightedEventRowIndex, (row, on) => row.IsHighlighted = on);
        }

        // プレイリスト実行中、今どのファイル(行)を再生しているかのハイライト(dataGrid_Playlist側)
        private void HighlightPlaylistRow(int idx)
        {
            HighlightRow(dataGrid_Playlist, PlaylistRows, idx, ref _highlightedPlaylistRowIndex, (row, on) => row.IsHighlighted = on);
        }

        // *******************************************************************************
        // レコード表(dataGrid_Events)のキー操作: Ctrl+V(貼り付け)・Delete(セルを空にする)・Ctrl+Z/Ctrl+Y(元に戻す/やり直す)

        private void dataGrid_Events_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // セル編集中(テキストボックスで入力中)は、そのテキストボックス自身の動作(文字の貼り付け・1文字戻す等)に任せる
            if (WpfGridHelper.IsEditing(dataGrid_Events))
            {
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }

            if (HandleUndoRedoKey(e, EventsUndo))
            {
                return;
            }

            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                ClearSelectedCells(dataGrid_Events, EventsUndo);
                e.Handled = true;
            }
        }

        // Ctrl+Z/Ctrl+Y(WinForms版DataGridViewEx.ProcessCmdKey相当)。処理したらtrue
        private static Boolean HandleUndoRedoKey<TRow>(KeyEventArgs e, GridUndoRedo<TRow> undo) where TRow : class, IEditableGridRow
        {
            if (!undo.EnableUndoRedo || Keyboard.Modifiers != ModifierKeys.Control)
            {
                return false;
            }

            if (e.Key == Key.Z)
            {
                undo.Undo();
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.Y)
            {
                undo.Redo();
                e.Handled = true;
                return true;
            }

            return false;
        }

        // 選択中のセルの中身を空にする(行そのものは削除しない。行削除は右クリックメニューの担当)。
        // 記録中/再生中は誤操作防止のため無効にする。複数セルをまとめて1回のCtrl+Zで戻せるようUndoバッチでまとめる。
        // プレイリストの設定ファイル列(プルダウン)は、WinForms版と同じくクリア対象から除外する
        internal void ClearSelectedCells<TRow>(DataGrid grid, GridUndoRedo<TRow> undo) where TRow : class, IEditableGridRow
        {
            ClearCells(grid.SelectedCells.ToList(), undo);
        }

        // 指定したセルの中身を空にする本体(テストからは選択状態を作らずにこちらを呼ぶ)
        internal void ClearCells<TRow>(IEnumerable<DataGridCellInfo> cells, GridUndoRedo<TRow> undo) where TRow : class, IEditableGridRow
        {
            if (_isRecording || _isPlaying)
            {
                return;
            }

            undo.BeginUndoBatch();
            try
            {
                foreach (DataGridCellInfo info in cells)
                {
                    TRow row = info.Item as TRow;
                    if (row == null || info.Column == null || info.Column.IsReadOnly || info.Column == col_PlaylistFile)
                    {
                        continue;
                    }

                    row.SetCell(WpfGridHelper.GetColumnName(info.Column), null);
                }
            }
            finally
            {
                undo.EndUndoBatch();
            }
        }

        // クリップボードのタブ区切りテキスト(Excelや他のグリッドからのコピーと同じ形式)を貼り付ける
        private void PasteFromClipboard()
        {
            if (_isRecording || _isPlaying || !Clipboard.ContainsText())
            {
                return;
            }

            String text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 他のアプリがクリップボードを開いたままの時など
                return;
            }

            DataGridColumn startColumn = dataGrid_Events.CurrentCell.Column;
            int startRow = EventRows.IndexOf(dataGrid_Events.CurrentCell.Item as EventRow);
            PasteText(text, startRow, startColumn);
        }

        // 貼り付けの本体。列がグリッドの表示列数をはみ出る分は切り捨て、行が足りない場合は全部貼り付けられるように追加する。
        // startRow/startColumnは貼り付け開始位置(カレントセル)。無ければ先頭行・先頭列から
        internal void PasteText(String text, int startRow, DataGridColumn startColumn)
        {
            if (_isRecording || _isPlaying || text == null)
            {
                return;
            }

            String[] lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length == 0)
            {
                return;
            }

            // X/Y/Key列は非表示なので、実際に見えている列(DisplayIndex順)だけを貼り付け対象にする
            List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(dataGrid_Events);

            if (startRow < 0)
            {
                startRow = 0;
            }
            int startVisibleCol = (startColumn != null) ? visibleColumns.IndexOf(startColumn) : 0;
            if (startVisibleCol < 0)
            {
                startVisibleCol = 0;
            }

            // 複数セルへの貼り付けをまとめて1回のCtrl+Zで戻せるよう、Undoバッチでまとめる
            EventsUndo.BeginUndoBatch();
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    int rowIndex = startRow + i;

                    // 行が足りなければ、貼り付け分を全部入れられるように追加する
                    while (rowIndex >= EventRows.Count)
                    {
                        EventRows.Add(new EventRow());
                    }

                    String[] values = lines[i].Split('\t');
                    for (int j = 0; j < values.Length; j++)
                    {
                        int visibleColIndex = startVisibleCol + j;

                        // 見えている列数をはみ出る分は切り捨てる
                        if (visibleColIndex >= visibleColumns.Count)
                        {
                            break;
                        }

                        EventRows[rowIndex].SetCell(WpfGridHelper.GetColumnName(visibleColumns[visibleColIndex]), values[j]);
                    }
                }
            }
            finally
            {
                EventsUndo.EndUndoBatch();
            }
        }

        // *******************************************************************************
        // レコード表の右クリックメニュー(行の追加/削除・WAIT時間の一括変更)

        // 右クリックしたセルの行。行が無い場所を右クリックした場合は-1(末尾扱い)
        private int _contextMenuRowIndex = -1;

        // 右クリック時に、そのセルの行を選択状態にしつつ対象行を覚えておく。
        // 右クリックした行がすでに選択済み(セルのドラッグ選択を含む)ならその選択状態を維持し、
        // 未選択の行を右クリックした場合だけその行の単一選択に切り替える
        private void dataGrid_Events_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _contextMenuRowIndex = SelectRowForContextMenu(dataGrid_Events, e);
        }

        private static int SelectRowForContextMenu(DataGrid grid, MouseButtonEventArgs e)
        {
            int rowIndex = WpfGridHelper.GetRowIndexFromSource(grid, e.OriginalSource);
            if (rowIndex < 0)
            {
                return -1;
            }

            object item = grid.Items[rowIndex];
            DataGridCell cell = WpfGridHelper.GetCellFromSource(e.OriginalSource);
            Boolean isAlreadySelected = WpfGridHelper.IsCellSelected(grid, item, cell != null ? cell.Column : null);
            if (!isAlreadySelected)
            {
                WpfGridHelper.SelectWholeRow(grid, item);
                if (cell != null)
                {
                    grid.CurrentCell = new DataGridCellInfo(item, cell.Column);
                }
            }

            return rowIndex;
        }

        // 記録中/再生中はメニューを出さない。行が無い場所を右クリックした場合は「行の削除」を選べないようにする
        private void dataGrid_Events_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_isRecording || _isPlaying)
            {
                e.Handled = true;
                return;
            }

            menuItem_DeleteRow.IsEnabled = IsValidIndex(_contextMenuRowIndex, EventRows.Count);
        }

        // 右クリックした行のすぐ下に空行を1件挿入する(何も無い場所を右クリックした場合は末尾に追加)
        private void menuItem_AddRow_Click(object sender, RoutedEventArgs e)
        {
            int insertAt = IsValidIndex(_contextMenuRowIndex, EventRows.Count) ? _contextMenuRowIndex + 1 : EventRows.Count;
            EventRows.Insert(insertAt, new EventRow());
        }

        // 指定したイベント名(ダイアログの初期値は右クリックした行のイベント名)の各行について、
        // 直前がWAIT_MS行ならその待機時間をまとめて指定値に変更する
        private void menuItem_BulkChangeEventWait_Click(object sender, RoutedEventArgs e)
        {
            String initialEventName = String.Empty;
            if (IsValidIndex(_contextMenuRowIndex, EventRows.Count))
            {
                String clickedType = EventRows[_contextMenuRowIndex].Type;
                if (!EventRules.IsWaitEventType(clickedType))
                {
                    initialEventName = clickedType;
                }
            }

            EventWaitBulkChangeWindow window = new EventWaitBulkChangeWindow(initialEventName);
            window.Owner = this;
            if (window.ShowDialog() != true)
            {
                return;
            }

            BulkChangeEventWait(window.EventName, window.WaitMs);
        }

        internal void BulkChangeEventWait(String eventType, int waitMs)
        {
            EventsUndo.BeginUndoBatch();
            try
            {
                EventRules.BulkChangeEventWait(EventRows, eventType, waitMs);
            }
            finally
            {
                EventsUndo.EndUndoBatch();
            }
        }

        // 選択されている行(複数選択時は全行、未選択なら右クリックした行)を削除する。
        // KeyDown/SysKeyDownの行なら、対応するKeyUp/SysKeyUp行も一緒に探して削除する(片方だけ残って孤立するのを防ぐため)
        private void menuItem_DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            DeleteEventRows(GetSelectedOrContextMenuRowIndexes(dataGrid_Events, _contextMenuRowIndex));
        }

        internal void DeleteEventRows(IList<int> targetIndexes)
        {
            if (targetIndexes.Count == 0)
            {
                return;
            }

            foreach (int idx in EventRules.CollectRowsToDelete(EventRows, targetIndexes))
            {
                if (idx == _highlightedEventRowIndex)
                {
                    _highlightedEventRowIndex = -1;
                }
                EventRows.RemoveAt(idx);
            }
        }

        // 複数行選択済みならその全行、未選択なら右クリックした1行(fallbackRowIndex)を対象にする。
        // 記録グリッド・プレイリストグリッドの両方の「行の削除」から共通で使う
        private static List<int> GetSelectedOrContextMenuRowIndexes(DataGrid grid, int fallbackRowIndex)
        {
            List<int> indexes = WpfGridHelper.GetSelectedRowIndexes(grid);
            if (indexes.Count == 0 && IsValidIndex(fallbackRowIndex, grid.Items.Count))
            {
                indexes.Add(fallbackRowIndex);
            }
            return indexes;
        }

        // indexが0以上count未満(=その行が実在する)か
        private static Boolean IsValidIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        // *******************************************************************************
        // Ctrl+マウスホイールで文字の拡大縮小(WinForms版DataGridViewExのZoomFactor相当、0.5〜3.0倍、1ノッチ0.1倍)

        private const double _minZoomFactor = 0.5;
        private const double _maxZoomFactor = 3.0;
        private const double _zoomStep = 0.1;

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            DataGrid grid = (DataGrid)sender;
            // 等倍の文字サイズは最初にCtrl+ホイールした時点の値を覚えておく
            if (!(grid.Tag is double))
            {
                grid.Tag = grid.FontSize;
            }
            double baseSize = (double)grid.Tag;
            double currentFactor = grid.FontSize / baseSize;
            int steps = e.Delta / Mouse.MouseWheelDeltaForOneLine;
            if (steps != 0)
            {
                double next = Math.Max(_minZoomFactor, Math.Min(_maxZoomFactor, Math.Round(currentFactor + steps * _zoomStep, 1)));
                grid.FontSize = baseSize * next;
            }

            // Ctrl+ホイールは拡大縮小専用にし、行スクロールへは渡さない
            e.Handled = true;
        }

        // *******************************************************************************
        // 再生前チェック

        // 再生開始前に全行のパラメータをチェックする。不正な値が見つかった行があれば、エラーメッセージをまとめて
        // ポップアップ表示し、再生を開始させない(false を返す)。該当行のDetail列は常時ピンク表示されている
        private Boolean ValidateEventsForPlayback()
        {
            List<String> errors = new List<String>();

            for (int i = 0; i < EventRows.Count; i++)
            {
                String message;
                if (EventRows[i].IsInvalidForPlayback(out message))
                {
                    errors.Add("行" + i + ": " + message);
                }
            }

            if (errors.Count == 0)
            {
                return true;
            }

            MessageBox.Show(
                this,
                "再生できない行が見つかったよ(該当セルはピンク色で表示してるよ)" + Environment.NewLine + Environment.NewLine
                    + String.Join(Environment.NewLine, errors),
                "EventRecorder - 再生前チェック",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        // 記録した1イベント分をSendInputで再現する(WinForms版と同じ)
        // ※ マウスのX(サイド)ボタン/ホイールとキーボードのSYSKEYは、HiMacroEx相当を作る段階では未対応(将来の機能拡張項目)
        private void PlayOneEvent(String type, String x, String y, String key)
        {
            List<InputSim.Input> inputs = new List<InputSim.Input>();

            InputSim.MouseStroke mouseStroke;
            if (Enum.TryParse<InputSim.MouseStroke>(type, out mouseStroke))
            {
                List<InputSim.MouseStroke> flags = new List<InputSim.MouseStroke> { InputSim.MouseStroke.MOVE, mouseStroke };

                int ix = _util.GetInteger(x);
                int iy = _util.GetInteger(y);
                InputSim.AddMouseInput(ref inputs, flags, 0, true, ix, iy);
                InputSim.SendInput(inputs);
                TrackPressedMouseButton(mouseStroke);
                return;
            }

            InputSim.KeyboardStroke keyStroke;
            Keys keyCode;
            if (Enum.TryParse<InputSim.KeyboardStroke>(type, out keyStroke)
                && Enum.TryParse<Keys>(key, out keyCode))
            {
                InputSim.AddKeyboardInput(ref inputs, keyStroke, keyCode);
                InputSim.SendInput(inputs);
                TrackPressedKey(keyStroke, keyCode);
            }
        }

        // 再生中にDownしたまま(まだUpしていない)のキー/マウスボタン。再生スレッドからしか触らない。
        // 途中で再生を中断すると、Downだけ送って止まったキー/ボタンが押しっぱなしになってしまうため、
        // 中断時にReleaseAllPressedInputsでまとめてUpに戻す
        private readonly HashSet<Keys> _playbackPressedKeys = new HashSet<Keys>();
        private readonly HashSet<InputSim.MouseStroke> _pressedMouseUpStrokes
            = new HashSet<InputSim.MouseStroke>();

        private void TrackPressedKey(InputSim.KeyboardStroke stroke, Keys keyCode)
        {
            if (stroke == InputSim.KeyboardStroke.KEY_DOWN)
            {
                _playbackPressedKeys.Add(keyCode);
            }
            else
            {
                _playbackPressedKeys.Remove(keyCode);
            }
        }

        // ボタンごとに「Down→対応するUp」の組で管理する。Downなら解除用のUpを登録、Upなら登録を外す
        private void TrackPressedMouseButton(InputSim.MouseStroke stroke)
        {
            switch (stroke)
            {
                case InputSim.MouseStroke.LEFT_DOWN:
                    _pressedMouseUpStrokes.Add(InputSim.MouseStroke.LEFT_UP);
                    break;
                case InputSim.MouseStroke.LEFT_UP:
                    _pressedMouseUpStrokes.Remove(InputSim.MouseStroke.LEFT_UP);
                    break;
                case InputSim.MouseStroke.RIGHT_DOWN:
                    _pressedMouseUpStrokes.Add(InputSim.MouseStroke.RIGHT_UP);
                    break;
                case InputSim.MouseStroke.RIGHT_UP:
                    _pressedMouseUpStrokes.Remove(InputSim.MouseStroke.RIGHT_UP);
                    break;
                case InputSim.MouseStroke.MIDDLE_DOWN:
                    _pressedMouseUpStrokes.Add(InputSim.MouseStroke.MIDDLE_UP);
                    break;
                case InputSim.MouseStroke.MIDDLE_UP:
                    _pressedMouseUpStrokes.Remove(InputSim.MouseStroke.MIDDLE_UP);
                    break;
            }
        }

        // Downのままになっているキー/マウスボタンを全部Upに戻す(カーソルは動かさない)
        private void ReleaseAllPressedInputs()
        {
            List<InputSim.Input> inputs = new List<InputSim.Input>();

            foreach (InputSim.MouseStroke upStroke in _pressedMouseUpStrokes)
            {
                InputSim.AddMouseInput(ref inputs, upStroke, 0, false, 0, 0);
            }

            foreach (Keys keyCode in _playbackPressedKeys)
            {
                InputSim.AddKeyboardInput(ref inputs, InputSim.KeyboardStroke.KEY_UP, keyCode);
            }

            _pressedMouseUpStrokes.Clear();
            _playbackPressedKeys.Clear();

            if (inputs.Count > 0)
            {
                InputSim.SendInput(inputs);
            }
        }
    }
}
