using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SecretaryGadget
{
    /// <summary>タスクトレイ(NotifyIcon)とメニュー。メニュー操作のたびに設定の全項目を書き戻す。</summary>
    public partial class App
    {
        private const string _runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string _runValueName = "SecretaryGadget";

        private Forms.NotifyIcon? _tray;

        /// <summary>スキンメニューに添えた絵。メニューを作り直すたびに捨てる。</summary>
        private readonly List<System.Drawing.Image> _skinMenuImages = new List<System.Drawing.Image>();

        private void CreateTray()
        {
            var menu = new Forms.ContextMenuStrip();
            _tray = new Forms.NotifyIcon
            {
                Text = "秘書さん",
                ContextMenuStrip = menu,
                Icon = SkinCatalog.LoadIcon(_skin),
            };
            BuildMenu(menu);
            // メニューを開くたびに作り直す(チェック状態・休憩の再開予定を反映)
            menu.Opening += (_, e) =>
            {
                BuildMenu(menu);
                e.Cancel = false;
            };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    OnTrayLeftClick();
                }
            };
            _tray.Visible = true;
            UpdateTrayText();
        }

        private void DisposeTray()
        {
            if (_tray == null)
            {
                return;
            }
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
            _tray = null;
        }

        /// <summary>左クリック: 表示中なら引っ込める、非表示なら「秘書さんを呼ぶ」。</summary>
        private void OnTrayLeftClick()
        {
            if (_window == null)
            {
                return;
            }
            if (_window.IsShowing)
            {
                _window.Retract();
            }
            else
            {
                Call();
            }
        }

        /// <summary>ツールチップ「秘書さん」＋「（おやすみ中）」「（HH:mm まで休憩）」「（HH:mm までスクショに映ります）」</summary>
        private void UpdateTrayText()
        {
            if (_tray == null)
            {
                return;
            }
            string text = "秘書さん";
            if (_settings.Paused)
            {
                text += "（おやすみ中）";
            }
            if (_onBreak && _breakUntil != null)
            {
                text += $"（{_breakUntil.Value:HH:mm} まで休憩）";
            }
            if (_noHideUntil != null)
            {
                text += $"（{_noHideUntil.Value:HH:mm} までスクショに映ります）";
            }
            _tray.Text = text.Length > 127 ? text.Substring(0, 127) : text;
        }

        private void UpdateTrayIcon()
        {
            if (_tray == null)
            {
                return;
            }
            System.Drawing.Icon? old = _tray.Icon;
            _tray.Icon = SkinCatalog.LoadIcon(_skin);
            old?.Dispose();
        }

        private void BuildMenu(Forms.ContextMenuStrip menu)
        {
            foreach (Forms.ToolStripItem old in menu.Items.Cast<Forms.ToolStripItem>().ToList())
            {
                old.Dispose();
            }
            menu.Items.Clear();
            foreach (System.Drawing.Image image in _skinMenuImages)
            {
                image.Dispose();
            }
            _skinMenuImages.Clear();

            // 秘書さんを呼ぶ(トレイ左クリック)・本日の予定・箱庭・株レーダー・タイマーは、呼んだときの吹き出しの選択肢(CallMenu)へ移した
            var breakMenu = new Forms.ToolStripMenuItem(_onBreak && _breakUntil != null ? $"休憩（{_breakUntil.Value:HH:mm} 再開予定）" : "休憩");
            foreach ((string label, int minutes) in new[] { ("30分", 30), ("1時間", 60), ("2時間", 120) })
            {
                breakMenu.DropDownItems.Add(Item(label, () => StartBreak(TimeSpan.FromMinutes(minutes)), check: _onBreak && _breakMinutes == minutes));
            }
            breakMenu.DropDownItems.Add(Item("おやすみ", TogglePaused, check: _settings.Paused));
            breakMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            breakMenu.DropDownItems.Add(Item("休憩終わり", EndBreak, enabled: _onBreak));
            menu.Items.Add(breakMenu);

            var sizeMenu = new Forms.ToolStripMenuItem("大きさ");
            foreach ((string label, double scale) in new[] { ("等倍", 1.0), ("1/2", 0.5), ("1/4", 0.25) })
            {
                sizeMenu.DropDownItems.Add(Item(label, () => ChangeScale(scale), check: Math.Abs(EffectiveScale - scale) < 0.001));
            }
            menu.Items.Add(sizeMenu);

            var skinMenu = new Forms.ToolStripMenuItem("スキン");
            // 絵とチェックを別の列に出す(既定だと選択中の行は絵がチェックの代わりになって見分けにくい)
            if (skinMenu.DropDown is Forms.ToolStripDropDownMenu skinDropDown)
            {
                skinDropDown.ShowCheckMargin = true;
                skinDropDown.ShowImageMargin = true;
            }
            foreach (SkinInfo skin in _skins)
            {
                SkinInfo captured = skin;
                Forms.ToolStripMenuItem skinItem = Item(skin.Name, () => ChangeSkin(captured), check: skin.Id == _skin.Id);
                try
                {
                    System.Drawing.Image image = SkinCatalog.LoadMenuImage(skin, menu.ImageScalingSize);
                    _skinMenuImages.Add(image);
                    skinItem.Image = image;
                }
                catch (Exception ex)
                {
                    Log.Write("スキンメニューの絵を読めませんでした: " + ex.GetType().Name);
                }
                skinMenu.DropDownItems.Add(skinItem);
            }
            if (_skins.Count > 0)
            {
                skinMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            }
            skinMenu.DropDownItems.Add(Item("1時間ごとに秘書を交代する", ToggleSkinRotation, check: _settings.SkinRotateHourly, enabled: _skins.Count >= 2 || _settings.SkinRotateHourly));
            skinMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            skinMenu.DropDownItems.Add(Item("スキン一覧を読み直す", ReloadSkins));
            skinMenu.DropDownItems.Add(Item("スキンを追加するフォルダを開く", () =>
            {
                Directory.CreateDirectory(SkinCatalog.UserDir);
                WindowFocus.OpenUri(SkinCatalog.UserDir);
            }));
            menu.Items.Add(skinMenu);

            var hideMenu = new Forms.ToolStripMenuItem("隠れるまでの時間");
            foreach (int seconds in new[] { 1, 5, 10, 20 })
            {
                hideMenu.DropDownItems.Add(Item($"{seconds}秒", () => ChangeDisplaySeconds(seconds), check: _settings.DisplaySeconds == seconds));
            }
            menu.Items.Add(hideMenu);

            menu.Items.Add(Item(
                _noHideUntil != null ? $"自動的に隠さない（{_noHideUntil.Value:HH:mm} まで）" : "自動的に隠さない",
                () => SetNoHide(_noHideUntil == null),
                check: _noHideUntil != null));
            menu.Items.Add(new Forms.ToolStripSeparator());

            var settingsMenu = new Forms.ToolStripMenuItem("設定");
            settingsMenu.DropDownItems.Add(Toggle("Outlook のメールを知らせる", () => _settings.NotifyOutlookMail, v => _settings.NotifyOutlookMail = v));
            settingsMenu.DropDownItems.Add(Toggle("会議の開始前に知らせる", () => _settings.NotifyMeetings, v => _settings.NotifyMeetings = v));
            settingsMenu.DropDownItems.Add(Toggle("Teams のメンションを知らせる", () => _settings.NotifyTeams, v => _settings.NotifyTeams = v));
            settingsMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            settingsMenu.DropDownItems.Add(Item("呼んだときの選択肢を編集…", EditCallMenu));
            settingsMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            settingsMenu.DropDownItems.Add(Item("テスト通知を出す", () => Enqueue(NoticeTexts.Test())));
            settingsMenu.DropDownItems.Add(Item("ログフォルダを開く", () =>
            {
                Directory.CreateDirectory(Settings.LocalDir);
                WindowFocus.OpenUri(Settings.LocalDir);
            }));
            settingsMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            settingsMenu.DropDownItems.Add(Item("設定ファイルのフォルダを変更", ChangeSettingsDir));
            menu.Items.Add(settingsMenu);

            menu.Items.Add(Item("PC起動時に自動で起動", () => SetAutoStart(!IsAutoStart()), check: IsAutoStart()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Item("終了", ExitApp));
        }

        /// <summary>メニュー項目。クリックしたら動作のあと設定の全項目を書き戻す。</summary>
        private Forms.ToolStripMenuItem Item(string text, Action action, bool check = false, bool enabled = true)
        {
            var item = new Forms.ToolStripMenuItem(text) { Checked = check, Enabled = enabled };
            item.Click += (_, __) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Write($"メニュー操作に失敗しました({text}): " + ex.GetType().Name);
                }
                _settings.Save();
            };
            return item;
        }

        /// <summary>ON/OFF の設定項目。OFF でも見張り自体は続ける(「秘書さんを呼ぶ」の吹き出しには OFF の項目を出さない)。</summary>
        private Forms.ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set)
        {
            return Item(text, () =>
            {
                bool value = !get();
                set(value);
                Enqueue(NoticeTexts.SettingChanged(text, value));
            }, check: get());
        }

        /// <summary>
        /// 設定ファイル(settings.json)のフォルダを選び直す。settings_location.json を書き換えるので次回の起動からもそのフォルダを使う。
        /// 選んだ先に settings.json があればそちらを読み込んで、スキン・大きさ等をすぐ反映する。
        /// </summary>
        private void ChangeSettingsDir()
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "設定ファイル（settings.json）を置くフォルダを選んでください",
                UseDescriptionForTitle = true,
                InitialDirectory = _settings.Dir,
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }
            string newDir = dialog.SelectedPath;
            if (string.Equals(Path.GetFullPath(newDir).TrimEnd('\\'), Path.GetFullPath(_settings.Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            Settings? changed = Settings.ChangeDir(_settings, newDir);
            if (changed == null)
            {
                Enqueue(NoticeTexts.SettingsDirFailed(newDir));
                return;
            }
            bool loaded = !ReferenceEquals(changed, _settings);
            _settings = changed;
            if (loaded)
            {
                if (_window != null)
                {
                    _window.DisplaySeconds = _settings.DisplaySeconds;
                }
                LoadSkins();
                ApplySkinToWindow();
                UpdateTrayText();
            }
            Enqueue(NoticeTexts.SettingsDirChanged(newDir, loaded));
        }

        /// <summary>
        /// 「秘書さんを呼ぶ」の吹き出しの選択肢を画面で編集する。settings.json を手で書き換えていてもそちらを元に開く。
        /// 保存はメニュー項目共通の書き戻し(Item)で行う。
        /// </summary>
        private void EditCallMenu()
        {
            _settings.CallMenu = Settings.ReadCallMenu(_settings.FilePath) ?? _settings.CallMenu;
            List<CallMenuEntry>? edited = CallMenuDialog.Ask(_settings.CallMenu);
            if (edited == null)
            {
                return;
            }
            _settings.CallMenu = edited;
            Log.Write($"呼んだときの選択肢を保存しました({edited.Count}件)");
            Enqueue(NoticeTexts.CallMenuSaved(edited.Count));
        }

        private void TogglePaused()
        {
            _settings.Paused = !_settings.Paused;
            Log.Write(_settings.Paused ? "おやすみ: ON" : "おやすみ: OFF");
            UpdateTrayText();
        }

        private void ChangeScale(double scale)
        {
            _options.ClearScale();
            _settings.SkinScale = scale;
            ApplySkinToWindow();
            Enqueue(NoticeTexts.SizeChanged());
        }

        private void ChangeSkin(SkinInfo skin)
        {
            _options.ClearSkin();
            _settings.Skin = skin.Id;
            _skin = skin;
            ApplySkinToWindow();
            Enqueue(NoticeTexts.SkinChanged(skin.Name));
        }

        private void ToggleSkinRotation()
        {
            _settings.SkinRotateHourly = !_settings.SkinRotateHourly;
            // 途中から ON にしたら、次の正時に1回目の交代
            _rotateHourMark = HourOf(DateTime.Now);
            Enqueue(NoticeTexts.SettingChanged("1時間ごとに秘書を交代する", _settings.SkinRotateHourly));
        }

        private static DateTime HourOf(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);

        /// <summary>
        /// 正時をまたいだら、今の秘書さん以外からランダムに選んだスキンへ交代する(Flush から3秒ごと)。吹き出しを出している間は引っ込むまで待つ。
        /// おやすみ・休憩中は絵だけ替えて、交代のあいさつは出さない。スリープ明けで何時間ぶんまたいでも交代は1回。
        /// </summary>
        private void CheckSkinRotation(DateTime now)
        {
            DateTime hour = HourOf(now);
            if (!_settings.SkinRotateHourly)
            {
                _rotateHourMark = hour;
                return;
            }
            if (hour == _rotateHourMark || _window == null || _window.IsShowing)
            {
                return;
            }
            _rotateHourMark = hour;
            SkinInfo? next = SkinCatalog.Next(_skins, _skin.Id, Random.Shared);
            if (next == null)
            {
                return;
            }
            _options.ClearSkin();
            _settings.Skin = next.Id;
            _skin = next;
            ApplySkinToWindow();
            _settings.Save();
            Log.Write("1時間ごとの交代: " + next.Id);
            if (!_settings.Paused && !_onBreak)
            {
                Enqueue(NoticeTexts.SkinRotated(hour, next.Name));
            }
        }

        private void ReloadSkins()
        {
            LoadSkins();
            ApplySkinToWindow();
        }

        private void ChangeDisplaySeconds(int seconds)
        {
            _settings.DisplaySeconds = seconds;
            if (_window != null)
            {
                _window.DisplaySeconds = seconds;
            }
            Enqueue(NoticeTexts.HideTimeChanged());
        }

        private static string CurrentExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SecretaryGadget.exe");

        private static bool IsAutoStart()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(_runValueName) != null;
        }

        private static void SetAutoStart(bool on)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            if (on)
            {
                key.SetValue(_runValueName, $"\"{CurrentExePath}\"");
            }
            else
            {
                key.DeleteValue(_runValueName, false);
            }
        }

        /// <summary>起動時、登録先が違えば今の exe に直す。</summary>
        private static void FixAutoStartPath()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_runKeyPath, true);
                if (key?.GetValue(_runValueName) is string value && !string.Equals(value.Trim('"'), CurrentExePath, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(_runValueName, $"\"{CurrentExePath}\"");
                    Log.Write("自動起動の登録先を今の exe に直しました");
                }
            }
            catch (Exception ex)
            {
                Log.Write("自動起動の登録を確認できませんでした: " + ex.GetType().Name);
            }
        }
    }
}
