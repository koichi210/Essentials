using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Forms = System.Windows.Forms;

namespace SecretaryGadget
{
    /// <summary>
    /// 「秘書さんを呼ぶ」の吹き出しに並べる選択肢(settings.json の CallMenu)を編集する画面。
    /// 左に並び順の一覧(追加・削除・上へ・下へ)、右に選んだ行の表示名・起動するもの・引数。
    /// 起動するものは「@schedule」等の秘書さん自身の機能を候補から選ぶか、ファイルを参照で選ぶか、直接書く。
    /// </summary>
    internal static class CallMenuDialog
    {
        private const string _newLabel = "新しい選択肢";

        /// <summary>保存したら新しい並び、キャンセルなら null。渡したリストは書き換えない。</summary>
        public static List<CallMenuEntry>? Ask(IReadOnlyList<CallMenuEntry> current)
        {
            List<CallMenuEntry> rows = current
                .Select(e => new CallMenuEntry { Label = e.Label, Path = e.Path, Arguments = e.Arguments })
                .ToList();

            using var form = new Forms.Form
            {
                Text = "呼んだときの選択肢を編集",
                FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
                StartPosition = Forms.FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                ClientSize = new Size(640, 336),
            };

            var listLabel = new Forms.Label { Text = "吹き出しに出す順番", AutoSize = true, Location = new Point(12, 10) };
            var list = new Forms.ListBox { Location = new Point(12, 30), Size = new Size(220, 244), IntegralHeight = false };
            var add = new Forms.Button { Text = "追加", Location = new Point(240, 30), Size = new Size(72, 27) };
            var remove = new Forms.Button { Text = "削除", Location = new Point(240, 62), Size = new Size(72, 27) };
            var up = new Forms.Button { Text = "↑ 上へ", Location = new Point(240, 104), Size = new Size(72, 27) };
            var down = new Forms.Button { Text = "↓ 下へ", Location = new Point(240, 136), Size = new Size(72, 27) };

            var labelCaption = new Forms.Label { Text = "表示名", AutoSize = true, Location = new Point(322, 10) };
            var labelBox = new Forms.TextBox { Location = new Point(322, 30), Size = new Size(306, 23) };
            var pathCaption = new Forms.Label { Text = "起動するもの", AutoSize = true, Location = new Point(322, 66) };
            var pathBox = new Forms.ComboBox { Location = new Point(322, 86), Size = new Size(226, 23), DropDownStyle = Forms.ComboBoxStyle.DropDown };
            foreach ((string name, _) in CallMenuEntry.Builtins)
            {
                pathBox.Items.Add(CallMenuEntry.BuiltinPrefix + name);
            }
            var browse = new Forms.Button { Text = "参照…", Location = new Point(554, 85), Size = new Size(74, 25) };
            var pathHint = new Forms.Label
            {
                Text = "ファイル（exe・bat・ps1・ショートカット）・フォルダ・URL を書くか、参照で選びます。"
                    + "▼の候補は秘書さん自身の機能です（"
                    + string.Join("、", CallMenuEntry.Builtins.Select(b => $"{CallMenuEntry.BuiltinPrefix}{b.Name}＝{b.Title}"))
                    + "）。",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Location = new Point(322, 114),
                Size = new Size(306, 48),
            };
            var argsCaption = new Forms.Label { Text = "引数（任意）", AutoSize = true, Location = new Point(322, 170) };
            var argsBox = new Forms.TextBox { Location = new Point(322, 190), Size = new Size(306, 23) };

            var error = new Forms.Label { ForeColor = Color.Firebrick, AutoSize = false, Location = new Point(12, 280), Size = new Size(616, 18) };
            var ok = new Forms.Button { Text = "保存", Location = new Point(472, 302), Size = new Size(75, 27) };
            var cancel = new Forms.Button { Text = "キャンセル", Location = new Point(553, 302), Size = new Size(75, 27), DialogResult = Forms.DialogResult.Cancel };

            form.Controls.AddRange(new Forms.Control[]
            {
                listLabel, list, add, remove, up, down,
                labelCaption, labelBox, pathCaption, pathBox, browse, pathHint, argsCaption, argsBox,
                error, ok, cancel,
            });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            // 左上→右上→左下→右下の順
            list.TabIndex = 0;
            add.TabIndex = 1;
            remove.TabIndex = 2;
            up.TabIndex = 3;
            down.TabIndex = 4;
            labelBox.TabIndex = 5;
            pathBox.TabIndex = 6;
            browse.TabIndex = 7;
            argsBox.TabIndex = 8;
            ok.TabIndex = 9;
            cancel.TabIndex = 10;

            // 一覧の選択を入力欄へ写している間は、入力欄の変更を行へ書き戻さない
            bool loading = false;

            string RowText(CallMenuEntry e) => string.IsNullOrWhiteSpace(e.Label) ? "（表示名なし）" : e.Label.Trim();

            CallMenuEntry? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < rows.Count ? rows[list.SelectedIndex] : null;

            void UpdateButtons()
            {
                int i = list.SelectedIndex;
                bool has = i >= 0;
                remove.Enabled = has;
                up.Enabled = has && i > 0;
                down.Enabled = has && i < rows.Count - 1;
                labelBox.Enabled = has;
                pathBox.Enabled = has;
                browse.Enabled = has;
                argsBox.Enabled = has;
            }

            void LoadSelected()
            {
                loading = true;
                CallMenuEntry? e = Selected();
                labelBox.Text = e?.Label ?? string.Empty;
                pathBox.Text = e?.Path ?? string.Empty;
                argsBox.Text = e?.Arguments ?? string.Empty;
                loading = false;
                UpdateButtons();
            }

            void Refill(int select)
            {
                list.BeginUpdate();
                list.Items.Clear();
                foreach (CallMenuEntry e in rows)
                {
                    list.Items.Add(RowText(e));
                }
                list.EndUpdate();
                list.SelectedIndex = rows.Count == 0 ? -1 : Math.Clamp(select, 0, rows.Count - 1);
                LoadSelected();
            }

            void Move(int delta)
            {
                int i = list.SelectedIndex;
                int j = i + delta;
                if (i < 0 || j < 0 || j >= rows.Count)
                {
                    return;
                }
                (rows[i], rows[j]) = (rows[j], rows[i]);
                Refill(j);
            }

            list.SelectedIndexChanged += (_, __) =>
            {
                if (!loading)
                {
                    LoadSelected();
                }
            };
            labelBox.TextChanged += (_, __) =>
            {
                CallMenuEntry? e = Selected();
                if (loading || e == null)
                {
                    return;
                }
                e.Label = labelBox.Text;
                loading = true;
                list.Items[list.SelectedIndex] = RowText(e);
                loading = false;
            };
            pathBox.TextChanged += (_, __) =>
            {
                CallMenuEntry? e = Selected();
                if (!loading && e != null)
                {
                    e.Path = pathBox.Text;
                }
            };
            // 候補を選んだとき、表示名がまだ初期値なら機能の名前を入れる
            pathBox.SelectionChangeCommitted += (_, __) =>
            {
                CallMenuEntry? e = Selected();
                if (e == null || pathBox.SelectedIndex < 0)
                {
                    return;
                }
                e.Path = (string)pathBox.SelectedItem!;
                if (string.IsNullOrWhiteSpace(e.Label) || e.Label == _newLabel)
                {
                    labelBox.Text = CallMenuEntry.Builtins[pathBox.SelectedIndex].Title;
                }
            };
            argsBox.TextChanged += (_, __) =>
            {
                CallMenuEntry? e = Selected();
                if (!loading && e != null)
                {
                    e.Arguments = argsBox.Text;
                }
            };

            add.Click += (_, __) =>
            {
                rows.Add(new CallMenuEntry { Label = _newLabel });
                Refill(rows.Count - 1);
                error.Text = string.Empty;
                labelBox.Focus();
                labelBox.SelectAll();
            };
            remove.Click += (_, __) =>
            {
                int i = list.SelectedIndex;
                if (i < 0)
                {
                    return;
                }
                rows.RemoveAt(i);
                Refill(i);
                error.Text = string.Empty;
            };
            up.Click += (_, __) => Move(-1);
            down.Click += (_, __) => Move(1);
            browse.Click += (_, __) =>
            {
                CallMenuEntry? e = Selected();
                if (e == null)
                {
                    return;
                }
                using var dialog = new Forms.OpenFileDialog
                {
                    Title = "起動するファイルを選んでください",
                    Filter = "起動できるファイル (*.exe;*.bat;*.cmd;*.ps1;*.lnk)|*.exe;*.bat;*.cmd;*.ps1;*.lnk|すべてのファイル (*.*)|*.*",
                    DereferenceLinks = false,
                };
                string expanded = Environment.ExpandEnvironmentVariables(e.Path ?? string.Empty);
                if (!e.IsBuiltin && File.Exists(expanded))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(expanded));
                    dialog.FileName = Path.GetFileName(expanded);
                }
                if (dialog.ShowDialog(form) != Forms.DialogResult.OK)
                {
                    return;
                }
                pathBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(e.Label) || e.Label == _newLabel)
                {
                    labelBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            };

            List<CallMenuEntry>? result = null;
            ok.Click += (_, __) =>
            {
                (int Index, string Message)? problem = CallMenuEntry.FindProblem(rows);
                if (problem != null)
                {
                    error.Text = problem.Value.Message;
                    list.SelectedIndex = problem.Value.Index;
                    return;
                }
                result = CallMenuEntry.Normalize(rows);
                form.DialogResult = Forms.DialogResult.OK;
                form.Close();
            };
            form.Shown += (_, __) =>
            {
                form.Activate();
                list.Focus();
            };

            Refill(0);
            form.ShowDialog();
            return result;
        }
    }
}
