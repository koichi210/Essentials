using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using StandardTemplate;

namespace HeifToPng
{
    internal sealed class ConvertOptions
    {
        // 変換元(フォルダとファイルが混在してよい)
        public List<string> Sources { get; set; } = new List<string>();
        public bool IncludeSubfolders { get; set; } = true;
        public bool Overwrite { get; set; }
        public bool KeepTimestamps { get; set; } = true;
        public int MaxParallelism { get; set; } = 1;
    }

    internal sealed class ConvertProgress
    {
        public bool Enumerating { get; set; }
        public int Total { get; set; }
        public int Done { get; set; }
        public string CurrentPath { get; set; }
    }

    internal sealed class ConvertResult
    {
        public int Total { get; set; }
        public int Converted { get; set; }
        public int Skipped { get; set; }
        // 「パス: 理由」の形
        public List<string> Failures { get; } = new List<string>();
        public bool Cancelled { get; set; }
    }

    // 1ファイル分の変換予定。出力先が他と重なるものは Conflict に理由を入れて変換しない
    internal sealed class ConvertJob
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string Conflict { get; set; }
    }

    internal enum JobOutcome
    {
        Converted,
        Skipped,
    }

    /// <summary>
    /// HEIF(.heic/.heif/.hif)をPNGに変換する。読み書きはMagick.NET(libheif内蔵)で行うので、Windowsの「HEIF画像拡張機能」は要らない。
    /// 最初はWindowsのWIC(HEIF画像拡張機能)で読んでいたが、WICのHEIFデコーダーはプロセス内で1枚ずつしか動かず、
    /// 並列にしても約2秒/枚から縮まなかったので差し替えた(Magick.NETは約0.3秒/枚で、並列にすると素直に速くなる)。
    /// 回転(irot/imir)はlibheifが適用済みの画素を返すので、EXIFの向きをこちらで当て直さない
    /// (iPhoneの写真はEXIFにも向きが残っていることがあり、当て直すと二重に回ってしまう)。
    /// </summary>
    internal static class HeifConverter
    {
        public static readonly string[] Extensions = { ".heic", ".heif", ".hif" };

        // 進捗の通知は間引く(1件ごとに出すと数千件で画面が詰まる)
        private const int _progressIntervalMs = 100;

        // PNGの圧縮レベル(0〜9)。ImageMagickの既定(7相当)だと書き出しが約5秒/枚かかる。
        // 1なら約0.8秒/枚で、サイズは既定より2割増える程度(それでもWICで書いていた頃より小さい)
        private const string _pngCompressionLevel = "1";

        public static bool IsHeif(string path)
        {
            string ext = Path.GetExtension(path);
            return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetOutputPath(string sourcePath)
        {
            return Path.ChangeExtension(sourcePath, ".png");
        }

        /// <summary>
        /// 変換元からHEIFファイルを集めて変換予定を作る。読めないフォルダは failures に積んで続ける。
        /// 同じファイルを2回指定しても1回だけにし、IMG_1.heic と IMG_1.heif のように出力先が重なるものは先の1つだけを変換する
        /// </summary>
        public static List<ConvertJob> PlanJobs(IEnumerable<string> sources, bool includeSubfolders, List<string> failures,
            Action<int, string> onFound, CancellationToken token)
        {
            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var jobs = new List<ConvertJob>();
            foreach (string source in sources)
            {
                token.ThrowIfCancellationRequested();
                IEnumerable<string> files;
                if (Directory.Exists(source))
                {
                    files = EnumerateHeifFiles(source, includeSubfolders, failures, token);
                }
                else if (File.Exists(source))
                {
                    files = IsHeif(source) ? new[] { source } : new string[0];
                }
                else
                {
                    failures.Add(source + ": 見つかりません");
                    continue;
                }

                foreach (string file in files)
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!seenSources.Add(fullPath))
                    {
                        continue;
                    }
                    var job = new ConvertJob { SourcePath = fullPath, OutputPath = GetOutputPath(fullPath) };
                    string owner;
                    if (outputOwners.TryGetValue(job.OutputPath, out owner))
                    {
                        job.Conflict = "出力先 " + Path.GetFileName(job.OutputPath) + " が " + Path.GetFileName(owner) + " の変換結果と重なるため変換しません";
                    }
                    else
                    {
                        outputOwners.Add(job.OutputPath, fullPath);
                    }
                    jobs.Add(job);
                    onFound?.Invoke(jobs.Count, fullPath);
                }
            }
            return jobs;
        }

        // サブフォルダは自前でたどる(Directory.EnumerateFilesのAllDirectoriesは、読めないフォルダが1つあるとそこで全体が止まる)
        private static IEnumerable<string> EnumerateHeifFiles(string root, bool includeSubfolders, List<string> failures, CancellationToken token)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string folder = pending.Pop();
                string[] files;
                string[] subfolders;
                try
                {
                    files = Directory.GetFiles(folder);
                    subfolders = includeSubfolders ? Directory.GetDirectories(folder) : new string[0];
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    failures.Add(folder + ": フォルダを読めません(" + ex.Message + ")");
                    continue;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files.Where(IsHeif))
                {
                    yield return file;
                }
                // Stackなので逆順に積むと名前順にたどれる
                Array.Sort(subfolders, StringComparer.OrdinalIgnoreCase);
                for (int i = subfolders.Length - 1; i >= 0; i--)
                {
                    pending.Push(subfolders[i]);
                }
            }
        }

        /// <summary>1ファイルを変換する。出力先にPNGがあって上書きしないならSkipped。失敗は例外で返す</summary>
        public static JobOutcome ConvertFile(string sourcePath, string outputPath, bool overwrite, bool keepTimestamps)
        {
            if (!overwrite && File.Exists(outputPath))
            {
                return JobOutcome.Skipped;
            }

            using (var image = new MagickImage(sourcePath))
            {
                RemoveMetadataExceptColor(image);
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", _pngCompressionLevel);
                // 途中で中止・失敗しても書きかけのPNGを残さない(一時ファイルは拡張子が .tmp なので形式を明示する)
                AtomicFile.Write(outputPath, tempPath => image.Write(tempPath, MagickFormat.Png));
            }

            if (keepTimestamps)
            {
                // 写真を撮影順に並べたいとき更新日時で並べられるように、元ファイルの日時を写す
                File.SetCreationTimeUtc(outputPath, File.GetCreationTimeUtc(sourcePath));
                File.SetLastWriteTimeUtc(outputPath, File.GetLastWriteTimeUtc(sourcePath));
            }
            return JobOutcome.Converted;
        }

        /// <summary>
        /// カラープロファイル(ICC)以外のメタデータを消す。ImageMagickは何もしないとEXIF(撮影位置のGPSを含む)とXMPを
        /// eXIfチャンクと大量のtEXtチャンクにしてPNGへ書き出すので、PNGを人に渡したとき撮影場所まで伝わってしまう。
        /// 色はプロファイルごと持っていく(iPhoneの写真はDisplay P3なので、ここで落とすと色が薄くなる)
        /// </summary>
        private static void RemoveMetadataExceptColor(MagickImage image)
        {
            foreach (string profileName in image.ProfileNames.ToList())
            {
                if (!string.Equals(profileName, "icc", StringComparison.OrdinalIgnoreCase))
                {
                    image.RemoveProfile(profileName);
                }
            }
            // exif:GPSLatitude などはプロファイルを消しても属性として残り、tEXtチャンクになる
            foreach (string attributeName in image.AttributeNames.ToList())
            {
                image.RemoveAttribute(attributeName);
            }
        }

        public static ConvertResult Run(ConvertOptions options, IProgress<ConvertProgress> progress, CancellationToken token)
        {
            var result = new ConvertResult();
            List<ConvertJob> jobs;
            int lastReport = Environment.TickCount;
            try
            {
                jobs = PlanJobs(options.Sources, options.IncludeSubfolders, result.Failures, (count, path) =>
                {
                    if (unchecked(Environment.TickCount - lastReport) >= _progressIntervalMs)
                    {
                        lastReport = Environment.TickCount;
                        progress?.Report(new ConvertProgress { Enumerating = true, Total = count, CurrentPath = path });
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                return result;
            }

            result.Total = jobs.Count;
            var failures = new List<string>();
            int done = 0;
            int converted = 0;
            int skipped = 0;
            object reportLock = new object();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
                CancellationToken = token,
            };
            try
            {
                Parallel.ForEach(jobs, parallelOptions, job =>
                {
                    string failure = null;
                    if (job.Conflict != null)
                    {
                        failure = job.Conflict;
                    }
                    else
                    {
                        try
                        {
                            if (ConvertFile(job.SourcePath, job.OutputPath, options.Overwrite, options.KeepTimestamps) == JobOutcome.Converted)
                            {
                                Interlocked.Increment(ref converted);
                            }
                            else
                            {
                                Interlocked.Increment(ref skipped);
                            }
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is MagickException)
                        {
                            failure = DescribeFailure(ex);
                        }
                    }

                    lock (reportLock)
                    {
                        if (failure != null)
                        {
                            failures.Add(job.SourcePath + ": " + failure);
                        }
                        done++;
                        if (done == jobs.Count || unchecked(Environment.TickCount - lastReport) >= _progressIntervalMs)
                        {
                            lastReport = Environment.TickCount;
                            progress?.Report(new ConvertProgress { Total = jobs.Count, Done = done, CurrentPath = job.SourcePath });
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }

            result.Converted = converted;
            result.Skipped = skipped;
            // 並列で終わった順はバラバラなので、パス順に並べ直して出す
            result.Failures.AddRange(failures.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        // ImageMagickのエラー文は英語で長い(パスや内部の関数名まで入る)ので、何が起きたかわかる日本語にする
        private static string DescribeFailure(Exception ex)
        {
            if (ex is MagickException)
            {
                return "画像として読めません(ファイルが壊れているか、中身がHEIFではありません)";
            }
            return ex.Message;
        }
    }
}
