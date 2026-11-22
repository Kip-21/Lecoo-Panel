using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class Palette
{
    internal static readonly Color Base = ColorTranslator.FromHtml("#0B1922");
    internal static readonly Color Card = ColorTranslator.FromHtml("#122B36");
    internal static readonly Color Raised = ColorTranslator.FromHtml("#17343F");
    internal static readonly Color Field = ColorTranslator.FromHtml("#0E202A");
    internal static readonly Color Mint = ColorTranslator.FromHtml("#70EAD0");
    internal static readonly Color Teal = ColorTranslator.FromHtml("#43D9B9");
    internal static readonly Color Ink = ColorTranslator.FromHtml("#F1F7FA");
    internal static readonly Color Muted = ColorTranslator.FromHtml("#A3BAC4");
    internal static readonly Color Dim = ColorTranslator.FromHtml("#728A96");
    internal static readonly Color Edge = ColorTranslator.FromHtml("#355663");
    internal static readonly Color Good = ColorTranslator.FromHtml("#42DB84");
    internal static readonly Color Error = ColorTranslator.FromHtml("#F27D80");
    internal static readonly Color Warning = ColorTranslator.FromHtml("#F2BD69");

    internal static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        if (d <= 0) return path;
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Surface(Graphics g, RectangleF box, float radius, Color top, Color bottom, Color edge)
    {
        if (box.Width < 1 || box.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = Rounded(box, radius))
        using (var gradient = new LinearGradientBrush(box, top, bottom, 90))
        using (var stroke = new Pen(edge))
        {
            g.FillPath(gradient, shape);
            g.DrawPath(stroke, shape);
        }
    }
}

internal sealed class CardGrid : TableLayoutPanel
{
    internal bool Inset, Wave, Banner;
    internal Color ContentSurface { get { return Banner ? Palette.Base : Inset ? Palette.Field : Palette.Card; } }
    public CardGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Palette.Base;
    }
    internal void RefreshSurface()
    {
        BackColor = ControlSurface.ParentSurface(this);
        foreach (Control child in Controls) ControlSurface.Apply(child, ContentSurface);
    }
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        RefreshSurface();
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        ControlSurface.Apply(e.Control, ContentSurface);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var area = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        float radius = (Inset ? 11 : 16) * (Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f);
        if (!Banner)
            Palette.Surface(e.Graphics, area, radius, Inset ? Palette.Card : Palette.Raised, Inset ? Palette.Field : Palette.Card, Palette.Edge);
        if (!Banner && !Wave || Width < 20 || Height < 20) return;
        var g = e.Graphics;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var clip = Palette.Rounded(area, radius))
        using (var wave = new GraphicsPath())
        {
            g.SetClip(clip, CombineMode.Intersect);
            float w = Width, h = Height;
            if (Banner)
            {
                wave.AddBezier(0, h, w * .09f, h * .50f, w * .48f, h * .98f, w * .50f, 0);
                wave.AddLine(w * .50f, 0, 0, 0);
            }
            else
            {
                wave.AddBezier(0, h * .85f, w * .22f, h * .86f, w * .21f, h * 1.02f, w * .53f, h);
                wave.AddLine(w * .53f, h, 0, h);
            }
            wave.CloseFigure();
            using (var tint = new LinearGradientBrush(area,
                Color.FromArgb(Banner ? 11 : 47, Palette.Teal), Color.FromArgb(Banner ? 39 : 0, Palette.Teal), 0f))
                g.FillPath(tint, wave);
        }
        g.Restore(state);
    }
}

internal sealed class LayoutGrid : TableLayoutPanel
{
    internal LayoutGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    internal void RefreshSurface()
    {
        var surface = ControlSurface.ParentSurface(this);
        foreach (Control child in Controls) ControlSurface.Apply(child, surface);
    }
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        RefreshSurface();
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        ControlSurface.Apply(e.Control, ControlSurface.ParentSurface(this));
    }
}

internal static class ControlSurface
{
    internal static Color ParentSurface(Control control)
    {
        for (Control parent = control.Parent; parent != null; parent = parent.Parent)
        {
            var card = parent as CardGrid;
            if (card != null) return card.ContentSurface;
        }
        return Palette.Base;
    }

    internal static void Apply(Control control, Color surface)
    {
        var card = control as CardGrid;
        if (card != null)
        {
            card.RefreshSurface();
            return;
        }
        var grid = control as LayoutGrid;
        if (grid != null)
        {
            grid.RefreshSurface();
            return;
        }
        if (control is ActionButton || control is Toggle || control is PwmSlider || control is Glyph)
            control.BackColor = surface;
        foreach (Control child in control.Controls) Apply(child, surface);
    }
}

internal class ActionButton : Button
{
    private bool hover, pressed;
    internal bool Primary;
    protected virtual bool Arrow { get { return false; } }

