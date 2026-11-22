using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace FiddleAroundString
{
    /// <summary>
    /// 「ファイル抽出」タブ: リファレンスファイル(テキスト)から、検索ワードを含む行だけを抜き出す。
    /// 旧TrimFileData(CS_Wpf\TrimFileData)のMainWindowを、タブに載せるためUserControlにしたもの(処理は変えていない)。
    ///
    /// 設定の保存先は、MainWindowが決めたデータ保存先の下の「TrimFileData」フォルダ(SetDataFolderで渡される)。
    /// 保存キー・既定の設定ファイル名(TrimFileData.json)は旧TrimFileDataと同じなので、旧版のJSONプロファイルをそのまま読める。
    /// </summary>
    public partial class TrimFileDataView : UserControl
    {
        // 既定の設定ファイル名(旧TrimFileDataと同じ)
        public const String SettingFileName = "TrimFileData.json";

        // WinForms版は旧XML(*.xml)のプロファイルも一覧に出していたが、WpfSaveRestoreはXMLを読めないのでJSONだけにする
        private static readonly String[] _profileExtensions = { "*.json" };

        // 「データ保存先を変更」で切り替わるので readonly にしない。SetDataFolderが呼ばれるまではnull
        private String _userDataFolder;
        private readonly StcUtils _util = new StcUtils();
        private readonly StcFileInputOutput _fio = new StcFileInputOutput();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        // 検索結果をクリップボードへ送る処理。テストで本物のクリップボードを書き換えないよう差し替えられるようにしてある
        internal Func<String, Boolean> CopyToClipboard { get; set; }

        // XAML(MainWindowのタブ)から作るとき用。データ保存先は後からSetDataFolderで渡す
        public TrimFileDataView()
        {
            CopyToClipboard = _util.SetClipboardText;

            InitializeComponent();

            RegisterSettingItems();
        }

        // テスト用に、データ保存先(このタブの設定フォルダ)を最初から渡せるようにしてある
        internal TrimFileDataView(String dataFolder) : this()
        {
            SetDataFolder(dataFolder);
        }

        // このタブの設定フォルダを決めて(無ければ作る)、既定の設定ファイルとプロファイル一覧を読み込み直す。
        // 起動時と「データ保存先を変更」の後にMainWindowから呼ばれる
        internal void SetDataFolder(String dataFolder)
        {
            _userDataFolder = dataFolder;
            try
            {
                Directory.CreateDirectory(dataFolder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 作れなくても起動はできるようにする(プロファイル一覧が空になるだけ。保存時にダイアログで場所を選べる)
            }
            LoadProfilesFromDataFolder();
        }

        // 現在のデータ保存先から、既定の設定ファイルとプロファイル一覧を読み込む
        private void LoadProfilesFromDataFolder()
        {
            // 既定の設定ファイル(TrimFileData.json)を読む。無ければ既定値(空欄・チェック無し)
            SaveRestore.LoadOrDefault(Path.Combine(_userDataFolder, SettingFileName));
            // 保存先を切り替えたときに前の保存先の一覧が残らないよう、いったん空にしてから作り直す
            comboBox_LoadSetting.Items.Clear();
            // WinForms版と同じく既定の設定ファイルを選んだ状態にする(SelectionChangedでもう一度読み込まれる)
            WpfProfile.UpdateProfileList(comboBox_LoadSetting, _profileExtensions, SettingFileName, _userDataFolder);
        }

        private void RegisterSettingItems()
        {
            // キーはWinForms版(SaveRestore.RegisterItem)と同じにすること。
            // typo修正前の旧キー(Serch)で保存された設定ファイルも、legacyAttrValueで読み替えて読み込む
            SaveRestore.RegisterCtrl("Common", "textBox_ReferencePath", textBox_ReferencePath);
            SaveRestore.RegisterCtrl("Common", "textBox_SearchCommonWord", textBox_SearchCommonWord, legacyAttrValue: "textBox_SerchCommonWord");
            SaveRestore.RegisterCtrl("Common", "checkBox_FirstWordOnly", checkBox_FirstWordOnly);
            SaveRestore.RegisterCtrl("Common", "checkBox_OrdinalCase", checkBox_OrdinalCase);
            SaveRestore.RegisterCtrl("Common", "checkBox_IgnoreWidth", checkBox_IgnoreWidth);
            SaveRestore.RegisterCtrl("Common", "textBox_SearchWordList", textBox_SearchWordList, legacyAttrValue: "textBox_SerchWordList");
            SaveRestore.RegisterCtrl("Common", "textBox_SearchResultList", textBox_SearchResultList, legacyAttrValue: "textBox_SerchResultList");
        }

        private void button_Execute_Click(object sender, RoutedEventArgs e)
        {
            ExecuteSearch();
        }

        // 実行ボタンの処理本体。成功したらtrue(リファレンスファイルが開けなければメッセージを出してfalse)
        internal Boolean ExecuteSearch()
        {
            // 出力先をクリア
            textBox_SearchResultList.Text = "";

            String referData = _fio.LoadFile(textBox_ReferencePath.Text);
            if (referData == String.Empty)
            {
                MessageBox.Show("リファレンスファイルが開けません。" + Environment.NewLine + textBox_ReferencePath.Text);
                return false;
            }

            // 検索ワードをリストアップ
            // (LFだけの文字列を貼り付けた場合やLF改行のファイルでも行に分かれるよう、改行コードを問わず分ける)
            String[] searchWordLines = TrimFileDataLogic.SplitLines(textBox_SearchWordList.Text);

            // リファレンスをリスト化
            String[] referLines = TrimFileDataLogic.SplitLines(referData);

            // 検索結果をコントロールにセット
            textBox_SearchResultList.Text = TrimFileDataLogic.GetSearchData(searchWordLines, referLines,
                checkBox_OrdinalCase.IsChecked == true, checkBox_FirstWordOnly.IsChecked == true, textBox_SearchCommonWord.Text,
                checkBox_IgnoreWidth.IsChecked == true);
            CopyToClipboard(textBox_SearchResultList.Text);
            return true;
        }

        private void textBox_ReferencePath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _util.ExecutePath(textBox_ReferencePath.Text);
            }
        }

        private void comboBox_LoadSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChangedの時点ではTextがまだ古いので、SelectedItemから読む
            if (comboBox_LoadSetting.SelectedItem == null || _userDataFolder == null)
            {
                return;
            }
            SaveRestore.Load(Path.Combine(_userDataFolder, comboBox_LoadSetting.SelectedItem.ToString()));
        }

        private void button_SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(_fio, comboBox_LoadSetting, _profileExtensions, SaveRestore.Save, _userDataFolder, SettingFileName);
        }
    }
}
