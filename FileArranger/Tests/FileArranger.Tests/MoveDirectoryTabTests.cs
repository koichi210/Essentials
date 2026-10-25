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
                window.ConfirmDelete = text => true;
                Click(window.md_button_Delete);

                Assert.IsFalse(Directory.Exists(Path.Combine(source, "A")));
                Assert.IsTrue(File.Exists(Path.Combine(source, "keep.txt")), "選んでいないものは消さない");
                Assert.AreEqual(0, window.md_listBox_Listup.Items.Count);
            });
        }

        [TestMethod]
        public void 削除は確認を出し_いいえなら消さない()
        {
            string source = Path.Combine(tempDirectory, "source");
            string target = Path.Combine(tempDirectory, "target");
            Directory.CreateDirectory(Path.Combine(source, "A"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, @"A\a.txt"), "dummy");

            StaRunner.Run(() =>
            {
                var window = new MainWindow(Path.Combine(tempDirectory, "data"));
                window.md_textBox_SourceDir.Text = source;
                window.md_comboBox_TargetDir.Text = target;
                Click(window.md_button_Listup);
                window.md_listBox_Listup.SelectAll();

                string asked = null;
                window.ConfirmDelete = text => { asked = text; return false; };
                Click(window.md_button_Delete);

                Assert.IsNotNull(asked, "削除前に確認を出す");
                StringAssert.Contains(asked, "1個のフォルダを中身ごと削除します");
                Assert.IsTrue(File.Exists(Path.Combine(source, @"A\a.txt")), "いいえなら消さない");
            });
        }

        [TestMethod]
        public void リストアップ後に格納元の欄を書き換えても_リストアップしたフォルダの中を操作する()
        {
            // 以前は操作時の欄の値＋前の一覧の名前で操作していたため、別のフォルダの同名フォルダを消していた
            string source = Path.Combine(tempDirectory, "source");
            string other = Path.Combine(tempDirectory, "other");
            string target = Path.Combine(tempDirectory, "target");
            Directory.CreateDirectory(Path.Combine(source, "A"));
            Directory.CreateDirectory(Path.Combine(other, "A"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, @"A\a.txt"), "dummy");
            File.WriteAllText(Path.Combine(other, @"A\a.txt"), "dummy");

            StaRunner.Run(() =>
            {
                var window = new MainWindow(Path.Combine(tempDirectory, "data"));
                window.md_textBox_SourceDir.Text = source;
                window.md_comboBox_TargetDir.Text = target;
                Click(window.md_button_Listup);
                window.md_listBox_Listup.SelectAll();

                window.md_textBox_SourceDir.Text = other;
                window.ConfirmDelete = text => true;
                Click(window.md_button_Delete);

                Assert.IsFalse(Directory.Exists(Path.Combine(source, "A")), "一覧に出ていたフォルダを消す");
                Assert.IsTrue(Directory.Exists(Path.Combine(other, "A")), "書き換えた欄のフォルダには触らない");
            });
        }
    }
}
