using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace SecretaryGadget
{
    /// <summary>
    /// タイマーの入力ダイアログ。ラジオボタンで「①時刻を指定」か「②〇時間〇分後」を選び、どちらもドラムロールで値を選ぶ。
    /// ②の初期値は 0 時間 5 分。
    /// </summary>
    internal static class TimerDialog
    {
        /// <summary>OK ならセットする内容、キャンセルなら null。</summary>
        public static TimerEntry? Ask()
        {
            DateTime start = DateTime.Now;
            using var form = new Forms.Form
            {
                Text = "タイマーをセット",
                FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
                StartPosition = Forms.FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                ClientSize = new Size(440, 280),
            };

            var clockRadio = new Forms.RadioButton { Text = "時刻を指定", Checked = true, AutoSize = true, Location = new Point(14, 12) };
            var clockHourLabel = HeaderLabel("時間", 30);
            var clockMinuteLabel = HeaderLabel("分", 110);
            var clockHour = new WheelColumn(24) { Location = new Point(30, 62), Value = start.Hour };
            var clockMinute = new WheelColumn(60) { Location = new Point(110, 62), Value = start.Minute };

            var relativeRadio = new Forms.RadioButton { Text = "指定時間後を指定", AutoSize = true, Location = new Point(234, 12) };
            var relHourLabel = HeaderLabel("時間", 250);
            var relMinuteLabel = HeaderLabel("分後", 330);
            var relHour = new WheelColumn(24) { Location = new Point(250, 62), Value = 0, Enabled = false };
            var relMinute = new WheelColumn(60) { Location = new Point(330, 62), Value = 5, Enabled = false };

            var error = new Forms.Label { ForeColor = Color.Firebrick, AutoSize = false, Location = new Point(14, 226), Size = new Size(412, 18) };
            var ok = new Forms.Button { Text = "セット", Location = new Point(270, 244), Size = new Size(75, 27) };
            var cancel = new Forms.Button { Text = "キャンセル", Location = new Point(351, 244), Size = new Size(75, 27), DialogResult = Forms.DialogResult.Cancel };
            form.Controls.AddRange(new Forms.Control[]
            {
                clockRadio, clockHourLabel, clockMinuteLabel, clockHour, clockMinute,
                relativeRadio, relHourLabel, relMinuteLabel, relHour, relMinute,
                error, ok, cancel,
            });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            // 左上→右上→左下→右下の順
            clockRadio.TabIndex = 0;
            clockHour.TabIndex = 1;
            clockMinute.TabIndex = 2;
            relativeRadio.TabIndex = 3;
            relHour.TabIndex = 4;
            relMinute.TabIndex = 5;
            ok.TabIndex = 6;
            cancel.TabIndex = 7;

            // ラジオで有効側を切り替える。選んだ側の入力へフォーカスを移す
            clockRadio.CheckedChanged += (_, __) =>
            {
                bool clock = clockRadio.Checked;
                clockHour.Enabled = clock;
                clockMinute.Enabled = clock;
                relHour.Enabled = !clock;
                relMinute.Enabled = !clock;
                error.Text = string.Empty;
                (clock ? clockHour : relHour).Focus();
            };

            TimerEntry? result = null;
            ok.Click += (_, __) =>
            {
                DateTime now = DateTime.Now;
                TimerEntry? entry = clockRadio.Checked
                    ? TimerInput.FromClock(clockHour.Value, clockMinute.Value, now)
                    : TimerInput.FromRelative(relHour.Value, relMinute.Value, now);
                if (entry == null)
                {
                    error.Text = "0 時間 0 分後にはセットできません。";
                    return;
                }
                result = entry;
                form.DialogResult = Forms.DialogResult.OK;
                form.Close();
            };
            form.Shown += (_, __) =>
            {
                form.Activate();
                clockHour.Focus();
            };
            form.ShowDialog();
            return result;
        }

        private static Forms.Label HeaderLabel(string text, int x)
        {
            return new Forms.Label { Text = text, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(x, 40), Size = new Size(70, 20) };
        }
    }
}
