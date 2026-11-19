using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEdit.Tests
{
    /// <summary>
    /// ExifDate(JPEGのEXIFにある撮影日時の読み書き)と、TimeStampの撮影日時オプションのテスト。
    /// EXIF(APP1)はテスト内で組み立てる。日時の19バイト以外は1バイトも変わらないこと、
    /// System.Drawing(GDI+)で読み直しても正しい値になることを確かめる。
    /// WinForms版(CS_Form/FFEdit)とWPF版(Essentials/FFEdit)で同じ内容にしてある。
    /// </summary>
    [TestClass]
    public class ExifDateTests
    {
        private const string _originalDate = "2001:02:03 04:05:06";
        private string tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "FFEditExifDateTests_" + Guid.NewGuid().ToString("N"));
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

        // ---- テスト用のJPEG/EXIFの組み立て ------------------------------------------------

        private static void PutUInt16(List<byte> buffer, int value, bool littleEndian)
        {
            byte[] bytes = { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
            buffer.AddRange(littleEndian ? bytes : bytes.Reverse());
        }

        private static void PutUInt32(List<byte> buffer, long value, bool littleEndian)
        {
            byte[] bytes = { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF) };
            buffer.AddRange(littleEndian ? bytes : bytes.Reverse());
        }

        private static void PutEntry(List<byte> buffer, int tag, int type, long count, long value, bool littleEndian)
        {
            PutUInt16(buffer, tag, littleEndian);
            PutUInt16(buffer, type, littleEndian);
            PutUInt32(buffer, count, littleEndian);
            PutUInt32(buffer, value, littleEndian);
        }

        // IFD0に変更日時(0x0132)とExif IFDへのポインタ、Exif IFDに撮影日時(0x9003)とデジタル化日時(0x9004)を持つAPP1
        private static byte[] BuildExifApp1(bool littleEndian, bool withDates = true)
        {
            var tiff = new List<byte>();
            tiff.AddRange(littleEndian ? new[] { (byte)'I', (byte)'I' } : new[] { (byte)'M', (byte)'M' });
            PutUInt16(tiff, 42, littleEndian);
            PutUInt32(tiff, 8, littleEndian);

            byte[] date = Encoding.ASCII.GetBytes(_originalDate + "\0");
            if (!withDates)
            {
                // IFD0に画像の幅(0x0100 SHORT)だけ
                PutUInt16(tiff, 1, littleEndian);
                PutEntry(tiff, 0x0100, 3, 1, 8, littleEndian);
                PutUInt32(tiff, 0, littleEndian);
            }
            else
            {
                // IFD0: 8～38、変更日時: 38～58、Exif IFD: 58～88、撮影日時: 88～108、デジタル化日時: 108～128
                PutUInt16(tiff, 2, littleEndian);
                PutEntry(tiff, 0x0132, 2, 20, 38, littleEndian);
                PutEntry(tiff, 0x8769, 4, 1, 58, littleEndian);
                PutUInt32(tiff, 0, littleEndian);
                tiff.AddRange(date);

                PutUInt16(tiff, 2, littleEndian);
                PutEntry(tiff, 0x9003, 2, 20, 88, littleEndian);
                PutEntry(tiff, 0x9004, 2, 20, 108, littleEndian);
                PutUInt32(tiff, 0, littleEndian);
                tiff.AddRange(date);
                tiff.AddRange(date);
            }

            var app1 = new List<byte> { 0xFF, 0xE1 };
            int length = 2 + 6 + tiff.Count;
            app1.Add((byte)(length >> 8));
            app1.Add((byte)(length & 0xFF));
            app1.AddRange(Encoding.ASCII.GetBytes("Exif\0\0"));
            app1.AddRange(tiff);
            return app1.ToArray();
        }

        // System.Drawingで作った本物のJPEG(8x8)のSOIの直後にAPP1を差し込む
        private static byte[] BuildJpeg(byte[] app1)
        {
            byte[] plain;
            using (var bitmap = new Bitmap(8, 8))
            using (var stream = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.CornflowerBlue);
                }
                bitmap.Save(stream, ImageFormat.Jpeg);
                plain = stream.ToArray();
            }

            var result = new List<byte> { 0xFF, 0xD8 };
            if (app1 != null)
            {
                result.AddRange(app1);
            }
            result.AddRange(plain.Skip(2));
            return result.ToArray();
        }

        private string WriteFile(string name, byte[] data)
        {
            string path = Path.Combine(tempDirectory, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        // ---- ExifDate ----------------------------------------------------------------------

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void 撮影日時を読める(bool littleEndian)
        {
            byte[] data = BuildJpeg(BuildExifApp1(littleEndian));

            DateTime dateTaken;
            Assert.IsTrue(ExifDate.TryRead(data, out dateTaken));
            Assert.AreEqual(new DateTime(2001, 2, 3, 4, 5, 6), dateTaken);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void 日時の3つの欄だけが書き換わり他のバイトは変わらない(bool littleEndian)
        {
            byte[] original = BuildJpeg(BuildExifApp1(littleEndian));
            byte[] data = (byte[])original.Clone();
            var target = new DateTime(2024, 12, 31, 23, 59, 58);

            Assert.AreEqual(3, ExifDate.Write(data, target));

            Dictionary<int, int> offsets = ExifDate.FindDateOffsets(data);
            Assert.AreEqual(3, offsets.Count);
            foreach (int offset in offsets.Values)
            {
                Assert.AreEqual("2024:12:31 23:59:58", Encoding.ASCII.GetString(data, offset, 19));
            }

            Assert.AreEqual(original.Length, data.Length);
            int changed = Enumerable.Range(0, data.Length).Count(i => data[i] != original[i]);
            int expectedMax = offsets.Values.Sum(offset =>
                Enumerable.Range(offset, 19).Count(i => data[i] != original[i]));
            Assert.AreEqual(expectedMax, changed, "日時の欄以外のバイトが変わっている");

            DateTime dateTaken;
            Assert.IsTrue(ExifDate.TryRead(data, out dateTaken));
            Assert.AreEqual(target, dateTaken);
        }

        [TestMethod]
        public void EXIFが無いJPEGとJPEG以外は書き換えない()
        {
            byte[] noExif = BuildJpeg(null);
            byte[] noDate = BuildJpeg(BuildExifApp1(true, withDates: false));
            byte[] text = Encoding.ASCII.GetBytes("not a jpeg file");

            foreach (byte[] data in new[] { noExif, noDate, text, new byte[0] })
            {
                byte[] copy = (byte[])data.Clone();
                Assert.AreEqual(0, ExifDate.Write(copy, DateTime.Now));
                CollectionAssert.AreEqual(data, copy);

                DateTime dateTaken;
                Assert.IsFalse(ExifDate.TryRead(copy, out dateTaken));
            }
        }

        [TestMethod]
        public void 壊れたEXIFでも例外にならない()
        {
            byte[] app1 = BuildExifApp1(true);
            // 途中で切れたAPP1(長さはそのまま)
            byte[] data = new byte[] { 0xFF, 0xD8 }.Concat(app1.Take(40)).ToArray();

            DateTime dateTaken;
            ExifDate.TryRead(data, out dateTaken);
            ExifDate.Write(data, DateTime.Now);
        }

        [TestMethod]
        public void ファイルに書き込むとGDIプラスで読んでも新しい撮影日時になり画像も壊れない()
        {
            string path = WriteFile("photo.jpg", BuildJpeg(BuildExifApp1(true)));
            long originalLength = new FileInfo(path).Length;
            var target = new DateTime(2023, 4, 5, 6, 7, 8);

            Assert.AreEqual(3, ExifDate.Write(path, target));
            Assert.AreEqual(originalLength, new FileInfo(path).Length);

            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            using (Image image = Image.FromStream(stream))
            {
                Assert.AreEqual(8, image.Width);
                string value = Encoding.ASCII.GetString(image.GetPropertyItem(0x9003).Value).TrimEnd('\0');
                Assert.AreEqual("2023:04:05 06:07:08", value);
            }

            DateTime dateTaken;
            Assert.IsTrue(ExifDate.TryRead(path, out dateTaken));
            Assert.AreEqual(target, dateTaken);
        }

        // ---- TimeStampの撮影日時オプション ------------------------------------------------

        [TestMethod]
        public void TimeStampで撮影日時を書き換えても対象外のファイル日時は変わらない()
        {
            string path = WriteFile("photo.jpg", BuildJpeg(BuildExifApp1(true)));
            var oldTime = new DateTime(2010, 1, 1, 0, 0, 0);
            File.SetLastWriteTime(path, oldTime);
            File.SetCreationTime(path, oldTime);

            var target = new DateTime(2022, 2, 22, 22, 22, 22);
            var stamp = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "photo.jpg" },
                BaseTicks = target.Ticks,
                UpdateDateTaken = true,
            };

            Assert.AreEqual(string.Empty, stamp.Execute());

            DateTime dateTaken;
            Assert.IsTrue(ExifDate.TryRead(path, out dateTaken));
            Assert.AreEqual(target, dateTaken);
            Assert.AreEqual(oldTime, File.GetLastWriteTime(path), "更新日時は対象にしていないので書き換え前のまま");
            Assert.AreEqual(oldTime, File.GetCreationTime(path));
        }

        [TestMethod]
        public void TimeStampで撮影日時とファイル日時を一緒に書き換えられ間隔も加算される()
        {
            WriteFile("a.jpg", BuildJpeg(BuildExifApp1(true)));
            WriteFile("b.jpg", BuildJpeg(BuildExifApp1(false)));

            var target = new DateTime(2022, 2, 22, 22, 22, 22);
            var stamp = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.jpg", "b.jpg" },
                BaseTicks = target.Ticks,
                IntervalTicks = TimeSpan.TicksPerMinute,
                UpdateLastWriteTime = true,
                UpdateDateTaken = true,
            };

            Assert.AreEqual(string.Empty, stamp.Execute());

            DateTime dateTaken;
            Assert.IsTrue(ExifDate.TryRead(Path.Combine(tempDirectory, "b.jpg"), out dateTaken));
            Assert.AreEqual(target.AddMinutes(1), dateTaken);
            Assert.AreEqual(target.AddMinutes(1), File.GetLastWriteTime(Path.Combine(tempDirectory, "b.jpg")));
        }

        [TestMethod]
        public void TimeStampで撮影日時の欄が無いファイルはエラー一覧に出しファイル日時は書き換える()
        {
            string path = WriteFile("a.txt", Encoding.ASCII.GetBytes("dummy"));
            byte[] before = File.ReadAllBytes(path);

            var target = new DateTime(2022, 2, 22, 22, 22, 22);
            var stamp = new TimeStamp
            {
                BaseDir = tempDirectory,
                FileList = new List<string> { "a.txt" },
                BaseTicks = target.Ticks,
                UpdateLastWriteTime = true,
                UpdateDateTaken = true,
            };

            string errors = stamp.Execute();

            StringAssert.Contains(errors, "a.txt");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.AreEqual(target, File.GetLastWriteTime(path));
        }
    }
}
