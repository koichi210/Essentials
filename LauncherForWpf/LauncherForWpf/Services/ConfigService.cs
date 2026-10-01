using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using LauncherForWpf.Models;
using StandardTemplate;

namespace LauncherForWpf.Services
{
    /// <summary>
    /// 設定（JSON）の読み書きを担当。
    /// 保存先フォルダはCheetos/FFEdit/FileArranger等と同じ[[_Common/UserDataLocation.cs]]方式
    /// （ポインタファイル＋既定は%LOCALAPPDATA%）で管理するため、ユーザーごとに自動的に
    /// 別設定になり、かつ画面から保存先フォルダを任意の場所に変更できる。
    /// </summary>
    public class ConfigService
    {
        private const string AppName = "LauncherForWpf";
        private const string ConfigFileName = "config.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 日本語を\uXXXXにエスケープせず、手動編集しやすいそのままの文字で出力する
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>現在の保存先フォルダ</summary>
        public string DataFolder { get; private set; }

        /// <summary>設定ファイルのフルパス</summary>
        public string ConfigFilePath => Path.Combine(DataFolder, ConfigFileName);

        public ConfigService()
        {
            DataFolder = UserDataLocation.GetUserDataFolder(AppName);
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

        /// <summary>
        /// 保存先フォルダ選択ダイアログを表示し、選ばれたフォルダに切り替える。
        /// 既存のconfig.jsonは新しい保存先へ移動し、次回以降もそのフォルダを使うよう
        /// ポインタファイルを書き換える（切り替えはこのセッションにも即時反映する）。
        /// キャンセル時・変更なしの場合はfalseを返す。
        /// </summary>
        public bool ChangeDataFolder(out string message)
        {
            string selected = DataFolderChooser.ChooseFolder("ランチャーの保存先フォルダを選んでください", DataFolder);
            if (string.IsNullOrEmpty(selected))
            {
                message = null;
                return false;
            }

            string newFolder = Path.GetFullPath(selected);
            if (string.Equals(newFolder.TrimEnd('\\'), Path.GetFullPath(DataFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                message = null;
                return false;
            }

            Directory.CreateDirectory(newFolder);

            string oldConfigPath = ConfigFilePath;
            string newConfigPath = Path.Combine(newFolder, ConfigFileName);

            if (File.Exists(newConfigPath))
            {
                message = $"選択したフォルダには既にconfig.jsonがあったため、そちらをそのまま使うよ。\n{newFolder}";
            }
            else if (File.Exists(oldConfigPath))
            {
                File.Move(oldConfigPath, newConfigPath);
                message = $"保存先を移動したよ。\n{newFolder}";
            }
            else
            {
                message = $"保存先を変更したよ。\n{newFolder}";
            }

            DataFolder = newFolder;
            UserDataLocation.SetUserDataFolder(AppName, newFolder);
            return true;
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
