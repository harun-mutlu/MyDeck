using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

static class Fx
{
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static readonly Dictionary<float, Font> IconFonts = new();
    static readonly string IconFamily =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Font Icons(float px)
    {
        if (!IconFonts.TryGetValue(px, out var f)) IconFonts[px] = f = new Font(IconFamily, px, GraphicsUnit.Pixel);
        return f;
    }

    public static void Glyph(Graphics g, string glyph, float px, Color color, RectangleF r)
    {
        var old = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var b = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, Icons(px), b, r, sf);
        g.TextRenderingHint = old;
    }

    static Icon? _appIcon;
    public static Icon AppIcon
    {
        get
        {
            if (_appIcon is not null) return _appIcon;
            using var st = typeof(Fx).Assembly.GetManifestResourceStream("MyDeck.ico");
            return _appIcon = st is null ? SystemIcons.Application : new Icon(st);
        }
    }

    public static void DrawLogo(Graphics g, RectangleF r)
    {
        var n = Math.Min(r.Width, r.Height);
        float m = n * 3 / 32, s = n * 12 / 32, gap = n * 2 / 32, rad = Math.Max(1, n * 2.5f / 32);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        void Sq(Color c, float x, float y)
        {
            using var p = Round(new RectangleF(r.X + x, r.Y + y, s, s), rad);
            using var b = new SolidBrush(c);
            g.FillPath(b, p);
        }
        Sq(Ui.Accent, m, m);
        Sq(Ui.Cyan, m + s + gap, m);
        Sq(Ui.Cyan, m, m + s + gap);
        Sq(ColorTranslator.FromHtml("#5A6680"), m + s + gap, m + s + gap);
        g.SmoothingMode = old;
    }

    public static Control Logo(int size)
    {
        var l = new Label { Size = new Size(size, size), Margin = Padding.Empty };
        l.Paint += (_, e) => { e.Graphics.Clear(l.Parent?.BackColor ?? Ui.Panel); DrawLogo(e.Graphics, new RectangleF(0, 0, size, size)); };
        return l;
    }

    public static void ClearToParent(Control c, Graphics g)
    {
        var bg = c.Parent?.BackColor ?? Ui.Panel;
        if (bg.A < 255) bg = Ui.Panel;
        g.Clear(bg);
    }

    public static class I
    {
        public const string Feedback = "", Download = "", Refresh = "", Info = "";
        public const string Add = "", Remove = "", Close = "", More = "", Settings = "",
            Edit = "", Search = "", Undo = "", Redo = "", Palette = "", Bolt = "",
            Puzzle = "", Library = "", Tablet = "", Monitor = "", Grid = "", Aspect = "",
            Expand = "", Square = "", Ruler = "", Star = "", App = "", Music = "",
            Cloud = "", Clock = "", Volume = "", Mic = "", Cpu = "", Chart = "",
            Game = "", Touch = "", ToggleIcon = "", Sliders = "", ChevronDown = "",
            Play = "", Copy = "", Paste = "", Delete = "", Check = "", Temp = "",
            Keyboard = "", Home = "", Folder = "", History = "", Up = "", Down = "",
            Share = "", Pin = "", Pinned = "", Save = "", Open = "", Smart = "", Plug = "", Back = "";
    }
}

public enum ButtonKind { Secondary, Primary, Ghost, Danger }

public class ModernButton : Button
{
    bool _hover, _down;
    string? _glyph;
    ButtonKind _kind;

