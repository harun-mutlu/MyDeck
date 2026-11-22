using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MyDeck.Server.Editor;

static class Chrome
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static readonly ConditionalWeakTable<Control, object> Hooked = new();

    public static void DarkTitleBar(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    public static void Apply(Control root)
    {
        if (Hooked.TryGetValue(root, out _)) return;
        Hooked.Add(root, true);

        Style(root);
        root.HandleCreated += (_, _) => Theme(root);
        if (root.IsHandleCreated) Theme(root);
        root.ControlAdded += (_, e) => { if (e.Control is { } c) Apply(c); };
        foreach (Control c in root.Controls) Apply(c);
    }

    static void Style(Control c)
    {
        switch (c)
        {
            case CheckBox cb when cb.Appearance == Appearance.Normal:
                cb.FlatStyle = FlatStyle.Flat;
                cb.FlatAppearance.BorderColor = Ui.Muted;
                cb.FlatAppearance.CheckedBackColor = Ui.Field;
                cb.FlatAppearance.MouseOverBackColor = Ui.Field;
                cb.FlatAppearance.MouseDownBackColor = Ui.Border;
                cb.Cursor = Cursors.Hand;

                cb.Paint += PaintCheck;
                cb.CheckedChanged += (_, _) => cb.Invalidate();
                break;
            case RadioButton rb:
                rb.FlatStyle = FlatStyle.Flat;
                rb.FlatAppearance.BorderColor = Ui.Muted;
                rb.Cursor = Cursors.Hand;
                break;
            case ListBox lb:
                lb.BorderStyle = BorderStyle.None;
                break;
        }
    }

    static void PaintCheck(object? sender, PaintEventArgs e)
    {
        if (sender is not CheckBox cb || cb.CheckAlign != ContentAlignment.MiddleLeft) return;
        var dpi = cb.DeviceDpi / 96f;
        var size = (int)Math.Round(14 * dpi);
        var r = new Rectangle(0, (cb.Height - size) / 2, size, size);
        var g = e.Graphics;
        using (var clear = new SolidBrush(cb.Parent?.BackColor is { A: 255 } pb ? pb : Ui.Panel)) g.FillRectangle(clear, r.X, r.Y, r.Width + 1, r.Height + 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
        using var path = new GraphicsPath();
        var d = 4 * dpi;
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        var on = cb.CheckState != CheckState.Unchecked;
        var accent = cb.Enabled ? Ui.Cyan : Ui.Muted;
        using (var fill = new SolidBrush(on ? accent : Ui.Field)) g.FillPath(fill, path);
        using (var border = new Pen(on ? accent : (cb.Enabled ? Ui.Muted : Ui.Border), 1.2f * dpi)) g.DrawPath(border, path);
        if (cb.CheckState == CheckState.Checked)
        {
            using var tick = new Pen(Ui.Bg, 2f * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(tick, new[]
            {
                new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
                new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.32f),
            });
        }
        else if (cb.CheckState == CheckState.Indeterminate)
            using (var bar = new SolidBrush(Ui.Bg)) g.FillRectangle(bar, box.X + box.Width * 0.25f, box.Y + box.Height * 0.45f, box.Width * 0.5f, box.Height * 0.12f);
    }

    static void Theme(Control c)
    {
        string? theme = c switch
        {
            ComboBox => "DarkMode_CFD",
            TextBoxBase or ListBox or ListView or TreeView or UpDownBase or ScrollableControl => "DarkMode_Explorer",
            _ when c.GetType().Name == "UpDownButtons" => "DarkMode_Explorer",
            _ => null,
        };
        if (theme is null) return;
        try { SetWindowTheme(c.Handle, theme, null); } catch {  }
    }

    public static void InstallMenuRenderer() => ToolStripManager.Renderer = new MenuRenderer();

    sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Ink : Ui.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ui.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Ui.Cyan, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.25f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.70f),
                new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.32f),
            });
        }
    }

    sealed class Colors : ProfessionalColorTable
    {
        public Colors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => Ui.Card;
        public override Color MenuBorder => Ui.Border;
        public override Color MenuItemBorder => Ui.Field;
        public override Color MenuItemSelected => Ui.Field;
        public override Color MenuItemSelectedGradientBegin => Ui.Field;
        public override Color MenuItemSelectedGradientEnd => Ui.Field;
        public override Color MenuItemPressedGradientBegin => Ui.Border;
        public override Color MenuItemPressedGradientEnd => Ui.Border;
        public override Color ImageMarginGradientBegin => Ui.Card;
        public override Color ImageMarginGradientMiddle => Ui.Card;
        public override Color ImageMarginGradientEnd => Ui.Card;
        public override Color SeparatorDark => Ui.Border;
        public override Color SeparatorLight => Ui.Card;
        public override Color CheckBackground => Ui.Card;
        public override Color CheckSelectedBackground => Ui.Field;
        public override Color CheckPressedBackground => Ui.Field;
        public override Color ToolStripBorder => Ui.Border;
        public override Color ToolStripGradientBegin => Ui.Panel;
        public override Color ToolStripGradientMiddle => Ui.Panel;
        public override Color ToolStripGradientEnd => Ui.Panel;
    }
}

