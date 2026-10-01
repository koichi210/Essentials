using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using LauncherForWpf.Models;

namespace LauncherForWpf.Services
{
    /// <summary>
    /// 設定（JSON）の読み書きを担当。
    /// 保存先はWindowsユーザーごとのAppDataフォルダなので、
    /// 自動的にユーザーごとに別設定になる。
    /// </summary>
    public class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 日本語を\uXXXXにエスケープせず、手動編集しやすいそのままの文字で出力する
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>設定ファイルのフルパス</summary>
        public string ConfigFilePath { get; }

        public ConfigService()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LauncherForWpf");
            Directory.CreateDirectory(dir);
            ConfigFilePath = Path.Combine(dir, "config.json");
        }

        /// <summary>
        /// 設定を読み込む。ファイルが無ければ初回用のサンプル設定を作成して返す。
        /// </summary>
        public LauncherConfig Load()
        {
            if (!File.Exists(ConfigFilePath))
            {
                var initial = CreateDefaultConfig();
                Save(initial);
                return initial;
            }

            string json = File.ReadAllText(ConfigFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultConfig();
            }

            var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);
            return config ?? CreateDefaultConfig();
        }

        /// <summary>設定をJSONファイルへ保存する</summary>
        public void Save(LauncherConfig config)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }

        private static LauncherConfig CreateDefaultConfig()
        {
            var config = new LauncherConfig();

            var section = new SectionModel { Name = "よく使うツール", IsExpanded = true };
            section.Items.Add(new ItemModel { Title = "メモ帳", Path = @"C:\Windows\System32\notepad.exe" });
            section.Items.Add(new ItemModel { Title = "電卓", Path = @"C:\Windows\System32\calc.exe" });
            section.Items.Add(new ItemModel { Title = "Google", Path = "https://www.google.com" });
            config.Sections.Add(section);

            return config;
        }
    }
}