    [DefaultValue(ButtonKind.Secondary)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    [DefaultValue(null)]
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    [DefaultValue(8)] public int Radius { get; set; } = 8;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Ui.Card;
        ForeColor = Ui.Ink;
        Font = Ui.Body;
        Cursor = Cursors.Hand;
        Padding = new Padding(12, 0, 12, 0);
        UseVisualStyleBackColor = false;
        TabStop = true;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
        var icon = _glyph is null && Image is null ? 0 : 18 + (text > 0 ? 8 : 0);
        var w = text + icon + Padding.Horizontal;
        if (text == 0) w = Math.Max(Height, 30);
        return new Size(Math.Max(w, 34), Height);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Width = GetPreferredSize(Size.Empty).Width; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    bool CustomFill => _kind == ButtonKind.Secondary && BackColor != Ui.Card && BackColor.A == 255;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, border, ink;
        switch (_kind)
        {
            case ButtonKind.Primary:
                fill = _down ? Ui.Mix(Ui.Accent, Color.Black, 0.12f) : _hover ? Ui.AccentHover : Ui.Accent;
                border = fill; ink = Color.White; break;
            case ButtonKind.Ghost:
                fill = _down ? Ui.Field : _hover ? Ui.Card : Color.Empty;
                border = Color.Empty; ink = _hover ? Ui.Ink : Ui.Muted; break;
            case ButtonKind.Danger:
                fill = _down ? Ui.Field : _hover ? Ui.Mix(Ui.Card, Ui.Bad, 0.12f) : Ui.Card;
                border = _hover ? Ui.Mix(Ui.Border, Ui.Bad, 0.5f) : Ui.Border; ink = Ui.Bad; break;
            default:
                fill = CustomFill ? BackColor : _down ? Ui.Border : _hover ? Ui.Field : Ui.Card;
                border = CustomFill ? Ui.Mix(BackColor, Color.White, 0.18f) : _hover ? Ui.Mix(Ui.Border, Ui.Ink, 0.12f) : Ui.Border;
                ink = CustomFill ? ForeColor : Ui.Ink; break;
        }
        if (!Enabled) ink = Ui.Mix(ink, Ui.Bg, 0.5f);
        using (var path = Fx.Round(r, Radius))
        {
            if (!fill.IsEmpty) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (!border.IsEmpty) using (var p = new Pen(border, 1)) g.DrawPath(p, path);
            if (Focused && ShowFocusCues) using (var p = new Pen(Ui.Cyan, 1.5f)) g.DrawPath(p, path);
        }
        var hasText = !string.IsNullOrEmpty(Text);
        var textW = hasText ? TextRenderer.MeasureText(Text, Font).Width : 0;
        var hasIcon = _glyph is not null || Image is not null;
        var iconW = hasIcon ? 18 : 0;
        var gap = hasIcon && hasText ? 8 : 0;
        var total = iconW + gap + textW;
        var x = (Width - total) / 2f;
        if (hasText && TextAlign == ContentAlignment.MiddleLeft) x = Padding.Left;
        if (_glyph is not null) Fx.Glyph(g, _glyph, 16, ink, new RectangleF(x - 1, 0, iconW + 2, Height));
        else if (Image is not null) g.DrawImage(Image, x, (Height - 18) / 2f, 18, 18);
        if (hasText)
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(x + iconW + gap), 0, Width - (int)(x + iconW + gap), Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public static ModernButton Icon(string glyph, string tip, ToolTip? tt = null, int size = 34)
    {
        var b = new ModernButton { Glyph = glyph, Kind = ButtonKind.Ghost, Width = size, Height = size, Margin = new Padding(1, 0, 1, 0), Padding = Padding.Empty };
        (tt ?? SharedTip).SetToolTip(b, tip);
        b.AccessibleName = tip;
        return b;
    }

    public static readonly ToolTip SharedTip = new() { InitialDelay = 350 };
}

public class ModernToggle : Control
{
    bool _checked, _hover;
    public event EventHandler? CheckedChanged;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public ModernToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 28; Width = 236; Font = Ui.Body; ForeColor = Ui.Ink2; Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 2);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Space or Keys.Enter) Checked = !Checked; }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int w = 38, h = 22;
        var sr = new RectangleF(Width - w - 1, (Height - h) / 2f, w, h);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, (int)sr.Left - 8, Height), Enabled ? (_hover ? Ui.Ink : ForeColor) : Ui.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var track = _checked ? (Enabled ? Ui.Accent : Ui.Muted) : (_hover ? Ui.Mix(Ui.Border, Ui.Ink, 0.15f) : Ui.Border);
        using (var p = Fx.Round(sr, h / 2f)) using (var b = new SolidBrush(track)) g.FillPath(b, p);
        var k = h - 6;
        var kx = _checked ? sr.Right - 3 - k : sr.Left + 3;
        using (var b = new SolidBrush(_checked ? Color.White : Ui.Ink2)) g.FillEllipse(b, kx, sr.Top + 3, k, k);
        if (Focused && ShowFocusCues) using (var p = Fx.Round(RectangleF.Inflate(sr, 2, 2), h / 2f + 2)) using (var pen = new Pen(Ui.Cyan, 1)) g.DrawPath(pen, p);
    }
}