    internal ActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Dock = DockStyle.Fill;
        Margin = new Padding(3, 4, 3, 4);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f;
        var top = !Enabled ? Color.FromArgb(23, 49, 58) : Primary ? (pressed ? Palette.Teal : hover ? Color.FromArgb(168, 255, 235) : Palette.Mint)
            : pressed ? Palette.Base : hover ? Palette.Raised : Palette.Field;
        var bottom = Enabled && Primary ? Palette.Teal : top;
        var edge = Enabled && (Primary || Focused || hover) ? Palette.Mint : Palette.Edge;
        Palette.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), 9 * scale, top, bottom, edge);
        var fg = !Enabled ? Palette.Muted : Primary ? Palette.Base : Palette.Ink;
        int pad = (int)(6 * scale);
        var bounds = new Rectangle(pad, 0, Math.Max(0, Width - pad * 2 - (Arrow ? (int)(18 * scale) : 0)), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, fg, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | (Arrow ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Arrow)
        {
            float x = Width - 17 * scale, y = Height / 2f;
            using (var pen = new Pen(fg, 1.5f * scale))
                e.Graphics.DrawLines(pen, new[] { new PointF(x - 4 * scale, y - 2 * scale), new PointF(x, y + 2 * scale), new PointF(x + 4 * scale, y - 2 * scale) });
        }
        if (Focused && ShowFocusCues && Width > 12 && Height > 12)
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), fg, top);
    }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Palette.Field; } }
    public override Color MenuBorder { get { return Palette.Edge; } }
    public override Color MenuItemBorder { get { return Palette.Edge; } }
    public override Color MenuItemSelected { get { return Palette.Raised; } }
    public override Color MenuItemSelectedGradientBegin { get { return Palette.Raised; } }
    public override Color MenuItemSelectedGradientEnd { get { return Palette.Raised; } }
    public override Color MenuItemPressedGradientBegin { get { return Palette.Card; } }
    public override Color MenuItemPressedGradientMiddle { get { return Palette.Card; } }
    public override Color MenuItemPressedGradientEnd { get { return Palette.Card; } }
    public override Color SeparatorDark { get { return Palette.Edge; } }
    public override Color SeparatorLight { get { return Palette.Edge; } }
    public override Color ToolStripGradientBegin { get { return Palette.Field; } }
    public override Color ToolStripGradientMiddle { get { return Palette.Field; } }
    public override Color ToolStripGradientEnd { get { return Palette.Field; } }
}

