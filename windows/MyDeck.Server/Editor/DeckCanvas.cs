using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class DeckCanvas : Control
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Profile? Profile { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Page? Page { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size Device { get; set; } = new(1024, 600);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DeckServer? Server { get; set; }

    Tile? _selected;
    bool _wallOn;
    int _cardAlpha = 255;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Tile? Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); SelectionChanged?.Invoke(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? PreviewOn { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PlayMode { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Tile, bool>? IsOn { get; set; }

    public event Action<Tile, string>? Pressed;

    public event Action? SelectionChanged;

    public event Action<string>? BeforeChange;
    public event Action? Changed;
    public event Action<Point>? EmptyCellDoubleClicked;
    public event Action<Point, Point>? ContextMenuAt;

    enum DragMode { None, Move, Resize }
    DragMode _drag;
    Point _dragStartCell;
    Rectangle _ghost;
    bool _ghostValid;
    Point _mouseCell;

    public DeckCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Ui.Panel;
    }

    float Gap => Profile?.Theme.Gap ?? 12;

    (float scale, float ox, float oy) View()
    {
        var margin = PlayMode ? 0f : 28f;
        var s = Math.Min((Width - 2 * margin) / Device.Width, (Height - 2 * margin) / Device.Height);
        s = Math.Max(0.1f, s);
        return (s, (Width - Device.Width * s) / 2, (Height - Device.Height * s) / 2);
    }

    GridGeo Geo(Page p) => Grid.Layout(Device.Width, Device.Height, p, Profile?.Theme ?? new Theme());

    RectangleF CellRect(Page p, int x, int y, int w, int h) => Geo(p).Rect(x, y, w, h);

    PointF ToDevice(Point screen)
    {
        var (s, ox, oy) = View();
        return new PointF((screen.X - ox) / s, (screen.Y - oy) / s);
    }

    Point CellAt(Page p, PointF d)
    {
        var g = Geo(p);
        var cx = (int)Math.Floor((d.X - g.Ox + g.Gx / 2) / (g.Cw + g.Gx));
        var cy = (int)Math.Floor((d.Y - g.Oy + g.Gy / 2) / (g.Ch + g.Gy));
        return new Point(Math.Clamp(cx, 0, p.Cols - 1), Math.Clamp(cy, 0, p.Rows - 1));
    }

    Tile? TileAt(Page p, PointF d) =>
        p.Tiles.LastOrDefault(t => CellRect(p, t.X, t.Y, t.W, t.H).Contains(d));

    RectangleF HandleRect(Page p, Tile t)
    {
        var r = CellRect(p, t.X, t.Y, t.W, t.H);
        var (s, _, _) = View();
        var size = 16 / s;
        return new RectangleF(r.Right - size, r.Bottom - size, size, size);
    }

    public bool Fits(Page p, Rectangle r, Tile? ignore)
    {
        if (r.X < 0 || r.Y < 0 || r.Right > p.Cols || r.Bottom > p.Rows || r.Width < 1 || r.Height < 1) return false;
        foreach (var t in p.Tiles)
        {
            if (ReferenceEquals(t, ignore)) continue;
            if (r.IntersectsWith(new Rectangle(t.X, t.Y, t.W, t.H))) return false;
        }
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Profile is null || Page is null) return;
        var th = Profile.Theme;
        var (s, ox, oy) = View();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(s, s);

        var bg = Ui.ParseColor(th.Background) ?? Deck.Bg;
        if (!PlayMode)
            using (var frame = Rounded(new RectangleF(-14, -14, Device.Width + 28, Device.Height + 28), 26))
            using (var fb = new SolidBrush(ColorTranslator.FromHtml("#1A1F2B")))
                g.FillPath(fb, frame);
        using (var screen = new SolidBrush(bg)) g.FillRectangle(screen, 0, 0, Device.Width, Device.Height);

        var wallPath = Page.Wallpaper ?? th.Wallpaper;
        _wallOn = !string.IsNullOrWhiteSpace(wallPath);
        if (_wallOn)
        {
            if (Server?.Assets.Poster(wallPath, Device.Width, Device.Height) is { } poster)
                g.DrawImage(poster, 0, 0, Device.Width, Device.Height);
            using var dim = new SolidBrush(Color.FromArgb((int)(Math.Clamp(th.WallpaperDim, 0, 80) / 100f * 255), 4, 5, 9));
            g.FillRectangle(dim, 0, 0, Device.Width, Device.Height);
        }
        _cardAlpha = _wallOn ? Math.Clamp((int)((1 - Math.Clamp(th.CardGlass, 0, 80) / 100f) * 255), 40, 255) : 255;
        LivePreview.CardAlpha = MediaPreview.CardAlpha = WidgetPreview.CardAlpha = _cardAlpha;

        if (!PlayMode)
        using (var dash = new Pen(Ui.Mix(bg, Deck.Muted, 0.22f), 1 / s) { DashStyle = DashStyle.Dash })
            for (var x = 0; x < Page.Cols; x++)
                for (var y = 0; y < Page.Rows; y++)
                {
                    if (Page.Tiles.Any(t => x >= t.X && x < t.X + t.W && y >= t.Y && y < t.Y + t.H)) continue;
                    using var path = Rounded(CellRect(Page, x, y, 1, 1), th.Radius);
                    g.DrawPath(dash, path);
                }

        foreach (var t in Page.Tiles) DrawTile(g, t, th, s);

        if (PlayMode)
        {
            if (_pressTile is not null && Page.Tiles.Contains(_pressTile))
            {
                var pr = CellRect(Page, _pressTile.X, _pressTile.Y, _pressTile.W, _pressTile.H);
                using var path = Rounded(pr, th.Radius);
                var c = Ui.ParseColor(_pressTile.Color) ?? Ui.ParseColor(th.Press) ?? Deck.Accent;
                using var fill = new SolidBrush(Color.FromArgb(70, c));
                using var pen = new Pen(c, 2 / s);
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            return;
        }

        if (_drag != DragMode.None && _selected is not null)
        {
            var r = CellRect(Page, _ghost.X, _ghost.Y, _ghost.Width, _ghost.Height);
            using var path = Rounded(r, th.Radius);
            var c = _ghostValid ? Ui.Cyan : Ui.Accent;
            using var fill = new SolidBrush(Color.FromArgb(40, c));
            using var pen = new Pen(c, 2 / s) { DashStyle = DashStyle.Dash };
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        if (_selected is not null && Page.Tiles.Contains(_selected))
        {
            var r = CellRect(Page, _selected.X, _selected.Y, _selected.W, _selected.H);
            r.Inflate(3, 3);
            using var path = Rounded(r, th.Radius + 3);
            using var pen = new Pen(Ui.Cyan, 2.5f / s);
            g.DrawPath(pen, path);
            var h = HandleRect(Page, _selected);
            using var hb = new SolidBrush(Ui.Cyan);
            g.FillEllipse(hb, h.X + h.Width * 0.15f, h.Y + h.Height * 0.15f, h.Width * 0.7f, h.Height * 0.7f);
        }
    }

    void DrawTile(Graphics g, Tile t, Theme th, float s)
    {
        var r = CellRect(Page!, t.X, t.Y, t.W, t.H);
        if (t.IsLive) { LivePreview.Draw(g, t, r, th, Server); return; }
        if (t.Kind == "media") { MediaPreview.Draw(g, t, r, th, Server); return; }
        if (t.Kind is "clock" or "timer" or "weather" or "plugin") { WidgetPreview.Draw(g, t, r, th, Server); return; }
        var isOn = t.Kind == "toggle" && (PreviewOn == t.Id || (PlayMode && IsOn?.Invoke(t) == true));
        var on = isOn ? t.On ?? new TileVariant() : null;
        var label = on?.Label ?? t.Label;
        var subText = on?.Sub ?? t.Sub;
        if (PlayMode && Server is not null) { label = Server.Vars.Expand(label); subText = Server.Vars.Expand(subText); }
        var iconRef = on?.Icon ?? t.Icon;
        var colorHex = on?.Color ?? t.Color;
        var accent = Ui.ParseColor(colorHex);
        var tile = Ui.ParseColor(th.Tile) ?? Deck.Card;
        var fill = Ui.ParseColor(t.Background) ?? (accent is { } a ? Ui.Mix(tile, a, 0.14f) : tile);
        var border = accent is { } b ? Ui.Mix(Ui.ParseColor(th.TileBorder) ?? Deck.Border, b, 0.75f) : Ui.ParseColor(th.TileBorder) ?? Deck.Border;
        var gif = t.Gif?.Path is { } gifPath && t.Kind is null or "button" or "toggle" ? gifPath : null;
        var gifBg = gif is not null && t.Gif!.Fill != "icon";
        using (var path = Rounded(r, th.Radius))
        using (var fb = new SolidBrush(Color.FromArgb(_cardAlpha, fill)))
        using (var pen = new Pen(Color.FromArgb(_cardAlpha < 255 ? Math.Min(255, 90 + (int)(_cardAlpha * 0.6f)) : 255, border), 1))
        {
            g.FillPath(fb, path);
            if (gifBg && GifFrame(gif!, (int)r.Width, (int)r.Height, true) is { } frame)
            {
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                g.DrawImage(frame, r);
                g.Restore(st);
            }
            g.DrawPath(pen, path);
        }

        var hasText = !string.IsNullOrWhiteSpace(label);
        var iconPx = (int)Math.Round(Math.Min(r.Width * 0.42, r.Height * (hasText ? 0.36 : 0.55)));
        var textColor = Ui.ParseColor(th.Text) ?? Deck.Ink;
        var iconColor = on?.Color ?? t.IconColor ?? t.Color ?? th.Text;

        var png = IconRenderer.Render(iconRef, Math.Max(8, (int)(iconPx * s)), iconColor);
        Image? icon = png is null ? null : Image.FromStream(new MemoryStream(png));
        if (gif is not null && !gifBg && GifFrame(gif, Math.Max(8, iconPx), Math.Max(8, iconPx), false) is { } gi)
        {
            icon?.Dispose();
            icon = new Bitmap(gi);
        }

        var labelSize = Math.Clamp(Math.Min(r.Height * 0.13f, r.Width * 0.12f), 10f, 17f);
        using var labelFont = new Font("Segoe UI Semibold", labelSize, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", labelSize * 0.8f, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(textColor);
        using var subBrush = new SolidBrush(accent is { } ac ? Ui.Mix(Deck.Muted, ac, 0.45f) : Deck.Muted);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        if (t.Kind == "slider")
        {
            var vertical = r.Height > r.Width * 1.1f;
            var pad = Math.Clamp(Math.Min(r.Width, r.Height) * 0.12f, 8, 18);
            RectangleF track;
            float labelY, valueY;
            if (vertical)
            {
                var tw = Math.Min(r.Width * 0.42f, 64);
                var topH = Math.Max(labelSize * 1.7f, 46);
                track = new RectangleF(r.Left + (r.Width - tw) / 2, r.Top + pad + topH, tw, r.Height - 2 * pad - topH - labelSize * 1.7f);
                labelY = r.Bottom - pad - labelSize * 1.2f; valueY = r.Top + pad;
            }
            else
            {
                var th2 = Math.Min(r.Height * 0.34f, 44);
                track = new RectangleF(r.Left + pad, r.Bottom - pad - th2, r.Width - 2 * pad, th2);
                labelY = r.Top + pad; valueY = r.Top + pad;
            }
            var rad = Math.Min(Math.Min(track.Width, track.Height) / 2, 12);

            double? level = null; var muted = false;
            if (t.Target != "tablet" && Server is not null) (level, muted) = Server.SliderState(t);
            if (_slideTile == t && _slideValue is { } sv) { level = sv; muted = false; }
            var ratio = (float)Math.Clamp(level ?? 0, 0, 1);
            var red = Deck.Accent;
            using (var tp = Rounded(track, rad)) using (var tb = new SolidBrush(Ui.Mix(tile, bg(th), 0.55f))) g.FillPath(tb, tp);
            var fillR = vertical
                ? new RectangleF(track.Left, track.Bottom - track.Height * ratio, track.Width, track.Height * ratio)
                : new RectangleF(track.Left, track.Top, track.Width * ratio, track.Height);
            var fillColor = accent ?? Deck.Cyan;
            if (muted) fillColor = Ui.Mix(fillColor, Ui.ParseColor(th.SubText) ?? Deck.Muted, 0.7f);
            if (ratio > 0.001f) using (var fp = Rounded(fillR, Math.Min(rad, Math.Min(fillR.Width, fillR.Height) / 2))) using (var fb2 = new SolidBrush(fillColor)) g.FillPath(fb2, fp);
            _sliderTracks[t.Id] = (track, vertical);
            var nf = new StringFormat(fmt) { Alignment = vertical ? StringAlignment.Center : StringAlignment.Far };

            var isz = Math.Max(labelSize * 1.2f, 18);
            var btnR = isz * 0.95f;
            var valueText = level is { } lv ? ((int)(lv * 100)).ToString() : "—";
            var vw = g.MeasureString(valueText, labelFont).Width;
            if (vertical) valueY = r.Top + pad + Math.Max(labelSize, 22) - labelSize;
            var vr = new RectangleF(r.Left + pad + (vertical ? btnR + 4 : 0), valueY, r.Width - 2 * pad, labelSize * 1.4f);
            using (var valueBrush = new SolidBrush(level is not null && !muted ? textColor : Ui.ParseColor(th.SubText) ?? Deck.Muted))
                g.DrawString(valueText, labelFont, valueBrush, vr, nf);
            var cxB = vertical ? r.Left + r.Width / 2 + btnR + 4 - vw / 2 - 8 - btnR : r.Right - pad - vw - 8 - btnR;
            var cyB = valueY + labelSize * 0.7f;
            if (t.Target != "tablet")
            {
                using (var bb = new SolidBrush(muted ? Ui.Mix(tile, red, 0.3f) : Ui.Mix(tile, Ui.ParseColor(th.Text) ?? Deck.Ink, 0.1f))) g.FillEllipse(bb, cxB - btnR, cyB - btnR, btnR * 2, btnR * 2);
                using (var bp = new Pen(muted ? red : Ui.Mix(Ui.ParseColor(th.TileBorder) ?? Deck.Border, Ui.ParseColor(th.Text) ?? Deck.Ink, 0.15f), 1)) g.DrawEllipse(bp, cxB - btnR, cyB - btnR, btnR * 2, btnR * 2);
                _muteButtons[t.Id] = new RectangleF(cxB - btnR, cyB - btnR, btnR * 2, btnR * 2);
            }
            var gs = isz * 0.72f;
            var glyph = t.Target == "tablet" ? "glyph:E706" : t.Target == "mic" ? (muted ? "glyph:EC54" : "glyph:E720") : (muted ? "glyph:E74F" : "glyph:E767");
            var spk = IconRenderer.Render(glyph, Math.Max(8, (int)(gs * s)), t.Target == "tablet" ? th.SubText : muted ? Ui.Hex(red) : th.Text);
            if (spk is not null) using (var im = Image.FromStream(new MemoryStream(spk))) g.DrawImage(im, cxB - gs / 2, cyB - gs / 2, gs, gs);
            if (label is not null)
            {
                var lf = new StringFormat(fmt) { Alignment = vertical ? StringAlignment.Center : StringAlignment.Near };
                g.DrawString(label, labelFont, textBrush, new RectangleF(r.Left + pad, labelY, r.Width - 2 * pad - (vertical ? 0 : 40), labelSize * 1.4f), lf);
            }
            icon?.Dispose();
            return;
        }

        var horizontal = t.W >= 2 && t.H == 1 && icon is not null && hasText;
        if (horizontal)
        {
            var ix = r.Left + r.Height * 0.22f;
            g.DrawImage(icon!, ix, r.Top + (r.Height - iconPx) / 2, iconPx, iconPx);
            var tx = ix + iconPx + r.Height * 0.18f;
            var lines = subText is null ? 1 : 2;
            var lineH = labelSize * 1.25f;
            var ty = r.Top + r.Height / 2 - lines * lineH / 2;
            fmt.Alignment = StringAlignment.Near;
            g.DrawString(label, labelFont, textBrush, new RectangleF(tx, ty, r.Right - tx - 8, lineH), fmt);
            if (subText is not null) g.DrawString(subText, subFont, subBrush, new RectangleF(tx, ty + lineH, r.Right - tx - 8, lineH), fmt);
        }
        else
        {
            var labelH = hasText ? labelSize * 1.3f : 0;
            var subH = subText is not null && r.Height > 90 ? labelSize * 1.1f : 0;
            var gap = icon is not null && hasText ? r.Height * 0.08f : 0;
            var total = (icon is null ? 0 : iconPx) + gap + labelH + subH;
            var y = r.Top + (r.Height - total) / 2;
            if (icon is not null) { g.DrawImage(icon, r.Left + (r.Width - iconPx) / 2, y, iconPx, iconPx); y += iconPx + gap; }
            if (hasText) g.DrawString(label, labelFont, textBrush, new RectangleF(r.Left + 6, y, r.Width - 12, labelH), fmt);
            if (subH > 0) g.DrawString(subText, subFont, subBrush, new RectangleF(r.Left + 6, y + labelH, r.Width - 12, subH), fmt);
        }
        icon?.Dispose();

        if (t.Action?.Type is "page" or "folder")
        {
            using var mark = new SolidBrush(Ui.Mix(fill, Deck.Cyan, 0.8f));
            g.FillEllipse(mark, r.Right - 14, r.Top + 8, 6, 6);
        }
    }

    Bitmap? GifFrame(string path, int w, int h, bool cover)
    {
        if (Server?.Assets.PreviewGif(path, w, h, cover) is not { } g || g.Frames.Count == 0) return null;
        var total = g.Delays.Sum();
        if (total <= 0) return g.Frames[0];
        var e = (int)(Environment.TickCount64 % total);
        for (var i = 0; i < g.Frames.Count; i++) { if (e < g.Delays[i]) return g.Frames[i]; e -= g.Delays[i]; }
        return g.Frames[0];
    }

    static Color bg(Theme th) => Ui.ParseColor(th.Background) ?? Deck.Bg;

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (PlayMode) { PlayDown(e); return; }
        Focus();
        if (Page is null) return;
        var d = ToDevice(e.Location);
        _mouseCell = CellAt(Page, d);

        if (e.Button == MouseButtons.Right)
        {
            var hit = TileAt(Page, d);
            if (hit is not null) Selected = hit;
            ContextMenuAt?.Invoke(e.Location, _mouseCell);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (_selected is not null && Page.Tiles.Contains(_selected) && HandleRect(Page, _selected).Contains(d))
        {
            _drag = DragMode.Resize;
            _ghost = new Rectangle(_selected.X, _selected.Y, _selected.W, _selected.H);
            _ghostValid = true;
            return;
        }
        var t = TileAt(Page, d);
        Selected = t;
        if (t is not null)
        {
            _drag = DragMode.Move;
            _dragStartCell = _mouseCell;
            _ghost = new Rectangle(t.X, t.Y, t.W, t.H);
            _ghostValid = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (PlayMode) { PlayMove(e); return; }
        if (Page is null) return;
        var d = ToDevice(e.Location);
        if (_drag == DragMode.None)
        {
            var overHandle = _selected is not null && Page.Tiles.Contains(_selected) && HandleRect(Page, _selected).Contains(d);
            Cursor = overHandle ? Cursors.SizeNWSE : TileAt(Page, d) is not null ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        var cell = CellAt(Page, d);
        var t = _selected!;
        _ghost = _drag == DragMode.Move
            ? new Rectangle(t.X + cell.X - _dragStartCell.X, t.Y + cell.Y - _dragStartCell.Y, t.W, t.H)
            : new Rectangle(t.X, t.Y, Math.Max(1, cell.X - t.X + 1), Math.Max(1, cell.Y - t.Y + 1));
        _ghostValid = Fits(Page, _ghost, t);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (PlayMode) { PlayUp(e); return; }
        if (_drag == DragMode.None || Page is null || _selected is null) { _drag = DragMode.None; return; }
        var t = _selected;
        var changed = _ghost != new Rectangle(t.X, t.Y, t.W, t.H);
        if (_ghostValid && changed)
        {
            BeforeChange?.Invoke(_drag == DragMode.Move ? T("Move") : T("Resize"));
            t.X = _ghost.X; t.Y = _ghost.Y; t.W = _ghost.Width; t.H = _ghost.Height;
            _drag = DragMode.None;
            Changed?.Invoke();
        }
        _drag = DragMode.None;
        Invalidate();
        SelectionChanged?.Invoke();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (PlayMode) return;
        if (Page is null || e.Button != MouseButtons.Left) return;
        var d = ToDevice(e.Location);
        if (TileAt(Page, d) is null) EmptyCellDoubleClicked?.Invoke(CellAt(Page, d));
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    Tile? _pressTile, _tapTile;
    bool _held;
    System.Windows.Forms.Timer? _holdTimer, _tapTimer;

    static bool Pressable(Tile t) => !t.IsLive && t.Kind is null or "button" or "toggle";

    public event Action<Tile, double>? Slid;
    public event Action<Tile, string, double>? MediaCommand;
    public event Action<Tile>? TimerFinished;

    Tile? _mediaTile, _timerTile;
    MediaPreview.Hit? _mediaHit;

    MediaPreview.Hit? MediaHitAt(Tile t, PointF d) =>
        MediaPreview.Hits.TryGetValue(t.Id, out var hits) ? hits.LastOrDefault(h => h.Area.Contains(d)) : null;

    public bool TickTimers() => Page is not null && WidgetPreview.TickTimers(Page.Tiles.Where(t => t.Kind == "timer"), t => TimerFinished?.Invoke(t));

    public event Action<Tile>? SlideMuteClicked;

    readonly Dictionary<string, (RectangleF Track, bool Vertical)> _sliderTracks = new();
    readonly Dictionary<string, RectangleF> _muteButtons = new();
    Tile? _slideTile;
    double? _slideValue;
    long _slideSentAt;

    double SlideValueAt(Tile t, PointF d)
    {
        if (!_sliderTracks.TryGetValue(t.Id, out var tr)) return 0;
        var v = tr.Vertical ? (tr.Track.Bottom - d.Y) / Math.Max(1, tr.Track.Height) : (d.X - tr.Track.Left) / Math.Max(1, tr.Track.Width);
        return Math.Round(Math.Clamp(v, 0, 1), 2);
    }

    void SlideTo(Tile t, PointF d, bool final)
    {
        _slideValue = SlideValueAt(t, d);
        Invalidate();
        var now = Environment.TickCount64;
        if (final || now - _slideSentAt > 40) { _slideSentAt = now; Slid?.Invoke(t, _slideValue.Value); }
    }

    void PlayDown(MouseEventArgs e)
    {
        if (Page is null) return;
        var d0 = ToDevice(e.Location);
        var t = TileAt(Page, d0);
        if (t is { Kind: "slider" } && t.Target != "tablet" && e.Button == MouseButtons.Left)
        {
            if (_muteButtons.TryGetValue(t.Id, out var mb) && RectangleF.Inflate(mb, 4, 4).Contains(d0)) { SlideMuteClicked?.Invoke(t); Invalidate(); return; }
            _slideTile = t;
            Capture = true;
            SlideTo(t, d0, final: false);
            return;
        }
        if (t is { Kind: "media" } && e.Button == MouseButtons.Left)
        {
            if (MediaHitAt(t, d0) is { } hit) { _mediaTile = t; _mediaHit = hit; Capture = true; }
            return;
        }
        if (t is { Kind: "timer" } && e.Button == MouseButtons.Left) { _timerTile = t; Capture = true; return; }
        if (t is null || !Pressable(t)) return;
        if (e.Button == MouseButtons.Right)
        {
            if (t.Hold is not null) Pressed?.Invoke(t, "hold");
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _pressTile = t;
        _held = false;
        Capture = true;
        if (t.Hold is not null)
        {
            _holdTimer ??= new System.Windows.Forms.Timer { Interval = 450 };
            _holdTimer.Tick -= OnHoldTick;
            _holdTimer.Tick += OnHoldTick;
            _holdTimer.Stop(); _holdTimer.Start();
        }
        Invalidate();
    }

    void OnHoldTick(object? sender, EventArgs e)
    {
        _holdTimer!.Stop();
        if (_pressTile is not { } t) return;
        _held = true;
        _pressTile = null;
        Invalidate();
        Pressed?.Invoke(t, "hold");
    }

    void PlayMove(MouseEventArgs e)
    {
        if (Page is null) return;
        if (_slideTile is { } st) { SlideTo(st, ToDevice(e.Location), final: false); return; }
        var t = TileAt(Page, ToDevice(e.Location));
        var d = ToDevice(e.Location);
        var hand = t is not null && (Pressable(t) || t.Kind is "timer" || t.Kind == "slider" && t.Target != "tablet" || t.Kind == "media" && MediaHitAt(t, d) is not null);
        Cursor = hand ? Cursors.Hand : Cursors.Default;
        if (_pressTile is not null && t != _pressTile) { _holdTimer?.Stop(); _pressTile = null; Invalidate(); }
    }

    void PlayUp(MouseEventArgs e)
    {
        if (_slideTile is { } st)
        {
            SlideTo(st, ToDevice(e.Location), final: true);
            Capture = false;

            var tile = st;
            _ = Task.Delay(600).ContinueWith(_ => { if (_slideTile == tile) { _slideTile = null; _slideValue = null; } try { BeginInvoke(Invalidate); } catch { } });
            return;
        }
        if (_mediaTile is { } mt && _mediaHit is { } mh)
        {
            _mediaTile = null; _mediaHit = null; Capture = false;
            var up = ToDevice(e.Location);
            if (!RectangleF.Inflate(mh.Area, 6, 6).Contains(up)) return;
            var v = 0.0;
            if (mh.Cmd == "seek" && mh.X1 > mh.X0)
            {
                var dur = Server?.Media.Get(mt.Media?.App?.Trim() ?? "")?.Dur ?? 0;
                v = Math.Clamp((up.X - mh.X0) / (mh.X1 - mh.X0), 0, 1) * dur;
            }
            MediaCommand?.Invoke(mt, mh.Cmd, v);
            return;
        }
        if (_timerTile is { } tt)
        {
            _timerTile = null; Capture = false;
            if (Page is null || TileAt(Page, ToDevice(e.Location)) != tt) return;
            WidgetPreview.TimerAction(tt, WidgetPreview.TimerHit(tt, CellRect(Page, tt.X, tt.Y, tt.W, tt.H), ToDevice(e.Location)));
            Invalidate();
            return;
        }
        _holdTimer?.Stop();
        Capture = false;
        var t = _pressTile;
        _pressTile = null;
        Invalidate();
        if (t is null || _held || Page is null || TileAt(Page, ToDevice(e.Location)) != t) return;
        if (t.Double is null) { Pressed?.Invoke(t, "tap"); return; }
        if (_tapTile == t)
        {
            _tapTimer?.Stop();
            _tapTile = null;
            Pressed?.Invoke(t, "double");
            return;
        }
        if (_tapTile is { } other) { _tapTimer?.Stop(); _tapTile = null; Pressed?.Invoke(other, "tap"); }
        _tapTile = t;
        _tapTimer ??= new System.Windows.Forms.Timer { Interval = 280 };
        _tapTimer.Tick -= OnTapTick;
        _tapTimer.Tick += OnTapTick;
        _tapTimer.Stop(); _tapTimer.Start();
    }

    void OnTapTick(object? sender, EventArgs e)
    {
        _tapTimer!.Stop();
        if (_tapTile is not { } t) return;
        _tapTile = null;
        Pressed?.Invoke(t, "tap");
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (PlayMode && _pressTile is not null && !Capture) { _holdTimer?.Stop(); _pressTile = null; Invalidate(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _holdTimer?.Dispose(); _tapTimer?.Dispose(); }
        base.Dispose(disposing);
    }
}
