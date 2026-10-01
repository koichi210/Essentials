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
            EDIT_BOX,
            CHECK_BOX,
            DROP_DOWN,
        };

        private struct DataGridColumnInfo
        {
            public String HeaderName;
            public DataGridType Type;
        }

        private const String GridHeaderSleepStr = "Sleep(msec)";
        private const String GridHeaderMouseXStr = "MouseX";
        private const String GridHeaderMouseYStr = "MouseY";
        private const String GridHeaderMouseActionStr = "MouseAction";
        private const String GridHeaderCaptureStr = "Capture";

        private const String MouseEventMoveStr = "Move";
        private const String MouseEventLeftDownStr = "LeftDown";

        private const String ExecuteStr = "〇";
        private const String NotExecuteStr = "×";

        // 列の並びはCaptureGridRowのプロパティ順と同じ
        private readonly DataGridColumnInfo[] dataGridColumns = new DataGridColumnInfo[]{
            new DataGridColumnInfo() { HeaderName = GridHeaderSleepStr, Type = DataGridType.EDIT_BOX },
            new DataGridColumnInfo() { HeaderName = GridHeaderMouseXStr, Type = DataGridType.EDIT_BOX },
            new DataGridColumnInfo() { HeaderName = GridHeaderMouseYStr, Type = DataGridType.EDIT_BOX },
            new DataGridColumnInfo() { HeaderName = GridHeaderMouseActionStr, Type = DataGridType.DROP_DOWN },
            new DataGridColumnInfo() { HeaderName = GridHeaderCaptureStr, Type = DataGridType.DROP_DOWN }
        };

        private readonly String[] mouseEventItems = new String[] {
            MouseEventMoveStr,
            MouseEventLeftDownStr
        };

        private readonly String[] captureEventItems = new String[] {
            ExecuteStr,
            NotExecuteStr
        };

        // データ保存先はWinForms版Cheetosと同じ(%LOCALAPPDATA%\Cheetos\、exe直下のDataFolder.txtで変更可)。
        // 保存キーも同じにしてあるので、WinForms版で保存したプロファイル(*.json)をそのまま読める
        private const String AppName = "Cheetos";
        private const String SettingFileName = "Cheetos.json";
        private static readonly String[] ProfileExtensions = { "*.json" };

        private readonly String userDataFolder;
        private readonly StcUtils util = new StcUtils();
        private readonly StcFileInputOutput fio = new StcFileInputOutput();
        private readonly StcDebug debug = new StcDebug();
        internal readonly WpfSaveRestore sr = new WpfSaveRestore();
        private bool isCaptureRunning = false;

        // cw_dataGridViewの中身(1要素=1行)
        internal readonly ObservableCollection<CaptureGridRow> cw_Rows = new ObservableCollection<CaptureGridRow>();

        // 各タブのBackgroundWorker(WinForms版はDesignerで配置していたもの)
        private readonly BackgroundWorker bkgWorkerTrim = new BackgroundWorker();
        private readonly BackgroundWorker bkgWorkerMerge = new BackgroundWorker();
        private readonly BackgroundWorker bkgWorkerOrient = new BackgroundWorker();
        private readonly BackgroundWorker bkgWorkerRotation = new BackgroundWorker();

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(AppName))
        {
        }

        // internal: テストでは実際のユーザーデータフォルダではなく一時フォルダを渡す
        internal MainWindow(String dataFolder)
        {
            InitializeComponent();
            userDataFolder = dataFolder;
            util.SetCurrentDirectory();

            // デバッグログに時間を表示
            debug.UseTimeInLog = true;

            InitializeBackgroundWorkers();

            // DataGridの初期設定
            InitializeDataGridView();

            RegisterSaveRestoreItems();
            sr.LoadOrDefault(Path.Combine(userDataFolder, SettingFileName));
            WpfProfile.UpdateProfileList(Profile, ProfileExtensions, "", userDataFolder);

            // CURRENT_SCREENキャプチャで「このウィンドウが今あるモニタ」を判定できるようにする
            SourceInitialized += (s, e) => cw.TargetWindow = WpfUtils.CreateScreenAnchor(this);

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(AppName, userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, AppName)));
        }

        internal void RegisterSaveRestoreItems()
        {
            // キーはWinForms版(SaveRestore.RegisterItem)と同じにすること。
            // 第2引数(設定ファイルのキー名)のtypoはWinForms版で修正済みで、旧キーはLegacyAttrValueで読み替える
            sr.RegisterCtrl("CaptureWindow", "cw_TextBox_SavePath", cw_TextBox_SavePath);
            sr.RegisterCtrl("CaptureWindow", "cw_TextBox_SaveFilePrefix", cw_TextBox_SaveFilePrefix, legacyAttrValue: "cw_TextBox_SaveFilePrifix");
            sr.RegisterCtrl("CaptureWindow", "cw_checkBox_AddTimeStamp", cw_checkBox_AddTimeStamp, legacyAttrValue: "cw_checkBox_AddTimeStump");
            sr.RegisterCtrl("CaptureWindow", "cw_Radio_FullScreen", cw_Radio_FullScreen, "True");
            sr.RegisterCtrl("CaptureWindow", "cw_Radio_CurrentScreen", cw_Radio_CurrentScreen);
            sr.RegisterCtrl("CaptureWindow", "cw_Radio_CurrentWindow", cw_Radio_CurrentWindow);
            sr.RegisterCtrl("CaptureWindow", "cw_TextBox_Sleep", cw_TextBox_Sleep, "2000");
            sr.RegisterCtrl("CaptureWindow", "cw_TextBox_Loop", cw_TextBox_Loop, "2");
            sr.RegisterGrid("DataGrid", "Cell", GetGridRows, SetGridRows);

            sr.RegisterCtrl("PictTrim", "pt_SourceFolderPath", pt_SourceFolderPath);
            sr.RegisterCtrl("PictTrim", "pt_BaseX", pt_BaseX);
            sr.RegisterCtrl("PictTrim", "pt_BaseY", pt_BaseY);
            sr.RegisterCtrl("PictTrim", "pt_Radio_SelectPointOfEnd", pt_Radio_SelectPointOfEnd);
            sr.RegisterCtrl("PictTrim", "pt_Radio_SelectSizeOfEnd", pt_Radio_SelectSizeOfEnd);
            sr.RegisterCtrl("PictTrim", "pt_TargetX", pt_TargetX);
            sr.RegisterCtrl("PictTrim", "pt_TargetY", pt_TargetY);

            sr.RegisterCtrl("Rotation", "pr_SourceFolderPath", pr_SourceFolderPath);
            sr.RegisterCtrl("Rotation", "pr_BaseX", pr_BaseX);
            sr.RegisterCtrl("Rotation", "pr_BaseY", pr_BaseY);
            sr.RegisterCtrl("Rotation", "pr_Angle", pr_Angle);

            sr.RegisterCtrl("DistOrient", "do_SourceFolderPath", do_SourceFolderPath);
            sr.RegisterCtrl("DistOrient", "do_DestPortFolderPath", do_DestPortFolderPath);
            sr.RegisterCtrl("DistOrient", "do_DestLandFolderPath", do_DestLandFolderPath);
            sr.RegisterCtrl("DistOrient", "do_TargetFileName", do_TargetFileName);
            sr.RegisterCtrl("DistOrient", "do_WhiteLength", do_WhiteLength);
            sr.RegisterCtrl("DistOrient", "do_WhiteCoef", do_WhiteCoef, "30");
            sr.RegisterCtrl("DistOrient", "do_SampleFilePath", do_SampleFilePath);

            sr.RegisterCtrl("PictMerge", "pm_SourceFolderPath", pm_SourceFolderPath);
            sr.RegisterCtrl("PictMerge", "pm_SourceFile1Prefix", pm_SourceFile1Prefix);
            sr.RegisterCtrl("PictMerge", "pm_SourceFile2Prefix", pm_SourceFile2Prefix);
            sr.RegisterCtrl("PictMerge", "pm_TrimmingHeight", pm_TrimmingHeight);

            sr.RegisterCtrl("FileCollect", "fc_SourceFolderPath", fc_SourceFolderPath);
            sr.RegisterCtrl("FileCollect", "fc_DestFolderPath", fc_DestFolderPath);
            sr.RegisterCtrl("FileCollect", "fc_TargetFileName", fc_TargetFileName);
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

            return sr.Load(loadFileName);
        }

        // *******************************************************************************
        // DataGrid(cw_dataGridView)の保存/読込。キーはWinForms版と同じ"DataGrid|Cell"、中身は行×列の文字列

        private List<List<String>> GetGridRows()
        {
            CommitGridEdit();
            return cw_Rows.Select(row => row.ToList()).ToList();
        }

        private void SetGridRows(List<List<String>> rows)
        {
            cw_Rows.Clear();
            foreach (List<String> values in rows)
            {
                cw_Rows.Add(CaptureGridRow.FromList(values));
            }

            // WinForms版は保存データが無くても1行は残していた(RowCount = 1)
            if (cw_Rows.Count == 0)
            {
                cw_Rows.Add(new CaptureGridRow());
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
            LoadProfile(Path.Combine(userDataFolder, Profile.SelectedItem.ToString()));
        }

        // プルダウンで既存ファイルが選ばれている時は、毎回ダイアログを開かず
        // 「上書きしますか?」の確認だけで済ませられるようにする(EventRecorderと同じ挙動)
        private void ProfileSave_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(fio, Profile, ProfileExtensions, sr.Save, userDataFolder, SettingFileName);
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
            for (int i = 0; i < dataGridColumns.Length; i++)
            {
                if (columnName.Equals(dataGridColumns[i].HeaderName))
                {
                    return i;
                }
            }
            return 0;
        }

        // "Move"以外(LeftDown・空欄など)はすべて左クリック扱い
        private CaptWindow.MOUSE_EVENT GetMouseEvent(String mouseEventStr)
        {
            if (mouseEventStr == MouseEventMoveStr)
            {
                return CaptWindow.MOUSE_EVENT.MOVE;
            }
            return CaptWindow.MOUSE_EVENT.LEFT_CLICK;
        }

        // "〇"のときだけキャプチャする(空欄・"×"はキャプチャしない)
        private Boolean IsCaptureEvent(String captureEventStr)
        {
            return captureEventStr == ExecuteStr;
        }

        private void fc_Button_Collect_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(fc_SourceFolderPath.Text))
            {
                MessageBox.Show("フォルダパスが不正です");
                return;
            }

            if (!fio.EnsureDirectory(fc_DestFolderPath.Text))
            {
                return;
            }

            // ファイルを一つ一つ移動する
            string[] files = Directory.GetFiles(fc_SourceFolderPath.Text, fc_TargetFileName.Text, SearchOption.TopDirectoryOnly);

            InitProgressBar(files.Length);
            for (int i = 0; i <= files.Length - 1; i++)
            {
                String destPath = fc_DestFolderPath.Text + @"\" + Path.GetFileName(files[i]);
                File.Move(files[i], destPath);

                // 進捗率の表示
                int progress = i + 1;
                TextBox_Status.Text = progress.ToString() + "/" + (int)ProgressBar_Status.Maximum;
                ProgressBar_Status.Value = progress;
            }
        }

        private void label_DebugMode_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            debug.IsDebugMode = !debug.IsDebugMode;
            MessageBox.Show("DebugMode=" + debug.IsDebugMode.ToString());
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

            // 終了時間(一個目)
            DateTime dtEnd = DateTime.Now;

            // 一個分の処理時間
            long procTime = dtEnd.Ticks - dtStart.Ticks;
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
                String filePath = folderPathCtrl.Text + @"\" + name;
                util.ExecutePath(filePath);
            }
        }

        private void InitProgressBar(int maximum)
        {
            ProgressBar_Status.Maximum = maximum;
            ProgressBar_Status.Minimum = 0;
            ProgressBar_Status.Value = 0;
        }

        private void InitializeBackgroundWorkers()
        {
            foreach (BackgroundWorker worker in new[] { bkgWorkerTrim, bkgWorkerMerge, bkgWorkerOrient, bkgWorkerRotation })
            {
                worker.WorkerReportsProgress = true;
                worker.WorkerSupportsCancellation = true;
                worker.ProgressChanged += BkgWorker_ProgressChanged;
            }

            bkgWorkerTrim.DoWork += bkgWorkerTrim_DoWork;
            bkgWorkerTrim.RunWorkerCompleted += bkgWorkerTrim_RunWorkerCompleted;
            bkgWorkerMerge.DoWork += bkgWorkerMerge_DoWork;
            bkgWorkerMerge.RunWorkerCompleted += bkgWorkerMerge_RunWorkerCompleted;
            bkgWorkerOrient.DoWork += bkgWorkerOrient_DoWork;
            bkgWorkerOrient.RunWorkerCompleted += bkgWorkerOrient_RunWorkerCompleted;
            bkgWorkerRotation.DoWork += bkgWorkerRotation_DoWork;
            bkgWorkerRotation.RunWorkerCompleted += bkgWorkerRotation_RunWorkerCompleted;
        }

        // 各タブのBackgroundWorker(Trim/Merge/Rotation/Orient)は進捗表示がまったく同じだったため、
        // 1つのハンドラを4つのProgressChangedから共有する
        private void BkgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // 進捗率の表示
            TextBox_Status.Text = e.ProgressPercentage + "/" + (int)ProgressBar_Status.Maximum;
            ProgressBar_Status.Value = e.ProgressPercentage;

            // 一回目の更新時に、予想終了時間を表示
            if (e.ProgressPercentage == 0)
            {
                SetExpectEndTime((int)ProgressBar_Status.Maximum);
            }
        }

        // 指定フォルダ直下のファイル名をリストボックスへ並べる(Trim/Merge/Rotationの各タブで共通)
        private void ListupFolderFiles(TextBox folderPathCtrl, ListBox listCtrl)
        {
            if (!Directory.Exists(folderPathCtrl.Text))
            {
                MessageBox.Show("フォルダパスが不正です");
                return;
            }

            string[] files = Directory.GetFiles(folderPathCtrl.Text, "*", SearchOption.TopDirectoryOnly);
            WpfUtils.SetSortedItems(listCtrl, files.Select(file => Path.GetFileName(file)).ToArray());
        }

        private void InitializeDataGridView()
        {
            // 左端(行ヘッダ)の非表示・最下部(新規行)の非表示・幅の自動調整はXAML側で設定済み

            // ヘッダ作成とComboBoxのリスト作成
            for (int i = 0; i < dataGridColumns.Length; i++)
            {
                cw_dataGridView.Columns[i].Header = dataGridColumns[i].HeaderName;
            }
            ((DataGridComboBoxColumn)cw_dataGridView.Columns[GetDataGridColumnIdx(GridHeaderMouseActionStr)]).ItemsSource = mouseEventItems;
            ((DataGridComboBoxColumn)cw_dataGridView.Columns[GetDataGridColumnIdx(GridHeaderCaptureStr)]).ItemsSource = captureEventItems;

            cw_dataGridView.ItemsSource = cw_Rows;
            cw_Rows.Add(new CaptureGridRow());
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
            if (columnIdx < 0 || columnIdx >= dataGridColumns.Length)
            {
                return;
            }

            switch (dataGridColumns[columnIdx].Type)
            {
                case DataGridType.DROP_DOWN:
                    if (!cell.IsFocused)
                    {
                        cell.Focus();
                    }
                    cw_dataGridView.BeginEdit(e);
                    break;

                case DataGridType.CHECK_BOX:
                    // TODO：ダブルクリックで値を設定したい
                    break;

                default:
                    break;
            }
        }

        // 現在行(WinForms版のCurrentRow)の位置。どこも選ばれていなければ-1
        private int GetCurrentGridRowIndex()
        {
            CaptureGridRow current = cw_dataGridView.CurrentItem as CaptureGridRow;
            if (current == null && cw_dataGridView.CurrentCell.Item != null)
            {
                current = cw_dataGridView.CurrentCell.Item as CaptureGridRow;
            }
            return (current == null) ? -1 : cw_Rows.IndexOf(current);
        }

        private void cw_Button_AddLine_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            int currentIdx = GetCurrentGridRowIndex();
            if (currentIdx < 0)
            {
                // WinForms版は行が必ずどれか選ばれていた(先頭行)ので、それに合わせる
                currentIdx = 0;
            }
            int insertIdx = Math.Min(currentIdx + 1, cw_Rows.Count);

            CaptureGridRow row = new CaptureGridRow();
            row[GetDataGridColumnIdx(GridHeaderMouseActionStr)] = MouseEventMoveStr;
            row[GetDataGridColumnIdx(GridHeaderCaptureStr)] = NotExecuteStr;
            cw_Rows.Insert(insertIdx, row);
        }

        private void cw_Button_DelLine_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            if (cw_Rows.Count > 1)
            {
                int currentIdx = GetCurrentGridRowIndex();
                if (currentIdx < 0)
                {
                    currentIdx = 0;
                }
                cw_Rows.RemoveAt(currentIdx);
            }
        }

        private void MergeExec_Click(object sender, RoutedEventArgs e)
        {
            MergeExec();
        }

        private void Button_MergeListup_Click(object sender, RoutedEventArgs e)
        {
            ListupPictMerge();
        }

        private void pm_ListBox_ListUp_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            pm_TextBox_Status.Text = "ファイル数：" + pm_ListBox_ListUp.SelectedItems.Count.ToString();
        }

        // ラベルのダブルクリックで、対応するパス欄の読み取り専用を切り替える
        private void UpdateReadOnly(object sender, MouseButtonEventArgs e)
        {
            switch ((sender as FrameworkElement).Name)
            {
                case "pt_Label_SourceFolderPath":
                    pt_SourceFolderPath.IsReadOnly = !pt_SourceFolderPath.IsReadOnly;
                    break;
                case "pr_Label_SourceFolderPath":
                    pr_SourceFolderPath.IsReadOnly = !pr_SourceFolderPath.IsReadOnly;
                    break;
                case "do_Label_SourceFolderPath":
                    do_SourceFolderPath.IsReadOnly = !do_SourceFolderPath.IsReadOnly;
                    break;
                case "do_Label_DestPortFolderPath":
                    do_DestPortFolderPath.IsReadOnly = !do_DestPortFolderPath.IsReadOnly;
                    break;
                case "do_Label_DestLandFolderPath":
                    do_DestLandFolderPath.IsReadOnly = !do_DestLandFolderPath.IsReadOnly;
                    break;
                case "pm_Label_SourceFolderPath":
                    pm_SourceFolderPath.IsReadOnly = !pm_SourceFolderPath.IsReadOnly;
                    break;
                case "fc_Label_SourceFolderPath":
                    fc_SourceFolderPath.IsReadOnly = !fc_SourceFolderPath.IsReadOnly;
                    break;
            }
        }

        // パス欄でEnterを押すと、そのパス(フォルダ/ファイル)を開く
        private void ExecutePath(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                util.ExecutePath((sender as TextBox).Text);
            }
        }
    }
}
