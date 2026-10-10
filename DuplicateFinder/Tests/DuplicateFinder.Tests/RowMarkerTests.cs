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
        public void 一覧で各グループの一番上にあるものを残す()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.TopOfGroup);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\keep\a.mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));

            // 並べ替えて渡せば、その順で一番上のものが残る
            rows.Reverse();
            RowMarker.ApplyKeepRule(rows, KeepRule.TopOfGroup);
            CollectionAssert.AreEqual(new[] { @"D:\Other\y.mp4", @"D:\Videos\keep\c.mp4", @"D:\Videos\b.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void ファイル名の長さで残す_同じ長さなら一覧で上の方()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.ShortestFileName);
            // グループ1は a.mp4 と b.mp4 が同じ長さなので上の方
            CollectionAssert.AreEqual(new[] { @"D:\Videos\keep\a.mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));

            RowMarker.ApplyKeepRule(rows, KeepRule.LongestFileName);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\copy\long\name\a (1).mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void フォルダのパスの長さで残す()
        {
            List<FileRow> rows = SampleRows();
            RowMarker.ApplyKeepRule(rows, KeepRule.ShortestFolder);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\b.mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));

            RowMarker.ApplyKeepRule(rows, KeepRule.LongestFolder);
            CollectionAssert.AreEqual(new[] { @"D:\Videos\copy\long\name\a (1).mp4", @"D:\Videos\copy\c.mp4", @"D:\Other\x.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void 対象フォルダ欄で上のフォルダにあるものを残す()
        {
            var rows = new List<FileRow>
            {
                new FileRow(new FileEntry(@"E:\Backup\a.mp4", 100, DateTime.Parse("2024/01/01"), 1), 1),
                new FileRow(new FileEntry(@"D:\Main\very\long\path\a.mp4", 100, DateTime.Parse("2025/01/01"), 0), 1),
                new FileRow(new FileEntry(@"E:\Backup\sub\b.mp4", 100, DateTime.Parse("2024/01/01"), 1), 2),
                new FileRow(new FileEntry(@"E:\Backup\b.mp4", 100, DateTime.Parse("2024/01/01"), 1), 2),
            };

            RowMarker.ApplyKeepRule(rows, KeepRule.RootOrder);

            // 同じ対象フォルダ同士なら、一覧で上の方
            CollectionAssert.AreEqual(new[] { @"D:\Main\very\long\path\a.mp4", @"E:\Backup\sub\b.mp4" }, Kept(rows));
        }

        [TestMethod]
        public void 並べ替えはグループの中だけ()
        {
            List<FileRow> rows = SampleRows();
            var list = new System.Collections.ArrayList(rows);

            list.Sort(new RowComparer(nameof(FileRow.FileName), System.ComponentModel.ListSortDirection.Descending));
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 2, 2, 3, 3 }, list.Cast<FileRow>().Select(r => r.GroupNumber).ToArray());
            CollectionAssert.AreEqual(new[] { "b.mp4", "a.mp4", "a (1).mp4", "c.mp4", "c.mp4", "y.mp4", "x.mp4" },
                list.Cast<FileRow>().Select(r => r.FileName).ToArray());
            // 同じ値(グループ2のc.mp4)はパスの順
            Assert.AreEqual(@"D:\Videos\copy\c.mp4", ((FileRow)list[3]).Path);

            list.Sort(new RowComparer(nameof(FileRow.LastWriteTime), System.ComponentModel.ListSortDirection.Ascending));
            CollectionAssert.AreEqual(new[] { @"D:\Videos\b.mp4", @"D:\Videos\keep\a.mp4", @"D:\Videos\copy\long\name\a (1).mp4" },
                list.Cast<FileRow>().Take(3).Select(r => r.Path).ToArray());

            // 列の指定なしなら検索直後の並び(パスの順)
            list.Sort(new RowComparer(null, System.ComponentModel.ListSortDirection.Ascending));
            CollectionAssert.AreEqual(new[] { @"D:\Videos\b.mp4", @"D:\Videos\copy\long\name\a (1).mp4", @"D:\Videos\keep\a.mp4" },
                list.Cast<FileRow>().Take(3).Select(r => r.Path).ToArray());
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