public class DarkForm : Form
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Chrome.DarkTitleBar(Handle);
        Chrome.Apply(this);
    }
}

sealed class FlatSlider : Control
{
    int _min, _max = 10, _value;
    bool _drag, _hover;

    public event EventHandler? ValueChanged;

    [DefaultValue(0)] public int Minimum { get => _min; set { _min = value; if (_max < _min) _max = _min; Value = _value; Invalidate(); } }
    [DefaultValue(10)] public int Maximum { get => _max; set { _max = value; if (_min > _max) _min = _max; Value = _value; Invalidate(); } }

    [DefaultValue(1)] public int TickFrequency { get; set; } = 1;

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public FlatSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        Height = 30;
        Width = 200;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        TabStop = true;
    }

    const float Knob = 8f;
    RectangleF Track => new(Knob + 2, Height / 2f - 2, Math.Max(1, Width - 2 * (Knob + 2)), 4);

    float Ratio => _max == _min ? 0 : (_value - _min) / (float)(_max - _min);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tr = Track;
        var x = tr.Left + tr.Width * Ratio;
        var accent = Enabled ? Ui.Cyan : Ui.Muted;
        using (var p = Pill(tr)) using (var b = new SolidBrush(Ui.Border)) g.FillPath(b, p);
        if (x > tr.Left + 0.5f)
            using (var p = Pill(new RectangleF(tr.Left, tr.Top, x - tr.Left, tr.Height))) using (var b = new SolidBrush(accent)) g.FillPath(b, p);
        var k = _drag ? Knob + 1 : _hover || Focused ? Knob : Knob - 1;
        using (var halo = new SolidBrush(Color.FromArgb(_drag || _hover ? 50 : 0, accent)))
            g.FillEllipse(halo, x - k - 4, Height / 2f - k - 4, (k + 4) * 2, (k + 4) * 2);
        using (var kb = new SolidBrush(Ui.Ink)) g.FillEllipse(kb, x - k, Height / 2f - k, k * 2, k * 2);
        using (var kp = new Pen(accent, 2.5f)) g.DrawEllipse(kp, x - k + 1.25f, Height / 2f - k + 1.25f, k * 2 - 2.5f, k * 2 - 2.5f);
    }

    static GraphicsPath Pill(RectangleF r)
    {
        var p = new GraphicsPath();
        var d = Math.Min(r.Height, r.Width);
        if (d < 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.Left, r.Top, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    void SetFromX(int x)
    {
        var tr = Track;
        var t = Math.Clamp((x - tr.Left) / tr.Width, 0, 1);
        Value = _min + (int)Math.Round(t * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _drag = true;
        Capture = true;
        SetFromX(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value--; break;
            case Keys.Right or Keys.Up: Value++; break;
            case Keys.Home: Value = _min; break;
            case Keys.End: Value = _max; break;
        }
    }
}
