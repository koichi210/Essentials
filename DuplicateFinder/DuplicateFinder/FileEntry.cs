using System;
using System.Collections.Generic;
using System.Linq;

namespace DuplicateFinder
{
    /// <summary>探索で見つけた1ファイル</summary>
    internal sealed class FileEntry
    {
        public FileEntry(string path, long length, DateTime lastWriteTime, int rootIndex = 0)
        {
            Path = path;
            Length = length;
            LastWriteTime = lastWriteTime;
            RootIndex = rootIndex;
        }

        public string Path { get; }
        public long Length { get; }
        public DateTime LastWriteTime { get; }

        /// <summary>何番目の対象フォルダ(0始まり)にあるか。対象フォルダが入れ子のときは、より深い方</summary>
        public int RootIndex { get; }

        public override string ToString()
        {
            return Path;
        }
    }

    /// <summary>中身がバイナリ一致したファイルのまとまり(2件以上)</summary>
    internal sealed class DuplicateGroup
    {
        public DuplicateGroup(IEnumerable<FileEntry> files)
        {
            Files = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            Length = Files[0].Length;
        }

        public IReadOnlyList<FileEntry> Files { get; }
        public long Length { get; }

        /// <summary>1件だけ残して他を消したときに空く容量</summary>
        public long WastedBytes
        {
            get { return Length * (Files.Count - 1); }
        }

        /// <summary>何か所の対象フォルダにまたがっているか</summary>
        public int RootCount
        {
            get { return Files.Select(f => f.RootIndex).Distinct().Count(); }
        }
    }

    /// <summary>探索の条件</summary>
    internal sealed class ScanOptions
    {
        /// <summary>探すフォルダ(サブフォルダも対象)。どのフォルダにあるファイル同士でも比べる</summary>
        public IList<string> RootFolders { get; set; } = new List<string>();

        /// <summary>trueなら、別々の対象フォルダにまたがる重複だけを探す(同じ対象フォルダの中だけでの重複は無視)</summary>
        public bool CrossRootOnly { get; set; }

        /// <summary>これより小さいファイルは対象外(0バイトのファイルは常に対象外)</summary>
        public long MinSizeBytes { get; set; }

        /// <summary>対象の拡張子(".mp4"の形・大小文字無視)。空なら全ファイル</summary>
        public ISet<string> Extensions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>隠し・システム属性のファイル/フォルダを飛ばす(desktop.ini・Thumbs.db等が大量に重複扱いになるのを防ぐ)</summary>
        public bool SkipHiddenAndSystem { get; set; } = true;

        /// <summary>同時に比較するグループ数。HDDは1、SSDなら2〜8が速い</summary>
        public int MaxParallelism { get; set; } = 1;

        /// <summary>"mp4; .MKV, avi" のような入力を拡張子の集合にする</summary>
        public static ISet<string> ParseExtensions(string text)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }
            foreach (string token in text.Split(new[] { ';', ',', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string ext = token.Trim().TrimStart('*');
                if (ext.Length == 0 || ext == ".")
                {
                    continue;
                }
                result.Add(ext.StartsWith(".") ? ext : "." + ext);
            }
            return result;
        }
    }

    internal enum ScanPhase
    {
        Enumerating,
        Comparing,
    }

    /// <summary>進捗通知の中身</summary>
    internal sealed class ScanProgress
    {
        public ScanPhase Phase { get; set; }
        public int FilesFound { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string CurrentPath { get; set; }
    }

    internal sealed class ScanResult
    {
        public List<DuplicateGroup> Groups { get; } = new List<DuplicateGroup>();
        public List<string> Errors { get; } = new List<string>();
        public int ScannedFileCount { get; set; }

        /// <summary>クラウド上にしか無い(未ダウンロードの)ため読まずに飛ばしたファイル数</summary>
        public int SkippedCloudFileCount { get; set; }

        /// <summary>比較のために実際に読んだバイト数</summary>
        public long BytesRead { get; set; }

        public TimeSpan Elapsed { get; set; }
    }
}