public class ModernStepper : Control
{
    int _min, _max = 100, _value;
    int _hot = -1;
    public event EventHandler? ValueChanged;

    [DefaultValue(0)] public int Minimum { get => _min; set { _min = value; if (_max < _min) _max = _min; Value = _value; Invalidate(); } }
    [DefaultValue(100)] public int Maximum { get => _max; set { _max = value; if (_min > _max) _min = _max; Value = _value; Invalidate(); } }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set { var v = Math.Clamp(value, _min, _max); if (v == _value) return; _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    public ModernStepper()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 36; Width = 104; Font = new Font("Segoe UI Semibold", 11f); Cursor = Cursors.Hand;
        Margin = new Padding(0);
    }

    [DefaultValue(1)] public int Increment { get; set; } = 1;
    string? _typed;
    readonly System.Windows.Forms.Timer _repeat = new() { Interval = 400 };
    int _repeatDir;

    int Zone(int x) => x < 34 ? 0 : x > Width - 34 ? 1 : -1;

    void Step(int dir) { Commit(); Value += dir * Increment; }

    void Commit()
    {
        if (_typed is null) return;
        var t = _typed; _typed = null;
        if (int.TryParse(t, out var v)) Value = v;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var z = Zone(e.X); if (z != _hot) { _hot = z; Invalidate(); } Cursor = z >= 0 ? Cursors.Hand : Cursors.IBeam; }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var z = Zone(e.X);
        if (z < 0) return;
        _repeatDir = z == 0 ? -1 : 1;
        Step(_repeatDir);
        _repeat.Interval = 400;
        _repeat.Tick -= OnRepeat; _repeat.Tick += OnRepeat;
        _repeat.Start();
    }
    void OnRepeat(object? s, EventArgs e) { _repeat.Interval = 60; Step(_repeatDir); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _repeat.Stop(); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Focused) return;
        Step(Math.Sign(e.Delta));
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Up or Keys.Right) Step(1);
        else if (e.KeyCode is Keys.Down or Keys.Left) Step(-1);
        else if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape && _typed is not null) { _typed = null; Invalidate(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Back && _typed is { Length: > 0 }) { _typed = _typed[..^1]; Invalidate(); }
    }
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (char.IsDigit(e.KeyChar) && (_typed?.Length ?? 0) < 6) { _typed = (_typed ?? "") + e.KeyChar; Invalidate(); e.Handled = true; }
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _repeat.Stop(); Commit(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Fx.Round(r, 9))
        {
            using (var b = new SolidBrush(Ui.Bg)) g.FillPath(b, path);
            var st = g.Save();
            g.SetClip(path);
            if (_hot >= 0)
                using (var hb = new SolidBrush(Ui.Field))
                    g.FillRectangle(hb, _hot == 0 ? 0 : Width - 34, 0, 34, Height);
            g.Restore(st);
            using (var p = new Pen(Focused ? Ui.Mix(Ui.Border, Ui.Cyan, 0.6f) : Ui.Border, 1)) g.DrawPath(p, path);
        }
        Fx.Glyph(g, Fx.I.Remove, 12, _value > _min ? (_hot == 0 ? Ui.Ink : Ui.Muted) : Ui.Border, new RectangleF(0, 0, 34, Height));
        Fx.Glyph(g, Fx.I.Add, 12, _value < _max ? (_hot == 1 ? Ui.Ink : Ui.Muted) : Ui.Border, new RectangleF(Width - 34, 0, 34, Height));
        TextRenderer.DrawText(g, _typed ?? _value.ToString(), Font, new Rectangle(34, 0, Width - 68, Height), _typed is null ? Ui.Ink : Ui.Cyan,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public class ModernSegmented : Control
{
    string[] _items = Array.Empty<string>();
    string?[] _glyphs = Array.Empty<string?>();
    bool[] _marks = Array.Empty<bool>();
    int _sel, _hot = -1;
    public event EventHandler? SelectedIndexChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Items { get => _items; set { _items = value; _marks = new bool[value.Length]; _glyphs = new string?[value.Length]; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string?[] Glyphs { get => _glyphs; set { _glyphs = value; Invalidate(); } }

    [DefaultValue(0)]
    public int SelectedIndex
    {
        get => _sel;
        set { if (value == _sel || value < 0 || value >= _items.Length) return; _sel = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(int index) { if (index >= 0 && index < _items.Length) { _sel = index; Invalidate(); } }

    public void SetMark(int i, bool on) { if (i >= 0 && i < _marks.Length && _marks[i] != on) { _marks[i] = on; Invalidate(); } }

    public ModernSegmented()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 36; Width = 236; Font = Ui.Body; Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 0, 4);
    }

    RectangleF Seg(int i)
    {
        var w = (Width - 6f - 3 * (_items.Length - 1)) / Math.Max(1, _items.Length);
        return new RectangleF(3 + i * (w + 3), 3, w, Height - 6);
    }

    int HitIndex(Point p) { for (var i = 0; i < _items.Length; i++) if (Seg(i).Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var h = HitIndex(e.Location); if (h != _hot) { _hot = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); var h = HitIndex(e.Location); if (h >= 0) SelectedIndex = h; }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _sel - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_items.Length - 1, _sel + 1);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Fx.Round(r, 10))
        {
            using (var b = new SolidBrush(Ui.Bg)) g.FillPath(b, path);
            using (var p = new Pen(Focused ? Ui.Mix(Ui.Border, Ui.Cyan, 0.5f) : Ui.Border, 1)) g.DrawPath(p, path);
        }
        for (var i = 0; i < _items.Length; i++)
        {
            var s = Seg(i);
            var on = i == _sel;
            if (on || i == _hot)
                using (var p = Fx.Round(s, 7)) using (var b = new SolidBrush(on ? Ui.Field : Ui.Card)) g.FillPath(b, p);
            var ink = on ? Ui.Ink : i == _hot ? Ui.Ink2 : Ui.Muted;
            var text = _items[i];
            var tw = TextRenderer.MeasureText(text, Font).Width;
            var glyph = i < _glyphs.Length ? _glyphs[i] : null;
            var iw = glyph is null ? 0 : 20;
            var mark = i < _marks.Length && _marks[i];
            var total = iw + tw + (mark ? 10 : 0);
            var x = s.Left + (s.Width - total) / 2;
            if (glyph is not null) Fx.Glyph(g, glyph, 14, ink, new RectangleF(x, s.Top, 18, s.Height));
            TextRenderer.DrawText(g, text, Font, new Rectangle((int)(x + iw), (int)s.Top, tw + 2, (int)s.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (mark) using (var b = new SolidBrush(Ui.Cyan)) g.FillEllipse(b, x + iw + tw + 4, s.Top + s.Height / 2 - 3, 6, 6);
        }
    }
}

public class CardStack : FlowLayoutPanel
{
    public CardStack()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Ui.Card;
        Padding = new Padding(12, 6, 12, 10);
        Margin = new Padding(0, 0, 0, 10);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var outer = Parent?.BackColor ?? Ui.Panel;
        if (outer.A < 255) outer = Ui.Panel;
        g.Clear(outer);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Fx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
        using (var p = new Pen(Ui.Border, 1)) g.DrawPath(p, path);
    }
}

public class ModernTextBox : TextBox
{
    sealed class Host : Panel
    {
        public readonly ModernTextBox Box;
        public Host(ModernTextBox box)
        {
            Box = box;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Ui.Bg;
            Cursor = Cursors.IBeam;
            Click += (_, _) => Box.Focus();
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var outer = Parent?.BackColor ?? Ui.Panel;
            if (outer.A < 255) outer = Ui.Panel;
            g.Clear(outer);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Fx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9);
            using (var b = new SolidBrush(Ui.Bg)) g.FillPath(b, path);
            var c = Box.Focused ? Ui.Mix(Ui.Border, Ui.Cyan, 0.7f) : Box._hover ? Ui.Mix(Ui.Border, Ui.Ink, 0.15f) : Ui.Border;
            using (var p = new Pen(c, 1)) g.DrawPath(p, path);
        }
    }

    Host? _host;
    bool _wrapping, _selfVisible = true, _hover;
    const int PadX = 10;

    public ModernTextBox()
    {
        BorderStyle = BorderStyle.None;
        BackColor = Ui.Bg;
        ForeColor = Ui.Ink;
        Font = Ui.Body;
    }

    int HostHeight => Multiline ? Math.Max(Height, PreferredHeight) + 14 : PreferredHeight + 14;
    int InnerHeight => Multiline && _host is not null ? Math.Max(PreferredHeight, _host.Height - 14) : PreferredHeight;

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (_wrapping || Parent is null || Parent is Host) return;
        var parent = Parent;
        _wrapping = true;
        try
        {
            var index = parent.Controls.GetChildIndex(this);
            _host ??= new Host(this);
            _host.Margin = Margin;
            _host.Anchor = Anchor;
            _host.Dock = Dock;
            _host.Size = new Size(Width, HostHeight);
            _host.Location = Location;
            _host.Visible = _selfVisible;
            Dock = DockStyle.None;
            parent.Controls.Add(_host);
            parent.Controls.SetChildIndex(_host, index);
            _host.Controls.Add(this);
            LayoutInHost();
        }
        finally { _wrapping = false; }
    }

    void LayoutInHost()
    {
        if (_host is null) return;
        var w = Math.Max(10, _host.Width - 2 * PadX);
        SetBounds(PadX, (_host.Height - InnerHeight) / 2, w, InnerHeight);
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_host is not null && !_wrapping && Parent == _host && (specified & BoundsSpecified.Width) != 0 && !_layingOut)
        {
            _layingOut = true;
            _host.Width = width;
            if (Multiline && (specified & BoundsSpecified.Height) != 0) _host.Height = height + 14;
            _layingOut = false;
            base.SetBoundsCore(PadX, (_host.Height - InnerHeight) / 2, Math.Max(10, width - 2 * PadX), InnerHeight, BoundsSpecified.All);
            return;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }
    bool _layingOut;

    protected override void SetVisibleCore(bool value)
    {
        _selfVisible = value;
        if (_host is not null) _host.Visible = value;
        base.SetVisibleCore(value);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); _host?.Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _host?.Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; _host?.Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _host?.Invalidate(); }
}

