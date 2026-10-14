using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace DigitalClock.Tests
{
    /// <summary>
    /// MainWindowを実際に生成して、WinForms版と同じ見た目の設定になっているかを確認するテスト。
    /// Close()すると終了時の位置保存が走って本物の設定ファイルを上書きしてしまうので、閉じずに捨てる。
    /// </summary>
    [TestClass]
    public class MainWindowTests
    {
        [TestMethod]
        public void 起動直後から時刻が表示されている()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow();

                TextBlock label = (TextBlock)window.FindName("label_time");
                StringAssert.Matches(label.Text, new Regex(@"^\d{2}:\d{2}:\d{2}$"));
            });
        }

        [TestMethod]
        public void 最前面固定でサイズ変更できないツールウィンドウになっている()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = new MainWindow();

                Assert.AreEqual("Clock", window.Title);
                Assert.IsTrue(window.Topmost);
                Assert.AreEqual(WindowStyle.ToolWindow, window.WindowStyle);
                Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
            });
        }
    }
}
