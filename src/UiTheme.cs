using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DisplayDock
{
    /// <summary>
    /// 统一视觉风格：浅色底 + 白色圆角卡片 + 蓝色主色。
    /// 只依赖 GDI+，不引入任何第三方库，保证仍是单文件绿色程序。
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(244, 246, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Faint = Color.FromArgb(156, 163, 175);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentDeep = Color.FromArgb(30, 64, 175);
        public static readonly Color AccentSoft = Color.FromArgb(219, 234, 254);
        public static readonly Color Ok = Color.FromArgb(22, 163, 74);
        public static readonly Color Warn = Color.FromArgb(180, 83, 9);
        public static readonly Color Danger = Color.FromArgb(190, 18, 60);
        public static readonly Color Hover = Color.FromArgb(241, 245, 249);
        public static readonly Color Pressed = Color.FromArgb(226, 232, 240);
        public static readonly Color LogBg = Color.FromArgb(17, 24, 39);
        public static readonly Color LogText = Color.FromArgb(148, 163, 184);
        public static readonly Color SwitchOff = Color.FromArgb(203, 213, 225);

        public static Font Body(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static GraphicsPath Round(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle rect, int radius, Color color)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = Round(rect, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void StrokeRound(Graphics g, Rectangle rect, int radius, Color color)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = Round(rect, radius))
            using (var pen = new Pen(color))
            {
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>白色圆角卡片容器。</summary>
    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 4 && Height > 4)
            {
                using (GraphicsPath path = Ui.Round(new Rectangle(0, 0, Width, Height), 12))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            Ui.StrokeRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 11, Ui.Border);
        }
    }

    /// <summary>自绘圆角按钮：Primary / Secondary / Ghost 三种样式。</summary>
    internal sealed class FlatButton : Control
    {
        public enum BtnStyle { Primary, Secondary, Ghost }

        private BtnStyle style = BtnStyle.Secondary;
        private bool hover;
        private bool pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = Ui.Body(9.5F, FontStyle.Regular);
            ForeColor = Ui.Text;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public BtnStyle Style
        {
            get { return style; }
            set { style = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Parent == null ? Color.White : Parent.BackColor;
            g.Clear(back);

            Color fill;
            Color border;
            Color text;
            if (!Enabled)
            {
                fill = Color.FromArgb(241, 245, 249);
                border = Ui.Border;
                text = Ui.Faint;
            }
            else if (style == BtnStyle.Primary)
            {
                fill = pressed ? Ui.AccentDeep : (hover ? Ui.AccentDark : Ui.Accent);
                border = fill;
                text = Color.White;
            }
            else if (style == BtnStyle.Ghost)
            {
                fill = pressed ? Ui.Pressed : (hover ? Ui.Hover : back);
                border = fill;
                text = hover ? Ui.Accent : Ui.Muted;
            }
            else
            {
                fill = pressed ? Ui.Pressed : (hover ? Ui.Hover : Color.White);
                border = hover ? Ui.Accent : Ui.Border;
                text = hover ? Ui.Accent : Ui.Text;
            }

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, rect, 8, fill);
            if (style != BtnStyle.Ghost) Ui.StrokeRound(g, rect, 8, border);

            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                pressed = true;
                Invalidate();
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                pressed = false;
                Invalidate();
                OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }
    }

    /// <summary>胶囊形开关（替代系统 CheckBox 的方块勾选）。</summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        private const int PillWidth = 44;
        private const int PillHeight = 22;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Size = new Size(260, 24);
            Cursor = Cursors.Hand;
            Font = Ui.Body(9.25F, FontStyle.Regular);
            ForeColor = Ui.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Color.White : Parent.BackColor);

            Color track = !Enabled ? Color.FromArgb(229, 231, 235) : (Checked ? Ui.Accent : Ui.SwitchOff);
            var pill = new Rectangle(0, (Height - PillHeight) / 2, PillWidth, PillHeight);
            Ui.FillRound(g, pill, PillHeight / 2, track);

            int knob = PillHeight - 6;
            int knobX = Checked ? pill.Right - knob - 3 : pill.X + 3;
            var knobRect = new Rectangle(knobX, pill.Y + 3, knob, knob);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                g.FillEllipse(shadow, knobRect.X, knobRect.Y + 1, knob, knob);
            }
            using (var brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, knobRect);
            }

            if (!String.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(PillWidth + 12, 0, Width - PillWidth - 12, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Ui.Text : Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>扁平下拉框（自绘条目，去掉了 3D 边框）。</summary>
    internal sealed class FlatCombo : ComboBox
    {
        public FlatCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 22;
            IntegralHeight = false;
            Font = Ui.Body(9.25F, FontStyle.Regular);
            BackColor = Color.White;
            ForeColor = Ui.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var back = new SolidBrush(selected ? Ui.AccentSoft : Color.White))
            {
                g.FillRectangle(back, e.Bounds);
            }
            string text = Convert.ToString(Items[e.Index]);
            var rect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            Color color = text != null && text.IndexOf("请选择", StringComparison.Ordinal) >= 0 ? Ui.Faint : Ui.Text;
            Font font = e.Font == null ? Font : e.Font;
            TextRenderer.DrawText(g, text, font, rect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>带圆角边框的输入框外壳（里面是真正的 TextBox）。</summary>
    internal sealed class InputShell : Panel
    {
        public readonly TextBox Inner;

        public InputShell(string text, bool readOnly)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.Font = Ui.Body(9.5F, FontStyle.Regular);
            Inner.BackColor = readOnly ? Color.FromArgb(248, 250, 252) : Color.White;
            Inner.ForeColor = Ui.Text;
            Inner.Text = text;
            Inner.ReadOnly = readOnly;
            Inner.TabStop = !readOnly;
            Inner.GotFocus += delegate { Invalidate(); };
            Inner.LostFocus += delegate { Invalidate(); };
            Inner.TextChanged += delegate { Invalidate(); };
            Controls.Add(Inner);
        }

        public override string Text
        {
            get { return Inner == null ? base.Text : Inner.Text; }
            set
            {
                if (Inner == null) base.Text = value;
                else Inner.Text = value;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Inner == null) return;
            Inner.SetBounds(10, (Height - Inner.Height) / 2 + 1, Math.Max(10, Width - 20), Inner.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Color.White : Parent.BackColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, rect, 8, Inner != null && Inner.ReadOnly ? Color.FromArgb(248, 250, 252) : Color.White);
            Color border = !Enabled ? Ui.Border : (Inner != null && Inner.Focused ? Ui.Accent : Ui.Border);
            Ui.StrokeRound(g, rect, 8, border);
        }
    }

    /// <summary>深色日志面板（圆角），内部承载只读 TextBox。</summary>
    internal sealed class LogShell : Panel
    {
        public readonly TextBox Inner;

        public LogShell()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.Multiline = true;
            Inner.ReadOnly = true;
            Inner.ScrollBars = ScrollBars.Vertical;
            Inner.BackColor = Ui.LogBg;
            Inner.ForeColor = Ui.LogText;
            Inner.Font = Ui.Mono(8.75F);
            Inner.WordWrap = true;
            Controls.Add(Inner);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 8 && Height > 8)
            {
                using (GraphicsPath path = Ui.Round(new Rectangle(0, 0, Width, Height), 10))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            if (Inner != null) Inner.SetBounds(12, 10, Math.Max(10, Width - 24), Math.Max(10, Height - 20));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            Ui.FillRound(g, new Rectangle(0, 0, Width, Height), 10, Ui.LogBg);
        }
    }
}
