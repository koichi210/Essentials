using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryGadget
{
    /// <summary>スキン1つ分(フォルダ名=ID)。</summary>
    internal sealed class SkinInfo
    {
        public SkinInfo(string id, string name, string? folder, double? bubbleOverlap)
        {
            Id = id;
            Name = name;
            Folder = folder;
            BubbleOverlap = bubbleOverlap;
        }

        public string Id { get; }

        /// <summary>メニュー表示名(skin.json の Name、省略時はフォルダ名)</summary>
        public string Name { get; }

        /// <summary>スキンのフォルダ。nullなら埋め込みリソースの既定絵。</summary>
        public string? Folder { get; }

        /// <summary>絵の幅に対する吹き出しの重なり率(手動指定)。nullなら自動計算。</summary>
        public double? BubbleOverlap { get; }
    }

    /// <summary>読み込んだスキン画像と、行ごとの「最も左の不透明画素」(重なり率の自動計算用)。</summary>
    internal sealed class SkinImage
    {
        public SkinImage(BitmapSource bitmap, int[] leftmostOpaque)
        {
            Bitmap = bitmap;
            LeftmostOpaque = leftmostOpaque;
        }

        public BitmapSource Bitmap { get; }

        public int PixelWidth => Bitmap.PixelWidth;

        public int PixelHeight => Bitmap.PixelHeight;

        /// <summary>行ごとに α&gt;32 の最も左の x。不透明画素が無い行は PixelWidth。</summary>
        public int[] LeftmostOpaque { get; }
    }

    /// <summary>
    /// スキン一覧。&lt;exeのフォルダ&gt;\Skins\(同梱) と %LOCALAPPDATA%\SecGad\skins\(追加分) を
    /// フォルダ名の昇順(大小無視)で走査し、secretary.png があるものだけ採用する。同じIDは同梱を優先。
    /// </summary>
    internal static class SkinCatalog
    {
        private const string _imageFileName = "secretary.png";
        private const string _iconFileName = "icon.ico";
        private const string _metaFileName = "skin.json";

        public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "Skins");

        public static string UserDir => Path.Combine(Settings.LocalDir, "skins");

        /// <summary>スキンが1つも無いときに使う、埋め込みの既定絵だけのスキン</summary>
        public static SkinInfo BuiltIn { get; } = new SkinInfo("builtin", "秘書さん", null, null);

        public static List<SkinInfo> Scan()
        {
            var result = new List<SkinInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new[] { BundledDir, UserDir })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                IEnumerable<string> dirs;
                try
                {
                    dirs = Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    Log.Write("スキンフォルダを読めませんでした: " + ex.GetType().Name);
                    continue;
                }
                foreach (string dir in dirs)
                {
                    string id = Path.GetFileName(dir);
                    if (!File.Exists(Path.Combine(dir, _imageFileName)) || !seen.Add(id))
                    {
                        continue;
                    }
                    result.Add(ReadMeta(id, dir));
                }
            }
            // 同梱→追加の順に走査したので、最後に全体を名前順へ揃える
            return result.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>IDで探す。見つからなければ一覧の先頭、一覧が空なら既定絵。</summary>
        public static SkinInfo Pick(IReadOnlyList<SkinInfo> skins, string? id)
        {
            SkinInfo? hit = skins.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            return hit ?? (skins.Count > 0 ? skins[0] : BuiltIn);
        }

        /// <summary>
        /// 絵を読む。BitmapCacheOption.OnLoad でファイルを掴まない(差し替え可能)。読めなければ埋め込みの既定絵。
        /// </summary>
        public static SkinImage LoadImage(SkinInfo skin)
        {
            BitmapSource? bitmap = null;
            if (skin.Folder != null)
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bi.UriSource = new Uri(Path.Combine(skin.Folder, _imageFileName));
                    bi.EndInit();
                    bi.Freeze();
                    bitmap = bi;
                }
                catch (Exception ex)
                {
                    Log.Write("スキン画像が読めないため既定絵を使います: " + ex.GetType().Name);
                }
            }
            bitmap ??= LoadBuiltInImage();
            return new SkinImage(bitmap, ComputeLeftmost(bitmap));
        }

        /// <summary>トレイアイコン。無い・読めない場合は埋め込みの Assets\secretary.ico。</summary>
        public static Icon LoadIcon(SkinInfo skin)
        {
            if (skin.Folder != null)
            {
                string path = Path.Combine(skin.Folder, _iconFileName);
                if (File.Exists(path))
                {
                    try
                    {
                        // ファイルを掴まないようメモリに読んでから作る
                        using var ms = new MemoryStream(File.ReadAllBytes(path));
                        return new Icon(ms);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("スキンのアイコンが読めないため既定アイコンを使います: " + ex.GetType().Name);
                    }
                }
            }
            using Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/secretary.ico")).Stream;
            return new Icon(stream);
        }

        /// <summary>行ごとに α&gt;32 の最も左の x を求める(画像読込時に前計算)。</summary>
        public static int[] ComputeLeftmost(BitmapSource bitmap)
        {
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);
            int[] leftmost = new int[h];
            for (int y = 0; y < h; y++)
            {
                leftmost[y] = w;
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x * 4 + 3] > 32)
                    {
                        leftmost[y] = x;
                        break;
                    }
                }
            }
            return leftmost;
        }

        private static BitmapSource LoadBuiltInImage()
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri("pack://application:,,,/Assets/secretary.png");
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private static SkinInfo ReadMeta(string id, string dir)
        {
            string name = id;
            double? overlap = null;
            string metaPath = Path.Combine(dir, _metaFileName);
            if (File.Exists(metaPath))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (doc.RootElement.TryGetProperty("Name", out JsonElement n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                    {
                        name = n.GetString()!;
                    }
                    if (doc.RootElement.TryGetProperty("BubbleOverlap", out JsonElement o) && o.ValueKind == JsonValueKind.Number)
                    {
                        overlap = o.GetDouble();
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("skin.json が読めないため既定値を使います: " + ex.GetType().Name);
                }
            }
            return new SkinInfo(id, name, dir, overlap);
        }
    }
}
