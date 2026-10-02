using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EventRecorder
{
    public partial class MainWindow
    {
        // *******************************************************************************
        // プレイリスト: 複数の設定ファイルを指定した順番で連続再生する

        // 右クリックしたセルの行(プレイリスト側)。行が無い場所を右クリックした場合は-1(末尾扱い)
        private int contextMenuPlaylistRowIndex = -1;

        // trueなら、実行チェックがONの行だけをプレイリストに表示する
        private Boolean showOnlyCheckedPlaylistRows = false;

        // UpdatePlaylistMissingFileHighlightsの非同期化用の世代カウンタ。
        // File.Existsチェック(バックグラウンド)が終わる前に別のプロファイルへ切り替えられた場合、
        // 古い結果をグリッドへ適用してしまわないようにするため
        private int playlistHighlightGeneration = 0;

        // プロファイル読込でプレイリストの行を作っている最中はtrue(1行ごとのファイル存在チェック・ループ回数の読み取りを抑止する)
        private Boolean isLoadingPlaylist = false;

        // プレイリストの設定ファイル列のプルダウン(playlistFileItems)を、comboBox_Profileと同じ内容に揃える。
        // ファイルシステムへの問い合わせはcomboBox_Profile側だけで行い、その結果をそのままコピーする。
        // 既にプレイリストの行が参照しているファイル名は、実体が削除されて一覧から消えていても残しておく。
        //
        // WPFのComboBoxは、選択中の項目が選択肢から一瞬でも消えると選択が外れてしまうため、
        // Clear→全件Addではなく、差分(消えた項目の削除・増えた項目の挿入)だけで更新する
        private void SyncPlaylistFileItems()
        {
            List<String> desired = new List<String>();
            foreach (object item in comboBox_Profile.Items)
            {
                desired.Add(Convert.ToString(item));
            }

            foreach (PlaylistRow row in playlistRows)
            {
                if (!String.IsNullOrEmpty(row.FileName) && !desired.Contains(row.FileName))
                {
                    desired.Add(row.FileName);
                }
            }

            for (int i = playlistFileItems.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(playlistFileItems[i]))
                {
                    playlistFileItems.RemoveAt(i);
                }
            }

            for (int i = 0; i < desired.Count; i++)
            {
                int current = playlistFileItems.IndexOf(desired[i]);
                if (current == i)
                {
                    continue;
                }

                if (current >= 0)
                {
                    playlistFileItems.Move(current, i);
                }
                else
                {
                    playlistFileItems.Insert(i, desired[i]);
                }
            }
        }

        // 右クリックメニュー「プレイリストを更新」。既存の行の並び・設定(実行チェック・ループ数)はそのまま残し、
        // 増えたファイルだけ末尾に追加する。消えたファイルの行は削除せず残す(ピンクでハイライトされる)
        private void menuItem_PlaylistRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlaylist();
        }

        internal void RefreshPlaylist()
        {
            if (isRecording || isPlaying)
            {
                return;
            }

            HashSet<String> existingFiles = new HashSet<String>();
            foreach (PlaylistRow row in playlistRows)
            {
                if (!String.IsNullOrEmpty(row.FileName))
                {
                    existingFiles.Add(row.FileName);
                }
            }

            foreach (String fileName in playlistFileItems.ToList())
            {
                if (existingFiles.Contains(fileName))
                {
                    continue;
                }

                int insertAt = playlistRows.Count;
                AddPlaylistRow(insertAt, isEnabled: true);
                // 設定ファイルを選ぶと、そのファイルに保存されているループ回数が初期値として入る(PlaylistRow_CellChanged)
                playlistRows[insertAt].FileName = fileName;
            }

            UpdatePlaylistMissingFileHighlights();
        }

        // 設定ファイル列が指しているファイルが(削除等で)もう存在しない行をMistyRoseで知らせる。行自体は消さない。
        // File.Existsはディスクアクセスを伴う(Googleドライブ上だと特に遅い)ため、判定自体はバックグラウンドスレッドで行い、
        // グリッドへの反映だけ完了後にUIスレッドへ戻す(WinForms版の「プロファイル選択時のUI更新を高速化」と同じ)
        private void UpdatePlaylistMissingFileHighlights()
        {
            List<String> fileNames = playlistRows
                .Select(row => row.FileName)
                .Where(name => !String.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            int generation = ++playlistHighlightGeneration;
            String folder = userDataFolder;

            Task.Run(() =>
            {
                Dictionary<String, Boolean> existsMap = new Dictionary<String, Boolean>();
                foreach (String fileName in fileNames)
                {
                    existsMap[fileName] = File.Exists(Path.Combine(folder, fileName));
                }
                return existsMap;
            }).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 判定中に別のプロファイルへ切り替えられていたら、古い結果は捨てる
                    if (generation != playlistHighlightGeneration)
                    {
                        return;
                    }

                    ApplyPlaylistMissingFileHighlights(task.Result);
                }));
            }, TaskScheduler.Default);
        }

        internal void ApplyPlaylistMissingFileHighlights(Dictionary<String, Boolean> existsMap)
        {
            foreach (PlaylistRow row in playlistRows)
            {
                Boolean exists;
                if (!String.IsNullOrEmpty(row.FileName) && existsMap.TryGetValue(row.FileName, out exists) && !exists)
                {
                    row.MissingFileText = "このファイル(" + row.FileName + ")は見つからないよ(削除された可能性があるよ)";
                }
                else
                {
                    row.MissingFileText = null;
                }
            }
        }

        private void dataGrid_Playlist_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            contextMenuPlaylistRowIndex = SelectRowForContextMenu(dataGrid_Playlist, e);
        }

        // *******************************************************************************
        // プレイリストのキー操作: Delete(セルを空にする)・Ctrl+Z/Ctrl+Y・ループ数列の↑/↓(値の増減)

        private void dataGrid_Playlist_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ループ数列にいる時は、↑/↓キーで行移動する代わりに値を1つ増減する(textBox_Loop側の↑/↓と同じ操作感)。
            // 編集中(テキストボックスで入力中)でも同じように増減する
            if ((e.Key == Key.Up || e.Key == Key.Down) && Keyboard.Modifiers == ModifierKeys.None
                && dataGrid_Playlist.CurrentCell.Column == col_PlaylistLoopCount)
            {
                StepPlaylistLoopCountCell(e.Key == Key.Up ? 1 : -1);
                e.Handled = true;
                return;
            }

            if (WpfGridHelper.IsEditing(dataGrid_Playlist))
            {
                return;
            }

            if (HandleUndoRedoKey(e, playlistUndo))
            {
                return;
            }

            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                ClearSelectedCells(dataGrid_Playlist, playlistUndo);
                e.Handled = true;
            }
        }

        // ループ数列のカレントセルの値をdeltaだけ増減する。1未満にはしない(ループ数0以下は再生時に1として扱われるため)
        private void StepPlaylistLoopCountCell(int delta)
        {
            if (WpfGridHelper.IsEditing(dataGrid_Playlist))
            {
                TextBox editBox = Keyboard.FocusedElement as TextBox;
                if (editBox == null)
                {
                    return;
                }

                int current;
                int.TryParse(editBox.Text, out current);
                editBox.Text = StepLoopCount(current, delta).ToString();
                editBox.CaretIndex = editBox.Text.Length;
                return;
            }

            PlaylistRow row = dataGrid_Playlist.CurrentCell.Item as PlaylistRow;
            if (row == null)
            {
                return;
            }

            int currentValue;
            int.TryParse(row.LoopCount, out currentValue);
            row.LoopCount = StepLoopCount(currentValue, delta).ToString();
        }

        internal static int StepLoopCount(int current, int delta)
        {
            return Math.Max(1, (current <= 0 ? 1 : current) + delta);
        }

        // *******************************************************************************
        // 設定ファイル列(プルダウン)の編集: 1クリックで編集を始めてプルダウンを開き、選んだ瞬間に確定する
        // (WinForms版のCurrentCellDirtyStateChangedでの即時コミット相当)

        private void PlaylistFileCell_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DataGridCell cell = sender as DataGridCell;
            if (cell == null || cell.IsEditing || isRecording || isPlaying)
            {
                return;
            }

            // Ctrl/Shift+クリック(複数選択)の時は編集を始めない
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (!cell.IsFocused)
            {
                cell.Focus();
            }
            dataGrid_Playlist.CurrentCell = new DataGridCellInfo(cell);
            dataGrid_Playlist.BeginEdit(e);
        }

        private void PlaylistFileEditor_Loaded(object sender, RoutedEventArgs e)
        {
            ComboBox editor = (ComboBox)sender;
            editor.Focus();
            editor.IsDropDownOpen = true;
        }

        private void PlaylistFileEditor_DropDownClosed(object sender, EventArgs e)
        {
            dataGrid_Playlist.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // プレイリストのセルの値が変わった(手入力・Undo/Redo・コードからの代入すべて)
        private void PlaylistRow_CellChanged(PlaylistRow row, GridCellChangedEventArgs e)
        {
            if (e.Contains(PlaylistRow.ColEnabled))
            {
                // チェックON/OFFが変わったら、表示フィルタが有効な時はその場で表示/非表示を切り替える
                ApplyPlaylistRowFilter(row);
            }

            if (!e.Contains(PlaylistRow.ColFileName) || isLoadingPlaylist)
            {
                return;
            }

            // 選び直した/クリアしたファイルが存在するかどうかで、ピンク表示を更新する
            UpdatePlaylistMissingFileHighlights();

            if (String.IsNullOrEmpty(row.FileName))
            {
                return;
            }

            // 設定ファイルを選ぶと、そのファイル自身に保存されているループ回数を、行のループ回数へ初期値として自動セットする
            String savedLoopCount = ReadSavedLoopCount(Path.Combine(userDataFolder, row.FileName));
            if (savedLoopCount != null)
            {
                row.LoopCount = savedLoopCount;
            }
        }

        // マクロファイル自身が保存しているループ回数だけを、レコード表等には一切触れずに読み取る
        // (WinForms版は旧XML形式からも読めたが、WPF版はJSONのみ対応)
        private static String ReadSavedLoopCount(String filePath)
        {
            if (!File.Exists(filePath) || !IsJsonFile(filePath))
            {
                return null;
            }

            EventRecorderProfile profile = StandardTemplate.JsonFileStorage.Load<EventRecorderProfile>(filePath);
            return profile?.LoopCount;
        }

        // *******************************************************************************
        // プレイリストの行のドラッグ&ドロップによる並び替え

        private const String PlaylistRowDragFormat = "EventRecorder.PlaylistRowIndex";

        // 掴んだ(左ボタンを押した)行のインデックス。ドラッグ開始の起点にもする
        private int dragStartPlaylistRowIndex = -1;
        private Point dragStartPlaylistPoint;

        private void dataGrid_Playlist_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isRecording || isPlaying)
            {
                dragStartPlaylistRowIndex = -1;
                return;
            }

            dragStartPlaylistRowIndex = WpfGridHelper.GetRowIndexFromSource(dataGrid_Playlist, e.OriginalSource);
            dragStartPlaylistPoint = e.GetPosition(dataGrid_Playlist);
        }

        // マウスを一定距離動かして初めてドラッグとみなす(チェックボックスのクリック等が誤ってドラッグ扱いにならないように)
        private void dataGrid_Playlist_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragStartPlaylistRowIndex < 0)
            {
                return;
            }

            // セル編集中(プルダウン・テキスト入力中)はドラッグを始めない
            if (WpfGridHelper.IsEditing(dataGrid_Playlist))
            {
                return;
            }

            Point p = e.GetPosition(dataGrid_Playlist);
            if (Math.Abs(p.X - dragStartPlaylistPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(p.Y - dragStartPlaylistPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            int sourceIndex = dragStartPlaylistRowIndex;
            dragStartPlaylistRowIndex = -1;
            DragDrop.DoDragDrop(dataGrid_Playlist, new DataObject(PlaylistRowDragFormat, sourceIndex), DragDropEffects.Move);
        }

        private void dataGrid_Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(PlaylistRowDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void dataGrid_Playlist_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(PlaylistRowDragFormat))
            {
                return;
            }

            int sourceIndex = (int)e.Data.GetData(PlaylistRowDragFormat);
            int targetIndex = WpfGridHelper.GetRowIndexFromSource(dataGrid_Playlist, e.OriginalSource);
            MovePlaylistRow(sourceIndex, targetIndex);
            e.Handled = true;
        }

        // sourceIndex行をtargetIndexの位置へ移動する(ドロップ先の行の位置に割り込む形)
        internal void MovePlaylistRow(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= playlistRows.Count
                || targetIndex < 0 || targetIndex >= playlistRows.Count
                || sourceIndex == targetIndex)
            {
                return;
            }

            PlaylistRow moved = playlistRows[sourceIndex];
            playlistRows.Move(sourceIndex, targetIndex);

            WpfGridHelper.SelectWholeRow(dataGrid_Playlist, moved);
        }

        // *******************************************************************************
        // プレイリストの右クリックメニュー

        private void dataGrid_Playlist_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (isRecording || isPlaying)
            {
                e.Handled = true;
                return;
            }

            menuItem_PlaylistDeleteRow.IsEnabled = contextMenuPlaylistRowIndex >= 0 && contextMenuPlaylistRowIndex < playlistRows.Count;
            // 右クリックした行にファイルが指定されていて、実在する時だけ開けるようにする
            menuItem_PlaylistOpenFile.IsEnabled = File.Exists(GetContextMenuPlaylistFilePath() ?? "");

            // 今どちらの表示モードか一目で分かるように、選択中の方にチェックを付ける
            menuItem_PlaylistShowCheckedOnly.IsChecked = showOnlyCheckedPlaylistRows;
            menuItem_PlaylistShowAll.IsChecked = !showOnlyCheckedPlaylistRows;
        }

        // 右クリックした行の設定ファイルのフルパスを返す(行が範囲外、またはファイル未指定ならnull)
        private String GetContextMenuPlaylistFilePath()
        {
            if (contextMenuPlaylistRowIndex < 0 || contextMenuPlaylistRowIndex >= playlistRows.Count)
            {
                return null;
            }

            String fileName = playlistRows[contextMenuPlaylistRowIndex].FileName;
            if (String.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return Path.Combine(userDataFolder, fileName);
        }

        // 右クリックした行の設定ファイルを、Windowsの関連付けアプリ(メモ帳など)で開く
        private void menuItem_PlaylistOpenFile_Click(object sender, RoutedEventArgs e)
        {
            String filePath = GetContextMenuPlaylistFilePath();
            if (!File.Exists(filePath ?? ""))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(filePath);
            }
            catch (Exception ex)
            {
                // 拡張子に関連付けアプリが無い場合など
                MessageBox.Show(this, ex.Message, "ファイルを開けませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void menuItem_PlaylistShowCheckedOnly_Click(object sender, RoutedEventArgs e)
        {
            showOnlyCheckedPlaylistRows = true;
            ApplyPlaylistRowFilter();
        }

        private void menuItem_PlaylistShowAll_Click(object sender, RoutedEventArgs e)
        {
            showOnlyCheckedPlaylistRows = false;
            ApplyPlaylistRowFilter();
        }

        // 実行列のチェック状態を見て、プレイリストの行の表示/非表示を切り替える
        internal void ApplyPlaylistRowFilter()
        {
            foreach (PlaylistRow row in playlistRows)
            {
                ApplyPlaylistRowFilter(row);
            }
        }

        // 1行分の表示/非表示を、今の表示フィルタと実行チェックの状態に合わせる
        private void ApplyPlaylistRowFilter(PlaylistRow row)
        {
            row.IsVisible = !showOnlyCheckedPlaylistRows || row.Enabled;
        }

        internal Boolean ShowOnlyCheckedPlaylistRows
        {
            get { return showOnlyCheckedPlaylistRows; }
            set { showOnlyCheckedPlaylistRows = value; ApplyPlaylistRowFilter(); }
        }

        private void menuItem_PlaylistAddRow_Click(object sender, RoutedEventArgs e)
        {
            Boolean isContextMenuRowIndexValid = contextMenuPlaylistRowIndex >= 0 && contextMenuPlaylistRowIndex < playlistRows.Count;
            int insertAt = isContextMenuRowIndexValid ? contextMenuPlaylistRowIndex + 1 : playlistRows.Count;
            // 右クリックで能動的に追加した行は、すぐ使うつもりのはずなので実行チェックはONにしておく
            AddPlaylistRow(insertAt, isEnabled: true);
        }

        // プレイリストに1行追加する(ループ回数=1がデフォルト。実行チェックの初期値はisEnabledで指定)
        internal void AddPlaylistRow(int insertAt, Boolean isEnabled)
        {
            // 呼び出し元の計算ミスで範囲外indexが渡ってきても落ちないよう防御的にクランプする
            insertAt = Math.Max(0, Math.Min(insertAt, playlistRows.Count));

            PlaylistRow newRow = PlaylistRow.FromData(isEnabled, "", "1");
            ApplyPlaylistRowFilter(newRow);
            playlistRows.Insert(insertAt, newRow);
        }

        private void menuItem_PlaylistDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            List<int> targetIndexes = GetSelectedOrContextMenuRowIndexes(dataGrid_Playlist, contextMenuPlaylistRowIndex);

            foreach (int idx in targetIndexes.OrderByDescending(x => x))
            {
                if (idx == highlightedPlaylistRowIndex)
                {
                    highlightedPlaylistRowIndex = -1;
                }
                playlistRows.RemoveAt(idx);
            }
        }

        private void menuItem_PlaylistCheckAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllPlaylistChecks(true);
        }

        private void menuItem_PlaylistUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllPlaylistChecks(false);
        }

        private void SetAllPlaylistChecks(Boolean isChecked)
        {
            foreach (PlaylistRow row in playlistRows)
            {
                row.Enabled = isChecked;
            }

            ApplyPlaylistRowFilter();
        }

        // *******************************************************************************
        // プレイリストの実行

        // プレイリストの1行分(ファイル名+そのファイル専用のループ回数)
        internal class PlaylistEntry
        {
            public String FileName { get; set; }
            public int LoopCount { get; set; }

            // プレイリスト上の元の行インデックス。実行中にその行をハイライトするために使う
            public int RowIndex { get; set; }
        }

        // プレイリストの各行のうち、チェックが入っていてファイルが選ばれている行だけを、上から順番に取り出す
        internal List<PlaylistEntry> GetPlaylistEntries()
        {
            List<PlaylistEntry> entries = new List<PlaylistEntry>();
            for (int i = 0; i < playlistRows.Count; i++)
            {
                PlaylistRow row = playlistRows[i];
                if (!row.Enabled || String.IsNullOrEmpty(row.FileName))
                {
                    continue;
                }

                entries.Add(new PlaylistEntry() { FileName = row.FileName, LoopCount = ParseLoopCount(row.LoopCount), RowIndex = i });
            }

            return entries;
        }

        // プレイリストグループの内容を実行する(isPlaying/isRecordingのチェックは呼び出し元のPlayOrStopで済んでいる)
        private void StartPlaylistRun()
        {
            List<PlaylistEntry> entries = GetPlaylistEntries();
            if (entries.Count == 0)
            {
                return;
            }

            // 「全体ループ」も単発再生の「ループ回数」とtextBox_Loopを共有している
            int overallLoopCount = ParseLoopCount(textBox_Loop.Text);

            cursorPositionBeforePlay = System.Windows.Forms.Cursor.Position;

            playbackOverallLoopNo = 0;
            playbackOverallLoopMax = overallLoopCount;
            playbackInnerLoopNo = 0;
            playbackInnerLoopMax = 0;

            isPlaying = true;
            stopPlayRequested = false;
            UpdatePlayButton();
            UpdateTitle();
            MinimizeIfRequested();

            playbackTask = Task.Run(() => PlaylistPlayLoop(entries, overallLoopCount));
        }

        // プレイリスト全体をoverallLoopCount回繰り返す。各周回の中で、
        // リストの各行を上から順に読み込み→その行のループ回数分だけ再生、を繰り返す
        private void PlaylistPlayLoop(List<PlaylistEntry> entries, int overallLoopCount)
        {
            try
            {
                for (int loopNo = 0; loopNo < overallLoopCount && !stopPlayRequested; loopNo++)
                {
                    int loopDisplayNo = loopNo + 1;
                    playbackOverallLoopNo = loopDisplayNo;

                    for (int i = 0; i < entries.Count && !stopPlayRequested; i++)
                    {
                        PlaylistEntry entry = entries[i];
                        int fileNo = i + 1;
                        List<String[]> rows = null;

                        InvokeOnUi(() =>
                        {
                            label_PlaylistStatus.Text = "実行中(全体" + loopDisplayNo + "/" + overallLoopCount + "): "
                                + entry.FileName + " (" + fileNo + "/" + entries.Count + ")";

                            // 今どのファイル(行)を再生しているか、プレイリスト側もハイライトする
                            HighlightPlaylistRow(entry.RowIndex);

                            // 再生対象の記録データだけ差し替える。プレイリスト自体には影響しない
                            LoadProfileForPlayback(Path.Combine(userDataFolder, entry.FileName));

                            // 読み込んだファイルに再生できない行が無いか確認する。あればプレイリスト全体を停止する
                            if (!ValidateEventsForPlayback())
                            {
                                stopPlayRequested = true;
                                return;
                            }

                            rows = SnapshotRows();
                        });

                        if (rows != null && rows.Count > 0)
                        {
                            PlayRows(rows, entry.LoopCount);
                        }
                    }
                }

                System.Windows.Forms.Cursor.Position = cursorPositionBeforePlay;
            }
            catch (Exception ex)
            {
                ReportPlaybackError(ex);
            }
            finally
            {
                // 途中で例外が起きても、必ず「再生中」状態を解除する
                isPlaying = false;
                stopPlayRequested = false;
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
    }
}
