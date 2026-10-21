using System;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FileArranger.Tests
{
    /// <summary>
    /// フォルダ移動タブ(md)の削除のテスト。実際に一時フォルダを作り、ボタンを押したのと同じイベントを起こして確認する。
    /// </summary>
    [TestClass]
    public class MoveDirectoryTabTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FileArrangerMoveDirectoryTabTests_" + Guid.NewGuid().ToString("N"));
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

        private static void Click(ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        [TestMethod]
        public void 親フォルダと子フォルダを両方選んで削除しても例外にならず両方消える()
        {
            string source = Path.Combine(tempDirectory, "source");
            string target = Path.Combine(tempDirectory, "target");
            Directory.CreateDirectory(Path.Combine(source, @"A\B"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, @"A\a.txt"), "dummy");
            File.WriteAllText(Path.Combine(source, @"A\B\b.txt"), "dummy");
            File.WriteAllText(Path.Combine(source, @"keep.txt"), "dummy");

            StaRunner.Run(() =>
            {
                var window = new MainWindow(Path.Combine(tempDirectory, "data"));
                window.md_textBox_SourceDir.Text = source;
                window.md_comboBox_TargetDir.Text = target;
                Click(window.md_button_Listup);
                // 前提: 親(A)と子(A\B)が両方一覧に出ている
                Assert.AreEqual(2, window.md_listBox_Listup.Items.Count);

                window.md_listBox_Listup.SelectAll();
                Click(window.md_button_Delete);

                Assert.IsFalse(Directory.Exists(Path.Combine(source, "A")));
                Assert.IsTrue(File.Exists(Path.Combine(source, "keep.txt")), "選んでいないものは消さない");
                Assert.AreEqual(0, window.md_listBox_Listup.Items.Count);
            });
        }
    }
}
