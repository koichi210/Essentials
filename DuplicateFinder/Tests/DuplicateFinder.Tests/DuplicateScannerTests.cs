using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DuplicateFinder.Tests
{
    [TestClass]
    public class DuplicateScannerTests
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "DuplicateFinderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, true);
        }

        private string Write(string relativePath, byte[] content)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return path;
        }

        private static byte[] RandomBytes(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        private static byte[] WithChange(byte[] source, int index)
        {
            var copy = (byte[])source.Clone();
            copy[index] ^= 0xFF;
            return copy;
        }

        private ScanResult Scan(Action<ScanOptions> configure = null)
        {
            var options = new ScanOptions { RootFolders = new[] { _root } };
            configure?.Invoke(options);
            return DuplicateScanner.Scan(options, null, CancellationToken.None);
        }

        private static List<List<string>> FileNames(ScanResult result)
        {
            return result.Groups
                .Select(g => g.Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n).ToList())
                .OrderBy(g => g[0])
                .ToList();
        }

        [TestMethod]
        public void 名前やフォルダが違っても中身が同じなら重複になる()
        {
            byte[] content = RandomBytes(300 * 1024, 1);
            Write("a.mp4", content);
            Write(@"sub\deep\b_copy.mp4", content);
            Write("other.mp4", RandomBytes(300 * 1024, 2));

            ScanResult result = Scan();

            CollectionAssert.AreEqual(new[] { "a.mp4", "b_copy.mp4" }, FileNames(result).Single());
            Assert.AreEqual(3, result.ScannedFileCount);
        }

        [TestMethod]
        public void 同じサイズでも1バイトでも違えば重複にしない_先頭_中間_末尾()
        {
            // 先頭・末尾の64KBは先読みでの絞り込み、中間は全体比較で弾かれる
            const int length = 20 * 1024 * 1024 + 123;
            byte[] content = RandomBytes(length, 3);
            Write("original.bin", content);
            Write("head.bin", WithChange(content, 0));
            Write("middle.bin", WithChange(content, length / 2));
            Write("tail.bin", WithChange(content, length - 1));
            // 先読み範囲のすぐ外(チャンクの境目付近)
            Write("edge.bin", WithChange(content, DuplicateScanner.SampleSize));

            ScanResult result = Scan();

            Assert.AreEqual(0, result.Groups.Count);
        }

        [TestMethod]
        public void 同じサイズの中に複数の重複の組があれば別々のグループになる()
        {
            byte[] a = RandomBytes(500 * 1024, 4);
            byte[] b = WithChange(a, 250 * 1024);
            Write("a1.bin", a);
            Write("a2.bin", a);
            Write("a3.bin", a);
            Write("b1.bin", b);
            Write("b2.bin", b);
            Write("c.bin", WithChange(a, 100));

            List<List<string>> groups = FileNames(Scan());

            Assert.AreEqual(2, groups.Count);
            CollectionAssert.AreEqual(new[] { "a1.bin", "a2.bin", "a3.bin" }, groups[0]);
            CollectionAssert.AreEqual(new[] { "b1.bin", "b2.bin" }, groups[1]);
        }

        [TestMethod]
        public void 小さいファイルも比較できる_0バイトは対象外()
        {
            Write("x1.txt", new byte[] { 1, 2, 3 });
            Write("x2.txt", new byte[] { 1, 2, 3 });
            Write("y.txt", new byte[] { 1, 2, 4 });
            Write("empty1.txt", new byte[0]);
            Write("empty2.txt", new byte[0]);

            ScanResult result = Scan();

            CollectionAssert.AreEqual(new[] { "x1.txt", "x2.txt" }, FileNames(result).Single());
            Assert.AreEqual(3, result.ScannedFileCount);
        }

        [TestMethod]
        public void 同じサイズのファイルが大量にあっても正しく仕分ける()
        {
            // 総当たりではなく簡易ハッシュでの振り分けを通る件数
            byte[] shared = RandomBytes(200 * 1024, 5);
            for (int i = 0; i < 12; i++)
            {
                Write("unique" + i + ".bin", RandomBytes(200 * 1024, 100 + i));
            }
            for (int i = 0; i < 5; i++)
            {
                Write("same" + i + ".bin", shared);
            }

            ScanResult result = Scan();

            Assert.AreEqual(1, result.Groups.Count);
            Assert.AreEqual(5, result.Groups[0].Files.Count);
            Assert.IsTrue(result.Groups[0].Files.All(f => Path.GetFileName(f.Path).StartsWith("same")));
        }

        [TestMethod]
        public void 最小サイズと拡張子で絞り込める()
        {
            byte[] big = RandomBytes(2 * 1024 * 1024, 6);
            byte[] small = RandomBytes(1000, 7);
            Write("big1.MP4", big);
            Write("big2.mp4", big);
            Write("big3.txt", big);
            Write("small1.mp4", small);
            Write("small2.mp4", small);

            ScanResult result = Scan(o =>
            {
                o.MinSizeBytes = 1024 * 1024;
                o.Extensions = ScanOptions.ParseExtensions("mp4; .mkv");
            });

            CollectionAssert.AreEqual(new[] { "big1.MP4", "big2.mp4" }, FileNames(result).Single());
        }

        [TestMethod]
        public void 隠しファイルは既定で対象外()
        {
            byte[] content = RandomBytes(1000, 8);
            Write("visible1.bin", content);
            Write("visible2.bin", content);
            string hidden = Write("hidden.bin", content);
            File.SetAttributes(hidden, FileAttributes.Hidden);

            Assert.AreEqual(2, Scan().Groups.Single().Files.Count);
            Assert.AreEqual(3, Scan(o => o.SkipHiddenAndSystem = false).Groups.Single().Files.Count);
        }

        [TestMethod]
        public void 並列でも結果は同じ()
        {
            for (int i = 0; i < 10; i++)
            {
                byte[] content = RandomBytes(100 * 1024 + i, 200 + i);
                Write("p" + i + "a.bin", content);
                Write("p" + i + "b.bin", content);
                Write("p" + i + "c.bin", WithChange(content, i));
            }

            List<List<string>> serial = FileNames(Scan());
            List<List<string>> parallel = FileNames(Scan(o => o.MaxParallelism = 4));

            Assert.AreEqual(10, serial.Count);
            Assert.AreEqual(serial.Count, parallel.Count);
            for (int i = 0; i < serial.Count; i++)
            {
                CollectionAssert.AreEqual(serial[i], parallel[i]);
            }
        }

        [TestMethod]
        public void 空く容量が大きいグループから並ぶ()
        {
            byte[] small = RandomBytes(1000, 9);
            byte[] large = RandomBytes(5000, 10);
            Write("s1.bin", small);
            Write("s2.bin", small);
            Write("s3.bin", small);
            Write("l1.bin", large);
            Write("l2.bin", large);

            ScanResult result = Scan();

            Assert.AreEqual(5000, result.Groups[0].WastedBytes);
            Assert.AreEqual(2000, result.Groups[1].WastedBytes);
        }

        [TestMethod]
        public void 進捗は最後に100パーセントになる()
        {
            byte[] content = RandomBytes(3 * 1024 * 1024, 11);
            Write("a.bin", content);
            Write("b.bin", content);
            Write("c.bin", WithChange(content, 10));
            Write("d.bin", WithChange(content, 2 * 1024 * 1024));

            var reports = new List<ScanProgress>();
            var progress = new SyncProgress(reports.Add);
            DuplicateScanner.Scan(new ScanOptions { RootFolders = new[] { _root } }, progress, CancellationToken.None);

            ScanProgress last = reports.Last();
            Assert.AreEqual(ScanPhase.Comparing, last.Phase);
            Assert.AreEqual(4L * content.Length, last.TotalBytes);
            Assert.AreEqual(last.TotalBytes, last.ProcessedBytes);
        }

        [TestMethod]
        public void ファイル一覧の作成中に中止したら結果は空()
        {
            Write("a.bin", new byte[] { 1 });
            Write("b.bin", new byte[] { 1 });
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                ScanResult result = DuplicateScanner.Scan(new ScanOptions { RootFolders = new[] { _root } }, null, cts.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.IsFalse(result.ReachedComparing);
                Assert.AreEqual(0, result.Groups.Count);
            }
        }

        [TestMethod]
        public void 比較の途中で中止したら確定した分だけ返す()
        {
            // サイズ違いの重複ペアを10組。1組ずつ順に比較させ、半分ほど進んだところで中止する
            var contents = new List<byte[]>();
            for (int i = 0; i < 10; i++)
            {
                byte[] content = RandomBytes(300 * 1024 + i, 300 + i);
                contents.Add(content);
                Write("pair" + i + "a.bin", content);
                Write("pair" + i + "b.bin", content);
            }

            using (var cts = new CancellationTokenSource())
            {
                var progress = new SyncProgress(p =>
                {
                    if (p.Phase == ScanPhase.Comparing && p.TotalBytes > 0 && p.ProcessedBytes * 2 >= p.TotalBytes)
                    {
                        cts.Cancel();
                    }
                });
                var options = new ScanOptions { RootFolders = new[] { _root }, ReportIntervalMs = 0 };
                ScanResult result = DuplicateScanner.Scan(options, progress, cts.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.IsTrue(result.ReachedComparing);
                Assert.IsTrue(result.Groups.Count > 0 && result.Groups.Count < 10, "途中まで: " + result.Groups.Count);
                Assert.IsTrue(result.ProcessedBytes < result.TotalBytes);
                // 返ってきたものは、すべて本物の重複ペア
                foreach (DuplicateGroup group in result.Groups)
                {
                    Assert.AreEqual(2, group.Files.Count);
                    CollectionAssert.AreEqual(File.ReadAllBytes(group.Files[0].Path), File.ReadAllBytes(group.Files[1].Path));
                }
            }
        }

        [TestMethod]
        public void 複数の対象フォルダをまたいで重複を見つける()
        {
            byte[] content = RandomBytes(200 * 1024, 12);
            string a = Write(@"A\movie.mp4", content);
            string b = Write(@"B\deep\renamed.mp4", content);
            Write(@"C\unrelated.mp4", content);

            ScanResult result = Scan(o => o.RootFolders = new[] { Path.Combine(_root, "A"), Path.Combine(_root, "B") });

            DuplicateGroup group = result.Groups.Single();
            CollectionAssert.AreEqual(new[] { a, b }, group.Files.Select(f => f.Path).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1 }, group.Files.Select(f => f.RootIndex).ToArray());
            Assert.AreEqual(2, group.RootCount);
        }

        [TestMethod]
        public void 入れ子の対象フォルダでも同じファイルを二重に数えない()
        {
            byte[] content = RandomBytes(1000, 13);
            Write("top.bin", content);
            Write(@"sub\inner.bin", content);

            // 末尾の\や重複指定があっても同じフォルダとして扱う
            ScanResult result = Scan(o => o.RootFolders = new[] { _root + "\\", Path.Combine(_root, "sub"), _root });

            Assert.AreEqual(2, result.ScannedFileCount);
            DuplicateGroup group = result.Groups.Single();
            // 入れ子のときは、より深い対象フォルダのものとして扱う
            Assert.AreEqual(0, group.Files.Single(f => f.Path.EndsWith("top.bin")).RootIndex);
            Assert.AreEqual(1, group.Files.Single(f => f.Path.EndsWith("inner.bin")).RootIndex);
        }

        [TestMethod]
        public void またがる重複だけに絞れる()
        {
            byte[] insideOnly = RandomBytes(1000, 14);
            byte[] across = RandomBytes(2000, 15);
            Write(@"A\in1.bin", insideOnly);
            Write(@"A\in2.bin", insideOnly);
            Write(@"A\x1.bin", across);
            Write(@"A\x2.bin", across);
            Write(@"B\x3.bin", across);
            string[] roots = { Path.Combine(_root, "A"), Path.Combine(_root, "B") };

            Assert.AreEqual(2, Scan(o => o.RootFolders = roots).Groups.Count);

            ScanResult crossOnly = Scan(o =>
            {
                o.RootFolders = roots;
                o.CrossRootOnly = true;
            });
            // またがっているグループは、同じフォルダ内の分も含めて全員出す
            CollectionAssert.AreEqual(new[] { "x1.bin", "x2.bin", "x3.bin" }, FileNames(crossOnly).Single());
        }

        [TestMethod]
        public void 見つからない対象フォルダがあっても他は探す()
        {
            byte[] content = RandomBytes(1000, 16);
            Write("a.bin", content);
            Write("b.bin", content);

            ScanResult result = Scan(o => o.RootFolders = new[] { Path.Combine(_root, "nothing"), _root });

            Assert.AreEqual(1, result.Groups.Count);
            Assert.AreEqual(1, result.Errors.Count);
        }

        [TestMethod]
        public void フォルダの表記をそろえる()
        {
            Assert.AreEqual(@"D:\Videos", DuplicateScanner.NormalizeFolder(@"D:\Videos\"));
            Assert.AreEqual(@"D:\Videos", DuplicateScanner.NormalizeFolder("\"D:\\Videos\" "));
            Assert.AreEqual(@"D:\", DuplicateScanner.NormalizeFolder(@"D:\"));
            CollectionAssert.AreEqual(new[] { @"D:\", @"E:\Movies" },
                DuplicateScanner.StartFolders(new[] { @"D:\", @"D:\Videos", @"E:\Movies", @"e:\movies" }));
        }

        [TestMethod]
        public void 更新日時が壊れているファイルがあっても止まらずに比較する()
        {
            byte[] content = RandomBytes(1000, 17);
            Write("normal.bin", content);
            string broken = Write("broken_time.bin", content);
            SetBrokenLastWriteTime(broken);
            // 前提: .NETで読むと例外になる日時が付いている
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new FileInfo(broken).LastWriteTime);

            ScanResult result = Scan();

            DuplicateGroup group = result.Groups.Single();
            Assert.AreEqual(2, group.Files.Count);
            Assert.AreEqual(DateTime.MinValue, group.Files.Single(f => f.Path == broken).LastWriteTime);
            Assert.AreEqual(DateTime.MinValue, DuplicateScanner.SafeLastWriteTime(new FileInfo(broken)));
        }

        // Windowsの日付(FILETIME)としては書けるが、.NETのDateTimeの範囲(西暦9999年)を超える値を付ける
        private static void SetBrokenLastWriteTime(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                long broken = 0x7FFFFFFFFFFFFFF0;
                long keep = 0;
                Assert.IsTrue(SetFileTime(stream.SafeFileHandle, ref keep, ref keep, ref broken));
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileTime(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, ref long lpCreationTime, ref long lpLastAccessTime, ref long lpLastWriteTime);

        [TestMethod]
        public void 拡張子の入力を解釈できる()
        {
            ISet<string> extensions = ScanOptions.ParseExtensions(" mp4;.MKV, *.avi　wmv ;; ");
            CollectionAssert.AreEquivalent(new[] { ".mp4", ".MKV", ".avi", ".wmv" }, extensions.ToList());
            Assert.IsTrue(extensions.Contains(".mkv"));
            Assert.AreEqual(0, ScanOptions.ParseExtensions("").Count);
        }

        // Progress<T>はUIスレッドへ非同期に送るので、テストでは呼ばれた場で記録する
        private sealed class SyncProgress : IProgress<ScanProgress>
        {
            private readonly Action<ScanProgress> _report;

            public SyncProgress(Action<ScanProgress> report)
            {
                _report = report;
            }

            public void Report(ScanProgress value)
            {
                lock (this)
                {
                    _report(value);
                }
            }
        }
    }
}
