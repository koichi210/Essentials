using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace DuplicateFinder
{
    /// <summary>一覧の1行(1ファイル)。IsMarked=trueのファイルが削除対象</summary>
    internal sealed class FileRow : INotifyPropertyChanged
    {
        private bool _isMarked;

        public FileRow(FileEntry entry, int groupNumber)
        {
            Entry = entry;
            GroupNumber = groupNumber;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FileEntry Entry { get; }
        public int GroupNumber { get; }

        /// <summary>一覧のグループ見出し(グループ分けのキーも兼ねる)</summary>
        public string GroupHeader { get; set; }

        public string Path
        {
            get { return Entry.Path; }
        }

        public string FileName
        {
            get { return System.IO.Path.GetFileName(Entry.Path); }
        }

        public string Folder
        {
            get { return System.IO.Path.GetDirectoryName(Entry.Path); }
        }

        /// <summary>何番目の対象フォルダにあるか(1始まり・画面表示用)</summary>
        public int RootNumber
        {
            get { return Entry.RootIndex + 1; }
        }

        public string SizeText
        {
            get { return SizeFormatter.Format(Entry.Length); }
        }

        public DateTime LastWriteTime
        {
            get { return Entry.LastWriteTime; }
        }

        public bool IsMarked
        {
            get { return _isMarked; }
            set
            {
                if (_isMarked == value)
                {
                    return;
                }
                _isMarked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
            }
        }
    }

    /// <summary>各グループで「どれを残すか」の自動選択ルール</summary>
    internal enum KeepRule
    {
        // 設定ファイルには数値で保存されるので、並びを変えるときは末尾に足すこと
        Newest,
        Oldest,
        ShortestPath,
        LongestPath,
        // 対象フォルダのリストで上にあるフォルダのものを残す(上を本体・下をバックアップにする使い方)
        RootOrder,
    }

    /// <summary>削除対象のチェックを付け外しする処理(画面から切り離してテストできるようにしてある)</summary>
    internal static class RowMarker
    {
        /// <summary>各グループでルールに合う1件を残し、他すべてに削除チェックを付ける</summary>
        public static void ApplyKeepRule(IEnumerable<FileRow> rows, KeepRule rule)
        {
            foreach (IGrouping<int, FileRow> group in rows.GroupBy(r => r.GroupNumber))
            {
                FileRow keep = PickKeeper(group, rule);
                foreach (FileRow row in group)
                {
                    row.IsMarked = row != keep;
                }
            }
        }

        internal static FileRow PickKeeper(IEnumerable<FileRow> group, KeepRule rule)
        {
            // 同点のときは、パスの短い方 → パスの辞書順 で決める(毎回同じ結果になるように)
            IOrderedEnumerable<FileRow> ordered;
            switch (rule)
            {
                case KeepRule.Newest:
                    ordered = group.OrderByDescending(r => r.LastWriteTime).ThenBy(r => r.Path.Length);
                    break;
                case KeepRule.Oldest:
                    ordered = group.OrderBy(r => r.LastWriteTime).ThenBy(r => r.Path.Length);
                    break;
                case KeepRule.LongestPath:
                    ordered = group.OrderByDescending(r => r.Path.Length);
                    break;
                case KeepRule.RootOrder:
                    ordered = group.OrderBy(r => r.Entry.RootIndex).ThenBy(r => r.Path.Length);
                    break;
                default:
                    ordered = group.OrderBy(r => r.Path.Length);
                    break;
            }
            return ordered.ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).First();
        }

        /// <summary>指定した1件を残し、同じグループの他すべてに削除チェックを付ける</summary>
        public static void KeepOnly(IEnumerable<FileRow> rows, FileRow keep)
        {
            foreach (FileRow row in rows.Where(r => r.GroupNumber == keep.GroupNumber))
            {
                row.IsMarked = row != keep;
            }
        }

        /// <summary>
        /// 指定フォルダ配下にあるファイルを残し、同じグループのフォルダ外のファイルに削除チェックを付ける。
        /// フォルダ配下のファイルを1件も含まないグループには触らない。
        /// 戻り値は対象になったグループ数
        /// </summary>
        public static int KeepUnderFolder(IEnumerable<FileRow> rows, string folder)
        {
            string prefix = folder.TrimEnd('\\') + "\\";
            int count = 0;
            foreach (IGrouping<int, FileRow> group in rows.GroupBy(r => r.GroupNumber))
            {
                if (!group.Any(r => IsUnder(r.Path, prefix)))
                {
                    continue;
                }
                foreach (FileRow row in group)
                {
                    row.IsMarked = !IsUnder(row.Path, prefix);
                }
                count++;
            }
            return count;
        }

        private static bool IsUnder(string path, string folderPrefix)
        {
            return path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static void ClearAll(IEnumerable<FileRow> rows)
        {
            foreach (FileRow row in rows)
            {
                row.IsMarked = false;
            }
        }

        /// <summary>全件に削除チェックが付いている(=全部消えてしまう)グループの番号</summary>
        public static List<int> FindFullyMarkedGroups(IEnumerable<FileRow> rows)
        {
            return rows.GroupBy(r => r.GroupNumber)
                .Where(g => g.All(r => r.IsMarked))
                .Select(g => g.Key)
                .OrderBy(n => n)
                .ToList();
        }
    }

    internal static class SizeFormatter
    {
        private static readonly string[] _units = { "B", "KB", "MB", "GB", "TB" };

        public static string Format(long bytes)
        {
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < _units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? bytes.ToString("N0") + " B" : value.ToString("0.00") + " " + _units[unit];
        }
    }
}
