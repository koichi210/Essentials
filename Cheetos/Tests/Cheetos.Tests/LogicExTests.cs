using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cheetos.Tests
{
    /// <summary>
    /// LogicEx.cs(バックアップ先の名前・キャプチャ画像の名前・予定時刻・表の貼り付け)のテスト。
    /// WinForms版とWPF版で同じファイル。
    /// </summary>
    [TestClass]
    public class LogicExTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosLogicExTests_" + Guid.NewGuid().ToString("N"));
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

        // ------------------------------------------------------------------
        // GetNewBackupDirectoryPath / CreateBackupDirectory
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetNewBackupDirectoryPathは元フォルダのCheetos_BKの下に日付と種類の名前を返す()
        {
            string path = Logic.GetNewBackupDirectoryPath(_tempDirectory, "Trim", new DateTime(2026, 11, 19, 10, 0, 0));

            Assert.AreEqual(Path.Combine(_tempDirectory, "Cheetos", "BK", "20261119_Trim"), path);
            Assert.IsFalse(Directory.Exists(path), "パスを返すだけでフォルダは作らない");
        }

        [TestMethod]
        public void GetNewBackupDirectoryPathは同じ名前があれば連番を付けて上書きしない()
        {
            string basePath = Path.Combine(_tempDirectory, "Cheetos", "BK", "20261119_Rotate");
            Directory.CreateDirectory(basePath);
            Directory.CreateDirectory(basePath + "_1");

            string path = Logic.GetNewBackupDirectoryPath(_tempDirectory, "Rotate", new DateTime(2026, 11, 19));

            Assert.AreEqual(basePath + "_2", path);
        }

        [TestMethod]
        public void GetNewBackupDirectoryPathは同じ名前のファイルがあっても避ける()
        {
            string basePath = Path.Combine(_tempDirectory, "Cheetos", "BK", "20261119_Merge");
            Directory.CreateDirectory(Path.GetDirectoryName(basePath));
            File.WriteAllText(basePath, "file");

            string path = Logic.GetNewBackupDirectoryPath(_tempDirectory, "Merge", new DateTime(2026, 11, 19));

            Assert.AreEqual(basePath + "_1", path);
        }

        [TestMethod]
        public void CreateBackupDirectoryは呼ぶたびに新しいフォルダを作る()
        {
            string first = Logic.CreateBackupDirectory(_tempDirectory, "Trim");
            string second = Logic.CreateBackupDirectory(_tempDirectory, "Trim");

            Assert.IsTrue(Directory.Exists(first));
            Assert.IsTrue(Directory.Exists(second));
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(first + "_1", second);
        }

        // ------------------------------------------------------------------
        // BuildCaptureFilePaths
        // ------------------------------------------------------------------

        [TestMethod]
        public void BuildCaptureFilePathsは繰り返し番号4桁と撮影番号で名前を並べる()
        {
            List<string> paths = Logic.BuildCaptureFilePaths(@"C:\cap\pre_", 2, 2);

            CollectionAssert.AreEqual(new[]
            {
                @"C:\cap\pre_0001_1.png",
                @"C:\cap\pre_0001_2.png",
                @"C:\cap\pre_0002_1.png",
                @"C:\cap\pre_0002_2.png",
            }, paths);
        }

        [TestMethod]
        public void BuildCaptureFilePathsはキャプチャする行が無ければ空()
        {
            Assert.AreEqual(0, Logic.BuildCaptureFilePaths(@"C:\cap\", 3, 0).Count);
        }

        // ------------------------------------------------------------------
        // CalcSleepEndTime / IsOverdue
        // ------------------------------------------------------------------

        [TestMethod]
        public void CalcSleepEndTimeは待ち時間を1秒単位に切り上げる()
        {
            DateTime start = new DateTime(2026, 11, 19, 10, 0, 0);

            Assert.AreEqual(start.AddSeconds(2), Logic.CalcSleepEndTime(start, TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(1)));
            Assert.AreEqual(start.AddSeconds(2), Logic.CalcSleepEndTime(start, TimeSpan.FromMilliseconds(2000), TimeSpan.FromSeconds(1)));
            Assert.AreEqual(start, Logic.CalcSleepEndTime(start, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        }

        [TestMethod]
        public void IsOverdueは予定時刻を猶予より長く過ぎたらtrue()
        {
            DateTime scheduled = new DateTime(2026, 11, 19, 10, 0, 0);
            TimeSpan grace = TimeSpan.FromSeconds(5);

            Assert.IsFalse(Logic.IsOverdue(scheduled.AddSeconds(5), scheduled, grace));
            Assert.IsTrue(Logic.IsOverdue(scheduled.AddSeconds(6), scheduled, grace));
        }

        // ------------------------------------------------------------------
        // ParseTableText
        // ------------------------------------------------------------------

        [TestMethod]
        public void ParseTableTextはタブと改行で区切り最後の空行を捨てる()
        {
            List<string[]> rows = Logic.ParseTableText("1000\t640\t480\r\n\t10\t20\r\n");

            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "1000", "640", "480" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "", "10", "20" }, rows[1]);
        }

        [TestMethod]
        public void ParseTableTextは改行が無い1セルも読める()
        {
            List<string[]> rows = Logic.ParseTableText("500");

            Assert.AreEqual(1, rows.Count);
            CollectionAssert.AreEqual(new[] { "500" }, rows[0]);
        }

        [TestMethod]
        public void ParseTableTextは空文字なら空()
        {
            Assert.AreEqual(0, Logic.ParseTableText("").Count);
            Assert.AreEqual(0, Logic.ParseTableText(null).Count);
        }
    }
}
