using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace SecretaryGadget
{
    /// <summary>
    /// ドラムロール風の数値選択(1列)。上下2つずつ前後の値を見せ、真ん中が選択中。
    /// マウスホイール・上下クリック・ドラッグ・↑↓キーで回り、端でぐるっと回り込む。
    /// </summary>
    internal sealed class WheelColumn : Forms.Control
    {
        private const int _rowHeight = 30;
        private const int _visibleRows = 5;

        private readonly int _count;
        private int _value;
        private int _dragStartY;
        private int _dragStartValue;
        private bool _dragging;

        public WheelColumn(int count)
        {
            _count = count;
            SetStyle(Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer | Forms.ControlStyles.UserPaint | Forms.ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(70, _rowHeight * _visibleRows);
            Font = new Font("Yu Gothic UI", 14f);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                _value = Wrap(value);
                Invalidate();
            }
        }

        private int Wrap(int v)
        {
            return ((v % _count) + _count) % _count;
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Enabled ? Color.White : SystemColors.Control);
            int centerY = _rowHeight * (_visibleRows / 2);
            Color band = Enabled ? Color.FromArgb(235, 238, 245) : Color.FromArgb(225, 225, 225);
            using (var brush = new SolidBrush(band))
            using (var pen = new Pen(Color.FromArgb(170, 175, 190)))
            {
                g.FillRectangle(brush, 0, centerY, Width, _rowHeight);
                g.DrawLine(pen, 0, centerY, Width, centerY);
                g.DrawLine(pen, 0, centerY + _rowHeight, Width, centerY + _rowHeight);
            }
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int i = 0; i < _visibleRows; i++)
            {
                int offset = i - _visibleRows / 2;
                int distance = Math.Abs(offset);
                Color color = !Enabled ? Color.Gray : distance == 0 ? Color.Black : distance == 1 ? Color.FromArgb(110, 110, 110) : Color.FromArgb(180, 180, 180);
                using var textBrush = new SolidBrush(color);
                using var font = new Font(Font.FontFamily, distance == 0 ? Font.Size : Font.Size - 2f, distance == 0 ? FontStyle.Bold : FontStyle.Regular);
                g.DrawString(Wrap(_value + offset).ToString("00"), font, textBrush, new RectangleF(0, i * _rowHeight, Width, _rowHeight), format);
            }
            if (Focused && Enabled)
            {
                Forms.ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - 3, Height - 3));
            }
        }

        protected override void OnMouseWheel(Forms.MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _value + (e.Delta > 0 ? -1 : 1);
        }

        protected override void OnMouseDown(Forms.MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartValue = _value;
        }

        protected override void OnMouseMove(Forms.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                // 下へ引くと前の値が降りてくる
                Value = _dragStartValue - (int)Math.Round((e.Y - _dragStartY) / (double)_rowHeight);
            }
        }

        protected override void OnMouseUp(Forms.MouseEventArgs e)
        {
            base.OnMouseUp(e);
            // ほぼ動かさずに離したら「クリック」: 押した行が真ん中に来るよう回す
            if (_dragging && Math.Abs(e.Y - _dragStartY) < 4)
            {
                Value = _dragStartValue + (e.Y / _rowHeight - _visibleRows / 2);
            }
            _dragging = false;
        }

        protected override bool IsInputKey(Forms.Keys keyData)
        {
            return keyData == Forms.Keys.Up || keyData == Forms.Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(Forms.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Forms.Keys.Up)
            {
                Value = _value - 1;
                e.Handled = true;
            }
            else if (e.KeyCode == Forms.Keys.Down)
            {
                Value = _value + 1;
                e.Handled = true;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}
