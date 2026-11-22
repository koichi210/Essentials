using System;
using System.IO;
using System.Text;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// ToyingFileView（「ファイル加工」タブ。複数ファイルから指定文字列を含む行を処理する）を実際に生成して操作するテスト。
    /// 旧ToyingFileのMainWindowTestsを移植したもの(WinForms版のForm1Testsに相当)。書き換えるファイルは必ず一時フォルダに作る。
    ///
    /// ⚠️ textBox_Directory が空だと MessageBox.Show を呼ぶため、テストでは常に実在するディレクトリを指定する。
    /// </summary>
    [TestClass]
    public class ToyingFileViewTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ToyingFileTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }

        private static T Find<T>(ToyingFileView window, string name) where T : class
        {
            T control = window.FindName(name) as T;
            Assert.IsNotNull(control, name + " が見つからない");
            return control;
        }

        [TestMethod]
        public void 実行ボタンで対象フォルダ内のファイルから文字列が削除される()
        {
            string filePath = Path.Combine(_tempDirectory, "a.txt");
            File.WriteAllText(filePath, "keep" + Environment.NewLine + "delete SECRET here");

            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();
                Find<TextBox>(window, "textBox_Directory").Text = _tempDirectory;
                Find<TextBox>(window, "textBox_DeleteString").Text = "SECRET";
                Find<RadioButton>(window, "radioButton_DeleteString").IsChecked = true;
                Find<CheckBox>(window, "checkBox_CaseSensitive").IsChecked = true;

                window.Execute();
            });

            Assert.AreEqual("keep" + Environment.NewLine + "delete  here", File.ReadAllText(filePath));
        }

        [TestMethod]
        public void SubDirectoryチェックで再帰的に対象ファイルを見つける()
        {
            string subDir = Path.Combine(_tempDirectory, "sub");
            Directory.CreateDirectory(subDir);
            string nestedFile = Path.Combine(subDir, "b.txt");
            File.WriteAllText(nestedFile, "delete TARGET word");

            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();
                Find<TextBox>(window, "textBox_Directory").Text = _tempDirectory;
                Find<TextBox>(window, "textBox_DeleteString").Text = "TARGET";
                Find<CheckBox>(window, "checkBox_SubDirectory").IsChecked = true;
                Find<RadioButton>(window, "radioButton_DeleteString").IsChecked = true;
                Find<CheckBox>(window, "checkBox_CaseSensitive").IsChecked = true;

                window.Execute();
            });

            Assert.AreEqual("delete  word", File.ReadAllText(nestedFile));
        }

        [TestMethod]
        public void ファイル名パターンに合わないファイルは書き換えない()
        {
            string txtFile = Path.Combine(_tempDirectory, "a.txt");
            string logFile = Path.Combine(_tempDirectory, "b.log");
            File.WriteAllText(txtFile, "x TARGET y");
            File.WriteAllText(logFile, "x TARGET y");

            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();
                Find<TextBox>(window, "textBox_Directory").Text = _tempDirectory;
                Find<TextBox>(window, "textBox_File").Text = "*.txt";
                Find<TextBox>(window, "textBox_DeleteString").Text = "TARGET";
                Find<RadioButton>(window, "radioButton_DeleteString").IsChecked = true;
                Find<CheckBox>(window, "checkBox_DeleteLine").IsChecked = true;

                window.Execute();
            });

            Assert.AreEqual("", File.ReadAllText(txtFile), "行を削除なので行ごと空になる");
            Assert.AreEqual("x TARGET y", File.ReadAllText(logFile));
        }

        [TestMethod]
        public void 削除する文字列が無かったファイルは書き戻さない()
        {
            // 以前は一致しなかったファイルもShift_JISで書き直していたため、UTF-8の日本語やバイナリが化けていた
            string hitFile = Path.Combine(_tempDirectory, "hit.txt");
            string utf8File = Path.Combine(_tempDirectory, "utf8.txt");
            byte[] utf8Bytes = new UTF8Encoding(false).GetBytes("日本語だけの行" + Environment.NewLine + "対象なし");
            File.WriteAllText(hitFile, "x TARGET y");
            File.WriteAllBytes(utf8File, utf8Bytes);

            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();
                Find<TextBox>(window, "textBox_Directory").Text = _tempDirectory;
                Find<TextBox>(window, "textBox_DeleteString").Text = "TARGET";
                Find<RadioButton>(window, "radioButton_DeleteString").IsChecked = true;
                Find<CheckBox>(window, "checkBox_CaseSensitive").IsChecked = true;

                window.Execute();
            });

            Assert.AreEqual("x  y", File.ReadAllText(hitFile));
            CollectionAssert.AreEqual(utf8Bytes, File.ReadAllBytes(utf8File), "一致しなかったファイルはバイト列ごとそのまま");
        }

        [TestMethod]
        public void ShiftJISのファイルを読み書きできる()
        {
            Encoding sjis = Encoding.GetEncoding("Shift_JIS");
            string filePath = Path.Combine(_tempDirectory, "sjis.txt");
            File.WriteAllText(filePath, "日本語の行" + Environment.NewLine + "消す文字列あり", sjis);

            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();
                Find<TextBox>(window, "textBox_Directory").Text = _tempDirectory;
                Find<TextBox>(window, "textBox_DeleteString").Text = "消す";
                Find<RadioButton>(window, "radioButton_DeleteString").IsChecked = true;

                window.Execute();
            });

            Assert.AreEqual("日本語の行" + Environment.NewLine + "文字列あり", File.ReadAllText(filePath, sjis));
        }

        [TestMethod]
        public void 起動直後の初期状態がWinForms版と同じ()
        {
            StaRunner.Run(() =>
            {
                ToyingFileView window = new ToyingFileView();

                // 旧版にあったタイトル("ToyingFile")の確認は、タブ名の確認としてMainWindowTestsへ移した
                Assert.AreEqual(true, Find<CheckBox>(window, "checkBox_SubDirectory").IsChecked, "サブディレクトリは既定でオン");
                Assert.AreNotEqual(true, Find<RadioButton>(window, "radioButton_DeleteString").IsChecked, "メニューは未選択");
                Assert.AreNotEqual(true, Find<CheckBox>(window, "checkBox_CaseSensitive").IsChecked);
                Assert.AreNotEqual(true, Find<CheckBox>(window, "checkBox_DeleteLine").IsChecked);
            });
        }
    }
}
