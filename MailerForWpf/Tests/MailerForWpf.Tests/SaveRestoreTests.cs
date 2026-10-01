using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace MailerForWpf.Tests
{
    /// <summary>
    /// MainWindowの設定保存/読み込み(WpfSaveRestore)のテスト。
    /// WinForms版Mailerで保存したJSONプロファイルをそのまま読めることも確認する。
    /// </summary>
    [TestClass]
    public class SaveRestoreTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "MailerForWpfSaveRestoreTests_" + Guid.NewGuid().ToString("N"));
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
        public void メール項目が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                string path = Path.Combine(tempDirectory, "setting.json");

                var writer = new MainWindow();
                writer.textBox_BrowserPath.Text = @"C:\chrome.exe";
                writer.textBox_MailTo.Text = "to@example.com";
                writer.textBox_MailCc.Text = "cc@example.com";
                writer.textBox_MailBcc.Text = "bcc@example.com";
                writer.textBox_MailSubject.Text = "件名 %%today%%";
                writer.textBox_MailBody.Text = "本文です\r\n2行目";
                Assert.IsTrue(writer.SaveRestore.Save(path));

                var reader = new MainWindow();
                Assert.IsTrue(reader.SaveRestore.Load(path));

                Assert.AreEqual(@"C:\chrome.exe", reader.textBox_BrowserPath.Text);
                Assert.AreEqual("to@example.com", reader.textBox_MailTo.Text);
                Assert.AreEqual("cc@example.com", reader.textBox_MailCc.Text);
                Assert.AreEqual("bcc@example.com", reader.textBox_MailBcc.Text);
                Assert.AreEqual("件名 %%today%%", reader.textBox_MailSubject.Text);
                Assert.AreEqual("本文です\r\n2行目", reader.textBox_MailBody.Text);
            });
        }

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            string path = Path.Combine(tempDirectory, "winforms.json");
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" +
                "    \"Common|textBox_BrowserPath\": \"C:\\\\chrome.exe\",\n" +
                "    \"Common|textBox_MailTo\": \"to@example.com\",\n" +
                "    \"Common|textBox_MailSubject\": \"件名\"\n" +
                "  },\n" +
                "  \"Lists\": {},\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {}\n" +
                "}", new UTF8Encoding(false));

            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_MailCc.Text = "消える値";
                Assert.IsTrue(window.SaveRestore.Load(path));

                Assert.AreEqual(@"C:\chrome.exe", window.textBox_BrowserPath.Text);
                Assert.AreEqual("to@example.com", window.textBox_MailTo.Text);
                Assert.AreEqual("件名", window.textBox_MailSubject.Text);
                // ファイルに無い項目は既定値(空)に戻る(WinForms版と同じ)
                Assert.AreEqual("", window.textBox_MailCc.Text);
            });
        }

        [TestMethod]
        public void Loadは存在しないファイルなら例外にならず失敗を返し値も変えない()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_MailTo.Text = "そのまま";

                Assert.IsFalse(window.SaveRestore.Load(Path.Combine(tempDirectory, "nothing.json")));
                Assert.AreEqual("そのまま", window.textBox_MailTo.Text);
            });
        }

        [TestMethod]
        public void LoadOrDefaultは存在しないファイルなら既定値に戻す()
        {
            StaRunner.Run(() =>
            {
                var window = new MainWindow();
                window.textBox_MailTo.Text = "消える値";

                window.SaveRestore.LoadOrDefault(Path.Combine(tempDirectory, "nothing.json"));
                Assert.AreEqual("", window.textBox_MailTo.Text);
            });
        }
    }
}
