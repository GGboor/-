using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenPilot
{
    static class RoundedStyle
    {
        public static readonly Color Background = Color.FromArgb(242, 242, 247);
        public static readonly Color Blue = Color.FromArgb(0, 122, 255);
        public static readonly Color Text = Color.FromArgb(28, 28, 30);
        public static readonly Color Secondary = Color.FromArgb(124, 124, 132);
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) return p;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }
    class RoundedPanel : Panel
    {
        public Color Fill { get; set; }
        public int Radius { get; set; }
        public RoundedPanel()
        {
            Fill = Color.White; Radius = 20; BackColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = RoundedStyle.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Radius))
            using (var b = new SolidBrush(Fill)) e.Graphics.FillPath(b, p);
            base.OnPaint(e);
        }
    }
    class RoundedButton : Button
    {
        bool hover, down;
        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Color.White; ForeColor = RoundedStyle.Blue; Cursor = Cursors.Hand; AutoSize = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent is RoundedPanel ? ((RoundedPanel)Parent).Fill : Parent == null ? RoundedStyle.Background : Parent.BackColor);
            Color fill = Enabled ? BackColor : RoundedStyle.Blend(BackColor, RoundedStyle.Background, .65f);
            if (Enabled && (hover || down)) fill = RoundedStyle.Blend(fill, Color.Black, down ? .10f : .04f);
            using (var p = RoundedStyle.Round(new RectangleF(1, 1, Width - 2, Height - 2), 12))
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(174, 174, 182), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(RoundedStyle.Blue, 1)) using (var p = RoundedStyle.Round(new RectangleF(3, 3, Width - 6, Height - 6), 10)) g.DrawPath(pen, p);
        }
    }
    class RoundedSwitch : CheckBox
    {
        readonly Timer animation = new Timer { Interval = 15 };
        float progress;
        public RoundedSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false; Size = new Size(58, 36); BackColor = Color.White; Text = ""; Cursor = Cursors.Hand;
            animation.Tick += delegate { float target = Checked ? 1 : 0; progress += Math.Sign(target - progress) * Math.Min(.18f, Math.Abs(target - progress)); Invalidate(); if (progress == target) animation.Stop(); };
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            if (IsHandleCreated && Visible) animation.Start(); else progress = Checked ? 1 : 0;
            Invalidate(); base.OnCheckedChanged(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width / 58f, Height / 36f); g.ScaleTransform(scale, scale);
            Color fill = RoundedStyle.Blend(Color.FromArgb(226, 226, 232), Color.FromArgb(52, 199, 89), progress);
            if (!Enabled) fill = RoundedStyle.Blend(fill, Color.White, .5f);
            using (var p = RoundedStyle.Round(new RectangleF(1, 2, 54, 32), 16)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            float x = 3 + 22 * progress;
            using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0))) g.FillEllipse(shadow, x, 5, 28, 28);
            using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, x, 4, 28, 28);
            if (Focused && ShowFocusCues) using (var pen = new Pen(RoundedStyle.Blue, 1.2f)) using (var p = RoundedStyle.Round(new RectangleF(0, 1, 56, 34), 17)) g.DrawPath(pen, p);
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
    class RoundedPicker : RoundedButton
    {
        public readonly List<object> Items = new List<object>();
        int index = -1;
        public int SelectedIndex { get { return index; } set { index = value; Text = SelectedItem == null ? "选择屏幕" : SelectedItem.ToString(); Invalidate(); } }
        public object SelectedItem { get { return index >= 0 && index < Items.Count ? Items[index] : null; } }
        public RoundedPicker() { AccessibleRole = AccessibleRole.ComboBox; BackColor = Color.FromArgb(245, 245, 250); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip { Font = Font, ShowImageMargin = false, BackColor = Color.White, ForeColor = RoundedStyle.Text };
            for (int i = 0; i < Items.Count; i++)
            {
                int choice = i; var item = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == index, Padding = new Padding(10, 8, 10, 8) };
                item.Click += delegate { SelectedIndex = choice; }; menu.Items.Add(item);
            }
            menu.Closed += delegate { menu.Dispose(); }; menu.Show(this, new Point(0, Height));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Leave a trailing area for the chevron and align the selected value to the left.
            var g = e.Graphics; g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = RoundedStyle.Round(new RectangleF(1, 1, Width - 2, Height - 2), 11)) using (var b = new SolidBrush(BackColor)) g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(14, 0, Width - 46, Height), Enabled ? RoundedStyle.Blue : RoundedStyle.Secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Enabled ? RoundedStyle.Secondary : Color.LightGray, 1.6f)) g.DrawLines(pen, new[] { new Point(Width - 27, Height / 2 - 2), new Point(Width - 22, Height / 2 + 3), new Point(Width - 17, Height / 2 - 2) });
            if (Focused && ShowFocusCues) using (var p = RoundedStyle.Round(new RectangleF(2, 2, Width - 4, Height - 4), 10)) using (var pen = new Pen(RoundedStyle.Blue)) g.DrawPath(pen, p);
        }
    }
    class MonitorGlyph : Control
    {
        public int Number { get; set; }
        public bool Active { get; set; }
        public MonitorGlyph() { Size = new Size(60, 60); BackColor = Color.White; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Width / 60f, Height / 60f);
            Color ink = Active ? RoundedStyle.Blue : RoundedStyle.Secondary;
            using (var p = RoundedStyle.Round(new RectangleF(0, 0, 59, 59), 16)) using (var b = new SolidBrush(Active ? Color.FromArgb(233, 243, 255) : RoundedStyle.Background)) g.FillPath(b, p);
            using (var pen = new Pen(ink, 1.8f))
            {
                using (var p = RoundedStyle.Round(new RectangleF(12, 13, 35, 26), 4)) g.DrawPath(pen, p);
                g.DrawLine(pen, 29.5f, 40, 29.5f, 46); g.DrawLine(pen, 21, 46, 38, 46);
            }
            using (var f = new Font("Segoe UI", 10, FontStyle.Bold)) TextRenderer.DrawText(g, Number.ToString(), f, new Rectangle(12, 13, 35, 26), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