internal sealed class Choice : ActionButton
{
    private readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false };
    private readonly string[] choices;
    private int selected = -1;
    internal event EventHandler Changed;
    protected override bool Arrow { get { return true; } }
    internal int SelectedIndex
    {
        get { return selected; }
        set
        {
            if (value == selected || value < -1 || value >= choices.Length) return;
            selected = value;
            Text = selected < 0 ? "Выбрать…" : choices[selected];
            for (int i = 0; i < menu.Items.Count; i++) menu.Items[i].ForeColor = i == selected ? Palette.Mint : Palette.Ink;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }
    internal Choice(params string[] options)
    {
        choices = options;
        Text = "Выбрать…";
        AccessibleRole = AccessibleRole.ComboBox;
        menu.BackColor = Palette.Field;
        menu.ForeColor = Palette.Ink;
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        foreach (var option in options)
        {
            int index = menu.Items.Count;
            var item = menu.Items.Add(option);
            item.BackColor = Palette.Field;
            item.ForeColor = Palette.Ink;
            item.Click += (s, e) => { SelectedIndex = index; Focus(); };
        }
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        menu.Font = Font;
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height));
        if (selected >= 0) menu.Items[selected].Select();
        else if (menu.Items.Count > 0) menu.Items[0].Select();
    }
    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key == Keys.Down || key == Keys.Up || key == Keys.F4 || base.IsInputKey(keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.F4)
        {
            OnClick(EventArgs.Empty);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class Toggle : CheckBox
{
    internal Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f;
        float w = 39 * scale, h = 21 * scale, y = (Height - h) / 2;
        var track = Checked ? Color.FromArgb(39, 139, 124) : Palette.Edge;
        Palette.Surface(e.Graphics, new RectangleF(1, y, w, h), h / 2, track, track, Checked ? Palette.Teal : Palette.Dim);
        using (var thumb = new SolidBrush(Palette.Ink))
            e.Graphics.FillEllipse(thumb, Checked ? w - h + 4 * scale : 4 * scale, y + 3 * scale, h - 6 * scale, h - 6 * scale);
        int x = (int)(w + 8 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x, 0, Math.Max(0, Width - x), Height), Palette.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class PwmSlider : Control
{
    private const int Minimum = 1, Maximum = 165;
    private int value = 150;
    private bool hover;

    internal event EventHandler ValueChanged;
    internal int Value
    {
        get { return value; }
        set
        {
            int next = Math.Max(Minimum, Math.Min(Maximum, value));
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }
    internal PwmSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.Slider;
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f, radius = 10 * scale;
        float width = Math.Max(1, Width - 2 * radius - 2), y = Height / 2f;
        float x = radius + 1 + width * (Value - Minimum) / (Maximum - Minimum);
        using (var rail = new Pen(Palette.Edge, 8 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var fill = new Pen(Enabled ? Palette.Mint : Palette.Dim, 8 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var thumb = new SolidBrush(Enabled ? (hover ? Palette.Ink : Palette.Mint) : Palette.Dim))
        {
            e.Graphics.DrawLine(rail, radius + 1, y, radius + 1 + width, y);
            e.Graphics.DrawLine(fill, radius + 1, y, x, y);
            e.Graphics.FillEllipse(thumb, x - radius, y - radius, 2 * radius, 2 * radius);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
    private void PointToValue(int x)
    {
        using (var g = CreateGraphics())
        {
            float padding = 10 * (Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f) + 1;
            Value = Minimum + (int)Math.Round((x - padding) * (Maximum - Minimum) / Math.Max(1, Width - 2 * padding));
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus(); Capture = true; PointToValue(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) PointToValue(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End ||
            key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: Value--; break;
            case Keys.Right: case Keys.Up: Value++; break;
            case Keys.PageDown: Value -= 10; break;
            case Keys.PageUp: Value += 10; break;
            case Keys.Home: Value = Minimum; break;
            case Keys.End: Value = Maximum; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }
}

internal sealed class Glyph : Control
{
    private readonly string kind;
    private int? battery;
    private bool charging;
    internal Glyph(string kind)
    {
        this.kind = kind;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
    }
    internal void SetBattery(int? percent, bool charging)
    {
        battery = percent; this.charging = charging; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float size = Math.Min(Width - 4, Height - 4);
        if (size < 3) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform((Width - size) / 2, (Height - size) / 2);
        g.ScaleTransform(size / 32, size / 32);
        using (var pen = new Pen(Palette.Mint, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            if (kind == "temp")
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLine(12, 18, 12, 7);
                    path.AddArc(12, 3, 8, 8, 180, 180);
                    path.AddLine(20, 7, 20, 18);
                    path.AddArc(9, 16, 14, 14, -53, 286);
                    path.CloseFigure();
                    g.DrawPath(pen, path);
                }
                g.DrawLine(pen, 16, 10, 16, 23);
                g.DrawEllipse(pen, 14, 21, 4, 4);
            }
            else if (kind == "fan")
            {
                for (int i = 0; i < 3; i++)
                {
                    var saved = g.Save();
                    using (var rotation = new Matrix())
                    { rotation.RotateAt(120 * i, new PointF(16, 16)); g.MultiplyTransform(rotation); }
                    g.DrawBezier(pen, 15, 12, 8, 2, 30, -1, 21, 9);
                    g.DrawLine(pen, 21, 9, 18, 13);
                    g.Restore(saved);
                }
                g.DrawEllipse(pen, 12, 12, 8, 8);
            }
            else if (kind == "battery")
            {
                using (var path = Palette.Rounded(new RectangleF(2, 8, 26, 16), 3)) g.DrawPath(pen, path);
                g.DrawLine(pen, 31, 13, 31, 19);
                if (battery.HasValue)
                {
                    using (var fill = new SolidBrush(battery.Value <= 15 ? Palette.Error : Palette.Good))
                        g.FillRectangle(fill, 5, 11, 20 * battery.Value / 100f, 10);
                }
                if (charging)
                    using (var fill = new SolidBrush(Palette.Ink))
                        g.FillPolygon(fill, new[] { new Point(17, 9), new Point(11, 17), new Point(15, 17), new Point(14, 23), new Point(21, 14), new Point(17, 14) });
            }
        }
    }
}

internal sealed class CaptionButton : Button
{
    private readonly bool close;
    private bool hover;

    internal CaptionButton(bool close)
    {
        this.close = close;
        AccessibleName = close ? "Закрыть" : "Свернуть";
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(hover ? close ? Color.FromArgb(175, 47, 59) : Palette.Raised : Palette.Field);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Font.Unit == GraphicsUnit.Pixel ? Font.Size / 14f : 1f;
        float x = Width / 2f, y = Height / 2f, d = 4.5f * scale;
        using (var pen = new Pen(Palette.Ink, Math.Max(1, 1.25f * scale)))
        {
            if (close)
            {
                e.Graphics.DrawLine(pen, x - d, y - d, x + d, y + d);
                e.Graphics.DrawLine(pen, x - d, y + d, x + d, y - d);
            }
            else e.Graphics.DrawLine(pen, x - d, y + d / 2, x + d, y + d / 2);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}
