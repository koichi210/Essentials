using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Launcher.Services
{
    /// <summary>
    /// この PC の画面の状態(どのセクションを閉じているか)を覚える。
    /// 共有の設定ファイル(launcher.json)には MagicLauncher に無い値を書き足さないよう、別のファイルに置く。
    /// 置き場: %LOCALAPPDATA%\Launcher\view.json
    /// </summary>
    public class ViewStateService
    {
        private readonly string _filePath;
        private readonly HashSet<string> _collapsed = new HashSet<string>();

        public ViewStateService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher", "view.json"))
        {
        }

        public ViewStateService(string filePath)
        {
            _filePath = filePath;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(_filePath, Encoding.UTF8))?["collapsed"] is JsonArray collapsed)
                {
                    foreach (string key in ConfigService.CleanTags(collapsed))
                    {
                        _collapsed.Add(key);
                    }
                }
            }
            catch (Exception)
            {
                // 無い・壊れている → 全部開いた状態から
            }
        }

        public bool IsExpanded(string key) => !_collapsed.Contains(key);

        public void SetExpanded(string key, bool expanded)
        {
            bool changed = expanded ? _collapsed.Remove(key) : _collapsed.Add(key);
            if (changed)
            {
                Save();
            }
        }

        /// <summary>セクションの名前を変えたとき、閉じている状態を引き継ぐ</summary>
        public void Rename(string from, string to)
        {
            if (_collapsed.Remove(from))
            {
                _collapsed.Add(to);
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                var json = new JsonObject { ["collapsed"] = new JsonArray(_collapsed.OrderBy(k => k, StringComparer.Ordinal).Select(k => (JsonNode)k).ToArray()) };
                File.WriteAllText(_filePath, JsJson.StringifyIndented(json) + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 開閉状態を覚えられないだけなので、画面の操作は止めない
            }
        }
    }
}
