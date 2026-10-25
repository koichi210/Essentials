using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace FileArranger.Tests
{
    /// <summary>
    /// フォルダ名変更タブ(rd)のリネームのテスト。実際に一時フォルダを作り、ボタンを押したのと同じイベントを起こして確認する。
    /// </summary>
    [TestClass]
    public class RenameDirectoryTabTests
    {
        private const int _renameDestIdx = 1;

        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FileArrangerRenameDirectoryTabTests_" + Guid.NewGuid().ToString("N"));
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

        // 格納元の直下に1つだけフォルダを作ってリストアップし、[変更後]をdestNameにしてリネームを実行する
        private string[] RenameOnlyFolder(string srcName, string destName, bool restore = false)
        {
            string parent = Path.Combine(tempDirectory, "parent");
            Directory.CreateDirectory(Path.Combine(parent, srcName));

            StaRunner.Run(() =>
            {
                var window = new MainWindow(Path.Combine(tempDirectory, "data"));
                window.rd_comboBox_RenameDir.Text = parent;
                Click(window.rd_button_Listup_Target);
                window.rd_listView_Target.SelectAll();
                ((ListViewRow)window.rd_listView_Target.Items[0])[_renameDestIdx] = destName;

                Click(window.rd_button_RenameDir);
                if (restore)
                {
                    Click(window.rd_button_RenameDirRestore);
                }
            });

            return Directory.GetDirectories(parent).Select(Path.GetFileName).ToArray();
        }

        [TestMethod]
        public void 変更後が今と同じ名前なら何もしない()
        {
            // 以前は自分自身と重複したとみなして、連番付きの名前にリネームしていた
            CollectionAssert.AreEqual(new[] { "Folder01" }, RenameOnlyFolder("Folder01", "Folder01"));
        }

        [TestMethod]
        public void 大文字小文字だけ違う名前ならその名前に変える()
        {
            CollectionAssert.AreEqual(new[] { "FOLDER01" }, RenameOnlyFolder("folder01", "FOLDER01"));
        }

        [TestMethod]
        public void 大文字小文字だけ変えたリネームも復元できる()
        {
            CollectionAssert.AreEqual(new[] { "folder01" }, RenameOnlyFolder("folder01", "FOLDER01", true));
        }

        [TestMethod]
        public void 別の名前ならその名前に変える()
        {
            CollectionAssert.AreEqual(new[] { "Folder02" }, RenameOnlyFolder("Folder01", "Folder02"));
        }
    }
}
