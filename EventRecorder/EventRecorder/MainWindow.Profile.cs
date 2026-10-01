using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using StandardTemplate;

namespace EventRecorder
{
    public partial class MainWindow
    {
        // *******************************************************************************
        // 保存/読込(JSON、[[_Common/JsonFileStorage.cs]])。ファイル形式はWinForms版と同じEventRecorderProfile([[Profile.cs]])。
        // WinForms版にあった旧XML形式(StcSaveRestore)の読み書きはWPF版では対応しない
        // (XMLのプロファイルはWinForms版で開いて.jsonで保存し直せば移行できる)

        private static readonly String[] ProfileExtensions = { "*.json" };

        private static Boolean IsJsonFile(String filePath)
        {
            return String.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase);
        }

        // コンボボックスで設定ファイルを選び直したら、そのままそれを読み込む。
        // SelectionChangedの時点ではTextがまだ古いので、SelectedItemから読む
        private void comboBox_Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBox_Profile.SelectedItem == null)
            {
                return;
            }

            LoadProfile(Path.Combine(userDataFolder, comboBox_Profile.SelectedItem.ToString()));
        }

        // 設定ファイル(記録データ+プレイリスト+モード)をまるごと読み込む
        internal void LoadProfile(String filePath)
        {
            LoadProfileFromJson(filePath, true);
            UpdatePlaylistMissingFileHighlights();
        }

        // プレイリスト再生時、各行の設定ファイルを1つずつ読み込む専用(記録データのみ差し替え、プレイリスト自体は触らない)
        private void LoadProfileForPlayback(String filePath)
        {
            if (!IsJsonFile(filePath))
            {
                // WPF版は旧XML形式を読めない。前のファイルの記録データを誤って再生しないよう、空にして何も再生しない
                eventRows.Clear();
                highlightedEventRowIndex = -1;
                return;
            }

            LoadProfileFromJson(filePath, false);
        }

        // JSONファイルを読み込み、記録データ(+loadPlaylistAndModeがtrueならプレイリストとモードも)を画面へ反映する
        internal void LoadProfileFromJson(String filePath, Boolean loadPlaylistAndMode)
        {
            EventRecorderProfile profile = JsonFileStorage.Load<EventRecorderProfile>(filePath);
            if (profile == null)
            {
                return;
            }

            // 編集途中のセルがあれば破棄してから入れ替える(古い行へ書き戻されないように)
            dataGrid_Events.CancelEdit(DataGridEditingUnit.Row);
            if (loadPlaylistAndMode)
            {
                dataGrid_Playlist.CancelEdit(DataGridEditingUnit.Row);
            }

            textBox_Loop.Text = String.IsNullOrEmpty(profile.LoopCount) ? "1" : profile.LoopCount;

            // 行は一旦リストに作ってから、グリッドへはまとめて1回で反映する(行数が多いプロファイルでも読込が遅くならないように)
            List<EventRow> rows = new List<EventRow>();
            foreach (MacroEventData eventData in profile.Events ?? new List<MacroEventData>())
            {
                if (eventData == null)
                {
                    continue;
                }
                rows.Add(EventRow.FromData(eventData.Type, eventData.X, eventData.Y, eventData.Key, eventData.Wait, eventData.Remarks));
            }

            // 万一、旧形式相当(各行が自分のWaitを持つ)のデータを読み込んでも安全なように、待機をWAIT_MS行へ切り出す
            EventRules.MigrateWaitColumnToRows(rows);

            highlightedEventRowIndex = -1;
            eventRows.ReplaceAll(rows);

            if (loadPlaylistAndMode)
            {
                List<PlaylistRow> playlist = new List<PlaylistRow>();
                foreach (PlaylistEntryData entryData in profile.Playlist ?? new List<PlaylistEntryData>())
                {
                    if (entryData == null)
                    {
                        continue;
                    }
                    PlaylistRow row = PlaylistRow.FromData(entryData.Enabled, entryData.FileName, entryData.LoopCount);
                    ApplyPlaylistRowFilter(row);
                    playlist.Add(row);
                }

                isLoadingPlaylist = true;
                try
                {
                    highlightedPlaylistRowIndex = -1;
                    playlistRows.ReplaceAll(playlist);
                }
                finally
                {
                    isLoadingPlaylist = false;
                }

                // 読み込んだプレイリストが参照しているファイル名を、プルダウンの選択肢にも入れておく
                SyncPlaylistFileItems();

                // モード切替ラジオボタン・最小化チェックボックスは、プレイリスト再生時の各行のファイル読込
                // (loadPlaylistAndMode=false)では適用しない(途中でモードが切り替わってしまうのを防ぐため)
                radioButton_Record.IsChecked = profile.IsRecordMode;
                radioButton_Playback.IsChecked = !profile.IsRecordMode;
                checkBox_MinimizeOnPlay.IsChecked = profile.MinimizeOnPlay;
            }

            // ファイル読込は「ユーザーの編集操作」ではないので、Ctrl+Zで戻せないようにする
            eventsUndo.ClearUndoHistory();
            if (loadPlaylistAndMode)
            {
                playlistUndo.ClearUndoHistory();
            }
        }

        // 今のグリッドの中身(記録データ+プレイリスト)をJSON保存用のPOCOに詰め替える
        internal EventRecorderProfile BuildProfileFromGrids()
        {
            EventRecorderProfile profile = new EventRecorderProfile();
            profile.LoopCount = textBox_Loop.Text;
            profile.IsRecordMode = radioButton_Record.IsChecked == true;
            profile.MinimizeOnPlay = checkBox_MinimizeOnPlay.IsChecked == true;

            foreach (EventRow row in eventRows)
            {
                profile.Events.Add(new MacroEventData
                {
                    Type = row.Type,
                    X = row.X,
                    Y = row.Y,
                    Key = row.Key,
                    Wait = row.Wait,
                    Remarks = row.Remarks,
                });
            }

            foreach (PlaylistRow row in playlistRows)
            {
                profile.Playlist.Add(new PlaylistEntryData
                {
                    Enabled = row.Enabled,
                    FileName = row.FileName,
                    LoopCount = row.LoopCount,
                });
            }

            return profile;
        }

        // 失敗時はダイアログを出さず理由だけ返す(エラー表示はSaveProfileWithErrorDialogで1回だけ行う)
        internal Boolean SaveProfile(String filePath, out String errorMessage)
        {
            // 編集途中のセルがあれば確定させてから保存する
            dataGrid_Events.CommitEdit(DataGridEditingUnit.Row, true);
            dataGrid_Playlist.CommitEdit(DataGridEditingUnit.Row, true);

            try
            {
                JsonFileStorage.Save(filePath, BuildProfileFromGrids());
                errorMessage = "";
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private Boolean SaveProfileWithErrorDialog(String filePath)
        {
            String errorMessage;
            if (SaveProfile(filePath, out errorMessage))
            {
                return true;
            }

            MessageBox.Show(this, "設定の保存に失敗したよ" + Environment.NewLine + filePath + Environment.NewLine + errorMessage,
                AppName + " - 保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        // comboBox_Profile(プレイリストの設定ファイル列も含む)へ、userDataFolder配下(サブフォルダ含む)の*.jsonを、
        // userDataFolderからの相対パスで並べる。WinForms版のComboBox(Sorted=true)と同じく名前順に並べ、
        // defaultProfileNameに一致する項目(無ければ先頭)を選ぶ。選択が変わればSelectionChangedでそのプロファイルが読み込まれる
        private void UpdateProfileListAll(String defaultProfileName)
        {
            List<String> names = ListProfileNames(userDataFolder);

            comboBox_Profile.Items.Clear();
            foreach (String name in names)
            {
                comboBox_Profile.Items.Add(name);
            }

            int index = names.IndexOf(defaultProfileName);
            comboBox_Profile.SelectedIndex = (index >= 0) ? index : (names.Count > 0 ? 0 : -1);
        }

        // internal: 画面を使わずにテストできるよう、一覧の作成だけを分けてある
        internal static List<String> ListProfileNames(String folder)
        {
            List<String> names = new List<String>();
            if (!Directory.Exists(folder))
            {
                return names;
            }

            try
            {
                foreach (String extension in ProfileExtensions)
                {
                    foreach (String file in Directory.GetFiles(folder, extension, SearchOption.AllDirectories))
                    {
                        // EventRecorder.json(アプリの設定ファイル)はプロファイルではないので除外する
                        if (IsNonProfileSettingFile(file))
                        {
                            continue;
                        }

                        String name = file.Substring(folder.TrimEnd('\\').Length + 1);
                        if (name != String.Empty && !names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return names;
            }

            names.Sort(StringComparer.CurrentCulture);
            return names;
        }

        // 保存直後は一覧の見た目を最新化したいだけで、選び直したわけではないので、
        // SelectionChanged(=プロファイルの再読み込み。プレイリストもクリアされる)を一時的に外してから呼ぶ
        private void UpdateProfileListAllWithoutReload(String defaultProfileName)
        {
            comboBox_Profile.SelectionChanged -= comboBox_Profile_SelectionChanged;
            try
            {
                UpdateProfileListAll(defaultProfileName);
            }
            finally
            {
                comboBox_Profile.SelectionChanged += comboBox_Profile_SelectionChanged;
            }
        }

        private void button_ProfileSave_Click(object sender, RoutedEventArgs e)
        {
            SaveProfileWithPrompt();
        }

        // プルダウンで既存ファイルが選ばれている時は、毎回ダイアログを開かず「上書きしますか?」の確認だけで済ませられるようにする。
        // はい=上書き保存、いいえ=別名で保存(ダイアログへ進む)、キャンセル=何もせず終了。
        // プルダウンが空の時は、ファイル選択ダイアログを出す(WinForms版と同じ)
        private void SaveProfileWithPrompt()
        {
            String currentName = comboBox_Profile.SelectedItem != null ? comboBox_Profile.SelectedItem.ToString() : "";

            if (!String.IsNullOrEmpty(currentName))
            {
                MessageBoxResult overwriteResult = MessageBox.Show(
                    this,
                    currentName + " を上書きしますか?",
                    "上書き確認",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (overwriteResult == MessageBoxResult.Cancel)
                {
                    return;
                }

                if (overwriteResult == MessageBoxResult.Yes)
                {
                    String overwriteFilePath = Path.Combine(userDataFolder, currentName);
                    if (!SaveProfileWithErrorDialog(overwriteFilePath))
                    {
                        return;
                    }

                    AfterProfileSaved(overwriteFilePath);
                    return;
                }
            }

            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.FileName = Path.GetFileName(currentName);
            dlg.InitialDirectory = String.IsNullOrEmpty(currentName)
                ? userDataFolder
                : Path.GetDirectoryName(Path.Combine(userDataFolder, currentName));
            dlg.Filter = "JSONファイル(*.json)|*.json";
            dlg.DefaultExt = ".json";
            dlg.AddExtension = true;
            dlg.Title = "保存するプロファイルを選択してください";

            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            String saveFilePath = dlg.FileName;
            if (!IsJsonFile(saveFilePath))
            {
                saveFilePath += ".json";
            }

            if (!SaveProfileWithErrorDialog(saveFilePath))
            {
                return;
            }

            AfterProfileSaved(saveFilePath);
        }

        // 保存したファイルをプルダウンで選択状態にして(読み直しはしない)、プレイリストの選択肢も最新にする
        private void AfterProfileSaved(String savedFilePath)
        {
            String relativeName = ToProfileName(savedFilePath);
            UpdateProfileListAllWithoutReload(relativeName);
            SyncPlaylistFileItems();
        }

        // 保存先のフルパスを、プルダウンに並べる名前(userDataFolderからの相対パス)に直す。
        // userDataFolderの外に保存した場合はファイル名だけ(WinForms版と同じくPath.GetFileName)
        private String ToProfileName(String fullPath)
        {
            String folder = userDataFolder.TrimEnd('\\') + "\\";
            if (fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(folder.Length);
            }
            return Path.GetFileName(fullPath);
        }
    }
}
