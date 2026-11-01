using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace HeifToPng
{
    /// <summary>前回の入力内容(データ保存先フォルダのsettings.json。置き場はUserDataLocationが決める)</summary>
    internal sealed class AppSettings
    {
        public const string FileName = "settings.json";

        public List<string> Sources { get; set; } = new List<string>();
        public bool IncludeSubfolders { get; set; } = true;
        public bool Overwrite { get; set; }
        public bool KeepTimestamps { get; set; } = true;
        public int MaxParallelism { get; set; } = 4;

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
