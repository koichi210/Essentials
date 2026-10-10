using System;
using System.Collections;
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
        // 設定ファイルには数値で保存される。廃止した番号(0〜3: 更新日時の新旧・フルパスの長短)は使い回さないこと
        // 対象フォルダ欄で上にあるフォルダのものを残す(上を本体・下をバックアップにする使い方)
        RootOrder = 4,
        // 一覧で各グループの一番上に表示されているものを残す(列ヘッダで並べ替えてから使う)
        TopOfGroup = 5,
        ShortestFileName = 6,
        LongestFileName = 7,
        ShortestFolder = 8,
        LongestFolder = 9,
    }

    /// <summary>削除対象のチェックを付け外しする処理(画面から切り離してテストできるようにしてある)</summary>
    internal static class RowMarker
    {
        /// <summary>
        /// 各グループでルールに合う1件を残し、他すべてに削除チェックを付ける。
        /// rowsは一覧の表示順で渡す(同じ条件のものが複数あるときは、一覧で上にある方を残す)
        /// </summary>
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

        // OrderByは安定ソートなので、同点のものは渡された順(=一覧の表示順)のまま残る
        internal static FileRow PickKeeper(IEnumerable<FileRow> group, KeepRule rule)
        {
            switch (rule)
            {
                case KeepRule.RootOrder:
                    return group.OrderBy(r => r.Entry.RootIndex).First();
                case KeepRule.ShortestFileName:
                    return group.OrderBy(r => r.FileName.Length).First();
                case KeepRule.LongestFileName:
                    return group.OrderByDescending(r => r.FileName.Length).First();
                case KeepRule.ShortestFolder:
                    return group.OrderBy(r => r.Folder.Length).First();
                case KeepRule.LongestFolder:
                    return group.OrderByDescending(r => r.Folder.Length).First();
                default:
                    return group.First();
            }
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

    /// <summary>
    /// 一覧の並べ替え。グループの並び(グループ番号順)は変えず、グループの中だけを指定した列で並べる。
    /// 同点のときはパスの順(検索直後の並び)
    /// </summary>
    internal sealed class RowComparer : IComparer
    {
        private readonly string _key;
        private readonly int _sign;

        /// <param name="key">並べる列(FileRowのプロパティ名)。nullなら検索直後の並び(パスの順)</param>
        public RowComparer(string key, ListSortDirection direction)
        {
            _key = key;
            _sign = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare(object x, object y)
        {
            var a = (FileRow)x;
            var b = (FileRow)y;
            int result = a.GroupNumber.CompareTo(b.GroupNumber);
            if (result != 0)
            {
                return result;
            }
            result = _sign * CompareByKey(a, b);
            return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
        }

        private int CompareByKey(FileRow a, FileRow b)
        {
            switch (_key)
            {
                case nameof(FileRow.IsMarked):
                    return a.IsMarked.CompareTo(b.IsMarked);
                case nameof(FileRow.RootNumber):
                    return a.RootNumber.CompareTo(b.RootNumber);
                case nameof(FileRow.FileName):
                    return StringComparer.CurrentCultureIgnoreCase.Compare(a.FileName, b.FileName);
                case nameof(FileRow.LastWriteTime):
                    return a.LastWriteTime.CompareTo(b.LastWriteTime);
                case nameof(FileRow.Folder):
                    return StringComparer.CurrentCultureIgnoreCase.Compare(a.Folder, b.Folder);
                default:
                    return 0;
            }
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
