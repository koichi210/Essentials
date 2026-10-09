using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DuplicateFinder.Tests
{
    [TestClass]
    public class RowMarkerTests
    {
        private static FileRow Row(int group, string path, string lastWrite)
        {
            return new FileRow(new FileEntry(path, 100, DateTime.Parse(lastWrite)), group);
        }

        private static List<FileRow> SampleRows()
        {
            return new List<FileRow>
            {
                Row(1, @"D:\Videos\keep\a.mp4", "2024/01/01"),
                Row(1, @"D:\Videos\copy\long\name\a (1).mp4", "2025/01/01"),
                Row(1, @"D:\Videos\b.mp4", "2023/01/01"),
                Row(2, @"D:\Videos\copy\c.mp4", "2024/05/01"),
                Row(2, @"D:\Videos\keep\c.mp4", "2024/06/01"),
                Row(3, @"D:\Other\x.mp4", "2024/01/01"),
                Row(3, @"D:\Other\y.mp4", "2024/01/01"),
            };
        }

        private static string[] Kept(IEnumerable<FileRow> rows)
        {
            return rows.Where(r => !r.IsMarked).Select(r => r.Path).ToArray();
        }

        [TestMethod]
        public void 古いものを残す()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.Oldest);
            // グループ3は同時刻なのでパスの短い方→辞書順
            CollectionAssert.AreEqual(new[] { @"D:\Videos\b.mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void 新しいものを残す()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.Newest);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\copy\long\name\a (1).mp4", @"D:\Videos\keep\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void パスの短い長いで残す()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.ShortestPath);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\b.mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));

            // グループ2は同じ長さなので辞書順
            RowMarker.ApplyKeepRule(rows, KeepRule.LongestPath);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\copy\long\name\a (1).mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void 指定した1件だけ残す_他のグループには触らない()
        {
            List<FileRow> rows = SampleRows();
            rows[5].IsMarked = true;
            RowMarker.KeepOnly(rows, rows[1]);

            CollectionAssert.AreEqual(new[] { false, true, false, true, true, false, true },
                rows.Select(r => !r.IsMarked).ToArray());
        }

        [TestMethod]
        public void フォルダ以下を残す_含まないグループには触らない()
        {
            List<FileRow> rows = SampleRows();
            rows[6].IsMarked = true;

            int count = RowMarker.KeepUnderFolder(rows, @"D:\Videos\keep\");

            Assert.AreEqual(2, count);
            CollectionAssert.AreEqual(new[] { false, true, true, true, false, false, true },
                rows.Select(r => r.IsMarked).ToArray());
        }

        [TestMethod]
        public void フォルダ名の前方一致だけでは配下扱いしない()
        {
            var rows = new List<FileRow>
            {
                Row(1, @"D:\Videos\keep2\a.mp4", "2024/01/01"),
                Row(1, @"D:\Videos\other\a.mp4", "2024/01/01"),
            };
            Assert.AreEqual(0, RowMarker.KeepUnderFolder(rows, @"D:\Videos\keep"));
            Assert.IsFalse(rows.Any(r => r.IsMarked));
        }

        [TestMethod]
        public void 全部チェックされたグループを見つける()
        {
            List<FileRow> rows = SampleRows();
            rows[3].IsMarked = true;
            rows[4].IsMarked = true;
            rows[0].IsMarked = true;

            CollectionAssert.AreEqual(new[] { 2 }, RowMarker.FindFullyMarkedGroups(rows));
            RowMarker.ClearAll(rows);
            Assert.AreEqual(0, RowMarker.FindFullyMarkedGroups(rows).Count);
        }

        [TestMethod]
        public void サイズ表示()
        {
            Assert.AreEqual("999 B", SizeFormatter.Format(999));
            Assert.AreEqual("1.50 KB", SizeFormatter.Format(1536));
            Assert.AreEqual("2.00 GB", SizeFormatter.Format(2L * 1024 * 1024 * 1024));
        }
    }
}
