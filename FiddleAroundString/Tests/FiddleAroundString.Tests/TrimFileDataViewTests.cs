using System;
using System.IO;
using System.Text;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// TrimFileDataView（「ファイル抽出」タブ。リファレンスファイルから検索ワードにヒットする行を抽出する）を実際に生成して確かめるテスト。
    /// 旧TrimFileDataのMainWindowTestsを移植したもの。
    /// データ保存先は一時フォルダに差し替え、クリップボードへのコピーもテスト用の処理に差し替える
    /// (本物の設定ファイルやクリップボードを書き換えないため)。Close()も呼ばない。
    ///
    /// ⚠️ リファレンスファイルが開けないと MessageBox.Show を呼ぶため、テストでは常に実在するファイルを指定する。
    /// 設定保存ボタンも保存ダイアログ・完了メッセージが出るため対象外。
    /// </summary>
    [TestClass]
    public class TrimFileDataViewTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "TrimFileDataTests_" + Guid.NewGuid().ToString("N"));
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

        [TestMethod]
        public void 実行でリファレンスファイルから検索結果を作りクリップボードにも送る()
        {
            string referPath = Path.Combine(tempDirectory, "refer.txt");
            File.WriteAllText(referPath, "apple pie" + Environment.NewLine + "banana split" + Environment.NewLine + "りんごapple",
                Encoding.GetEncoding("Shift_JIS"));

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                string copied = null;
                window.CopyToClipboard = text => { copied = text; return true; };

                window.textBox_ReferencePath.Text = referPath;
                window.textBox_SearchWordList.Text = "apple";

                Assert.IsTrue(window.ExecuteSearch());

                string result = window.textBox_SearchResultList.Text;
                StringAssert.Contains(result, "◆apple");
                StringAssert.Contains(result, "apple pie");
                // Shift_JISとして読み込むので日本語の行も化けない
                StringAssert.Contains(result, "りんごapple");
                Assert.IsFalse(result.Contains("banana"));
                Assert.AreEqual(result, copied);
            });
        }

        [TestMethod]
        public void LF改行のリファレンスファイルでもヒットした行だけを抽出する()
        {
            // 以前はCRLFだけで分けていたため、LFだけのファイルは全体が1行扱いになり、ヒットしない行まで結果に出ていた
            string referPath = Path.Combine(tempDirectory, "refer_lf.txt");
            File.WriteAllText(referPath, "apple pie\nbanana split\ncherry apple\n", Encoding.GetEncoding("Shift_JIS"));

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                window.CopyToClipboard = text => true;

                window.textBox_ReferencePath.Text = referPath;
                // LFだけの文字列を貼り付けた検索ワード欄も1行ずつのワードとして扱う
                window.textBox_SearchWordList.Text = "pie\ncherry";

                Assert.IsTrue(window.ExecuteSearch());

                string result = window.textBox_SearchResultList.Text;
                StringAssert.Contains(result, "◆pie" + Environment.NewLine + "apple pie" + Environment.NewLine);
                StringAssert.Contains(result, "◆cherry" + Environment.NewLine + "cherry apple" + Environment.NewLine);
                Assert.IsFalse(result.Contains("banana"));
            });
        }

        [TestMethod]
        public void 画面のチェックと共通ワードが検索に反映される()
        {
            string referPath = Path.Combine(tempDirectory, "refer.txt");
            File.WriteAllText(referPath,
                "ERROR apple 1" + Environment.NewLine +
                "error apple 2" + Environment.NewLine +
                "info apple 3",
                Encoding.GetEncoding("Shift_JIS"));

            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);
                window.CopyToClipboard = text => true;

                window.textBox_ReferencePath.Text = referPath;
                window.textBox_SearchWordList.Text = "apple";
                window.textBox_SearchCommonWord.Text = "error";
                window.checkBox_OrdinalCase.IsChecked = true;
                window.checkBox_FirstWordOnly.IsChecked = true;

                Assert.IsTrue(window.ExecuteSearch());

                string result = window.textBox_SearchResultList.Text;
                // 大文字小文字を区別するので"ERROR"の行は共通ワード"error"に合わない。最初の1件だけ
                Assert.IsFalse(result.Contains("apple 1"));
                StringAssert.Contains(result, "apple 2");
                Assert.IsFalse(result.Contains("apple 3"));
            });
        }

        [TestMethod]
        public void WinForms版と同じツールチップになっている()
        {
            StaRunner.Run(() =>
            {
                var window = new TrimFileDataView(tempDirectory);

                // 旧版にあったタイトル("TrimFileData")の確認は、タブ名の確認としてMainWindowTestsへ移した
                Assert.AreEqual("各ワードについて、最初に見つかった1行だけを抽出する", window.checkBox_FirstWordOnly.ToolTip);
                Assert.AreEqual("検索結果を検索結果欄に出し、クリップボードにもコピーする", ((Button)window.FindName("button_Execute")).ToolTip);
                // リファレンスはファイルを指定する欄なので、ドロップしたファイルのパスをそのまま入れる
                Assert.IsFalse(window.textBox_ReferencePath.FolderPathOnly);
            });
        }
    }
}
