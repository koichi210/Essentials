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
        private const String AppName = "FileArranger";
        private const String SettingFileName = "FileArranger.json";
        private static readonly String[] ProfileExtensions = { "*.json" };

        // ReferenceCandidateFolders(RegisterCtrlを介さない専用の配列)の保存キー。WinForms版と同じ"ReferenceCandidate|Value_"
        private const String ReferenceCandidateAttrName = "ReferenceCandidate";
        private const String ReferenceCandidateAttrValue = "Value_";

        private const int RenameSrcIdx = 0;
        private const int RenameDestIdx = 1;

        private const int PartitionTargetIdx = 0;
        private const int PartitionMoveSrcIdx = 1;
        private const int PartitionMoveDestIdx = 2;

        public String[] ReferenceCandidateFolders { get; set; }     // リファレンス名の候補

        private readonly String userDataFolder;
        private readonly StcFileInputOutput fio = new StcFileInputOutput();
        // FileArranger固有の拡張メソッド(AvoidFolderNameConflict等)を持つUtils(StcUtilsを継承)
        private readonly Utils util = new Utils();
        // フォルダ名変更タブ(rd)の「元に戻す」用の履歴
        private readonly StcProcessMemory renameDirMemory = new StcProcessMemory();
        private readonly FileSorter sorter = new FileSorter();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        private readonly BackgroundWorker bgWorkerMove = new BackgroundWorker { WorkerReportsProgress = true };
        private readonly BackgroundWorker bgPartition = new BackgroundWorker { WorkerReportsProgress = true };

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(AppName))
        {
        }

        // テスト用に、データ保存先を差し替えられるようにしてある(ユーザーの実データフォルダに触れないため)
        internal MainWindow(String dataFolder)
        {
            userDataFolder = dataFolder;

            InitializeComponent();
            util.SetCurrentDirectory();
            InitializeControls();

            //ListView初期設定
            ResizeRenameColumnsEvenly();
            ResizePartitionColumnsEvenly();

            RegisterLoadItem();

            // 起動時は既定の設定ファイル(FileArranger.json)を読む。旧XMLからの移行はWinForms版で済んでいる前提
            SaveRestore.LoadOrDefault(Path.Combine(userDataFolder, SettingFileName));
            RefreshAfterLoad();

            // 一覧の先頭が選ばれ、SelectionChangedでそのプロファイルが読み込まれる(WinForms版と同じ)
            WpfProfile.UpdateProfileList(comboBox_LoadSetting, ProfileExtensions, "", userDataFolder);

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(AppName, userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, AppName)));
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

            bgWorkerMove.DoWork += bgWorkerMove_DoWork;
            bgWorkerMove.ProgressChanged += bgWorkerMove_ProgressChanged;
            bgWorkerMove.RunWorkerCompleted += bgWorkerMove_RunWorkerCompleted;

            bgPartition.DoWork += bgPartition_DoWork;
            bgPartition.ProgressChanged += bgPartition_ProgressChanged;
            bgPartition.RunWorkerCompleted += bgPartition_RunWorkerCompleted;
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
            SaveRestore.RegisterList(ReferenceCandidateAttrName, ReferenceCandidateAttrValue,
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
            if (!SaveRestore.Load(filePath))
            {
                return false;
            }
            RefreshAfterLoad();
            return true;
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
            LoadProfile(Path.Combine(userDataFolder, comboBox_LoadSetting.SelectedItem.ToString()));
        }

        private void SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(fio, comboBox_LoadSetting, ProfileExtensions, SaveProfile, userDataFolder, SettingFileName);
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

        // Enterキーでパスを開く(WinForms版StcUtils.ExecutePath(String, KeyEventArgs)相当)
        private void ExecutePathOnEnter(String execPath, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                util.ExecutePath(execPath);
            }
        }

        private static Boolean IsCtrlEnter(KeyEventArgs e)
        {
            return e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control;
        }

        private void cmn_textBox_AddList_KeyDown(object sender, KeyEventArgs e)
        {
            // WinForms版はCtrl+Aで全選択していた(StcUtils.SelectAll)。WPFのTextBoxはCtrl+Aで標準的に全選択される
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                cmn_textBox_AddList.SelectAll();
                e.Handled = true;
            }
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
        private static String GetDisplayName(String fullPath, String baseFolderPath)
        {
            return fullPath.Remove(0, baseFolderPath.Length + 1);
        }

        // WinForms版のSorted=trueなListView/ListBoxと同じく、表示名の昇順に並べる
        private static String[] SortedByName(IEnumerable<String> names)
        {
            String[] sorted = names.ToArray();
            Array.Sort(sorted, StringComparer.CurrentCulture);
            return sorted;
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
                addReferenceList = addReferenceList.Select(str => cmn_textBox_Reference.Text + @"\" + str + cmn_textBox_AddListSuffix.Text).ToArray();

                ReferenceCandidateFolders = ReferenceCandidateFolders.Concat(addReferenceList).ToArray();
            }

            // コンボボックス更新
            UpdateRenameComboBox();
            UpdateMoveDestDirComboBox();
        }
    }
}