public class ModernCombo : ComboBox
{
    bool _hover;
    const int WM_PAINT = 0x000F;

    public ModernCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Ui.Bg;
        ForeColor = Ui.Ink;
        Font = Ui.Body;
        ItemHeight = 26;
        IntegralHeight = false;
        MaxDropDownItems = 14;
        Cursor = Cursors.Hand;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(sel ? Ui.Field : Ui.Card)) e.Graphics.FillRectangle(b, e.Bounds);
        if (sel) using (var a = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y + 5, 3, e.Bounds.Height - 10);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), Ui.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT && DropDownStyle == ComboBoxStyle.DropDownList) PaintClosed();
    }

    void PaintClosed()
    {
        using var g = CreateGraphics();
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Fx.Round(r, 9))
        {
            using (var b = new SolidBrush(_hover || DroppedDown ? Ui.Card : Ui.Bg)) g.FillPath(b, path);
            var c = Focused || DroppedDown ? Ui.Mix(Ui.Border, Ui.Cyan, 0.6f) : _hover ? Ui.Mix(Ui.Border, Ui.Ink, 0.15f) : Ui.Border;
            using (var p = new Pen(c, 1)) g.DrawPath(p, path);
        }
        var text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
        TextRenderer.DrawText(g, text, Font, new Rectangle(10, 0, Width - 40, Height), Enabled ? Ui.Ink : Ui.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        Fx.Glyph(g, Fx.I.ChevronDown, 11, Ui.Muted, new RectangleF(Width - 30, 0, 24, Height));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}

