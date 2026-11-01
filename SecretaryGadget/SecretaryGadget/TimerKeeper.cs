using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryGadget
{
    /// <summary>セット中のタイマー1件。Label は「22:30」「15分後」のように入力どおりの呼び名。</summary>
    internal sealed record TimerEntry(DateTime Due, string Label);

    /// <summary>
    /// タイマーの入力文字列を時刻にする。「22:30」「22時30分」のような時刻指定と、
    /// 「15分後」「1時間10分後」のような相対指定の2通り。時刻を引数で受け取る純粋な関数にしてあり単体テストできる。
    /// </summary>
    internal static class TimerInput
    {
        private static readonly Regex _clock = new Regex(@"^(\d{1,2})\s*(?::|時)\s*(?:(\d{1,2})\s*分?)?$");
        private static readonly Regex _relative = new Regex(@"^(?:(\d+)\s*時間)?\s*(?:(\d+)\s*分)?\s*後?$");
        private static readonly TimeSpan _maxRelative = TimeSpan.FromHours(24);

        /// <summary>
        /// 解釈できれば true。時刻指定で既に過ぎている時刻は翌日にする。
        /// 相対指定で 0 分や 24 時間超は不可。入力は全角数字・全角コロンでも可。
        /// </summary>
        public static bool TryParse(string? input, DateTime now, out TimerEntry? entry)
        {
            entry = null;
            string s = (input ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
            if (s.Length == 0)
            {
                return false;
            }

            Match clock = _clock.Match(s);
            if (clock.Success)
            {
                int hour = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
                int minute = clock.Groups[2].Success ? int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                if (hour > 23 || minute > 59)
                {
                    return false;
                }
                entry = FromClock(hour, minute, now);
                return true;
            }

            Match relative = _relative.Match(s);
            if (relative.Success && (relative.Groups[1].Success || relative.Groups[2].Success))
            {
                // 桁あふれを避けるため長い数字は先に弾く
                if (relative.Groups[1].Value.Length > 3 || relative.Groups[2].Value.Length > 4)
                {
                    return false;
                }
                int hours = relative.Groups[1].Success ? int.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                int minutes = relative.Groups[2].Success ? int.Parse(relative.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                entry = FromRelative(hours, minutes, now);
                return entry != null;
            }
            return false;
        }

        /// <summary>時刻指定。既に過ぎている時刻は翌日にする。範囲外(時0-23・分0-59以外)は null。</summary>
        public static TimerEntry? FromClock(int hour, int minute, DateTime now)
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
            {
                return null;
            }
            DateTime due = now.Date.AddHours(hour).AddMinutes(minute);
            if (due <= now)
            {
                due = due.AddDays(1);
            }
            return new TimerEntry(due, $"{hour:00}:{minute:00}");
        }

        /// <summary>「〇時間〇分後」。0分後・負数・24時間超は null。</summary>
        public static TimerEntry? FromRelative(int hours, int minutes, DateTime now)
        {
            if (hours < 0 || minutes < 0 || hours > 999 || minutes > 9999)
            {
                return null;
            }
            TimeSpan span = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            if (span <= TimeSpan.Zero || span > _maxRelative)
            {
                return null;
            }
            // 秒は切り捨てて、分単位の時刻にそろえる
            DateTime due = now.Add(span);
            due = new DateTime(due.Year, due.Month, due.Day, due.Hour, due.Minute, 0, due.Kind);
            return new TimerEntry(due, FormatSpan(hours, minutes) + "後");
        }

        /// <summary>「1時間10分」「15分」「2時間」</summary>
        public static string FormatSpan(int hours, int minutes)
        {
            var sb = new StringBuilder();
            if (hours > 0)
            {
                sb.Append($"{hours}時間");
            }
            if (minutes > 0)
            {
                sb.Append($"{minutes}分");
            }
            return sb.ToString();
        }
    }

    /// <summary>セット中のタイマーの一覧。設定フォルダ外(LocalDir)のファイルに保存し、再起動しても残す。</summary>
    internal sealed class TimerKeeper
    {
        private readonly List<TimerEntry> _items = new List<TimerEntry>();
        private readonly string _path;

        public TimerKeeper(string path)
        {
            _path = path;
        }

        /// <summary>時刻の早い順</summary>
        public IReadOnlyList<TimerEntry> Items => _items;

        public void Add(TimerEntry entry)
        {
            _items.Add(entry);
            _items.Sort((a, b) => a.Due.CompareTo(b.Due));
            Save();
        }

        public void Remove(TimerEntry entry)
        {
            if (_items.Remove(entry))
            {
                Save();
            }
        }

        /// <summary>期限が来たものを取り出して一覧から外す(早い順)。</summary>
        public IReadOnlyList<TimerEntry> TakeDue(DateTime now)
        {
            List<TimerEntry> due = _items.Where(t => t.Due <= now).ToList();
            if (due.Count > 0)
            {
                _items.RemoveAll(t => t.Due <= now);
                Save();
            }
            return due;
        }

        /// <summary>保存ファイルを読む。壊れていたら空から始める。</summary>
        public void Load()
        {
            _items.Clear();
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }
                List<TimerEntry>? loaded = JsonSerializer.Deserialize<List<TimerEntry>>(File.ReadAllText(_path, Encoding.UTF8));
                if (loaded != null)
                {
                    _items.AddRange(loaded.Where(t => t != null && t.Label != null).OrderBy(t => t.Due));
                }
            }
            catch (Exception ex)
            {
                Log.Write("タイマーの保存ファイルを読めませんでした: " + ex.GetType().Name);
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_items), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Write("タイマーを保存できませんでした: " + ex.GetType().Name);
            }
        }
    }
}
