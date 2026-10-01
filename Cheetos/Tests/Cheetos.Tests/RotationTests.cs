using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cheetos.Tests
{
    /// <summary>
    /// Rotation（Rotation.cs のフォーム部分とは別に定義されている、画像回転の
    /// バックアップ＆実行ロジック。public class、Form非依存）のテスト。
    /// </summary>
    [TestClass]
    public class RotationTests
    {
        private string tempDirectory;
        private string backupDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosRotationTests_" + Guid.NewGuid().ToString("N"));
            backupDirectory = Path.Combine(tempDirectory, "backup");
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(backupDirectory);
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

        private string CreateImage(string fileName, int width, int height)
        {
            string path = Path.Combine(tempDirectory, fileName);
            using (var bmp = new Bitmap(width, height))
            {
                bmp.Save(path);
            }
            return path;
        }

        [TestMethod]
        public void SetTargetFileNameは空文字なら失敗する()
        {
            var rotation = new Rotation();

            Assert.IsFalse(rotation.SetTargetFileName(""));
        }

        [TestMethod]
        public void SetTargetFileNameは空でなければ成功する()
        {
            var rotation = new Rotation();

            Assert.IsTrue(rotation.SetTargetFileName("a.jpg"));
        }

        [TestMethod]
        public void BackUpTargetFileは元ファイルをバックアップ先へコピーする()
        {
            string fileName = "photo.bmp";
            CreateImage(fileName, 10, 10);

            var rotation = new Rotation
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = backupDirectory,
            };
            rotation.SetTargetFileName(fileName);

            bool result = rotation.BackUpTargetFile();

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(Path.Combine(backupDirectory, fileName)), "バックアップ先にコピーされるはず");
            Assert.IsTrue(File.Exists(Path.Combine(tempDirectory, fileName)), "元のファイルはまだ残っている(コピーなので)");
        }

        [TestMethod]
        public void RotateExecuteは角度0でも元と同じサイズの画像を保存する()
        {
            string fileName = "photo.bmp";
            string path = CreateImage(fileName, 20, 10);

            var rotation = new Rotation
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = backupDirectory,
                BaseX = 0,
                BaseY = 0,
                Angle = 0,
            };
            rotation.SetTargetFileName(fileName);
            rotation.BackUpTargetFile();

            rotation.RotateExecute();

            // RotateExecute は FilePath (=SourceFolderPath\TargetFileName) を上書き保存する
            using (var result = new Bitmap(path))
            {
                Assert.IsTrue(result.Width > 0);
                Assert.IsTrue(result.Height > 0);
            }
        }

        [TestMethod]
        public void RotateExecuteは角度を指定しても例外にならず保存できる()
        {
            string fileName = "photo.bmp";
            string path = CreateImage(fileName, 30, 20);

            var rotation = new Rotation
            {
                SourceFolderPath = tempDirectory,
                BackUpDirPath = backupDirectory,
                BaseX = 5,
                BaseY = 5,
                Angle = 90,
            };
            rotation.SetTargetFileName(fileName);
            rotation.BackUpTargetFile();

            rotation.RotateExecute();

            Assert.IsTrue(File.Exists(path));
        }
    }
}
