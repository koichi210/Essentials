using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace FileArranger
{
    // WinForms版FileArrangerのForm1.cs + SaveRestore.csに当たる部分。
    // タブごとの処理はWinForms版と同じくpartialファイル(MainWindow.<タブ名>.cs)に分けてある
    public partial class MainWindow : Window
    {
        // データ保存先・設定ファイル名はWinForms版FileArrangerと同じ(%LOCALAPPDATA%\FileArranger\FileArranger.json)。
        // 保存キーも同じにしてあるので、WinForms版で保存したJSONプロファイルをそのまま読める
        private const String _appName = "FileArranger";
        private const String _settingFileName = "FileArranger.json";
        private static readonly String[] _profileExtensions = { "*.json" };

        // ReferenceCandidateFolders(RegisterCtrlを介さない専用の配列)の保存キー。WinForms版と同じ"ReferenceCandidate|Value_"
        private const String _referenceCandidateAttrName = "ReferenceCandidate";
        private const String _referenceCandidateAttrValue = "Value_";
        // 旧キー(typoのまま保存されていたもの)。WinForms版は2026-09のtypo一括修正でキーも"ReferenceCandidate"に
        // 変わったため、それより前に保存されたプロファイルはこちらのキーで入っている。読み込み時だけ読み替える
        private const String _legacyReferenceCandidateAttrName = "RefrenceCandidate";

        private const int _renameSrcIdx = 0;
        private const int _renameDestIdx = 1;

        private const int _partitionTargetIdx = 0;
        private const int _partitionMoveSrcIdx = 1;
        private const int _partitionMoveDestIdx = 2;

        public String[] ReferenceCandidateFolders { get; set; }     // リファレンス名の候補

        private readonly String _userDataFolder;
        private readonly StcFileInputOutput _fio = new StcFileInputOutput();
        // FileArranger固有の拡張メソッド(AvoidFolderNameConflict等)を持つUtils(StcUtilsを継承)
        private readonly Utils _util = new Utils();
        // フォルダ名変更タブ(rd)の「元に戻す」用の履歴
        private readonly StcProcessMemory _renameDirMemory = new StcProcessMemory();
        private readonly FileSorter _sorter = new FileSorter();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        private readonly BackgroundWorker _bgWorkerMove = new BackgroundWorker { WorkerReportsProgress = true };
        private readonly BackgroundWorker _bgPartition = new BackgroundWorker { WorkerReportsProgress = true };

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(_appName))
        {
        }

        // テスト用に、データ保存先を差し替えられるようにしてある(ユーザーの実データフォルダに触れないため)
        internal MainWindow(String dataFolder)
        {
            _userDataFolder = dataFolder;

            InitializeComponent();
            _util.SetCurrentDirectory();
            InitializeControls();

            //ListView初期設定
            ResizeRenameColumnsEvenly();
            ResizePartitionColumnsEvenly();

            RegisterLoadItem();

            // 起動時は既定の設定ファイル(FileArranger.json)を読む。旧XMLからの移行はWinForms版で済んでいる前提
            SaveRestore.ApplyGenericProfile(LoadGenericProfile(Path.Combine(_userDataFolder, _settingFileName)));
            RefreshAfterLoad();

            // 一覧の先頭が選ばれ、SelectionChangedでそのプロファイルが読み込まれる(WinForms版と同じ)
            WpfProfile.UpdateProfileList(comboBox_LoadSetting, _profileExtensions, "", _userDataFolder);

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, _appName)));
        }

        private void InitializeControls()
        {
            // WinForms版でSorted=trueだったComboBoxは、項目を入れ替えるときに昇順に並べる
            // (ListBox/ListViewはリストアップ時に並べ替えてから追加している)
            WpfControlHelper.SetSorted(rd_comboBox_MergeWord, true);
            WpfControlHelper.SetSorted(rd_comboBox_AddTitlePostWord, true);
            WpfControlHelper.SetSorted(pf_comboBox_MoveDestDirName, true);

            // WPFのComboBoxにはTextChangedが無いので、Textプロパティの変更を監視する
            // (WinForms版はrd_comboBox_MergeWord/rd_comboBox_AddTitlePostWordのTextChangedにつないでいた)
            DependencyPropertyDescriptor comboText = DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox));
            comboText.AddValueChanged(rd_comboBox_MergeWord, rd_comboBox_TextChanged);
            comboText.AddValueChanged(rd_comboBox_AddTitlePostWord, rd_comboBox_TextChanged);

            _bgWorkerMove.DoWork += bgWorkerMove_DoWork;
            _bgWorkerMove.ProgressChanged += bgWorker_ProgressChanged;
            _bgWorkerMove.RunWorkerCompleted += bgWorkerMove_RunWorkerCompleted;

            _bgPartition.DoWork += bgPartition_DoWork;
            _bgPartition.ProgressChanged += bgWorker_ProgressChanged;
            _bgPartition.RunWorkerCompleted += bgPartition_RunWorkerCompleted;
        }

        // *******************************************************************************
        // 設定の保存/読込(WinForms版SaveRestore.cs)

        internal void RegisterLoadItem()
        {
            // キーはWinForms版(SaveRestore.RegisterLoadItem)と同じにすること。
            // 第2引数(キー名)にtypoが残っているもの・タブと合っていないもの(rd_comboBox_RenameDirが"MoveDir")があるが、
            // 直すと既存の設定ファイルの値が読めなくなるため、WinForms版のまま
            SaveRestore.RegisterCtrl("Common", "cmn_textBox_Reference", cmn_textBox_Reference);
            SaveRestore.RegisterCtrl("Common", "cmn_textBox_AddList", cmn_textBox_AddList);
            SaveRestore.RegisterCtrl("Common", "cmn_textBox_AddListSuffix", cmn_textBox_AddListSuffix);

            SaveRestore.RegisterCtrl("MoveDir", "md_textBox_SourceDir", md_textBox_SourceDir);
            RegisterComboHistory("MoveDir", "md_comboBox_TargetDir", md_comboBox_TargetDir);
            SaveRestore.RegisterCtrl("MoveDir", "md_comboBox_TargetDir", md_comboBox_TargetDir);

            RegisterComboHistory("MoveDir", "rd_comboBox_RenameDir", rd_comboBox_RenameDir);
            SaveRestore.RegisterCtrl("MoveDir", "rd_comboBox_RenameDir", rd_comboBox_RenameDir);
            SaveRestore.RegisterCtrl("RenameDir", "rd_textBox_ExistItemDir", rd_textBox_ExistItemDir);
            SaveRestore.RegisterCtrl("RenameDir", "rd_comboBox_MergeWord", rd_comboBox_MergeWord);
            SaveRestore.RegisterCtrl("RenameDir", "rd_checkBox_FileOpen", rd_checkBox_FileOpen);
            SaveRestore.RegisterCtrl("RenameDir", "rd_textBox_SplitWord3", rd_textBox_SplitWord3);
            SaveRestore.RegisterCtrl("RenameDir", "rd_textBox_AddTitlePreWord", rd_textBox_AddTitlePreWord);
            SaveRestore.RegisterCtrl("RenameDir", "rd_textBox_SearchTitleLine", rd_textBox_SearchTitleLine);
            SaveRestore.RegisterCtrl("RenameDir", "rd_textBox_SearchTitleLength", rd_textBox_SearchTitleLength);
            RegisterComboHistory("RenameDir", "rd_comboBox_AddTitlePostWord", rd_comboBox_AddTitlePostWord);
            SaveRestore.RegisterCtrl("RenameDir", "rd_comboBox_AddTitlePostWord", rd_comboBox_AddTitlePostWord);

            SaveRestore.RegisterCtrl("SortFileName", "sf_textBox_TargetFile", sf_textBox_TargetFile);

            SaveRestore.RegisterCtrl("MoveFile", "mf_textBox_SourceDir", mf_textBox_SourceDir);
            SaveRestore.RegisterCtrl("MoveFile", "mf_textBox_TargetDir", mf_textBox_TargetDir);

            SaveRestore.RegisterCtrl("PartitionFile", "pf_textBox_TargetFile", pf_textBox_TargetFile);
            SaveRestore.RegisterCtrl("PartitionFile", "pf_textBox_ReferenceFile", pf_textBox_ReferenceFile, legacyAttrValue: "pf_textBox_RefrenceFile");
            SaveRestore.RegisterCtrl("PartitionFile", "pf_textBox_TargetSeparator", pf_textBox_TargetSeparator, legacyAttrValue: "pf_textBox_TargetSeprator");
            SaveRestore.RegisterCtrl("PartitionFile", "pf_textBox_SearchTitleLine", pf_textBox_SearchTitleLine);
            SaveRestore.RegisterCtrl("PartitionFile", "pf_textBox_SearchTitleLength", pf_textBox_SearchTitleLength);
            SaveRestore.RegisterCtrl("PartitionFile", "pf_checkBox_CreateNewDir", pf_checkBox_CreateNewDir);

            // WinForms版SaveJsonFile/LoadJsonFileが"ReferenceCandidate|Value_"というキーでprofileに相乗りさせていた配列
            SaveRestore.RegisterList(_referenceCandidateAttrName, _referenceCandidateAttrValue,
                () => (ReferenceCandidateFolders ?? new String[0]).ToList(),
                items => ReferenceCandidateFolders = items.ToArray());
        }

        // ComboBoxの項目一覧(履歴)を保存する(WinForms版のRegisterCtrlList相当)。
        // WpfSaveRestore.RegisterCtrlListは値(Text)を戻した後にItems.Clearするため、入力可能なComboBoxでは
        // 戻したTextが消えてしまうことがある。入れ替え時にTextを残すよう、ここで登録している
        private void RegisterComboHistory(String attrName, String attrValue, ComboBox ctrl)
        {
            SaveRestore.RegisterList(attrName, attrValue,
                () => WpfControlHelper.GetItems(ctrl),
                items => WpfControlHelper.SetItemsKeepText(ctrl, items));
        }

        // 設定ファイルを読み込む(WinForms版LoadJsonFile)。読み込めたら、コンボボックス更新・リストリセットも行う
        internal Boolean LoadProfile(String filePath)
        {
            GenericProfile profile = LoadGenericProfile(filePath);
            if (profile == null)
            {
                return false;
            }
            SaveRestore.ApplyGenericProfile(profile);
            RefreshAfterLoad();
            return true;
        }

        // 設定ファイルを読む(無い・壊れている場合はnull)。WpfSaveRestore.Load/LoadOrDefaultと同じ読み方だが、
        // RegisterListには旧キーの読み替え(legacyAttrValue)が無いので、参照候補フォルダの旧キーだけここで読み替える
        private static GenericProfile LoadGenericProfile(String filePath)
        {
            GenericProfile profile = JsonFileStorage.Load<GenericProfile>(filePath);
            if (profile == null)
            {
                return null;
            }

            String key = GenericProfile.MakeKey(_referenceCandidateAttrName, _referenceCandidateAttrValue);
            String legacyKey = GenericProfile.MakeKey(_legacyReferenceCandidateAttrName, _referenceCandidateAttrValue);
            List<String> legacyItems;
            if (!profile.Lists.ContainsKey(key) && profile.Lists.TryGetValue(legacyKey, out legacyItems))
            {
                profile.Lists[key] = legacyItems;
            }
            return profile;
        }

        // WinForms版LoadJsonFile/LoadProcの後処理
        private void RefreshAfterLoad()
        {
            // コンボボックス更新
            UpdateRenameComboBox();
            UpdateMoveDestDirComboBox();

            // リストをリセット
            sf_listBox_Target.Items.Clear();
            rd_listView_Target.Items.Clear();
            pf_listView_Target.Items.Clear();
        }

        // 設定ファイルを保存する(WinForms版SaveJsonFile)。コンボボックスの入力値を履歴へ追加してから保存する
        internal Boolean SaveProfile(String filePath)
        {
            WpfControlHelper.AddComboBoxTextToItems(md_comboBox_TargetDir);
            WpfControlHelper.AddComboBoxTextToItems(rd_comboBox_RenameDir);
            WpfControlHelper.AddComboBoxTextToItems(rd_comboBox_AddTitlePostWord);

            return SaveRestore.Save(filePath);
        }

        private void comboBox_LoadSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChangedの時点ではTextがまだ古いので、SelectedItemから読む
            if (comboBox_LoadSetting.SelectedItem == null)
            {
                return;
            }
            LoadProfile(Path.Combine(_userDataFolder, comboBox_LoadSetting.SelectedItem.ToString()));
        }

        private void SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(_fio, comboBox_LoadSetting, _profileExtensions, SaveProfile, _userDataFolder, _settingFileName);
        }

        // Ctrl+Sで「設定値保存」ボタンと同じ動作にする(テキストボックス等にフォーカスがあっても拾える)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                SaveSetting_Click(this, new RoutedEventArgs());
            }
        }

        // *******************************************************************************
        // 共通

        // パス入力欄(TextBox/ComboBox)でEnterを押したら、入力中のパスを開く
        // (WinForms版StcUtils.ExecutePath(String, KeyEventArgs)相当。各タブのフォルダ入力欄で共通のハンドラ)
        private void PathInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            String path = sender is ComboBox comboBox ? comboBox.Text : ((TextBox)sender).Text;
            _util.ExecutePath(path);
        }

        // Ctrl+Aで全選択(複数行テキスト・リストボックスで共通のハンドラ)
        private void SelectAll_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.A || Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
            else
            {
                ((ListBox)sender).SelectAll();
            }
            e.Handled = true;
        }

        private static Boolean IsCtrlEnter(KeyEventArgs e)
        {
            return e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control;
        }

        // リストボックスのキー操作: Enterで実行、Ctrl+Aで全選択(md/sfタブで共通)
        private static void HandleListBoxKeyDown(ListBox listBox, KeyEventArgs e, Action executeOnEnter)
        {
            if (e.Key == Key.Enter)
            {
                executeOnEnter();
            }
            else
            {
                WpfControlHelper.SelectAll(listBox, e);
            }
        }

        // 選択項目が無ければメッセージを出してfalseを返す(各タブの実行ボタンで共通)
        private static Boolean HasSelectedItems(int selectedCount)
        {
            if (selectedCount > 0)
            {
                return true;
            }

            MessageBox.Show("項目が選択されていません。");
            return false;
        }

        private static String FormatSelectedCount(int selectedCount)
        {
            return "選択数：" + selectedCount.ToString();
        }

        // リストアップ前のフォルダ確認(showErrorPopup=falseなら、無効でもメッセージを出さずに中断する)
        private static Boolean IsValidFolderPath(String folderPath, Boolean showErrorPopup = true)
        {
            if (Directory.Exists(folderPath))
            {
                return true;
            }

            if (showErrorPopup)
            {
                MessageBox.Show("フォルダパスが不正です。" + folderPath);
            }
            return false;
        }

        // フルパスから基準フォルダの分を取り除いて、表示用の名前にする
        // (基準フォルダの末尾に「\」が付いていると名前の先頭1文字まで削れていたため、末尾の区切りは除いて数える)
        private static String GetDisplayName(String fullPath, String baseFolderPath)
        {
            return fullPath.Remove(0, baseFolderPath.TrimEnd('\\', '/').Length + 1);
        }

        // フルパスの一覧を表示用の名前にし、WinForms版のSorted=trueなListView/ListBoxと同じく昇順に並べる
        private static String[] GetSortedDisplayNames(IEnumerable<String> fullPaths, String baseFolderPath)
        {
            String[] sorted = fullPaths.Select(path => GetDisplayName(path, baseFolderPath)).ToArray();
            Array.Sort(sorted, StringComparer.CurrentCulture);
            return sorted;
        }

        // 進捗バーを0/maxに戻す(ファイル移動・フォルダ振り分けの開始時)
        private void ResetProgress(int max)
        {
            progressBar.Maximum = max;
            progressBar.Minimum = 0;
            progressBar.Value = 0;
        }

        // リストアップ処理の共通部分。フォルダを確認し、getPathsで集めたパスを表示用の名前にして昇順で一覧に並べ、
        // 件数を表示する。makeItemで一覧に入れる項目を作る(省略時は名前そのもの)。フォルダが無効ならnullを返す
        private static String[] ListupInto(ItemsControl listCtrl, TextBlock totalLabel, String countName,
            String folderPath, Func<String, IEnumerable<String>> getPaths,
            Func<String, Object> makeItem = null, Boolean showErrorPopup = true)
        {
            if (!IsValidFolderPath(folderPath, showErrorPopup))
            {
                return null;
            }

            String[] names = GetSortedDisplayNames(getPaths(folderPath), folderPath);
            WpfControlHelper.SetListItems(listCtrl, names.Select(makeItem ?? (name => name)));
            totalLabel.Text = countName + "：" + names.Length;
            return names;
        }

        // 進捗の表示(mf/pfタブのBackgroundWorkerで共通のハンドラ。ProgressPercentageには完了件数を入れている)
        private void bgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            int done = e.ProgressPercentage;
            progressText.Text = done + "/" + progressBar.Maximum + " 完了";
            progressBar.Value = done;
        }

        // BackgroundWorker完了時のキャンセル/エラー表示(mf/pfタブで共通)。正常に終わっていればtrue
        private static Boolean IsWorkerCompletedNormally(RunWorkerCompletedEventArgs e, String errorMessage)
        {
            if (e.Cancelled)
            {
                // この場合はe.Resultにはアクセスできない
                MessageBox.Show("キャンセルされました");
                return false;
            }

            if (e.Error != null)
            {
                MessageBox.Show(errorMessage + Environment.NewLine + e.Error.Message);
                return false;
            }

            return true;
        }

        private void cmn_textBox_Reference_TextChanged(object sender, TextChangedEventArgs e)
        {
            rd_textBox_ExistItemDir.Text = cmn_textBox_Reference.Text;
            pf_textBox_ReferenceFile.Text = cmn_textBox_Reference.Text;
        }

        private void cmn_button_Listup_Click(object sender, RoutedEventArgs e)
        {
            if (!IsValidFolderPath(cmn_textBox_Reference.Text))
            {
                return;
            }

            // フォルダをリストアップ
            ReferenceCandidateFolders = Directory.GetDirectories(cmn_textBox_Reference.Text);

            // 新規追加
            if (!cmn_textBox_AddList.Text.Equals(String.Empty))
            {
                String[] addReferenceList = cmn_textBox_AddList.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                Logic.DeleteDuplicate(ReferenceCandidateFolders, ref addReferenceList, rd_textBox_SplitWord3.Text);
                addReferenceList = addReferenceList.Select(str => Path.Combine(cmn_textBox_Reference.Text, str + cmn_textBox_AddListSuffix.Text)).ToArray();

                ReferenceCandidateFolders = ReferenceCandidateFolders.Concat(addReferenceList).ToArray();
            }

            // コンボボックス更新
            UpdateRenameComboBox();
            UpdateMoveDestDirComboBox();
        }
    }
}
