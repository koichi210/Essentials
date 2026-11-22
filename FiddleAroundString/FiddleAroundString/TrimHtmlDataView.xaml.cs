using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace FiddleAroundString
{
    /// <summary>
    /// 「HTML抽出」タブ: 複数のURLのHTMLを取得し、検索文字を含む行(とそこから指定行数)だけを抜き出す。
    /// 旧TrimHtmlData(CS_Wpf\TrimHtmlData)のMainWindowを、タブに載せるためUserControlにしたもの(処理は変えていない)。
    ///
    /// UserControlになったことで変えたのは次の3点だけ:
    /// ・取得中の表示(旧版はウィンドウのタイトルに直接書いていた)は ShowStatus で親(MainWindow)へ渡す
    /// ・ウィンドウが閉じられたこと(旧版はOnClosed)は、親から NotifyHostClosed で知らせてもらう
    /// ・設定の保存先は、MainWindowが決めたデータ保存先の下の「TrimHtmlData」フォルダ(SetDataFolderで渡される)。
    ///   保存キー・既定の設定ファイル名(TrimHtmlData.json)は旧TrimHtmlDataと同じなので、旧版のJSONプロファイルをそのまま読める
    /// </summary>
    public partial class TrimHtmlDataView : UserControl
    {
        // 既定の設定ファイル名(旧TrimHtmlDataと同じ)
        public const String SettingFileName = "TrimHtmlData.json";

        // WinForms版は旧XML(*.xml)のプロファイルも一覧に出していたが、WpfSaveRestoreはXMLを読めないのでJSONだけにする
        private static readonly String[] _profileExtensions = { "*.json" };

        // 「データ保存先を変更」で切り替わるので readonly にしない。SetDataFolderが呼ばれるまではnull
        private String _userDataFolder;
        private readonly StcUtils _util = new StcUtils();
        private readonly StcFileInputOutput _fio = new StcFileInputOutput();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        // デバッグモード中は、取得したHTMLと抽出結果をexeの場所へ連番ファイルで書き出す(StcDebug)。
        // 画面の余白をダブルクリックするたびに切り替わる(WinForms版と同じ隠し機能)
        internal Boolean IsDebug { get; private set; } = false;

        // URLからHTMLを取得する処理(UIスレッドの外で呼ばれる。失敗時は例外を投げる)と、結果をクリップボードへ送る処理、
        // メッセージを出す処理。テストで実サイトへ通信したり本物のクリップボードを書き換えたり
        // メッセージボックスで止まったりしないよう、差し替えられるようにしてある
        internal Func<String, String> FetchHtmlSource { get; set; }
        internal Func<String, Boolean> CopyToClipboard { get; set; }
        internal Action<String> ShowMessage { get; set; }

        // 取得中の進み具合(例:「HTML取得中 (1/3)」)を親に知らせる処理。取得が終わったらnullで呼ぶ。
        // MainWindowがウィンドウのタイトルに出す(旧TrimHtmlDataと同じくタイトルで進み具合が分かるように)
        internal Action<String> ShowStatus { get; set; }

        // XAML(MainWindowのタブ)から作るとき用。データ保存先は後からSetDataFolderで渡す
        public TrimHtmlDataView()
        {
            FetchHtmlSource = DownloadHtmlSource;
            CopyToClipboard = _util.SetClipboardText;
            ShowMessage = ShowMessageBox;
            ShowStatus = status => { };

            InitializeComponent();

            RegisterSettingItems();
        }

        // テスト用に、データ保存先(このタブの設定フォルダ)を最初から渡せるようにしてある
        internal TrimHtmlDataView(String dataFolder) : this()
        {
            SetDataFolder(dataFolder);
        }

        // 親ウィンドウがあればそれを持ち主にしてメッセージを出す(旧版の MessageBox.Show(this, ...) に合わせる)
        private void ShowMessageBox(String message)
        {
            Window owner = Window.GetWindow(this);
            if (owner != null)
            {
                MessageBox.Show(owner, message);
            }
            else
            {
                MessageBox.Show(message);
            }
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
            // 既定の設定ファイル(TrimHtmlData.json)を読む。無ければ既定値(空欄・チェック無し)
            SaveRestore.LoadOrDefault(Path.Combine(_userDataFolder, SettingFileName));
            // 保存先を切り替えたときに前の保存先の一覧が残らないよう、いったん空にしてから作り直す
            comboBox_LoadSetting.Items.Clear();
            // WinForms版と同じく既定の設定ファイルを選んだ状態にする(SelectionChangedでもう一度読み込まれる)
            WpfProfile.UpdateProfileList(comboBox_LoadSetting, _profileExtensions, SettingFileName, _userDataFolder);
        }

        private void RegisterSettingItems()
        {
            // キーはWinForms版(SaveRestore.RegisterItem)と同じにすること。
            // 「最初にヒットしたワードだけを抽出」はWinForms版でも保存対象外だったので、そのまま登録しない
            // (足すとWinForms版との間で保存内容が食い違うため)。
            // 取得元(textBox_SourceList)・取得値(textBox_DestList)は毎回入れ替える「データ」で設定ではないので、
            // プロファイルの保存・読み込みの対象にしない(WinForms版も同時に外した)。
            // 以前のプロファイルに残っているこの2項目の値は、読み込み時に無視される
            SaveRestore.RegisterCtrl("Common", "textBox_SearchWord", textBox_SearchWord);
            SaveRestore.RegisterCtrl("Common", "textBox_DelimiterWord", textBox_DelimiterWord);
            SaveRestore.RegisterCtrl("Common", "textBox_TrimLineNum", textBox_TrimLineNum);
            SaveRestore.RegisterCtrl("Common", "checkBox_OrdinalCase", checkBox_OrdinalCase);
        }

        private void layoutRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // WinForms版のForm.DoubleClickはコントロールの無い所でしか起きないので、余白(layoutRoot自身)のときだけ反応する。
            // WPFのMouseDoubleClickだとテキストボックス内のダブルクリック(単語選択)でも起きてしまうため使わない
            if (e.ClickCount == 2 && e.OriginalSource == layoutRoot)
            {
                ToggleDebugMode();
                MessageBox.Show("IsDebug=" + IsDebug.ToString());
            }
        }

        internal void ToggleDebugMode()
        {
            IsDebug = !IsDebug;
        }

        // 取得中か(実行ボタンの二重押し防止)と、ウィンドウが閉じられたか。
        // 取得はUIスレッドの外で行うので、取得中に閉じられた後で結果が届いても、
        // 閉じた画面やクリップボードに触らないよう途中で打ち切るために見る
        internal Boolean IsFetching { get; private set; } = false;
        private Boolean _isClosed = false;

        // 親ウィンドウが閉じられたときにMainWindow.OnClosedから呼ばれる(旧版のOnClosedに当たる)
        internal void NotifyHostClosed()
        {
            _isClosed = true;
        }

        private async void button_Execute_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTrimAsync();
        }

        // 実行ボタンの処理本体。URLごとに「◆URL」の見出しと抽出結果を並べて取得値欄に出し、クリップボードにもコピーする。
        // 以前はUIスレッドでそのまま取得していたため、取得が終わるまで画面が固まっていた。
        // 取得(FetchHtmlSource)だけを裏のスレッドに回し、結果の処理はURLごとにUIスレッドへ戻ってから行う
        // (失敗時のメッセージをURLの順に1件ずつ出す、以前の挙動を保つため)
        internal async Task ExecuteTrimAsync()
        {
            if (IsFetching)
            {
                return;
            }

            int trimLineNum;
            if (!TrimHtmlDataLogic.TryGetTrimLine(textBox_TrimLineNum.Text, out trimLineNum))
            {
                ShowMessage(TrimHtmlDataLogic.TrimLineNumErrorMessage);
                textBox_TrimLineNum.Focus();
                textBox_TrimLineNum.SelectAll();
                return;
            }

            // 出力先をクリア
            textBox_DestList.Text = "";

            // Text +=はURLごとにテキストボックス全体を作り直すため、StringBuilderに溜めて最後に1回だけセットする
            StringBuilder destText = new StringBuilder();

            // 取得中も画面は操作できる(設定の読み込みで各欄が書き換わることもある)ので、
            // 処理に使う値は開始時点でまとめて読み取っておく
            String searchWord = textBox_SearchWord.Text;
            StringComparison comparison = (checkBox_OrdinalCase.IsChecked == true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            Boolean firstWordOnly = checkBox_FirstWordOnly.IsChecked == true;

            StcDebug dbg = new StcDebug();
            dbg.IsDebugMode = IsDebug;

            String[] urls = TrimHtmlDataLogic.SplitLines(textBox_SourceList.Text);
            Func<String, String> fetchHtmlSource = FetchHtmlSource;

            SetFetching(true);
            progressBar_Fetch.Maximum = Math.Max(1, urls.Length);
            progressBar_Fetch.Value = 0;
            for (int i = 0; i < urls.Length; i++)
            {
                String url = urls[i];
                destText.Append("◆").Append(url).Append(Environment.NewLine);
                ShowStatus("HTML取得中 (" + (i + 1) + "/" + urls.Length + ")");
                progressBar_Fetch.Value = i;

                String htmlSource = "";
                Boolean isFailed = false;
                try
                {
                    htmlSource = await Task.Run(() => fetchHtmlSource(url));
                }
                catch (Exception)
                {
                    isFailed = true;
                }

                if (_isClosed)
                {
                    return;
                }

                if (isFailed)
                {
                    ShowMessage("ソース取得に失敗しました。" + Environment.NewLine + url);

                    // メッセージを出している間に閉じられることもある
                    if (_isClosed)
                    {
                        return;
                    }
                }

                htmlSource = _util.ChangeNewLineCodeLf2Crlf(htmlSource ?? "");
                dbg.WriteDataInNewFile(htmlSource, "_1_source");

                String result = TrimHtmlDataLogic.GetSearchString(htmlSource, searchWord, trimLineNum, comparison, firstWordOnly);
                dbg.WriteDataInNewFile(result, "_2_search");

                destText.Append(result);
            }
            // 全件終わったらバーを満タンにする(次の実行の開始時に0へ戻す)
            progressBar_Fetch.Value = progressBar_Fetch.Maximum;
            textBox_DestList.Text = destText.ToString();
            SetFetching(false);

            CopyToClipboard(textBox_DestList.Text);
        }

        // 取得中は実行ボタンを押せなくし、ボタンとタイトル(ShowStatus)で取得中であることを示す
        private void SetFetching(Boolean isFetching)
        {
            IsFetching = isFetching;
            button_Execute.IsEnabled = !isFetching;
            button_Execute.Content = isFetching ? "取得中..." : "実行";
            if (!isFetching)
            {
                ShowStatus(null);
            }
        }

        /// <summary>
        /// HTMLのソースを取得する(UTF-8として読む)。UIスレッドの外で呼ばれる。
        /// 失敗時は例外をそのまま投げ、呼び出し元(ExecuteTrimAsync)がメッセージを出す
        /// </summary>
        private static String DownloadHtmlSource(String url)
        {
            // .NET Framework 4.5向けのビルドは既定でTLS1.2を使わないため、今どきのHTTPSサイトにつながらない。
            // 既定の設定に1.2を足してから取得する
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                return client.DownloadString(url);
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
