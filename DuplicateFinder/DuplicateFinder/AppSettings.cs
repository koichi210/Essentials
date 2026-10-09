using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace DuplicateFinder
{
    /// <summary>前回の入力内容(%LOCALAPPDATA%\DuplicateFinder\settings.json)</summary>
    internal sealed class AppSettings
    {
        public string RootFolder { get; set; } = "";
        public string MinSizeMb { get; set; } = "0";
        public string Extensions { get; set; } = "";
        public bool SkipHiddenAndSystem { get; set; } = true;
        public int MaxParallelism { get; set; } = 1;
        public bool UseRecycleBin { get; set; } = true;
        public KeepRule KeepRule { get; set; } = KeepRule.Oldest;

        public static string DefaultFilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DuplicateFinder", "settings.json");
            }
        }

        public static AppSettings Load(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(filePath, Encoding.UTF8)) ?? new AppSettings();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                // 壊れていたら既定値で起動する(設定は次回終了時に上書きされる)
            }
            return new AppSettings();
        }

        public void Save(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
        }
    }
}
