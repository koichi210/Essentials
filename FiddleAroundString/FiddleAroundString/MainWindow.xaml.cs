using System;
using System.IO;
using System.Windows;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace FiddleAroundString
{
    /// <summary>
    /// 文字列いじりの4ツール(旧ToyingFile・ToyingData・TrimFileData・TrimHtmlData)をタブで切り替える画面。
    /// 各タブの中身(ToyingFileView等)は旧アプリのMainWindowをUserControlにしたもので、
    /// ここではデータ保存先の決定・「データ保存先を変更」メニュー・タイトルの表示・閉じたことの通知だけを受け持つ。
    ///
    /// データ保存先は %LOCALAPPDATA%\FiddleAroundString\ (exeの横のDataFolder.txtで変更可)。
    /// 設定の保存/復元があるタブ(ファイル抽出・HTML抽出)は、その下のサブフォルダに旧アプリと同じ名前で保存する:
    ///   &lt;データ保存先&gt;\TrimFileData\TrimFileData.json
    ///   &lt;データ保存先&gt;\TrimHtmlData\TrimHtmlData.json
    /// 案内板(DataFolder.txt)はexe1つにつき1つなので、タブごとに保存先を分けず、1つの保存先の中でフォルダを分けている。
    /// </summary>
    public partial class MainWindow : Window
    {
        private const String _appName = "FiddleAroundString";

        // 設定を持つタブの、データ保存先の下のサブフォルダ名(旧アプリ名と同じ)
        public const String TrimFileDataFolderName = "TrimFileData";
        public const String TrimHtmlDataFolderName = "TrimHtmlData";

        // 「データ保存先を変更」で切り替わるので readonly にしない
        private String _userDataFolder;
        private readonly StcUtils _util = new StcUtils();

        public MainWindow() : this(UserDataLocation.GetUserDataFolder(_appName), true)
        {
        }

        // テスト用に、データ保存先を差し替えられるようにしてある(ユーザーの実データフォルダに触れないため)。
        // importLegacySettings=trueなら、旧TrimFileData・旧TrimHtmlDataの既定の保存先にある設定を初回だけコピーする
        // (テストでは本物の%LOCALAPPDATA%を見ないようfalseにする)
        internal MainWindow(String dataFolder, Boolean importLegacySettings = false)
        {
            _userDataFolder = dataFolder;
            if (importLegacySettings)
            {
                ImportLegacySettings();
            }

            InitializeComponent();
            // リファレンスファイルパスに相対パスを書いた場合や、HTML抽出のデバッグ出力の書き出し先をexeの場所にする
            // (旧TrimFileData・旧TrimHtmlDataのコンストラクタでやっていたもの)
            _util.SetCurrentDirectory();

            trimHtmlDataView.ShowStatus = ShowFetchStatus;

            ApplyDataFolderToViews();

            // 保存先を変更したら、再起動しなくても新しい保存先のプロファイルを読み込み直す。
            // サブフォルダ(TrimFileData・TrimHtmlData)の中のプロファイルも、フォルダ構成を保ったまま引っ越す
            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, _appName),
                newFolder =>
                {
                    _userDataFolder = newFolder;
                    ApplyDataFolderToViews();
                }));
        }

        // 旧アプリの既定の保存先から、まだ無い各タブの設定フォルダへ設定をコピーする(初回だけ。詳しくはLegacySettingImporter)
        private void ImportLegacySettings()
        {
            LegacySettingImporter.ImportIfFirstRun(LegacySettingImporter.GetLegacyDefaultFolder("TrimFileData"),
                Path.Combine(_userDataFolder, TrimFileDataFolderName));
            LegacySettingImporter.ImportIfFirstRun(LegacySettingImporter.GetLegacyDefaultFolder("TrimHtmlData"),
                Path.Combine(_userDataFolder, TrimHtmlDataFolderName));
        }

        // 設定を持つタブに、それぞれのサブフォルダを渡して設定を読み込み直させる(起動時と保存先変更時)
        private void ApplyDataFolderToViews()
        {
            trimFileDataView.SetDataFolder(Path.Combine(_userDataFolder, TrimFileDataFolderName));
            trimHtmlDataView.SetDataFolder(Path.Combine(_userDataFolder, TrimHtmlDataFolderName));
        }

        // HTML抽出タブの取得中の進み具合をタイトルに出す(nullなら元のタイトルに戻す)。
        // 旧TrimHtmlDataが「TrimHtmlData - 取得中 (1/3)」とタイトルに出していたのに合わせたもの
        internal void ShowFetchStatus(String status)
        {
            Title = (status == null) ? _appName : _appName + " - " + status;
        }

        protected override void OnClosed(EventArgs e)
        {
            // HTML抽出タブが取得中なら、閉じた後に結果が届いても画面やクリップボードに触らないようにする
            trimHtmlDataView.NotifyHostClosed();
            base.OnClosed(e);
        }
    }
}