public class ScrollPanel : Control
{
    public readonly FlowLayoutPanel Content;
    readonly Bar _bar;
    int _offset;

    public ScrollPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Panel;
        Content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = Padding.Empty,
        };
        Content.BackColor = BackColor;
        _bar = new Bar(this);
        Controls.Add(_bar);
        Controls.Add(Content);
        _bar.BringToFront();
        Content.SizeChanged += (_, _) => Relayout();
        Hook(Content);
    }

    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); if (Content is not null) Content.BackColor = BackColor; }

    [DefaultValue(typeof(Padding), "0, 0, 0, 0")]
    public Padding Inset { get; set; }

    void Hook(Control c)
    {
        c.MouseWheel += OnChildWheel;
        c.ControlAdded += (_, e) => { if (e.Control is { } x) Hook(x); };
        foreach (Control x in c.Controls) Hook(x);
    }

    void OnChildWheel(object? sender, MouseEventArgs e)
    {
        if (sender is ComboBox { DroppedDown: true } || sender is ModernStepper { Focused: true } || sender is FlatSlider) return;
        if (sender is ComboBox cb && !cb.DroppedDown && e is HandledMouseEventArgs h0) h0.Handled = true;
        ScrollBy(-e.Delta / 2);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollBy(-e.Delta / 2); }

    int MaxOffset => Math.Max(0, Content.Height + Inset.Vertical - Height);

    public void ScrollBy(int dy) => ScrollTo(_offset + dy);

    public void ScrollTo(int y)
    {
        var v = Math.Clamp(y, 0, MaxOffset);
        if (v == _offset) return;
        _offset = v;
        Relayout();
    }

    public void Reveal(Control c)
    {
        var p = Content.PointToClient(c.PointToScreen(Point.Empty));
        if (p.Y - _offset < 0) ScrollTo(p.Y);
        else if (p.Y + c.Height - _offset > Height) ScrollTo(p.Y + c.Height - Height + 12);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    void Relayout()
    {
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        Content.Location = new Point(Inset.Left, Inset.Top - _offset);
        var need = MaxOffset > 0;
        _bar.Visible = need;
        _bar.SetBounds(Width - Bar.HitWidth, 0, Bar.HitWidth, Height);
        _bar.Invalidate();
    }

    sealed class Bar : Control
    {
        public const int HitWidth = 12;
        readonly ScrollPanel _p;
        bool _hover, _drag;
        int _dragStart, _dragOffset;

        public Bar(ScrollPanel p)
        {
            _p = p;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Default;
        }

        RectangleF Thumb()
        {
            var total = _p.Content.Height + _p.Inset.Vertical;
            var h = Math.Max(28f, Height * (float)Height / Math.Max(1, total));
            var y = _p.MaxOffset == 0 ? 0 : (Height - h) * _p._offset / _p.MaxOffset;
            var w = _hover || _drag ? 8f : 4f;
            return new RectangleF(Width - w - 2, y + 2, w, h - 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_p.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Thumb();
            using var path = Fx.Round(t, t.Width / 2);
            using var b = new SolidBrush(_hover || _drag ? Ui.Mix(Ui.Border, Ui.Ink, 0.35f) : Color.FromArgb(150, Ui.Mix(Ui.Border, Ui.Ink, 0.2f)));
            g.FillPath(b, path);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var t = Thumb();
            if (e.Y < t.Top || e.Y > t.Bottom)
            {
                var ratio = Math.Clamp((e.Y - t.Height / 2) / Math.Max(1, Height - t.Height), 0, 1);
                _p.ScrollTo((int)(ratio * _p.MaxOffset));
            }
            _drag = true; Capture = true; _dragStart = e.Y; _dragOffset = _p._offset;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_drag) return;
            var t = Thumb();
            var per = _p.MaxOffset / Math.Max(1f, Height - t.Height - 4);
            _p.ScrollTo(_dragOffset + (int)((e.Y - _dragStart) * per));
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Capture = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _p.ScrollBy(-e.Delta / 2); }
    }
}

