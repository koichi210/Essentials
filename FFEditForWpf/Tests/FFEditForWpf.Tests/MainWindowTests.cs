using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// MainWindow（WinForms版Form1.csから移植した画面の処理）のテスト。
    /// ダイアログ(MessageBox)が出る分岐は、誰もクリックできずハングするので踏まない。
    /// </summary>
    [TestClass]
    public class MainWindowTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditForWpfMainWindowTests_" + Guid.NewGuid().ToString("N"));
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
        public void 起動直後の初期状態がWinForms版と同じ()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();

                Assert.AreEqual(7, window.comboBox_ChangeNumber_Digit.Items.Count);
                Assert.AreEqual("自動", window.comboBox_ChangeNumber_Digit.SelectedItem);
                Assert.AreEqual(5, window.comboBox_TimeSpan.Items.Count);
                Assert.AreEqual("秒", window.comboBox_TimeSpan.SelectedItem);
                Assert.AreEqual("*", window.textBox_Target_Extension.Text);
                Assert.AreEqual(true, window.radioButton_Target_File.IsChecked);
                Assert.AreEqual(true, window.radioButton_ChangeNumber.IsChecked);
                Assert.AreEqual(true, window.radioButton_Delete_BlankDir.IsChecked);
                Assert.IsFalse(window.comboBox_String1.IsEnabled);
                Assert.IsFalse(window.comboBox_String2.IsEnabled);
                Assert.IsFalse(window.checkBox_Operation_AnyDir.IsEnabled);
                Assert.IsFalse(window.textBox_Function_Any_Directory.IsEnabled);
                Assert.AreEqual(DateTime.Today, window.dateTimePicker_Days.SelectedDate);
                TimeSpan time;
                Assert.IsTrue(TimeText.TryParse(window.textBox_Time.Text, out time));
            });
        }

        [TestMethod]
        public void 名称変換の種類でラベルと入力欄の有効無効が切り替わる()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();

                window.radioButton_ChangeReplace.IsChecked = true;
                Assert.AreEqual("置換前", window.label_String1.Content);
                Assert.AreEqual("置換後", window.label_String2.Content);
                Assert.IsTrue(window.comboBox_String1.IsEnabled);
                Assert.IsTrue(window.comboBox_String2.IsEnabled);
                Assert.IsFalse(window.textBox_ChangeNumber_FirstVal.IsEnabled);
                Assert.IsFalse(window.comboBox_ChangeNumber_Digit.IsEnabled);
                Assert.IsFalse(window.checkBox_ChangeNumber_OrgName.IsEnabled);

                window.radioButton_ChangeExt.IsChecked = true;
                Assert.AreEqual("拡張子", window.label_String1.Content);
                Assert.AreEqual("", window.label_String2.Content);
                Assert.IsTrue(window.comboBox_String1.IsEnabled);
                Assert.IsFalse(window.comboBox_String2.IsEnabled);

                window.radioButton_ChangeNumber.IsChecked = true;
                Assert.AreEqual("", window.label_String1.Content);
                Assert.IsFalse(window.comboBox_String1.IsEnabled);
                Assert.IsTrue(window.textBox_ChangeNumber_FirstVal.IsEnabled);
                Assert.IsTrue(window.label_ChangeNumber_FirstVal.IsEnabled);
            });
        }

        [TestMethod]
        public void 機能タブの移動コピーでだけフォルダ指定が有効になる()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();

                window.radioButton_Move_Target.IsChecked = true;
                Assert.IsTrue(window.checkBox_Operation_AnyDir.IsEnabled);
                Assert.IsFalse(window.textBox_Function_Any_Directory.IsEnabled);

                window.checkBox_Operation_AnyDir.IsChecked = true;
                Assert.IsTrue(window.textBox_Function_Any_Directory.IsEnabled);

                window.radioButton_Delete_BlankDir.IsChecked = true;
                Assert.IsFalse(window.checkBox_Operation_AnyDir.IsEnabled);
                Assert.IsFalse(window.textBox_Function_Any_Directory.IsEnabled);
            });
        }

        [TestMethod]
        public void リストアップは自然順で並び基準フォルダを除いた名前になる()
        {
            foreach (string name in new[] { "HOGE_10.txt", "HOGE_2.txt", "HOGE_1.txt" })
            {
                File.WriteAllText(Path.Combine(tempDirectory, name), "dummy");
            }
            Directory.CreateDirectory(Path.Combine(tempDirectory, "sub"));

            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.comboBox_TargetDir.Text = tempDirectory + @"\";
                window.UpdateListBox();

                Assert.AreEqual(tempDirectory, window.comboBox_TargetDir.Text, "終端の\\は取り除かれる");
                Assert.AreEqual(3, window.listBox.Items.Count);
                Assert.AreEqual("HOGE_1.txt", window.listBox.Items[0]);
                Assert.AreEqual("HOGE_2.txt", window.listBox.Items[1]);
                Assert.AreEqual("HOGE_10.txt", window.listBox.Items[2]);
                Assert.AreEqual("総数=3 選択数=0", window.textBox_StatusBar.Text);

                // フォルダ表示に切り替えるとリストが更新される(WinForms版のCheckedChangedと同じ)
                window.radioButton_Target_Dir.IsChecked = true;
                Assert.AreEqual(1, window.listBox.Items.Count);
                Assert.AreEqual("sub", window.listBox.Items[0]);
            });
        }

        [TestMethod]
        public void 選択項目のみの対象は画面の並び順になる()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.listBox.Items.Add("a.txt");
                window.listBox.Items.Add("b.txt");
                window.listBox.Items.Add("c.txt");

                // クリックした順(c→a)ではなく、WinForms版と同じインデックス順で返る
                window.listBox.SelectedItems.Add("c.txt");
                window.listBox.SelectedItems.Add("a.txt");
                Assert.AreEqual("総数=3 選択数=2", window.textBox_StatusBar.Text);

                window.checkBox_Target_SelectFile.IsChecked = true;
                CollectionAssert.AreEqual(new List<string> { "a.txt", "c.txt" }, window.GetFileList());

                window.checkBox_Target_SelectFile.IsChecked = false;
                CollectionAssert.AreEqual(new List<string> { "a.txt", "b.txt", "c.txt" }, window.GetFileList());

                Assert.AreEqual(@"C:\root\a.txt" + Environment.NewLine + @"C:\root\c.txt" + Environment.NewLine,
                    WpfControlUtils.GetSelectName(window.listBox, @"C:\root"));
                Assert.AreEqual("a.txt" + Environment.NewLine + "c.txt" + Environment.NewLine,
                    WpfControlUtils.GetSelectName(window.listBox));
            });
        }

        [TestMethod]
        public void GetTickTime_加算間隔ごとのTick数になる()
        {
            Assert.AreEqual(0L, MainWindow.GetTickTime(0));
            Assert.AreEqual(TimeSpan.TicksPerSecond, MainWindow.GetTickTime(1));
            Assert.AreEqual(TimeSpan.TicksPerMinute, MainWindow.GetTickTime(2));
            Assert.AreEqual(TimeSpan.TicksPerHour, MainWindow.GetTickTime(3));
            Assert.AreEqual(TimeSpan.TicksPerDay, MainWindow.GetTickTime(4));
            Assert.AreEqual(0L, MainWindow.GetTickTime(-1));
        }

        [TestMethod]
        public void エラー一覧ダイアログにメッセージが表示される()
        {
            StaRunner.Run(() =>
            {
                var dlg = new ErrorMsg("Src=a" + Environment.NewLine + "Dst=b");
                Assert.AreEqual("Src=a" + Environment.NewLine + "Dst=b", dlg.textBox_ErrorMessage.Text);
                Assert.IsTrue(dlg.textBox_ErrorMessage.IsReadOnly);
                dlg.Close();
            });
        }
    }
}
