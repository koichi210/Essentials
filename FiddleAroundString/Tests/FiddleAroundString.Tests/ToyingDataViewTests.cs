using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FiddleAroundString.Tests
{
    /// <summary>
    /// ToyingDataView（「データ加工」タブ。重複除去・全角半角変換）を実際に生成して操作するテスト。
    /// 旧ToyingDataのMainWindowTestsを移植したもの(WinForms版のForm1Testsに相当)。
    ///
    /// ⚠️ 全角半角変換を選び、かつ変換対象チェックボックスを1つも選ばないと MessageBox.Show が出るため、
    /// テストでは必ずどれか1つチェックする。
    /// 本物のクリップボードを書き換えないよう、ClipboardWriterは差し替えてから実行する。
    /// </summary>
    [TestClass]
    public class ToyingDataViewTests
    {
        private static ToyingDataView CreateWindow(Action<String> onClipboard = null)
        {
            ToyingDataView window = new ToyingDataView();
            window.ClipboardWriter = text =>
            {
                onClipboard?.Invoke(text);
                return true;
            };
            return window;
        }

        private static T Find<T>(ToyingDataView window, String name) where T : class
        {
            T control = window.FindName(name) as T;
            Assert.IsNotNull(control, name + " が見つからない");
            return control;
        }

        [TestMethod]
        public void 重複除去ラジオを選ぶと重複行が除去されクリップボードにも入る()
        {
            StaRunner.Run(() =>
            {
                String clipboard = null;
                ToyingDataView window = CreateWindow(text => clipboard = text);
                Find<TextBox>(window, "textBox_Source").Text = "a" + Environment.NewLine + "b" + Environment.NewLine + "a";
                Find<RadioButton>(window, "radioButton_DeleteDuplicate").IsChecked = true;

                window.Execute();

                String expected = "a" + Environment.NewLine + "b";
                Assert.AreEqual(expected, Find<TextBox>(window, "textBox_Dest").Text);
                Assert.AreEqual(expected, clipboard);
            });
        }

        [TestMethod]
        public void 全角半角変換ラジオを選ぶと変換される()
        {
            StaRunner.Run(() =>
            {
                ToyingDataView window = CreateWindow();
                Find<TextBox>(window, "textBox_Source").Text = "１２３abc";
                Find<RadioButton>(window, "radioButton_ChangeWide2Narrow").IsChecked = true;
                Find<CheckBox>(window, "checkBox_Wide2Narrow_Number").IsChecked = true;

                window.Execute();

                Assert.AreEqual("123abc", Find<TextBox>(window, "textBox_Dest").Text);
            });
        }

        [TestMethod]
        public void 空行は取り除かれる()
        {
            StaRunner.Run(() =>
            {
                ToyingDataView window = CreateWindow();
                Find<TextBox>(window, "textBox_Source").Text = "a" + Environment.NewLine + Environment.NewLine + "b";
                Find<RadioButton>(window, "radioButton_DeleteDuplicate").IsChecked = true;

                window.Execute();

                Assert.AreEqual("a" + Environment.NewLine + "b", Find<TextBox>(window, "textBox_Dest").Text);
            });
        }

        [TestMethod]
        public void メニュー未選択なら結果は空でクリップボードも触らない()
        {
            StaRunner.Run(() =>
            {
                Boolean clipboardCalled = false;
                ToyingDataView window = CreateWindow(text => clipboardCalled = true);
                Find<TextBox>(window, "textBox_Source").Text = "a";

                window.Execute();

                Assert.AreEqual("", Find<TextBox>(window, "textBox_Dest").Text);
                Assert.IsFalse(clipboardCalled);
            });
        }

        [TestMethod]
        public void 起動直後はWinForms版と同じくどのメニューも未選択()
        {
            StaRunner.Run(() =>
            {
                ToyingDataView window = CreateWindow();

                // 旧版にあったタイトル("ToyingData")の確認は、タブ名の確認としてMainWindowTestsへ移した
                Assert.AreNotEqual(true, Find<RadioButton>(window, "radioButton_DeleteDuplicate").IsChecked);
                Assert.AreNotEqual(true, Find<RadioButton>(window, "radioButton_ChangeWide2Narrow").IsChecked);
            });
        }

        [TestMethod]
        public void ドロップしたファイルパス一覧が改行区切りで入力欄へ入る()
        {
            StaRunner.Run(() =>
            {
                ToyingDataView window = CreateWindow();

                window.SetDroppedFiles(new[] { @"C:\a.txt", @"C:\b.txt" });

                Assert.AreEqual(@"C:\a.txt" + Environment.NewLine + @"C:\b.txt", Find<TextBox>(window, "textBox_Source").Text);
            });
        }
    }
}
