using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// TrimHtmlDataView（「HTML抽出」タブ。URLのHTMLから検索文字を含む行を取り出す）を実際に生成して確かめるテスト。
    /// 旧TrimHtmlDataのMainWindowTestsを移植したもの。
    /// HTML取得は差し替えて実サイトへは通信せず、クリップボードへのコピーもテスト用の処理に差し替える。
    /// データ保存先は一時フォルダに差し替え、Close()も呼ばない(本物の設定ファイルを書き換えないため)。
    /// 例外は「取得中に閉じる」テストだけで、そこではタブを載せたMainWindowを画面外に表示してからClose()する(保存先は一時フォルダのまま)。
    ///
    /// ⚠️ 余白のダブルクリック(デバッグ切替)と設定保存ボタンは MessageBox / 保存ダイアログが出るため、
    /// 切替そのもの(ToggleDebugMode)だけを確かめる。
    /// </summary>
    [TestClass]
    public class TrimHtmlDataViewTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "TrimHtmlDataTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }

        // 取得は裏のスレッドで行われ、結果の処理はUIスレッド(Dispatcher)に戻ってくるので、
        // テストのSTAスレッドでDispatcherを回しながら条件が整うまで待つ
        private static void PumpUntil(Func<bool> condition, string message)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > 10000)
                {
                    Assert.Fail(message);
                }
                DispatcherFrame frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }
        }

        // awaitの続きをこのSTAスレッドのDispatcherへ戻すため、同期コンテキストを設定してから実行する
        // (アプリではDispatcherが回っているので自動で設定されているが、テストのスレッドでは設定されていない)
        private static Task StartExecute(TrimHtmlDataView window)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            return window.ExecuteTrimAsync();
        }

        private static void Execute(TrimHtmlDataView window)
        {
            Task task = StartExecute(window);
            PumpUntil(() => task.IsCompleted, "実行が終わらない");
            task.GetAwaiter().GetResult();
        }

        [TestMethod]
        public void 実行でURLごとに見出しと該当行を並べクリップボードにも送る()
        {
            var pages = new Dictionary<string, string>
            {
                { "https://a.example/", "<html>\r\n<title>A page</title>\r\n<body>a</body>" },
                { "https://b.example/", "<html>\r\n<title>B page</title>\r\n<body>b</body>" },
            };

            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var fetched = new List<string>();
                string copied = null;
                window.FetchHtmlSource = url => { fetched.Add(url); return pages[url]; };
                window.CopyToClipboard = text => { copied = text; return true; };

                // 空行は無視される
                window.textBox_SourceList.Text = "https://a.example/\r\n\r\nhttps://b.example/";
                window.textBox_SearchWord.Text = "<title>";
                window.textBox_TrimLineNum.Text = "2";

                Execute(window);

                CollectionAssert.AreEqual(new[] { "https://a.example/", "https://b.example/" }, fetched);
                string expected =
                    "◆https://a.example/\r\n<title>A page</title>\r\n<body>a</body>\r\n\r\n\r\n" +
                    "◆https://b.example/\r\n<title>B page</title>\r\n<body>b</body>\r\n\r\n\r\n";
                Assert.AreEqual(expected, window.textBox_DestList.Text);
                Assert.AreEqual(expected, copied);
            });
        }

        [TestMethod]
        public void 取得中はプログレスバーが進み終わると満タンになる()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var progressDuringFetch = new List<double>();
                // 取得はUIスレッドの外で呼ばれるので、バーの値はDispatcher経由で読む
                window.FetchHtmlSource = url =>
                {
                    progressDuringFetch.Add((double)window.Dispatcher.Invoke(new Func<object>(() => window.progressBar_Fetch.Value)));
                    return "<title>x</title>";
                };
                window.CopyToClipboard = text => true;

                window.textBox_SourceList.Text = "https://a.example/\r\nhttps://b.example/\r\nhttps://c.example/";
                window.textBox_SearchWord.Text = "<title>";

                Execute(window);

                CollectionAssert.AreEqual(new[] { 0.0, 1.0, 2.0 }, progressDuringFetch, "取得し終えたURLの数だけ進むはず");
                Assert.AreEqual(3.0, window.progressBar_Fetch.Maximum);
                Assert.AreEqual(window.progressBar_Fetch.Maximum, window.progressBar_Fetch.Value);
            });
        }

        [TestMethod]
        public void 大文字小文字の区別と最初のヒットだけのチェックが反映される()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                window.FetchHtmlSource = url => "<TITLE>upper</TITLE>\r\n<title>one</title>\r\n<title>two</title>";
                window.CopyToClipboard = text => true;

                window.textBox_SourceList.Text = "https://a.example/";
                window.textBox_SearchWord.Text = "<title>";
                window.checkBox_OrdinalCase.IsChecked = true;
                window.checkBox_FirstWordOnly.IsChecked = true;

                Execute(window);

                Assert.AreEqual("◆https://a.example/\r\n<title>one</title>\r\n\r\n", window.textBox_DestList.Text);
            });
        }

        [TestMethod]
        public void 取得中は実行ボタンが押せず取得中と表示され終わると元に戻る()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                int fetchCount = 0;
                Boolean fetchStarted = false;
                // 旧版はウィンドウのタイトルに直接書いていた進み具合を、ShowStatusで親へ渡すようにした
                string status = "未設定";
                using (var release = new ManualResetEventSlim(false))
                {
                    int uiThreadId = Thread.CurrentThread.ManagedThreadId;
                    int fetchThreadId = uiThreadId;
                    window.FetchHtmlSource = url =>
                    {
                        Interlocked.Increment(ref fetchCount);
                        fetchThreadId = Thread.CurrentThread.ManagedThreadId;
                        Volatile.Write(ref fetchStarted, true);
                        release.Wait();
                        return "<title>A</title>";
                    };
                    string copied = null;
                    window.CopyToClipboard = text => { copied = text; return true; };
                    window.ShowStatus = text => status = text;
                    window.textBox_SourceList.Text = "https://a.example/";
                    window.textBox_SearchWord.Text = "<title>";

                    Task task = StartExecute(window);
                    PumpUntil(() => Volatile.Read(ref fetchStarted), "取得が始まらない");

                    // 取得中でもUIスレッドは止まっていない(=ここに戻ってこられている)。取得は別スレッドで動いている
                    Assert.AreNotEqual(uiThreadId, fetchThreadId);
                    Assert.IsTrue(window.IsFetching);
                    Assert.IsFalse(window.button_Execute.IsEnabled);
                    Assert.AreEqual("取得中...", window.button_Execute.Content);
                    Assert.AreEqual("HTML取得中 (1/1)", status);
                    Assert.IsNull(copied);

                    // 取得中にもう一度実行しても二重には走らない
                    Task second = window.ExecuteTrimAsync();
                    Assert.IsTrue(second.IsCompleted);

                    release.Set();
                    PumpUntil(() => task.IsCompleted, "実行が終わらない");
                    task.GetAwaiter().GetResult();
                }

                Assert.AreEqual(1, fetchCount);
                Assert.IsFalse(window.IsFetching);
                Assert.IsTrue(window.button_Execute.IsEnabled);
                Assert.AreEqual("実行", window.button_Execute.Content);
                Assert.IsNull(status, "終わったらnullで元のタイトルに戻してもらう");
                Assert.AreEqual("◆https://a.example/\r\n<title>A</title>\r\n\r\n\r\n", window.textBox_DestList.Text);
            });
        }

        [TestMethod]
        public void 取得中にウィンドウを閉じても例外にならず結果を書き込まない()
        {
            StaRunner.Run(() =>
            {
                // タブはウィンドウ(MainWindow)に載せて使うので、閉じるのはMainWindowの方。
                // MainWindow.OnClosedからNotifyHostClosedで知らされる
                var host = new MainWindow(tempDirectory);
                var window = host.trimHtmlDataView;
                Boolean fetchStarted = false;
                Boolean fetchFinished = false;
                using (var release = new ManualResetEventSlim(false))
                {
                    window.FetchHtmlSource = url =>
                    {
                        Volatile.Write(ref fetchStarted, true);
                        release.Wait();
                        Volatile.Write(ref fetchFinished, true);
                        return "<title>A</title>";
                    };
                    string copied = null;
                    var messages = new List<string>();
                    window.CopyToClipboard = text => { copied = text; return true; };
                    window.ShowMessage = messages.Add;
                    window.textBox_SourceList.Text = "https://a.example/\r\nhttps://b.example/";
                    window.textBox_SearchWord.Text = "<title>";
                    window.textBox_DestList.Text = "";

                    ShowOffscreen(host);
                    Task task = StartExecute(window);
                    PumpUntil(() => Volatile.Read(ref fetchStarted), "取得が始まらない");

                    host.Close();
                    release.Set();
                    PumpUntil(() => task.IsCompleted, "実行が終わらない");

                    Assert.IsTrue(Volatile.Read(ref fetchFinished));
                    Assert.IsFalse(task.IsFaulted);
                    Assert.IsNull(copied);
                    Assert.AreEqual(0, messages.Count);
                    Assert.AreEqual("", window.textBox_DestList.Text);
                }
            });
        }

        [TestMethod]
        public void 取得に失敗したURLはメッセージを出し残りのURLは続けて処理する()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var messages = new List<string>();
                string copied = null;
                window.FetchHtmlSource = url =>
                {
                    if (url == "https://bad.example/")
                    {
                        throw new System.Net.WebException("接続できない");
                    }
                    return "<title>B</title>";
                };
                window.CopyToClipboard = text => { copied = text; return true; };
                window.ShowMessage = messages.Add;

                window.textBox_SourceList.Text = "https://bad.example/\r\nhttps://b.example/";
                window.textBox_SearchWord.Text = "<title>";

                Execute(window);

                CollectionAssert.AreEqual(new[] { "ソース取得に失敗しました。\r\nhttps://bad.example/" }, messages);
                string expected =
                    "◆https://bad.example/\r\n\r\n" +
                    "◆https://b.example/\r\n<title>B</title>\r\n\r\n\r\n";
                Assert.AreEqual(expected, window.textBox_DestList.Text);
                Assert.AreEqual(expected, copied);
                Assert.IsTrue(window.button_Execute.IsEnabled);
            });
        }

        [TestMethod]
        public void 取得行数が数字でなければメッセージを出して取得しない()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var messages = new List<string>();
                int fetchCount = 0;
                string copied = null;
                window.FetchHtmlSource = url => { fetchCount++; return "<title>A</title>"; };
                window.CopyToClipboard = text => { copied = text; return true; };
                window.ShowMessage = messages.Add;

                window.textBox_SourceList.Text = "https://a.example/";
                window.textBox_SearchWord.Text = "<title>";
                window.textBox_TrimLineNum.Text = "abc";
                window.textBox_DestList.Text = "前回の結果";

                Execute(window);

                CollectionAssert.AreEqual(new[] { "取得行数は0以上の数字で入力してください。" }, messages);
                Assert.AreEqual(0, fetchCount);
                Assert.IsNull(copied);
                Assert.AreEqual("前回の結果", window.textBox_DestList.Text);
                Assert.IsTrue(window.button_Execute.IsEnabled);
            });
        }

        [TestMethod]
        public void 取得元のURLがLFやCRだけで区切られていても1行ずつ取得する()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                var fetched = new List<string>();
                window.FetchHtmlSource = url => { fetched.Add(url); return "<title>A</title>"; };
                window.CopyToClipboard = text => true;

                window.textBox_SourceList.Text = "https://a.example/\nhttps://b.example/\rhttps://c.example/";
                window.textBox_SearchWord.Text = "<title>";

                Execute(window);

                CollectionAssert.AreEqual(new[] { "https://a.example/", "https://b.example/", "https://c.example/" }, fetched);
            });
        }

        // 画面外に、アクティブにせず表示する(閉じる操作を確かめるテスト用)
        private static void ShowOffscreen(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
        }

        [TestMethod]
        public void 余白ダブルクリックのデバッグ切替は押すたびに反転する()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);
                Assert.IsFalse(window.IsDebug);

                window.ToggleDebugMode();
                Assert.IsTrue(window.IsDebug);

                window.ToggleDebugMode();
                Assert.IsFalse(window.IsDebug);
            });
        }

        [TestMethod]
        public void 検索文字終端の欄はWinForms版と同じく非表示になっている()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimHtmlDataView(tempDirectory);

                // 旧版にあったタイトル("TrimHtmlData")の確認は、タブ名の確認としてMainWindowTestsへ移した
                Assert.AreEqual(Visibility.Collapsed, window.textBox_DelimiterWord.Visibility);
                Assert.AreEqual(Visibility.Collapsed, window.label_DelimiterWord.Visibility);
                Assert.AreEqual(Visibility.Visible, window.textBox_SearchWord.Visibility);
                Assert.AreEqual("各URLで最初にヒットした箇所だけを取り出す", window.checkBox_FirstWordOnly.ToolTip);
            });
        }
    }
}
