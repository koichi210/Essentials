using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// TimeStamp（ファイルの作成日時・更新日時・アクセス日時を書き換える機能）のテスト。
    /// </summary>
    [TestClass]
    public class TimeStampTests
    {
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditTimeStampTests_" + Guid.NewGuid().ToString("N"));
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

        private string CreateFile(string name)
        {
            string path = Path.Combine(tempDirectory, name);
            File.WriteAllText(path, "dummy");
            return path;
        }

        [TestMethod]
        public void 更新日時だけを書き換えられる()
        {
            string path = CreateFile("a.txt");
            var before = new FileInfo(path);
            DateTime originalCreate = before.CreationTime;

            var target = new DateTime(2020, 1, 2, 3, 4, 5);
            var stump = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.txt" },
                BaseTicks = target.Ticks,
                IntervalTicks = 0,
                UpdateLastWriteTime = true,
            };

            stump.Execute();

            var after = new FileInfo(path);
            Assert.AreEqual(target, after.LastWriteTime);
            Assert.AreEqual(originalCreate, after.CreationTime, "対象にしていない項目は変わらないはず");
        }

        [TestMethod]
        public void 作成日時と更新日時とアクセス日時をまとめて書き換えられる()
        {
            string path = CreateFile("a.txt");

            var target = new DateTime(2019, 5, 6, 7, 8, 9);
            var stump = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.txt" },
                BaseTicks = target.Ticks,
                UpdateCreationTime = true,
                UpdateLastWriteTime = true,
                UpdateLastAccessTime = true,
            };

            stump.Execute();

            var after = new FileInfo(path);
            Assert.AreEqual(target, after.CreationTime);
            Assert.AreEqual(target, after.LastWriteTime);
            Assert.AreEqual(target, after.LastAccessTime);
        }

        [TestMethod]
        public void 複数ファイルは1件ごとに時間をずらして設定できる()
        {
            CreateFile("a.txt");
            CreateFile("b.txt");
            CreateFile("c.txt");

            var baseTime = new DateTime(2021, 1, 1);
            var oneDay = TimeSpan.FromDays(1).Ticks;

            var stump = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.txt", "b.txt", "c.txt" },
                BaseTicks = baseTime.Ticks,
                IntervalTicks = oneDay,
                UpdateLastWriteTime = true,
            };

            stump.Execute();

            Assert.AreEqual(baseTime, new FileInfo(Path.Combine(tempDirectory, "a.txt")).LastWriteTime);
            Assert.AreEqual(baseTime.AddDays(1), new FileInfo(Path.Combine(tempDirectory, "b.txt")).LastWriteTime);
            Assert.AreEqual(baseTime.AddDays(2), new FileInfo(Path.Combine(tempDirectory, "c.txt")).LastWriteTime);
        }

        [TestMethod]
        public void どの項目も指定しなければ日時は変わらない()
        {
            string path = CreateFile("a.txt");
            var before = new FileInfo(path);
            DateTime originalWrite = before.LastWriteTime;

            var stump = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.txt" },
                BaseTicks = new DateTime(2020, 1, 1).Ticks,
            };

            stump.Execute();

            Assert.AreEqual(originalWrite, new FileInfo(path).LastWriteTime);
        }
    }
}
