using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SecretaryGadget
{
    /// <summary>
    /// 「秘書さんを呼ぶ」の吹き出しに並べる選択肢1行分(settings.json の CallMenu の1要素)。
    /// Path は起動するファイル(exe・bat・ショートカット等。環境変数可)か、「@」で始まる組み込みの機能名。
    /// </summary>
    internal sealed class CallMenuEntry
    {
        /// <summary>組み込みの機能名の頭に付ける印</summary>
        public const string BuiltinPrefix = "@";

        public string Label { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        /// <summary>起動するファイルに渡す引数(組み込みの機能では使わない)</summary>
        public string Arguments { get; set; } = string.Empty;

        public bool IsBuiltin => Path.StartsWith(BuiltinPrefix, StringComparison.Ordinal);

        /// <summary>組み込みの機能名(@ を除いたもの、小文字)。組み込みでなければ空。</summary>
        public string BuiltinName => IsBuiltin ? Path.Substring(BuiltinPrefix.Length).Trim().ToLowerInvariant() : string.Empty;

        /// <summary>既定の並び(トレイメニュー上側と同じ)。</summary>
        public static List<CallMenuEntry> Defaults()
        {
            return new List<CallMenuEntry>
            {
                new CallMenuEntry { Label = "本日の予定", Path = "@schedule" },
                new CallMenuEntry { Label = "箱庭", Path = "@hakoniwa" },
                new CallMenuEntry { Label = "株レーダー", Path = "@stockradar" },
                new CallMenuEntry { Label = "タイマー", Path = "@timer" },
            };
        }

        /// <summary>手で編集されたリストを整える。null なら既定の並び、表示名か Path が空の行は捨てる。</summary>
        public static List<CallMenuEntry> Normalize(List<CallMenuEntry>? entries)
        {
            if (entries == null)
            {
                return Defaults();
            }
            return entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Label) && !string.IsNullOrWhiteSpace(e.Path))
                .Select(e => new CallMenuEntry { Label = e.Label.Trim(), Path = e.Path.Trim(), Arguments = e.Arguments?.Trim() ?? string.Empty })
                .ToList();
        }

        /// <summary>
        /// 組み込みでない行を起動する。起動できたら null、できなければ理由(吹き出しに出す文面)。
        /// exe・bat 以外(ショートカット・フォルダ・URL)も関連付けで開けるよう ShellExecute で起動する。
        /// </summary>
        public string? Launch()
        {
            string target = Environment.ExpandEnvironmentVariables(Path);
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = Environment.ExpandEnvironmentVariables(Arguments ?? string.Empty),
            };
            if (File.Exists(target))
            {
                // bat が自分のフォルダのファイルを相対パスで読めるように
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target)) ?? string.Empty;
            }
            try
            {
                using Process? p = Process.Start(psi);
                return null;
            }
            catch (Win32Exception ex)
            {
                Log.Write($"メニュー「{Label}」を起動できませんでした: Win32 {ex.NativeErrorCode}");
                return File.Exists(target) || Directory.Exists(target)
                    ? $"「{Label}」を起動できませんでした。"
                    : $"「{Label}」の起動先が見つかりません。settings.json の CallMenu を確認してください。";
            }
            catch (Exception ex)
            {
                Log.Write($"メニュー「{Label}」を起動できませんでした: " + ex.GetType().Name);
                return $"「{Label}」を起動できませんでした。";
            }
        }
    }
}
