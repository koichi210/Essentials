using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeifToPng.Tests
{
    [TestClass]
    public class HeifConverterTests
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "HeifToPngTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        private string Write(string relativePath, byte[] content)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        // TestData の本物のHEICを置く
        private string CopyFixture(string fixtureName, string relativePath)
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", fixtureName);
            return Write(relativePath, File.ReadAllBytes(fixture));
        }

        private static BitmapSource LoadPng(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                BitmapDecoder decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }

        // (x, y) の色を R,G,B で返す
        private static Color PixelAt(BitmapSource bitmap, int x, int y)
        {
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            bgra.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        }

        private static void AssertRed(Color c)
        {
            Assert.IsTrue(c.R > 200 && c.G < 60 && c.B < 60, "赤のはず: " + c);
        }

        private static void AssertBlue(Color c)
        {
            Assert.IsTrue(c.B > 200 && c.R < 60 && c.G < 60, "青のはず: " + c);
        }

        private List<ConvertJob> Plan(bool includeSubfolders, List<string> failures, params string[] sources)
        {
            return HeifConverter.PlanJobs(sources, includeSubfolders, failures, null, CancellationToken.None);
        }

        // *******************************************************************************
        // ファイルの選び方

        [TestMethod]
        public void IsHeif_ExtensionsAreCaseInsensitive()
        {
            Assert.IsTrue(HeifConverter.IsHeif(@"C:\a\IMG_0001.HEIC"));
            Assert.IsTrue(HeifConverter.IsHeif(@"C:\a\b.heif"));
            Assert.IsTrue(HeifConverter.IsHeif(@"C:\a\DSCF0001.HIF"));
            Assert.IsFalse(HeifConverter.IsHeif(@"C:\a\b.jpg"));
            Assert.IsFalse(HeifConverter.IsHeif(@"C:\a\b.heic.png"));
            Assert.IsFalse(HeifConverter.IsHeif(@"C:\a\heic"));
        }

        [TestMethod]
        public void GetOutputPath_ReplacesOnlyExtension()
        {
            Assert.AreEqual(@"C:\a.b\IMG_0001.png", HeifConverter.GetOutputPath(@"C:\a.b\IMG_0001.HEIC"));
            Assert.AreEqual(@"C:\a\x.y.png", HeifConverter.GetOutputPath(@"C:\a\x.y.heif"));
        }

        [TestMethod]
        public void PlanJobs_FindsHeifOnly_AndHonorsSubfolderSwitch()
        {
            Write("a.heic", new byte[1]);
            Write("b.HEIF", new byte[1]);
            Write("c.jpg", new byte[1]);
            Write(@"sub\d.hif", new byte[1]);
            Write(@"sub\deeper\e.heic", new byte[1]);
            var failures = new List<string>();

            List<string> flat = Plan(false, failures, _root).Select(j => Path.GetFileName(j.SourcePath)).ToList();
            CollectionAssert.AreEqual(new[] { "a.heic", "b.HEIF" }, flat);

            List<string> deep = Plan(true, failures, _root).Select(j => Path.GetFileName(j.SourcePath)).ToList();
            CollectionAssert.AreEqual(new[] { "a.heic", "b.HEIF", "d.hif", "e.heic" }, deep);
            Assert.AreEqual(0, failures.Count);
        }

        [TestMethod]
        public void PlanJobs_SameFileTwice_IsConvertedOnce()
        {
            string a = Write(@"sub\a.heic", new byte[1]);
            var failures = new List<string>();

            List<ConvertJob> jobs = Plan(true, failures, _root, a, Path.Combine(_root, "sub"));

            Assert.AreEqual(1, jobs.Count);
            Assert.IsNull(jobs[0].Conflict);
        }

        [TestMethod]
        public void PlanJobs_SameOutputName_SecondOneIsConflict()
        {
            Write("x.heic", new byte[1]);
            Write("x.heif", new byte[1]);
            var failures = new List<string>();

            List<ConvertJob> jobs = Plan(false, failures, _root);

            Assert.AreEqual(2, jobs.Count);
            Assert.IsNull(jobs[0].Conflict);
            Assert.IsNotNull(jobs[1].Conflict);
        }

        [TestMethod]
        public void PlanJobs_FileSource_NonHeifIsIgnored_MissingIsFailure()
        {
            string jpg = Write("c.jpg", new byte[1]);
            var failures = new List<string>();

            List<ConvertJob> jobs = Plan(true, failures, jpg, Path.Combine(_root, "nothing"));

            Assert.AreEqual(0, jobs.Count);
            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains(failures[0], "nothing");
        }

        // PNGのチャンクを (種類, 中身) の並びで返す
        private static List<KeyValuePair<string, byte[]>> ReadPngChunks(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            var chunks = new List<KeyValuePair<string, byte[]>>();
            int pos = 8;
            while (pos + 8 <= data.Length)
            {
                int length = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                var body = new byte[length];
                Array.Copy(data, pos + 8, body, 0, length);
                chunks.Add(new KeyValuePair<string, byte[]>(type, body));
                pos += 12 + length;
            }
            return chunks;
        }

        // *******************************************************************************
        // 変換

        [TestMethod]
        public void ConvertFile_ProducesPngWithSameLook()
        {
            string source = CopyFixture("landscape.heic", "IMG_0001.HEIC");
            string output = HeifConverter.GetOutputPath(source);

            Assert.AreEqual(JobOutcome.Converted, HeifConverter.ConvertFile(source, output, false, false));

            BitmapSource png = LoadPng(output);
            Assert.AreEqual(64, png.PixelWidth);
            Assert.AreEqual(32, png.PixelHeight);
            AssertRed(PixelAt(png, 8, 16));
            AssertBlue(PixelAt(png, 56, 16));
            Assert.IsFalse(File.Exists(output + ".tmp"), "一時ファイルが残っている");
        }

        // HEIFの回転(irot)はデコーダーが適用済みなので、縦長になって左半分の赤が上に来ていればよい(二重に回していない)
        [TestMethod]
        public void ConvertFile_RotatedImage_IsUpright()
        {
            string source = CopyFixture("rotated90.heic", "rotated.heic");
            string output = HeifConverter.GetOutputPath(source);

            HeifConverter.ConvertFile(source, output, false, false);

            BitmapSource png = LoadPng(output);
            Assert.AreEqual(32, png.PixelWidth);
            Assert.AreEqual(64, png.PixelHeight);
            AssertRed(PixelAt(png, 16, 8));
            AssertBlue(PixelAt(png, 16, 56));
        }

        [TestMethod]
        public void ConvertFile_TenBitImage_IsConverted()
        {
            string source = CopyFixture("tenbit.heic", "tenbit.heic");
            string output = HeifConverter.GetOutputPath(source);

            Assert.AreEqual(JobOutcome.Converted, HeifConverter.ConvertFile(source, output, false, false));

            BitmapSource png = LoadPng(output);
            Assert.AreEqual(64, png.PixelWidth);
            AssertRed(PixelAt(png, 8, 16));
            AssertBlue(PixelAt(png, 56, 16));
        }

        [TestMethod]
        public void ConvertFile_ExistingPng_SkippedUnlessOverwrite()
        {
            string source = CopyFixture("landscape.heic", "a.heic");
            string output = Write("a.png", new byte[] { 1, 2, 3 });

            Assert.AreEqual(JobOutcome.Skipped, HeifConverter.ConvertFile(source, output, false, false));
            Assert.AreEqual(3, new FileInfo(output).Length);

            Assert.AreEqual(JobOutcome.Converted, HeifConverter.ConvertFile(source, output, true, false));
            Assert.AreEqual(64, LoadPng(output).PixelWidth);
        }

        // 写真のEXIF(撮影位置など)はPNGに持っていかず、色のプロファイル(iCCP)だけ残す
        [TestMethod]
        public void ConvertFile_DropsExifAndGps_KeepsColorProfile()
        {
            string source = CopyFixture("withexif.heic", "a.heic");
            string output = HeifConverter.GetOutputPath(source);

            HeifConverter.ConvertFile(source, output, false, false);

            List<KeyValuePair<string, byte[]>> chunks = ReadPngChunks(output);
            List<string> types = chunks.Select(c => c.Key).ToList();
            CollectionAssert.Contains(types, "iCCP");
            CollectionAssert.DoesNotContain(types, "eXIf");
            CollectionAssert.DoesNotContain(types, "tEXt");
            CollectionAssert.DoesNotContain(types, "iTXt");
            foreach (KeyValuePair<string, byte[]> chunk in chunks.Where(c => c.Key != "IDAT"))
            {
                string text = Encoding.ASCII.GetString(chunk.Value);
                Assert.IsFalse(text.Contains("GPS") || text.Contains("TestMaker"), chunk.Key + " にEXIFが残っている");
            }
            AssertRed(PixelAt(LoadPng(output), 8, 16));
        }

        [TestMethod]
        public void ConvertFile_KeepTimestamps_CopiesSourceTimes()
        {
            string source = CopyFixture("landscape.heic", "a.heic");
            var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var modified = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);
            File.SetCreationTimeUtc(source, created);
            File.SetLastWriteTimeUtc(source, modified);
            string output = HeifConverter.GetOutputPath(source);

            HeifConverter.ConvertFile(source, output, false, true);

            Assert.AreEqual(created, File.GetCreationTimeUtc(output));
            Assert.AreEqual(modified, File.GetLastWriteTimeUtc(output));
        }

        [TestMethod]
        public void Run_CountsConvertedSkippedAndFailed()
        {
            CopyFixture("landscape.heic", @"p\1.heic");
            CopyFixture("landscape.heic", @"p\2.heic");
            CopyFixture("rotated90.heic", @"p\sub\3.heic");
            CopyFixture("landscape.heic", @"p\4.heic");
            Write(@"p\4.png", new byte[] { 1 });
            Write(@"p\broken.heic", new byte[] { 0, 1, 2, 3, 4, 5 });
            var options = new ConvertOptions
            {
                Sources = new List<string> { Path.Combine(_root, "p") },
                IncludeSubfolders = true,
                MaxParallelism = 4,
            };

            ConvertResult result = HeifConverter.Run(options, null, CancellationToken.None);

            Assert.IsFalse(result.Cancelled);
            Assert.AreEqual(5, result.Total);
            Assert.AreEqual(3, result.Converted);
            Assert.AreEqual(1, result.Skipped);
            Assert.AreEqual(1, result.Failures.Count);
            StringAssert.Contains(result.Failures[0], "broken.heic");
            StringAssert.Contains(result.Failures[0], "壊れている");
            Assert.IsFalse(File.Exists(Path.Combine(_root, @"p\broken.png")), "失敗したのにPNGが残っている");
            Assert.IsTrue(File.Exists(Path.Combine(_root, @"p\sub\3.png")));
        }

        [TestMethod]
        public void Run_Cancelled_ReportsCancelled()
        {
            CopyFixture("landscape.heic", "1.heic");
            var options = new ConvertOptions { Sources = new List<string> { _root } };
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                ConvertResult result = HeifConverter.Run(options, null, cts.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(0, result.Converted);
            }
        }
    }
}
