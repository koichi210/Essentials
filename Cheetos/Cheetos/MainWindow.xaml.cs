using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace Cheetos
{
    public partial class MainWindow : Window
    {
        private enum DataGridType
        {
            EditBox,
            CheckBox,
            DropDown,
        };

        private struct DataGridColumnInfo
        {
            public String HeaderName;
            public DataGridType Type;
        }

        private const String _gridHeaderSleepStr = "Sleep(msec)";
        private const String _gridHeaderMouseXStr = "MouseX";
        private const String _gridHeaderMouseYStr = "MouseY";
        private const String _gridHeaderMouseActionStr = "MouseAction";
        private const String _gridHeaderCaptureStr = "Capture";

        private const String _mouseEventMoveStr = "Move";
        private const String _mouseEventLeftDownStr = "LeftDown";

        private const String _executeStr = "〇";
        private const String _notExecuteStr = "×";

        // 列の並びはCaptureGridRowのプロパティ順と同じ
        private readonly DataGridColumnInfo[] _dataGridColumns = new DataGridColumnInfo[]{
            new DataGridColumnInfo() { HeaderName = _gridHeaderSleepStr, Type = DataGridType.EditBox },
            new DataGridColumnInfo() { HeaderName = _gridHeaderMouseXStr, Type = DataGridType.EditBox },
            new DataGridColumnInfo() { HeaderName = _gridHeaderMouseYStr, Type = DataGridType.EditBox },
            new DataGridColumnInfo() { HeaderName = _gridHeaderMouseActionStr, Type = DataGridType.DropDown },
            new DataGridColumnInfo() { HeaderName = _gridHeaderCaptureStr, Type = DataGridType.DropDown }
        };

        private readonly String[] _mouseEventItems = new String[] {
            _mouseEventMoveStr,
            _mouseEventLeftDownStr
        };

        private readonly String[] _captureEventItems = new String[] {
            _executeStr,
            _notExecuteStr
        };

        // データ保存先はWinForms版Cheetosと同じ(%LOCALAPPDATA%\Cheetos\、exe直下のDataFolder.txtで変更可)。
        // 保存キーも同じにしてあるので、WinForms版で保存したプロファイル(*.json)をそのまま読める
        private const String _appName = "Cheetos";
        private const String _settingFileName = "Cheetos.json";
        private static readonly String[] _profileExtensions = { "*.json" };

        private readonly String _userDataFolder;
        private readonly StcUtils _util = new StcUtils();
        private readonly StcFileInputOutput _fio = new StcFileInputOutput();
        private readonly StcDebug _debug = new StcDebug();
        internal readonly WpfSaveRestore _sr = new WpfSaveRestore();
        private bool _isCaptureRunning = false;

        // cw_dataGridViewの中身(1要素=1行)
        internal readonly ObservableCollection<CaptureGridRow> _cwRows = new ObservableCollection<CaptureGridRow>();

        // 各タブのBackgroundWorker(WinForms版はDesignerで配置していたもの)
        private readonly BackgroundWorker _bkgWorkerTrim = new BackgroundWorker();
        private readonly BackgroundWorker _bkgWorkerMerge = new BackgroundWorker();
        private readonly BackgroundWorker _bkgWorkerOrient = new BackgroundWorker();
        private readonly BackgroundWorker _bkgWorkerRotation = new BackgroundWorker();

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(_appName))
        {
        }

        // internal: テストでは実際のユーザーデータフォルダではなく一時フォルダを渡す
        internal MainWindow(String dataFolder)
        {
            InitializeComponent();
            _userDataFolder = dataFolder;
            _util.SetCurrentDirectory();

            // デバッグログに時間を表示
            _debug.UseTimeInLog = true;

            InitializeBackgroundWorkers();

            // DataGridの初期設定
            InitializeDataGridView();

            RegisterSaveRestoreItems();
            _sr.LoadOrDefault(Path.Combine(_userDataFolder, _settingFileName));
            WpfProfile.UpdateProfileList(Profile, _profileExtensions, "", _userDataFolder);

            // CurrentScreenキャプチャで「このウィンドウが今あるモニタ」を判定できるようにする
            SourceInitialized += (s, e) => _cw.TargetWindow = WpfUtils.CreateScreenAnchor(this);

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, _appName)));
        }

        internal void RegisterSaveRestoreItems()
        {
            // キーはWinForms版(SaveRestore.RegisterItem)と同じにすること。
            // 第2引数(設定ファイルのキー名)のtypoはWinForms版で修正済みで、旧キーはLegacyAttrValueで読み替える
            _sr.RegisterCtrl("CaptureWindow", "cw_TextBox_SavePath", cw_TextBox_SavePath);
            _sr.RegisterCtrl("CaptureWindow", "cw_TextBox_SaveFilePrefix", cw_TextBox_SaveFilePrefix, legacyAttrValue: "cw_TextBox_SaveFilePrifix");
            _sr.RegisterCtrl("CaptureWindow", "cw_checkBox_AddTimeStamp", cw_checkBox_AddTimeStamp, legacyAttrValue: "cw_checkBox_AddTimeStump");
            _sr.RegisterCtrl("CaptureWindow", "cw_Radio_FullScreen", cw_Radio_FullScreen, "True");
            _sr.RegisterCtrl("CaptureWindow", "cw_Radio_CurrentScreen", cw_Radio_CurrentScreen);
            _sr.RegisterCtrl("CaptureWindow", "cw_Radio_CurrentWindow", cw_Radio_CurrentWindow);
            _sr.RegisterCtrl("CaptureWindow", "cw_TextBox_Sleep", cw_TextBox_Sleep, "2000");
            _sr.RegisterCtrl("CaptureWindow", "cw_TextBox_Loop", cw_TextBox_Loop, "2");
            _sr.RegisterGrid("DataGrid", "Cell", GetGridRows, SetGridRows);

            _sr.RegisterCtrl("PictTrim", "pt_SourceFolderPath", pt_SourceFolderPath);
            _sr.RegisterCtrl("PictTrim", "pt_BaseX", pt_BaseX);
            _sr.RegisterCtrl("PictTrim", "pt_BaseY", pt_BaseY);
            _sr.RegisterCtrl("PictTrim", "pt_Radio_SelectPointOfEnd", pt_Radio_SelectPointOfEnd);
            _sr.RegisterCtrl("PictTrim", "pt_Radio_SelectSizeOfEnd", pt_Radio_SelectSizeOfEnd);
            _sr.RegisterCtrl("PictTrim", "pt_TargetX", pt_TargetX);
            _sr.RegisterCtrl("PictTrim", "pt_TargetY", pt_TargetY);

            _sr.RegisterCtrl("Rotation", "pr_SourceFolderPath", pr_SourceFolderPath);
            _sr.RegisterCtrl("Rotation", "pr_BaseX", pr_BaseX);
            _sr.RegisterCtrl("Rotation", "pr_BaseY", pr_BaseY);
            _sr.RegisterCtrl("Rotation", "pr_Angle", pr_Angle);

            _sr.RegisterCtrl("DistOrient", "do_SourceFolderPath", do_SourceFolderPath);
            _sr.RegisterCtrl("DistOrient", "do_DestPortFolderPath", do_DestPortFolderPath);
            _sr.RegisterCtrl("DistOrient", "do_DestLandFolderPath", do_DestLandFolderPath);
            _sr.RegisterCtrl("DistOrient", "do_TargetFileName", do_TargetFileName);
            _sr.RegisterCtrl("DistOrient", "do_WhiteLength", do_WhiteLength);
            _sr.RegisterCtrl("DistOrient", "do_WhiteCoef", do_WhiteCoef, "30");
            _sr.RegisterCtrl("DistOrient", "do_SampleFilePath", do_SampleFilePath);

            _sr.RegisterCtrl("PictMerge", "pm_SourceFolderPath", pm_SourceFolderPath);
            _sr.RegisterCtrl("PictMerge", "pm_SourceFile1Prefix", pm_SourceFile1Prefix);
            _sr.RegisterCtrl("PictMerge", "pm_SourceFile2Prefix", pm_SourceFile2Prefix);
            _sr.RegisterCtrl("PictMerge", "pm_TrimmingHeight", pm_TrimmingHeight);

            _sr.RegisterCtrl("FileCollect", "fc_SourceFolderPath", fc_SourceFolderPath);
            _sr.RegisterCtrl("FileCollect", "fc_DestFolderPath", fc_DestFolderPath);
            _sr.RegisterCtrl("FileCollect", "fc_TargetFileName", fc_TargetFileName);
        }

        // WinForms版SaveRestore.LoadProcと同じく、読み込み前にdo_WhiteCoefを既定値(30)へ戻してから読む
        internal Boolean LoadProfile(String loadFileName)
        {
            if (loadFileName == String.Empty)
            {
                return false;
            }

            // Default値
            do_WhiteCoef.Text = @"30";

            return _sr.Load(loadFileName);
        }

        // *******************************************************************************
        // DataGrid(cw_dataGridView)の保存/読込。キーはWinForms版と同じ"DataGrid|Cell"、中身は行×列の文字列

        private List<List<String>> GetGridRows()
        {
            CommitGridEdit();
            return _cwRows.Select(row => row.ToList()).ToList();
        }

        private void SetGridRows(List<List<String>> rows)
        {
            _cwRows.Clear();
            foreach (List<String> values in rows)
            {
                _cwRows.Add(CaptureGridRow.FromList(values));
            }

            // WinForms版は保存データが無くても1行は残していた(RowCount = 1)
            if (_cwRows.Count == 0)
            {
                _cwRows.Add(new CaptureGridRow());
            }
        }

        // 編集中のセルの値を確定させる(WPFのDataGridは編集中の値がまだ行データへ反映されていないことがある)
        private void CommitGridEdit()
        {
            cw_dataGridView.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void Profile_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChangedの時点ではTextがまだ古いので、SelectedItemから読む
            if (Profile.SelectedItem == null)
            {
                return;
            }
            LoadProfile(Path.Combine(_userDataFolder, Profile.SelectedItem.ToString()));
        }

        // プルダウンで既存ファイルが選ばれている時は、毎回ダイアログを開かず
        // 「上書きしますか?」の確認だけで済ませられるようにする(EventRecorderと同じ挙動)
        private void ProfileSave_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(_fio, Profile, _profileExtensions, _sr.Save, _userDataFolder, _settingFileName);
        }

        // Ctrl+Sで「設定値保存」ボタンと同じ動作にする(テキストボックス等にフォーカスがあっても拾える)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ProfileSave_Click(this, new RoutedEventArgs());
            }
        }

        // 見つからなければ先頭列(0)を返す
        private int GetDataGridColumnIdx(String columnName)
        {
            int idx = Array.FindIndex(_dataGridColumns, column => column.HeaderName == columnName);
            return Math.Max(idx, 0);
        }

        // "Move"以外(LeftDown・空欄など)はすべて左クリック扱い
        private CaptWindow.MouseEventType GetMouseEvent(String mouseEventStr)
        {
            return (mouseEventStr == _mouseEventMoveStr) ? CaptWindow.MouseEventType.Move : CaptWindow.MouseEventType.LeftClick;
        }

        // "〇"のときだけキャプチャする(空欄・"×"はキャプチャしない)
        private Boolean IsCaptureEvent(String captureEventStr)
        {
            return captureEventStr == _executeStr;
        }

        private void fc_Button_Collect_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(fc_SourceFolderPath.Text))
            {
                MessageBox.Show("フォルダパスが不正です");
                return;
            }

            if (!_fio.EnsureDirectory(fc_DestFolderPath.Text))
            {
                return;
            }

            // ファイルを一つ一つ移動する
            string[] files = Directory.GetFiles(fc_SourceFolderPath.Text, fc_TargetFileName.Text, SearchOption.TopDirectoryOnly);

            InitProgressBar(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                String destPath = fc_DestFolderPath.Text + @"\" + Path.GetFileName(files[i]);
                File.Move(files[i], destPath);

                // 進捗率の表示
                ShowProgress(i + 1);
            }
        }

        private void label_DebugMode_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            _debug.IsDebugMode = !_debug.IsDebugMode;
            MessageBox.Show("DebugMode=" + _debug.IsDebugMode.ToString());
        }

        public void SetStartTime()
        {
            textBox_StartTime.Text = DateTime.Now.ToString();
            textBox_ExpectEndTime.Text = "";
        }

        public void SetExpectEndTime(int totalCount)
        {
            // 開始時間
            DateTime dtStart = DateTime.Parse(textBox_StartTime.Text);

            // 一個分の処理時間(開始から今=一個目の終了まで)
            long procTime = DateTime.Now.Ticks - dtStart.Ticks;
            DateTime dtExpect = new DateTime(dtStart.Ticks + procTime * totalCount);

            textBox_ExpectEndTime.Text = dtExpect.ToString();
        }

        private void pt_Radio_SelectPointOfEnd_Click(object sender, RoutedEventArgs e)
        {
            UpdatePictTrimSize();
        }

        private void pt_Radio_SelectSizeOfEnd_Click(object sender, RoutedEventArgs e)
        {
            UpdatePictTrimSize();
        }

        private void pt_ListBox_ListUp_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedFiles(pt_SourceFolderPath, pt_ListBox_ListUp);
        }

        private void pm_ListBox_ListUp_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedFiles(pm_SourceFolderPath, pm_ListBox_ListUp);
        }

        // リストボックスで選択中のファイルを開く(Trim/Mergeの各タブで共通)
        private void ExecuteSelectedFiles(TextBox folderPathCtrl, ListBox listCtrl)
        {
            foreach (String name in WpfUtils.GetSelectedStrArray(listCtrl))
            {
                _util.ExecutePath(folderPathCtrl.Text + @"\" + name);
            }
        }

        private void InitProgressBar(int maximum)
        {
            ProgressBar_Status.Maximum = maximum;
            ProgressBar_Status.Minimum = 0;
            ProgressBar_Status.Value = 0;
        }

        // 進捗バーと「進捗/最大」の表示を更新する
        private void ShowProgress(int progress)
        {
            TextBox_Status.Text = progress + "/" + (int)ProgressBar_Status.Maximum;
            ProgressBar_Status.Value = progress;
        }

        // フォルダが無ければ作る。作れなければメッセージを出してfalseを返す
        private Boolean EnsureDirectoryWithMessage(String dirPath)
        {
            if (_fio.EnsureDirectory(dirPath))
            {
                return true;
            }
            MessageBox.Show("無効なフォルダパスです。\n" + dirPath);
            return false;
        }

        private void InitializeBackgroundWorkers()
        {
            foreach (BackgroundWorker worker in new[] { _bkgWorkerTrim, _bkgWorkerMerge, _bkgWorkerOrient, _bkgWorkerRotation })
            {
                worker.WorkerReportsProgress = true;
                worker.WorkerSupportsCancellation = true;
                worker.ProgressChanged += BkgWorker_ProgressChanged;
            }

            _bkgWorkerTrim.DoWork += bkgWorkerTrim_DoWork;
            _bkgWorkerTrim.RunWorkerCompleted += bkgWorkerTrim_RunWorkerCompleted;
            _bkgWorkerMerge.DoWork += bkgWorkerMerge_DoWork;
            _bkgWorkerMerge.RunWorkerCompleted += bkgWorkerMerge_RunWorkerCompleted;
            _bkgWorkerOrient.DoWork += bkgWorkerOrient_DoWork;
            _bkgWorkerOrient.RunWorkerCompleted += bkgWorkerOrient_RunWorkerCompleted;
            _bkgWorkerRotation.DoWork += bkgWorkerRotation_DoWork;
            _bkgWorkerRotation.RunWorkerCompleted += bkgWorkerRotation_RunWorkerCompleted;
        }

        // 各タブのBackgroundWorker(Trim/Merge/Rotation/Orient)は進捗表示がまったく同じだったため、
        // 1つのハンドラを4つのProgressChangedから共有する
        private void BkgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 進捗率の表示
            ShowProgress(e.ProgressPercentage);

            // 一回目の更新時に、予想終了時間を表示
            if (e.ProgressPercentage == 0)
            {
                SetExpectEndTime((int)ProgressBar_Status.Maximum);
            }
        }

        // 各タブのRunWorkerCompletedで共通の「キャンセル/エラー」表示。
        // 正常終了(e.Resultを読んでよい)ならtrueを返す
        private Boolean ShowWorkerCompletion(RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                // この場合はe.Resultにはアクセスできない
                MessageBox.Show("キャンセルされました");
                return false;
            }
            if (e.Error != null)
            {
                MessageBox.Show("エラーが発生しました[" + e.Error.Message + "]");
                return false;
            }
            return true;
        }

        // 指定フォルダ直下のファイル名をリストボックスへ並べる(Trim/Merge/Rotationの各タブで共通)
        private void ListUpFolderFiles(TextBox folderPathCtrl, ListBox listCtrl)
        {
            if (!Directory.Exists(folderPathCtrl.Text))
            {
                MessageBox.Show("フォルダパスが不正です");
                return;
            }

            string[] files = Directory.GetFiles(folderPathCtrl.Text, "*", SearchOption.TopDirectoryOnly);
            WpfUtils.SetSortedItems(listCtrl, files.Select(Path.GetFileName).ToArray());
        }

        private void InitializeDataGridView()
        {
            // 左端(行ヘッダ)の非表示・最下部(新規行)の非表示・幅の自動調整はXAML側で設定済み

            // ヘッダ作成とComboBoxのリスト作成
            for (int i = 0; i < _dataGridColumns.Length; i++)
            {
                cw_dataGridView.Columns[i].Header = _dataGridColumns[i].HeaderName;
            }
            ((DataGridComboBoxColumn)cw_dataGridView.Columns[GetDataGridColumnIdx(_gridHeaderMouseActionStr)]).ItemsSource = _mouseEventItems;
            ((DataGridComboBoxColumn)cw_dataGridView.Columns[GetDataGridColumnIdx(_gridHeaderCaptureStr)]).ItemsSource = _captureEventItems;

            cw_dataGridView.ItemsSource = _cwRows;
            _cwRows.Add(new CaptureGridRow());
        }

        // ドロップダウンの列は1回のクリックで編集を始めてプルダウンを開く(WinForms版のCellClickと同じ)
        private void cw_dataGridView_CellClick(object sender, MouseButtonEventArgs e)
        {
            DataGridCell cell = sender as DataGridCell;
            if (cell == null || cell.IsEditing || cell.IsReadOnly || cell.Column == null)
            {
                return;
            }

            int columnIdx = cw_dataGridView.Columns.IndexOf(cell.Column);
            if (columnIdx < 0 || columnIdx >= _dataGridColumns.Length)
            {
                return;
            }

            switch (_dataGridColumns[columnIdx].Type)
            {
                case DataGridType.DropDown:
                    if (!cell.IsFocused)
                    {
                        cell.Focus();
                    }
                    cw_dataGridView.BeginEdit(e);
                    break;

                case DataGridType.CheckBox:
                    // TODO：ダブルクリックで値を設定したい
                    break;

                default:
                    break;
            }
        }

        // 現在行(WinForms版のCurrentRow)の位置。どこも選ばれていなければ先頭行(0)
        // (WinForms版は行が必ずどれか選ばれていた(先頭行)ので、それに合わせる)
        private int GetCurrentGridRowIndex()
        {
            CaptureGridRow current = cw_dataGridView.CurrentItem as CaptureGridRow
                ?? cw_dataGridView.CurrentCell.Item as CaptureGridRow;
            return (current == null) ? 0 : Math.Max(_cwRows.IndexOf(current), 0);
        }

        private void cw_Button_AddLine_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            int insertIdx = Math.Min(GetCurrentGridRowIndex() + 1, _cwRows.Count);

            CaptureGridRow row = new CaptureGridRow();
            row[GetDataGridColumnIdx(_gridHeaderMouseActionStr)] = _mouseEventMoveStr;
            row[GetDataGridColumnIdx(_gridHeaderCaptureStr)] = _notExecuteStr;
            _cwRows.Insert(insertIdx, row);
        }

        private void cw_Button_DelLine_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            if (_cwRows.Count <= 1)
            {
                return;
            }
            _cwRows.RemoveAt(GetCurrentGridRowIndex());
        }

        private void MergeExec_Click(object sender, RoutedEventArgs e)
        {
            MergeExec();
        }

        private void Button_MergeListup_Click(object sender, RoutedEventArgs e)
        {
            ListUpPictMerge();
        }

        private void pm_ListBox_ListUp_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            pm_TextBox_Status.Text = "ファイル数：" + pm_ListBox_ListUp.SelectedItems.Count.ToString();
        }

        // ラベルのダブルクリックで、対応するパス欄の読み取り専用を切り替える
        private void UpdateReadOnly(object sender, MouseButtonEventArgs e)
        {
            TextBox target = GetPathTextBoxOfLabel((sender as FrameworkElement).Name);
            if (target != null)
            {
                target.IsReadOnly = !target.IsReadOnly;
            }
        }

        private TextBox GetPathTextBoxOfLabel(String labelName)
        {
            switch (labelName)
            {
                case "pt_Label_SourceFolderPath": return pt_SourceFolderPath;
                case "pr_Label_SourceFolderPath": return pr_SourceFolderPath;
                case "do_Label_SourceFolderPath": return do_SourceFolderPath;
                case "do_Label_DestPortFolderPath": return do_DestPortFolderPath;
                case "do_Label_DestLandFolderPath": return do_DestLandFolderPath;
                case "pm_Label_SourceFolderPath": return pm_SourceFolderPath;
                case "fc_Label_SourceFolderPath": return fc_SourceFolderPath;
                default: return null;
            }
        }

        // パス欄でEnterを押すと、そのパス(フォルダ/ファイル)を開く
        private void ExecutePath(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _util.ExecutePath((sender as TextBox).Text);
            }
        }
    }
}
