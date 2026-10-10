using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace DuplicateFinder
{
    /// <summary>
    /// フォルダ以下(サブフォルダ含む)の重複ファイルを探す。大きな動画が多くても速いよう、読む量を最小にしている。
    /// 1. サイズが同じファイルだけを候補にする(サイズ違いは1バイトも読まずに除外)
    /// 2. 先頭と末尾の一部だけ読んで比べる(別物の動画はほぼここで脱落する)
    /// 3. 残った候補は全員を同時に少しずつ読み進めながらバイナリで直接比べ、違いが出たファイルはその時点で読むのをやめる。
    ///    ハッシュ値ではなく中身そのものを比べるので、ハッシュ衝突による誤判定も起きない
    /// </summary>
    internal sealed class DuplicateScanner
    {
        internal const int SampleSize = 64 * 1024;
        private const int _maxChunkSize = 32 * 1024 * 1024;
        private const int _minChunkSize = 256 * 1024;
        // 1グループの全体比較で使うバッファ合計の上限(ファイル数で割ってチャンクサイズを決める)
        private const long _maxGroupBufferBudget = 64L * 1024 * 1024;
        // 同時比較数を増やしたときの、全グループ合計のバッファの上限(32並列でも1GBに収める)
        private const long _totalBufferBudget = 1024L * 1024 * 1024;
        // これ以下の件数なら総当たりで仕分ける。多いときは簡易ハッシュで振り分けてから比べる
        private const int _linearPartitionLimit = 8;
        // .NETのFileAttributesに無い、クラウドの未ダウンロードファイルを表す属性(読むとダウンロードが始まってしまう)
        private const FileAttributes _recallOnOpen = (FileAttributes)0x00040000;
        private const FileAttributes _recallOnDataAccess = (FileAttributes)0x00400000;

        private readonly ScanOptions _options;
        private readonly IProgress<ScanProgress> _progress;
        private readonly CancellationToken _token;
        private readonly ScanResult _result = new ScanResult();
        private readonly object _lock = new object();
        private readonly Stopwatch _reportTimer = Stopwatch.StartNew();
        private readonly long _groupBufferBudget;
        private ScanPhase _phase = ScanPhase.Enumerating;
        private int _filesFound;
        private long _processedBytes;
        private long _totalBytes;
        private long _bytesRead;

        private DuplicateScanner(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
            _options = options;
            _progress = progress;
            _token = token;
            _groupBufferBudget = Math.Min(_maxGroupBufferBudget, _totalBufferBudget / Math.Max(1, options.MaxParallelism));
        }

        /// <summary>
        /// 重複ファイルを探す(時間がかかるので、UIからはTask.Runで呼ぶ)。
        /// 中止されたときは例外にせず、それまでに比較し終えて重複と確定したグループだけを返す(Cancelled=true)
        /// </summary>
        public static ScanResult Scan(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
            return new DuplicateScanner(options, progress, token).Run();
        }

        private ScanResult Run()
        {
            Stopwatch watch = Stopwatch.StartNew();
            var found = new ConcurrentBag<DuplicateGroup>();
            try
            {
                List<FileEntry> files = EnumerateFiles();
                _result.ScannedFileCount = files.Count;
                CompareAll(files, found);
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                _result.Cancelled = true;
            }
            catch (AggregateException) when (_token.IsCancellationRequested)
            {
                _result.Cancelled = true;
            }

            // 消したときに空く容量が大きい順(一番効くものから見られるように)
            _result.Groups.AddRange(found
                .OrderByDescending(g => g.WastedBytes)
                .ThenBy(g => g.Files[0].Path, StringComparer.OrdinalIgnoreCase));
            _result.BytesRead = Interlocked.Read(ref _bytesRead);
            _result.ProcessedBytes = Interlocked.Read(ref _processedBytes);
            _result.TotalBytes = _totalBytes;
            _result.ReachedComparing = _phase == ScanPhase.Comparing;
            _result.Elapsed = watch.Elapsed;
            Report(null, true);
            return _result;
        }

        // 比較し終えて重複と確定したグループから順にfoundへ入れる(中止されても、入れた分はそのまま使える)
        private void CompareAll(List<FileEntry> files, ConcurrentBag<DuplicateGroup> found)
        {
            // フォルダをまたぐ重複だけ探すときは、同じサイズのファイルが1つの対象フォルダにしか無ければ比べるまでもない
            List<List<FileEntry>> candidates = files
                .GroupBy(f => f.Length)
                .Where(g => g.Count() >= 2)
                .Where(g => !_options.CrossRootOnly || g.Select(f => f.RootIndex).Distinct().Skip(1).Any())
                .Select(g => g.ToList())
                .ToList();
            _totalBytes = candidates.Sum(g => g[0].Length * g.Count);
            _phase = ScanPhase.Comparing;
            Report(null, true);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelism),
                CancellationToken = _token,
            };
            Parallel.ForEach(candidates, parallelOptions, group =>
            {
                // 途中で中止されたサイズグループは、ここまで来ずに捨てられる(確定していないものは出さない)
                foreach (List<FileEntry> duplicates in CompareGroup(group))
                {
                    var duplicateGroup = new DuplicateGroup(duplicates);
                    if (!_options.CrossRootOnly || duplicateGroup.RootCount >= 2)
                    {
                        found.Add(duplicateGroup);
                    }
                }
            });
        }

        // *******************************************************************************
        // 1. ファイル一覧の作成

        private List<FileEntry> EnumerateFiles()
        {
            List<string> roots = _options.RootFolders.Select(NormalizeFolder).ToList();
            var files = new List<FileEntry>();
            var pending = new Stack<string>();
            var existing = new List<string>();
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(root))
                {
                    existing.Add(root);
                }
                else
                {
                    AddError("対象フォルダが見つかりません: " + root);
                }
            }
            foreach (string root in StartFolders(existing))
            {
                pending.Push(root);
            }
            while (pending.Count > 0)
            {
                _token.ThrowIfCancellationRequested();
                var dir = new DirectoryInfo(pending.Pop());
                FileSystemInfo[] entries;
                try
                {
                    entries = dir.GetFileSystemInfos();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    AddError("フォルダを開けませんでした: " + dir.FullName + " (" + ex.Message + ")");
                    continue;
                }

                foreach (FileSystemInfo info in entries)
                {
                    FileAttributes attributes = info.Attributes;
                    if (_options.SkipHiddenAndSystem && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        // ジャンクション・シンボリックリンクのフォルダは辿らない(同じファイルを2回数えたり、無限ループしたりするため)
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(info.FullName);
                        }
                        continue;
                    }
                    if ((attributes & (FileAttributes.Offline | _recallOnOpen | _recallOnDataAccess)) != 0)
                    {
                        _result.SkippedCloudFileCount++;
                        continue;
                    }

                    var file = (FileInfo)info;
                    long length = file.Length;
                    if (length == 0 || length < _options.MinSizeBytes)
                    {
                        continue;
                    }
                    if (_options.Extensions.Count > 0 && !_options.Extensions.Contains(file.Extension))
                    {
                        continue;
                    }
                    files.Add(new FileEntry(file.FullName, length, file.LastWriteTime, RootIndexOf(file.FullName, roots)));
                    _filesFound = files.Count;
                    Report(file.FullName, false);
                }
            }
            _filesFound = files.Count;
            return files;
        }

        // "D:\Videos\" → "D:\Videos"、"D:" → "D:\"(ドライブ直下だけは末尾の\を残す)
        internal static string NormalizeFolder(string folder)
        {
            string full = Path.GetFullPath(folder.Trim().Trim('"') + "\\");
            string trimmed = full.TrimEnd('\\');
            return trimmed.EndsWith(":") ? trimmed + "\\" : trimmed;
        }

        // 実際に探索を始めるフォルダ。重複指定や、他の対象フォルダの中にある対象フォルダは除く
        // (外側を探索すれば含まれるので、別に探索すると同じファイルを2回数えてしまう)
        internal static List<string> StartFolders(IList<string> roots)
        {
            List<string> distinct = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return distinct.Where(r => !distinct.Any(other => IsStrictlyUnder(r, other))).ToList();
        }

        // ファイルが何番目の対象フォルダのものか。入れ子のとき("D:\Videos"と"D:\Videos\本体")は、より深い方
        internal static int RootIndexOf(string path, IList<string> roots)
        {
            int found = 0;
            int foundLength = -1;
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i].Length > foundLength && IsStrictlyUnder(path, roots[i]))
                {
                    found = i;
                    foundLength = roots[i].Length;
                }
            }
            return found;
        }

        private static bool IsStrictlyUnder(string path, string folder)
        {
            string prefix = folder.EndsWith("\\") ? folder : folder + "\\";
            return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // *******************************************************************************
        // 2. 先頭・末尾での絞り込み → 3. 全体の比較

        // 同じサイズのファイル群を、中身が一致するもの同士(2件以上)に分ける
        private List<List<FileEntry>> CompareGroup(List<FileEntry> group)
        {
            long length = group[0].Length;
            // 小さいファイルは先頭・末尾を読むのと全体を読むのがほぼ同じなので、いきなり全体を比べる
            List<List<FileEntry>> classes = length <= SampleSize * 2
                ? new List<List<FileEntry>> { group }
                : PartitionBySample(group, length);

            var result = new List<List<FileEntry>>();
            foreach (List<FileEntry> files in classes)
            {
                result.AddRange(CompareContents(files, length));
            }
            return result;
        }

        private List<List<FileEntry>> PartitionBySample(List<FileEntry> group, long length)
        {
            var samples = new List<KeyValuePair<FileEntry, byte[]>>(group.Count);
            foreach (FileEntry file in group)
            {
                _token.ThrowIfCancellationRequested();
                Report(file.Path, false);
                byte[] sample = ReadSample(file);
                if (sample == null)
                {
                    AddProcessed(length);
                    continue;
                }
                samples.Add(new KeyValuePair<FileEntry, byte[]>(file, sample));
            }

            var result = new List<List<FileEntry>>();
            foreach (List<KeyValuePair<FileEntry, byte[]>> part in Partition(samples, s => s.Value, SampleSize * 2))
            {
                if (part.Count >= 2)
                {
                    result.Add(part.Select(s => s.Key).ToList());
                }
                else
                {
                    // ここで脱落したファイルは、残りを読まずに済んだ
                    AddProcessed(length * part.Count);
                }
            }
            return result;
        }

        // 先頭SampleSize + 末尾SampleSize を読む。読めなければnull
        private byte[] ReadSample(FileEntry file)
        {
            var buffer = new byte[SampleSize * 2];
            try
            {
                using (FileStream stream = OpenRead(file.Path))
                {
                    if (stream.Length != file.Length)
                    {
                        AddError("探索中にファイルが変更されました: " + file.Path);
                        return null;
                    }
                    ReadFully(stream, buffer, 0, SampleSize);
                    stream.Position = file.Length - SampleSize;
                    ReadFully(stream, buffer, SampleSize, SampleSize);
                }
                Interlocked.Add(ref _bytesRead, buffer.Length);
                return buffer;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                AddError("ファイルを読めませんでした: " + file.Path + " (" + ex.Message + ")");
                return null;
            }
        }

        // 全員を同時にチャンク単位で読み進め、チャンクの中身で仕分けし続ける。
        // 1件だけになった組はその時点で読むのをやめるので、別物同士なら最初のチャンクで終わる
        private List<List<FileEntry>> CompareContents(List<FileEntry> files, long length)
        {
            int chunkSize = ChooseChunkSize(files.Count, length, _groupBufferBudget);
            var readers = new List<ChunkReader>(files.Count);
            try
            {
                foreach (FileEntry file in files)
                {
                    string error;
                    ChunkReader reader = ChunkReader.Open(file, chunkSize, out error);
                    if (reader == null)
                    {
                        AddError(error);
                        AddProcessed(length);
                        continue;
                    }
                    readers.Add(reader);
                }

                var classes = new List<List<ChunkReader>>();
                if (readers.Count >= 2)
                {
                    classes.Add(readers);
                }
                else
                {
                    AddProcessed(length * readers.Count);
                }

                long offset = 0;
                while (offset < length && classes.Count > 0)
                {
                    _token.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(chunkSize, length - offset);
                    long restAfterChunk = length - offset - count;
                    var next = new List<List<ChunkReader>>();
                    foreach (List<ChunkReader> members in classes)
                    {
                        var alive = new List<ChunkReader>(members.Count);
                        foreach (ChunkReader reader in members)
                        {
                            Report(reader.Entry.Path, false);
                            string error;
                            if (reader.TryRead(count, out error))
                            {
                                alive.Add(reader);
                                Interlocked.Add(ref _bytesRead, count);
                                AddProcessed(count);
                            }
                            else
                            {
                                AddError(error);
                                AddProcessed(count + restAfterChunk);
                                reader.Dispose();
                            }
                        }

                        foreach (List<ChunkReader> part in Partition(alive, r => r.Buffer, count))
                        {
                            if (part.Count >= 2)
                            {
                                next.Add(part);
                                continue;
                            }
                            AddProcessed(restAfterChunk * part.Count);
                            // 脱落したファイルはここで閉じて、バッファもすぐ手放す
                            part[0].Dispose();
                        }
                    }
                    classes = next;
                    offset += count;
                }
                return classes.Select(c => c.Select(r => r.Entry).ToList()).ToList();
            }
            finally
            {
                foreach (ChunkReader reader in readers)
                {
                    reader.Dispose();
                }
            }
        }

        private static int ChooseChunkSize(int fileCount, long length, long groupBufferBudget)
        {
            long size = groupBufferBudget / Math.Max(1, fileCount);
            size = Math.Max(_minChunkSize, Math.Min(_maxChunkSize, size));
            return (int)Math.Min(size, length);
        }

        // *******************************************************************************
        // 仕分け・バイト比較

        // バッファの先頭count バイトが一致するもの同士に分ける
        internal static List<List<T>> Partition<T>(IList<T> items, Func<T, byte[]> getBuffer, int count)
        {
            if (items.Count <= _linearPartitionLimit)
            {
                var parts = new List<List<T>>();
                foreach (T item in items)
                {
                    AddToMatchingPart(parts, item, getBuffer, count);
                }
                return parts;
            }

            // 件数が多いときは、簡易ハッシュで振り分けてから同じ振り分け先の中だけで比べる(一致の判定は必ずバイト比較)
            var buckets = new Dictionary<ulong, List<List<T>>>();
            foreach (T item in items)
            {
                ulong key = QuickHash(getBuffer(item), count);
                List<List<T>> parts;
                if (!buckets.TryGetValue(key, out parts))
                {
                    parts = new List<List<T>>();
                    buckets.Add(key, parts);
                }
                AddToMatchingPart(parts, item, getBuffer, count);
            }
            return buckets.Values.SelectMany(p => p).ToList();
        }

        private static void AddToMatchingPart<T>(List<List<T>> parts, T item, Func<T, byte[]> getBuffer, int count)
        {
            byte[] buffer = getBuffer(item);
            foreach (List<T> part in parts)
            {
                if (BytesEqual(getBuffer(part[0]), buffer, count))
                {
                    part.Add(item);
                    return;
                }
            }
            parts.Add(new List<T> { item });
        }

        // 振り分け用の簡易ハッシュ(FNV-1a)。速さ優先で、最大64か所の8バイトだけを見る
        private static ulong QuickHash(byte[] buffer, int count)
        {
            const ulong offsetBasis = 14695981039346656037;
            const ulong prime = 1099511628211;
            ulong hash = offsetBasis;
            int words = count / 8;
            int step = Math.Max(1, words / 64);
            for (int i = 0; i < words; i += step)
            {
                hash = (hash ^ BitConverter.ToUInt64(buffer, i * 8)) * prime;
            }
            for (int i = words * 8; i < count; i++)
            {
                hash = (hash ^ buffer[i]) * prime;
            }
            return hash;
        }

        internal static bool BytesEqual(byte[] a, byte[] b, int count)
        {
            return count == 0 || memcmp(a, b, (UIntPtr)count) == 0;
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(byte[] b1, byte[] b2, UIntPtr count);

        // *******************************************************************************
        // ファイル読み込み

        private static FileStream OpenRead(string path)
        {
            // 自前で大きく読むので、FileStream内部のバッファは使わない(bufferSize=1で無効)。
            // 動画プレーヤー等で開いているファイルも比較できるよう、共有モードは緩めにする
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    throw new EndOfStreamException("ファイルが途中で終わっています");
                }
                offset += read;
                count -= read;
            }
        }

        private sealed class ChunkReader : IDisposable
        {
            private FileStream _stream;

            private ChunkReader(FileEntry entry, FileStream stream, int chunkSize)
            {
                Entry = entry;
                _stream = stream;
                Buffer = new byte[chunkSize];
            }

            public FileEntry Entry { get; }
            public byte[] Buffer { get; private set; }

            public static ChunkReader Open(FileEntry entry, int chunkSize, out string error)
            {
                FileStream stream = null;
                try
                {
                    stream = OpenRead(entry.Path);
                    if (stream.Length != entry.Length)
                    {
                        stream.Dispose();
                        error = "探索中にファイルが変更されました: " + entry.Path;
                        return null;
                    }
                    error = null;
                    return new ChunkReader(entry, stream, chunkSize);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    stream?.Dispose();
                    error = "ファイルを読めませんでした: " + entry.Path + " (" + ex.Message + ")";
                    return null;
                }
            }

            public bool TryRead(int count, out string error)
            {
                try
                {
                    ReadFully(_stream, Buffer, 0, count);
                    error = null;
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    error = "ファイルを読めませんでした: " + Entry.Path + " (" + ex.Message + ")";
                    return false;
                }
            }

            public void Dispose()
            {
                _stream?.Dispose();
                _stream = null;
                Buffer = null;
            }
        }

        // *******************************************************************************
        // 進捗・エラー

        private void AddProcessed(long bytes)
        {
            Interlocked.Add(ref _processedBytes, bytes);
        }

        private void AddError(string message)
        {
            lock (_lock)
            {
                _result.Errors.Add(message);
            }
        }

        // UIが追いつかなくならないよう、通知は一定間隔に間引く
        private void Report(string currentPath, bool force)
        {
            if (_progress == null)
            {
                return;
            }
            lock (_lock)
            {
                if (!force && _reportTimer.ElapsedMilliseconds < _options.ReportIntervalMs)
                {
                    return;
                }
                _reportTimer.Restart();
            }
            _progress.Report(new ScanProgress
            {
                Phase = _phase,
                FilesFound = _filesFound,
                ProcessedBytes = Interlocked.Read(ref _processedBytes),
                TotalBytes = _totalBytes,
                CurrentPath = currentPath,
            });
        }
    }
}