public class PageGrid : Control
{
    Func<IReadOnlyList<Page>>? _source;
    Func<Page, bool>? _isStart;
    int _sel = -1, _hot = -1;
    public event EventHandler? SelectedIndexChanged;
    public event Action<int, Point>? ContextMenuAt;

    const int Cols = 2, Gap = 8, LabelH = 22;

    public PageGrid()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 236; Font = Ui.Small; Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 0, 10);
    }

    public void Bind(Func<IReadOnlyList<Page>> source, Func<Page, bool> isStart) { _source = source; _isStart = isStart; RefreshItems(); }

    IReadOnlyList<Page> Pages => _source?.Invoke() ?? Array.Empty<Page>();

    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _sel;
        set { if (value == _sel) return; _sel = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(int i) { _sel = i; Invalidate(); }

    int CellW => (Width - Gap * (Cols - 1)) / Cols;
    int ThumbH => (int)(CellW * 600f / 1024f) + 8;
    int CellH => ThumbH + LabelH + 8;

    public void RefreshItems()
    {
        var rows = (Pages.Count + Cols - 1) / Cols;
        Height = Math.Max(CellH, rows * CellH + (rows - 1) * Gap);
        Invalidate();
    }

    Rectangle CellRect(int i) => new(i % Cols * (CellW + Gap), i / Cols * (CellH + Gap), CellW, CellH);

    int HitIndex(Point p) { for (var i = 0; i < Pages.Count; i++) if (CellRect(i).Contains(p)) return i; return -1; }

    protected override void OnResize(EventArgs e) { base.OnResize(e); RefreshItems(); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var h = HitIndex(e.Location); if (h != _hot) { _hot = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var h = HitIndex(e.Location);
        if (h < 0) return;
        SelectedIndex = h;
        if (e.Button == MouseButtons.Right) ContextMenuAt?.Invoke(h, e.Location);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Fx.ClearToParent(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pages = Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            var pg = pages[i];
            var r = CellRect(i);
            var on = i == _sel;
            using (var path = Fx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 10))
            {
                using (var b = new SolidBrush(i == _hot && !on ? Ui.Field : Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(on ? Ui.Accent : Ui.Border, on ? 1.6f : 1)) g.DrawPath(p, path);
            }

            var tr = new RectangleF(r.X + 6, r.Y + 6, r.Width - 12, ThumbH - 6);
            using (var sp = Fx.Round(tr, 5)) using (var sb = new SolidBrush(Ui.Bg)) g.FillPath(sb, sp);
            var geo = Grid.Layout(tr.Width, tr.Height, pg.Cols, pg.Rows, 1.5f, 3f, pg.Shape == "square");
            var taken = new bool[pg.Cols, pg.Rows];
            using (var cb = new SolidBrush(on ? Ui.Mix(Ui.Border, Ui.Accent, 0.45f) : Ui.Border))
            {
                foreach (var t in pg.Tiles)
                {
                    if (t.X + t.W > pg.Cols || t.Y + t.H > pg.Rows) continue;
                    var rr = geo.Rect(t.X, t.Y, t.W, t.H);
                    rr.Offset(tr.X, tr.Y);
                    using var tp = Fx.Round(rr, 1.5f);
                    g.FillPath(cb, tp);
                    for (var x = t.X; x < t.X + t.W; x++) for (var y = t.Y; y < t.Y + t.H; y++) taken[x, y] = true;
                }
            }
            using (var ep = new Pen(Color.FromArgb(70, Ui.Border), 1))
                for (var x = 0; x < pg.Cols; x++) for (var y = 0; y < pg.Rows; y++)
                {
                    if (taken[x, y]) continue;
                    var rr = geo.Rect(x, y, 1, 1); rr.Offset(tr.X, tr.Y);
                    g.DrawRectangle(ep, rr.X, rr.Y, rr.Width, rr.Height);
                }

            var ly = r.Y + ThumbH + 4;
            var icon = pg.Folder ? Fx.I.Folder : _isStart?.Invoke(pg) == true ? Fx.I.Star : null;
            var nx = r.X + 8;
            if (icon is not null)
            {
                Fx.Glyph(g, icon, 11, pg.Folder ? Ui.Muted : ColorTranslator.FromHtml("#FFC53D"), new RectangleF(nx - 2, ly, 16, LabelH));
                nx += 15;
            }
            var size = $"{pg.Cols}×{pg.Rows}";
            var sw = TextRenderer.MeasureText(size, Font).Width;
            TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(pg.Name) ? pg.Id : pg.Name, Font,
                new Rectangle(nx, ly, r.Right - nx - sw - 8, LabelH), on ? Ui.Ink : Ui.Ink2,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, size, Font, new Rectangle(r.Right - sw - 8, ly, sw + 4, LabelH), Ui.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }
}
