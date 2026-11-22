using System.Drawing.Drawing2D;
using System.Globalization;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

static class LivePreview
{
    public static int CardAlpha = 255;
    static readonly CultureInfo Tr = new("tr-TR");

    public static void Draw(Graphics g, Tile t, RectangleF r, Theme th, DeckServer? srv)
    {
        var sen = srv?.Sensors;
        var l = t.Live ?? new LiveSpec();
        var ink = Ui.ParseColor(th.Text) ?? Deck.Ink;
        var sub = Ui.ParseColor(th.SubText) ?? Deck.Muted;
        var bg = Ui.ParseColor(th.Background) ?? Deck.Bg;
        var tile = Ui.ParseColor(t.Background) ?? Ui.ParseColor(th.Tile) ?? Deck.Card;
        var border = Ui.ParseColor(th.TileBorder) ?? Deck.Border;

        using (var path = Rounded(r, th.Radius))
        using (var fb = new SolidBrush(Color.FromArgb(CardAlpha, tile)))
        using (var pen = new Pen(border, 1))
        {
            g.FillPath(fb, path);
            var clip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            var ctx = new Ctx(g, r, th, sen, srv, l, ink, sub, bg, Color.FromArgb(26, sub), Color.FromArgb(23, sub));
            try
            {
                switch (t.Kind)
                {
                    case "graph": Graph(ctx, t); break;
                    case "ring": Ring(ctx, t); break;
                    case "gauge": Gauge(ctx, t); break;
                    case "net": Net(ctx, t); break;
                    case "procs": Procs(ctx, t); break;
                }
            }
            finally { g.Clip = clip; }
            g.DrawPath(pen, path);
        }
    }

    sealed record Ctx(Graphics G, RectangleF R, Theme Th, Sensors? Sen, DeckServer? Srv, LiveSpec L,
        Color Ink, Color Sub, Color Bg, Color Track, Color Grid);

    sealed record Ser(string Key, MetricInfo? Info, Color Color, bool Dash);

    static Ser MakeSer(Ctx c, string key, string? colorHex, bool dash) =>
        new(key, c.Sen?.Info(key), Ui.ParseColor(colorHex ?? c.Srv?.DefaultColor(key)) ?? Deck.Accent, dash);

    static double Cur(Ctx c, string key) => c.Sen?.Latest.GetValueOrDefault(key) ?? 0;

    static void Graph(Ctx c, Tile t)
    {
        var key = c.L.Metric ?? "cpu";
        var s0 = MakeSer(c, key, t.Color, false);
        var list = new List<Ser> { s0 };
        if (!string.IsNullOrWhiteSpace(c.L.Metric2))
        {
            var c2 = c.L.Color2 ?? c.Srv?.DefaultColor(c.L.Metric2);
            if (c.L.Color2 is null && c2 == (t.Color ?? c.Srv?.DefaultColor(key))) c2 = "#FFB000";
            list.Add(MakeSer(c, c.L.Metric2, c2, true));
        }
        var r = c.R; const float p = 14;
        var small = r.Width < 200; var shrt = r.Height < 200;
        var title = t.Label ?? s0.Info?.Short ?? key;
        var (value, unit) = ValueText(c, s0);
        if (list.Count == 1)
        {
            if (r.Height < 130) GraphStrip(c, list, title, value, unit);
            else if (r.Width < 200) GraphTall(c, list, title, value, unit);
            else GraphFull(c, list, title, value, unit);
            return;
        }
        if (small && shrt)
        {
            Label(c, title, r.Left + p, r.Top + 24);
            Big(c, value, unit, r.Left + p, r.Top + 66, 30, c.Ink);
            Series(c, new RectangleF(r.Left, r.Bottom - 50, r.Width, 50), list, true, false);
        }
        else if (shrt)
        {
            var lw = Math.Min(150, r.Width * 0.42f);
            Label(c, title, r.Left + p, r.Top + 24);
            Big(c, value, unit, r.Left + p, r.Top + 68, 34, c.Ink);
            var i = 0;
            foreach (var k in (c.L.Info ?? new()).Take(Math.Clamp((int)((r.Height - 104) / 19) + 1, 0, 4))) InfoRow(c, k, r.Left + p, r.Left + lw - 8, r.Top + 94 + 19 * i++);
            Series(c, RectangleF.FromLTRB(r.Left + lw, r.Top + 14, r.Right - p, r.Bottom - 14), list, false, true);
        }
        else if (small)
        {
            Label(c, title, r.Left + p, r.Top + 24);
            Big(c, value, unit, r.Left + p, r.Top + 68, 34, c.Ink);
            var i = 0;
            foreach (var k in (c.L.Info ?? new()).Take(Math.Clamp((int)((r.Height * 0.55f - 106) / 19) + 1, 3, 4))) InfoRow(c, k, r.Left + p, r.Right - p, r.Top + 96 + 19 * i++);
            Series(c, RectangleF.FromLTRB(r.Left, r.Top + r.Height * 0.55f, r.Right, r.Bottom), list, true, false);
        }
        else
        {
            Label(c, title, r.Left + p + 1, r.Top + 26);
            if (c.Srv?.DetailFor(t) is { } det) Text(c, det, r.Right - p, r.Top + 26, 12, c.Sub, right: true);
            var vw = Big(c, value, unit, r.Left + p, r.Top + 76, 46, c.Ink);
            var x = r.Left + p + Math.Max(vw, 96) + 24;
            var colW = r.Width > 420 ? 86f : 74f;
            foreach (var k in c.L.Info ?? new())
            {
                var vt = InfoText(c, k);
                using var vf = new Font("Segoe UI Semibold", 15, GraphicsUnit.Pixel);
                var cw = Math.Max(colW, c.G.MeasureString(vt, vf, PointF.Empty, StringFormat.GenericTypographic).Width + 16);
                if (x + cw - 16 > r.Right - p) break;
                var m = c.Sen?.Info(k);
                Text(c, m?.Short ?? k, x, r.Top + 56, 11, c.Sub);
                Text(c, vt, x, r.Top + 76, 15, c.Ink, semibold: true);
                x += cw;
            }
            var lg = list.Count > 1 ? 20 : 0;
            Series(c, RectangleF.FromLTRB(r.Left + p, r.Top + 94, r.Right - p, r.Bottom - p - lg), list, false, true);
            if (lg > 0)
            {
                var lx = r.Left + p + 4;
                foreach (var s in list)
                {
                    using var pen = new Pen(s.Color, 2) { DashStyle = s.Dash ? DashStyle.Dash : DashStyle.Solid };
                    c.G.DrawLine(pen, lx, r.Bottom - p - 4, lx + 14, r.Bottom - p - 4);
                    var name = s.Info?.Short ?? s.Key;
                    var w = Text(c, name, lx + 19, r.Bottom - p, 11, c.Sub);
                    lx += 24 + w + 12;
                }
            }
        }
    }

    static void GraphFull(Ctx c, List<Ser> list, string title, string value, string unit)
    {
        var r = c.R; float w = r.Width, h = r.Height;
        var k = Math.Clamp(h / 183f, 0.62f, 1.5f);
        var p = 14 * k;
        var header = Math.Min(h * .25f, 40 * k);
        var valueSize = Math.Min(header * .80f, 32 * k);
        Label(c, title, r.Left + p, r.Top + header, Math.Max(9, 11 * k));
        var valueWidth = Measure(c, value, valueSize, true) + 22 * k;
        Big(c, value, unit, r.Right - p - valueWidth, r.Top + header, valueSize, c.Ink);
        var items = (c.L.Info ?? new()).Take(4).ToArray();
        var avail = w - 2 * p;
        var perRow = Math.Max(1, items.Length);
        var rows = items.Length == 0 ? 0 : 1;
        if (items.Length > 0 && avail / perRow < 74 * Math.Min(k, 1)) { perRow = (items.Length + 1) / 2; rows = 2; }
        var rowH = 38 * k;
        if (rows > 0 && header + rows * rowH + 40 * k > h) rows = Math.Clamp((int)((h - header - 40 * k) / rowH), 0, rows);
        var column = avail / perRow;
        for (var i = 0; i < Math.Min(items.Length, rows * perRow); i++)
        {
            var metric = items[i];
            var x = r.Left + p + column * (i % perRow);
            var y0 = r.Top + header + rowH * (i / perRow);
            Text(c, c.Sen?.Info(metric)?.Short ?? metric, x, y0 + 14 * k, 10 * k, c.Sub, maxW: column - 5 * k);
            Text(c, InfoText(c, metric), x, y0 + 32 * k, Math.Min(16 * k, column * .24f), c.Ink, semibold: true, maxW: column - 5 * k);
        }
        var graphTop = r.Top + header + (rows > 0 ? rows * rowH : 8 * k);
        if (r.Bottom > graphTop)
            Series(c, RectangleF.FromLTRB(r.Left, graphTop, r.Right, r.Bottom), list, true, true, k);
    }

    static void GraphTall(Ctx c, List<Ser> list, string title, string value, string unit)
    {
        var r = c.R; float w = r.Width, h = r.Height;
        var k = Math.Clamp(Math.Min(w / 131f, h / 183f), 0.62f, 1.5f);
        var p = 14 * k;
        Label(c, title, r.Left + p, r.Top + p + 11 * k, Math.Max(9, 11 * k));
        Big(c, value, unit, r.Left + p, r.Top + p + 47 * k, 32 * k, c.Ink);
        var gh = h * .38f;
        var start = r.Top + p + 67 * k;
        var rowH = 17 * k;
        var info = c.L.Info ?? new();
        var rows = Math.Clamp((int)Math.Floor((r.Bottom - gh - start) / rowH) + 1, 0, info.Count);
        for (var i = 0; i < rows; i++)
        {
            var y = start + i * rowH;
            var vw = Text(c, InfoText(c, info[i]), r.Right - p, y, 12 * k, c.Ink, right: true, semibold: true);
            Text(c, c.Sen?.Info(info[i])?.Short ?? info[i], r.Left + p, y, 10.5f * k, c.Sub, maxW: w - 2 * p - vw - 6 * k);
        }
        Series(c, RectangleF.FromLTRB(r.Left, r.Bottom - gh, r.Right, r.Bottom), list, true, false, k);
    }

    static void GraphStrip(Ctx c, List<Ser> list, string title, string value, string unit)
    {
        var r = c.R;
        var k = Math.Clamp(r.Height / 120f, 0.62f, 1.5f);
        var p = 14 * k;
        var header = 40 * k;
        var valueSize = 32 * k;
        var labelSize = Math.Max(9, 11 * k);
        var lw = Label(c, title, r.Left + p, r.Top + header, labelSize);
        var vx = r.Right - p - (Measure(c, value, valueSize, true) + 22 * k);
        Big(c, value, unit, vx, r.Top + header, valueSize, c.Ink);
        var ix = r.Left + p + lw + 16 * k;
        var iw = vx - 16 * k - ix;
        var info = c.L.Info ?? new();
        if (iw > 120 * k && info.Count > 0)
            Text(c, string.Join("  ·  ", info.Take(3).Select(m => InfoText(c, m))), ix, r.Top + header, 11.5f * k, c.Sub, maxW: iw);
        var top = r.Top + header + 8 * k;
        if (r.Bottom > top) Series(c, RectangleF.FromLTRB(r.Left, top, r.Right, r.Bottom), list, true, true, k);
    }

    static float Measure(Ctx c, string s, float size, bool semibold)
    {
        using var font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, GraphicsUnit.Pixel);
        return c.G.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width;
    }

    static void Ring(Ctx c, Tile t)
    {
        var s = MakeSer(c, c.L.Metric ?? "ram.used", t.Color, false);
        var r = c.R; const float p = 14;
        var max = s.Info?.Max > 0 ? s.Info.Max : 100;
        var v = Cur(c, s.Key);
        var f = (float)Math.Clamp((v - (s.Info?.GaugeMin ?? 0)) / (max - (s.Info?.GaugeMin ?? 0)), 0, 1);
        Label(c, t.Label ?? s.Info?.Short ?? s.Key, r.Left + p, r.Top + 24);
        if (s.Info?.Unit != "%") Text(c, $"{(int)(f * 100)}%", r.Right - p, r.Top + 24, 12, c.Sub, right: true);
        var rad = Math.Min(r.Width, r.Height) * 0.3f;
        var cx = r.Left + r.Width / 2; var cy = r.Top + r.Height / 2 + 12;
        var oval = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        var sw = Math.Max(6, rad * 0.2f);
        using (var pen = new Pen(c.Track, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) c.G.DrawArc(pen, oval, 135, 270);
        if (f > 0.005f)
            using (var pen = new Pen(ColorFor(c, s, f), sw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) c.G.DrawArc(pen, oval, 135, 270 * f);
        var vs = Math.Clamp(rad * 0.5f, 16, 48);
        if (s.Info?.Unit == "%") TextC(c, $"{Fmt(v, 0)}%", cx, cy + vs * 0.35f, vs, c.Ink, true);
        else
        {
            TextC(c, Fmt(v, s.Info?.Dec ?? 0), cx, cy + vs * 0.2f, vs, c.Ink, true);
            TextC(c, $"/ {Fmt(max, 0)} {s.Info?.Unit}", cx, cy + vs * 0.95f, vs * 0.5f, c.Sub, false);
        }
    }

    static void Gauge(Ctx c, Tile t)
    {
        var s = MakeSer(c, c.L.Metric ?? "gpu.temp", t.Color, false);
        var r = c.R; const float p = 14;
        var lo = s.Info?.GaugeMin ?? 0;
        var hi = s.Info?.Max > 0 ? s.Info.Max : 100;
        var v = Cur(c, s.Key);
        var f = (float)Math.Clamp((v - lo) / (hi - lo), 0, 1);
        Label(c, t.Label ?? s.Info?.Short ?? s.Key, r.Left + p, r.Top + 24);
        var rad = Math.Max(20, Math.Min(r.Width * 0.36f, (r.Height - 58) * 0.95f));
        var cx = r.Left + r.Width / 2; var cy = r.Bottom - Math.Max(34, rad * 0.6f);
        var oval = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
        var sw = Math.Max(6, rad * 0.14f);
        using (var pen = new Pen(c.Track, sw)) c.G.DrawArc(pen, oval, 180, 180);
        const int segs = 40;
        for (var i = 0; i < segs && (float)i / segs < f; i++)
        {
            var t0 = (float)i / segs; var t1 = Math.Min((i + 1f) / segs, f);
            var col = c.L.ColorMode == "load" ? LoadColor(t0) : Color.FromArgb((int)(255 * (0.45f + 0.55f * t0)), s.Color);
            using var pen = new Pen(col, sw);
            c.G.DrawArc(pen, oval, 180 + 180 * t0, 180 * (t1 - t0) + 0.6f);
        }
        var vs = Math.Clamp(rad * 0.46f, 16, 44);
        var unit = s.Info?.Unit == "°C" ? "°" : s.Info?.Unit ?? "";
        TextC(c, Fmt(v, s.Info?.Dec ?? 0) + unit, cx, cy - 2, vs, c.Ink, true);
        TextC(c, Fmt(lo, 0), cx - rad, cy + 16, 10, c.Sub, false);
        TextC(c, Fmt(hi, 0), cx + rad, cy + 16, 10, c.Sub, false);
    }

    static void Net(Ctx c, Tile t)
    {
        var down = MakeSer(c, "net.down", t.Color ?? "#3CC8F0", false);
        var up = MakeSer(c, "net.up", c.L.Color2 ?? "#FF3D5A", false);
        var r = c.R; const float p = 14;
        Label(c, t.Label ?? T("Network"), r.Left + p, r.Top + 24);
        RectangleF gr;
        if (r.Height < 200)
        {
            var lw = Math.Min(140, r.Width * 0.42f);
            Text(c, "↓ " + Fmt(Cur(c, "net.down"), 1), r.Left + p, r.Top + 62, 20, down.Color, semibold: true);
            Text(c, "↑ " + Fmt(Cur(c, "net.up"), 1), r.Left + p, r.Top + 92, 20, up.Color, semibold: true);
            Text(c, "Mbps" + (string.IsNullOrWhiteSpace(t.Sub) ? "" : " · " + t.Sub), r.Left + p, r.Top + 116, 11, c.Sub);
            gr = RectangleF.FromLTRB(r.Left + lw, r.Top + 14, r.Right - p, r.Bottom - 14);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(t.Sub)) Text(c, t.Sub, r.Right - p, r.Top + 24, 12, c.Sub, right: true);
            var half = r.Left + r.Width / 2 + 4;
            Text(c, T("DOWNLOAD"), r.Left + p, r.Top + 50, 11, c.Sub);
            Text(c, T("UPLOAD"), half, r.Top + 50, 11, c.Sub);
            var w1 = Text(c, "↓ " + Fmt(Cur(c, "net.down"), 1), r.Left + p, r.Top + 78, 26, down.Color, semibold: true);
            Text(c, "Mbps", r.Left + p + w1 + 5, r.Top + 78, 12, c.Sub);
            var w2 = Text(c, "↑ " + Fmt(Cur(c, "net.up"), 1), half, r.Top + 78, 26, up.Color, semibold: true);
            Text(c, "Mbps", half + w2 + 5, r.Top + 78, 12, c.Sub);
            gr = RectangleF.FromLTRB(r.Left + p, r.Top + 94, r.Right - p, r.Bottom - p);
        }
        GridLines(c, gr);
        var n = c.L.Seconds + 1;
        Mirror(c, gr, down, n, up: false);
        Mirror(c, gr, up, n, up: true);
        using var mid = new Pen(Color.FromArgb(64, c.Sub), 1);
        c.G.DrawLine(mid, gr.Left, gr.Top + gr.Height / 2, gr.Right, gr.Top + gr.Height / 2);
    }

    static void Procs(Ctx c, Tile t)
    {
        var r = c.R; const float p = 14;
        var color = Ui.ParseColor(t.Color) ?? Deck.Accent;
        Label(c, t.Label ?? T("Top CPU"), r.Left + p, r.Top + 24);
        Text(c, T("last 1 s"), r.Right - p, r.Top + 24, 12, c.Sub, right: true);
        var rows = Math.Clamp((int)((r.Height - 40) / 28), 1, 5);
        var list = c.Sen?.TopProcesses.Take(rows).ToList() ?? new();
        var scale = Math.Max(30, list.Select(x => x.Cpu).DefaultIfEmpty(0).Max() * 1.1);
        var nameW = Math.Min(110, r.Width * 0.3f);
        var i = 0;
        foreach (var (name, v) in list)
        {
            var y = r.Top + 50 + i++ * 28;
            Text(c, name, r.Left + p, y + 4, 13, c.Ink, maxW: nameW - 8);
            var bx = r.Left + p + nameW; var bw = r.Right - p - 46 - bx;
            using (var tp = Rounded(new RectangleF(bx, y - 4, bw, 8), 4)) using (var tb = new SolidBrush(c.Track)) c.G.FillPath(tb, tp);
            var fw = Math.Max(4, bw * (float)Math.Clamp(v / scale, 0, 1));
            using (var fp = Rounded(new RectangleF(bx, y - 4, fw, 8), 4))
            using (var fb = new SolidBrush(c.L.ColorMode == "load" ? LoadColor((float)v / 100) : color)) c.G.FillPath(fb, fp);
            Text(c, Fmt(v, 1) + "%", r.Right - p, y + 4, 13, c.Ink, right: true, semibold: true);
        }
        if (list.Count == 0) Text(c, T("Measuring…"), r.Left + p, r.Top + 54, 12, c.Sub);
    }

    static void GridLines(Ctx c, RectangleF gr)
    {
        using var pen = new Pen(c.Grid, 1);
        foreach (var f in new[] { 0.25f, 0.5f, 0.75f }) c.G.DrawLine(pen, gr.Left, gr.Top + gr.Height * f, gr.Right, gr.Top + gr.Height * f);
    }

    static double[] Hist(Ctx c, string key, int n) => c.Sen?.History(key, n) ?? new double[n];

    static double ScaleOf(double[] h) => Math.Max(1, h.Max() * 1.15);

    static void Series(Ctx c, RectangleF gr, List<Ser> list, bool flush, bool grid, float k = 1)
    {
        var g = c.G;
        var state = g.Save();
        g.SetClip(gr, CombineMode.Intersect);
        if (grid) GridLines(c, gr);
        var n = c.L.Seconds + 1;
        var step = gr.Width / (n - 1);
        var usable = gr.Height * (flush ? 0.82f : 0.94f);
        var order = list.Count > 1 ? list.Skip(1).Append(list[0]) : list;
        foreach (var s in order)
        {
            var h = Hist(c, s.Key, n);
            const double min = 0;
            var max = s.Info?.Max > 0 ? s.Info.Max : ScaleOf(h);
            float Y(double v) => gr.Bottom - (float)Math.Clamp((v - min) / (max - min), 0, 1) * usable;
            var col = ColorFor(c, s, (float)(h[^1] / max));
            var style = s.Dash ? "line" : c.L.Style ?? "area";
            var pts = new PointF[n];
            for (var i = 0; i < n; i++) pts[i] = new PointF(gr.Left + i * step, Y(h[i]));
            if (style == "bars")
            {
                var bw = Math.Max(1, step * 0.62f);
                for (var i = 0; i < n; i++)
                {
                    var bc = c.L.ColorMode == "load" && (s.Info?.Loadable ?? false) ? LoadColor((float)(h[i] / max)) : col;
                    using var b = new SolidBrush(Color.FromArgb((int)(255 * (0.55f + 0.4f * i / n)), bc));
                    g.FillRectangle(b, pts[i].X - bw / 2, pts[i].Y, bw, Math.Max(1.5f, gr.Bottom - pts[i].Y));
                }
                continue;
            }
            if (style == "area")
            {
                using var path = new GraphicsPath();
                path.AddLines(pts);
                path.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, gr.Bottom);
                path.AddLine(pts[^1].X, gr.Bottom, pts[0].X, gr.Bottom);
                path.CloseFigure();
                using var lg = new LinearGradientBrush(new PointF(0, gr.Bottom - usable - 1), new PointF(0, gr.Bottom + 1),
                    Color.FromArgb(107, col), Color.FromArgb(5, col));
                g.FillPath(lg, path);
            }
            using (var pen = new Pen(s.Dash ? Color.FromArgb(217, col) : col, (s.Dash ? 1.5f : 2f) * Math.Max(0.8f, k))
                   { LineJoin = LineJoin.Round, DashStyle = s.Dash ? DashStyle.Dash : DashStyle.Solid })
                g.DrawLines(pen, pts);
            if (!s.Dash)
            {
                var ex = gr.Right - (flush ? 7 : 4) * k; var ey = Y(h[^1]);
                using var halo = new SolidBrush(Color.FromArgb(64, col));
                using var dot = new SolidBrush(col);
                using var hole = new SolidBrush(c.Bg);
                g.FillEllipse(halo, ex - 7 * k, ey - 7 * k, 14 * k, 14 * k);
                g.FillEllipse(dot, ex - 3.5f * k, ey - 3.5f * k, 7 * k, 7 * k);
                g.FillEllipse(hole, ex - 1.4f * k, ey - 1.4f * k, 2.8f * k, 2.8f * k);
            }
        }
        g.Restore(state);
    }

    static void Mirror(Ctx c, RectangleF gr, Ser s, int n, bool up)
    {
        var h = Hist(c, s.Key, n);
        var max = ScaleOf(h);
        var mid = gr.Top + gr.Height / 2;
        var hgt = gr.Height / 2 * 0.9f;
        var step = gr.Width / (n - 1);
        var pts = new PointF[n];
        for (var i = 0; i < n; i++) pts[i] = new PointF(gr.Left + i * step, mid + (up ? 1 : -1) * (float)Math.Clamp(h[i] / max, 0, 1) * hgt);
        var state = c.G.Save();
        c.G.SetClip(gr, CombineMode.Intersect);
        using (var path = new GraphicsPath())
        {
            path.AddLines(pts);
            path.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, mid);
            path.AddLine(pts[^1].X, mid, pts[0].X, mid);
            path.CloseFigure();
            using var lg = new LinearGradientBrush(new PointF(0, up ? mid + hgt + 1 : mid - hgt - 1), new PointF(0, mid),
                Color.FromArgb(107, s.Color), Color.FromArgb(5, s.Color));
            c.G.FillPath(lg, path);
        }
        using (var pen = new Pen(s.Color, 2) { LineJoin = LineJoin.Round }) c.G.DrawLines(pen, pts);
        c.G.Restore(state);
    }

    static string Fmt(double v, int dec) => v.ToString(dec <= 0 ? "0" : "0." + new string('0', dec), Tr);

    static (string Value, string Unit) ValueText(Ctx c, Ser s)
    {
        var v = Cur(c, s.Key);
        if (s.Info?.Unit == "süre") return (Duration(v), "");
        return (Fmt(v, s.Info?.Dec ?? 0), s.Info?.Unit ?? "");
    }

    static string InfoText(Ctx c, string key)
    {
        var m = c.Sen?.Info(key);
        var v = Cur(c, key);
        if (m is null) return "—";
        return m.Unit switch
        {
            "süre" => Duration(v),
            "GB" when m.Max > 0 => $"{Fmt(v, m.Dec)} / {Fmt(m.Max, 0)} GB",
            "" => Fmt(v, m.Dec),
            "%" => Fmt(v, m.Dec) + "%",
            "°C" => Fmt(v, m.Dec) + " °C",
            _ => $"{Fmt(v, m.Dec)} {m.Unit}",
        };
    }

    static string Duration(double sec)
    {
        var m = (long)(sec / 60);
        long days = m / 1440, hours = m / 60 % 24, mins = m % 60;
        return days > 0 ? $"{days}g {hours}s" : hours > 0 ? $"{hours}s {mins}d" : $"{mins}d";
    }

    static void InfoRow(Ctx c, string key, float x, float right, float baseline)
    {
        Text(c, c.Sen?.Info(key)?.Short ?? key, x, baseline, 12, c.Sub);
        Text(c, InfoText(c, key), right, baseline, 12, c.Ink, right: true, semibold: true);
    }

    static float Label(Ctx c, string s, float x, float baseline, float size = 11) =>
        Text(c, s.ToUpper(Tr), x, baseline, size, c.Sub, bold: true);

    static float Big(Ctx c, string value, string unit, float x, float baseline, float size, Color color)
    {
        var w = Text(c, value, x, baseline, size, color, semibold: true);
        if (unit.Length == 0) return w;
        return w + 3 + Text(c, unit, x + w + 3, baseline, MathF.Round(size * 0.42f), c.Sub, semibold: true);
    }

    static float Text(Ctx c, string s, float x, float baseline, float size, Color color,
        bool right = false, bool bold = false, bool semibold = false, float maxW = 0)
    {
        using var font = new Font(bold ? "Segoe UI" : semibold ? "Segoe UI Semibold" : "Segoe UI", size,
            bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;
        if (maxW > 0)
            while (s.Length > 1 && c.G.MeasureString(s, font, PointF.Empty, fmt).Width > maxW) s = s[..^2] + "…";
        var w = c.G.MeasureString(s, font, PointF.Empty, fmt).Width;
        var ascent = size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);
        using var b = new SolidBrush(color);
        c.G.DrawString(s, font, b, right ? x - w : x, baseline - ascent, fmt);
        return w;
    }

    static void TextC(Ctx c, string s, float cx, float baseline, float size, Color color, bool semibold)
    {
        using var font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, GraphicsUnit.Pixel);
        var w = c.G.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width;
        Text(c, s, cx - w / 2, baseline, size, color, semibold: semibold);
    }

    static Color ColorFor(Ctx c, Ser s, float f) =>
        c.L.ColorMode == "load" && (s.Info?.Loadable ?? false) ? LoadColor(f) : s.Color;

    static Color LoadColor(float f)
    {
        f = Math.Clamp(f, 0, 1);
        Color a = Color.FromArgb(74, 222, 128), b = Color.FromArgb(255, 176, 0), r = Color.FromArgb(255, 61, 90);
        return f <= 0.55f ? Ui.Mix(a, b, f / 0.55f) : f <= 0.85f ? Ui.Mix(b, r, (f - 0.55f) / 0.30f) : r;
    }

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
}

static class MediaPreview
{
    public static int CardAlpha = 255;
    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");
    static Image? _art;
    static int _artRev = -1;
    static string _artKey = "";

    public sealed record Hit(string Cmd, RectangleF Area, float X0 = 0, float X1 = 0);
    public static readonly Dictionary<string, List<Hit>> Hits = new();
    static List<Hit> _hits = new();

    static void AddHit(string cmd, float cx, float cy, float radius) =>
        _hits.Add(new Hit(cmd, new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2)));

    public static void Draw(Graphics g, Tile t, RectangleF r, Theme th, DeckServer? srv)
    {
        _hits = new List<Hit>();
        Hits[t.Id] = _hits;
        var spec = t.Media ?? new MediaSpec();
        var key = spec.App?.Trim() ?? "";
        var st = srv?.Media.Get(key);
        var ink = Ui.ParseColor(th.Text) ?? Deck.Ink;
        var sub = Ui.ParseColor(th.SubText) ?? Deck.Muted;
        var tileC = Ui.ParseColor(th.Tile) ?? Deck.Card;
        var has = st is { Empty: false };
        var tint = has && (spec.FollowArt ?? true) ? Ui.ParseColor(st!.Tint) ?? Deck.Accent : Ui.ParseColor(t.Color) ?? Ui.ParseColor(th.Accent) ?? Deck.Accent;

        using var path = Rounded(r, th.Radius);
        using (var fb = new SolidBrush(Color.FromArgb(CardAlpha, Ui.Mix(tileC, tint, 0.13f)))) g.FillPath(fb, path);
        var clip = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        using (var glow = new GraphicsPath())
        {
            var rad = Math.Max(r.Width, r.Height) * 0.9f;
            glow.AddEllipse(r.Left + r.Width * 0.15f - rad, r.Top - rad, rad * 2, rad * 2);
            using var pg = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(56, tint), SurroundColors = new[] { Color.FromArgb(0, tint) } };
            g.FillPath(pg, glow);
        }

        if (!has)
        {
            using var f = new Font("Segoe UI", 12, GraphicsUnit.Pixel);
            using var b = new SolidBrush(sub);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(T("♪\nNothing is playing"), f, b, r, sf);
        }
        else
        {
            var art = ArtOf(key, st!);
            var small = r.Width < 200; var shrt2 = r.Height < 130;
            var ctl = spec.Controls ?? true; var times = spec.Times ?? true;

            if (t.Id == "yenisayfa-4d2509")
            {
                const float p = 12;
                var a = r.Height - 2 * p;
                var ar = new RectangleF(r.Left + p, r.Top + p, a, a);
                using (var ap = Rounded(ar, 10))
                {
                    var state = g.Save();
                    g.SetClip(ap, CombineMode.Intersect);
                    if (art is not null) g.DrawImage(art, ar);
                    else using (var placeholder = new SolidBrush(Color.FromArgb(30, sub))) g.FillRectangle(placeholder, ar);
                    g.Restore(state);
                }
                var x0 = ar.Right + 16;
                var iw = r.Right - p - x0;
                var barY = r.Bottom - (times ? 26 : 12);
                var size = Math.Min(29, r.Height * 0.18f);
                var cy = barY - size - 18;
                Text(g, st.Title, x0, r.Top + 20, 16, ink, true, iw);
                if (!ctl || cy - size >= r.Top + 42)
                    Text(g, st.Artist, x0, r.Top + 38, 12.5f, sub, false, iw);
                if (ctl) Controls(g, (ar.Right + r.Right) / 2, cy, size,
                    Math.Min(72, (r.Right - ar.Right) / 3), ink, st.Playing);
                Bar(g, x0, barY - 2, iw, 4, Frac(st), tint, st.CanSeek);
                if (times) Times(g, st, x0, iw, barY + 20, sub);
            }
            else if (small && shrt2) MCover(g, r, th, st!, art, ink, tint);
            else if (shrt2) MStrip(g, r, st!, art, ink, sub, tint, ctl);
            else if (small) MTall(g, r, th, st!, art, ink, sub, tint, ctl);
            else if (r.Width >= r.Height * 1.55f) MPlayer(g, r, st!, art, ink, sub, tint, ctl, times);
            else MSquare(g, r, st!, art, ink, sub, tint, ctl, times);
        }
        g.Clip = clip;
        using var pen = new Pen(Ui.Mix(Ui.ParseColor(th.TileBorder) ?? Deck.Border, tint, 0.35f), 1);
        g.DrawPath(pen, path);
    }

    static void DrawArt(Graphics g, Image? art, RectangleF ar, float radius, Color sub)
    {
        if (ar.Width <= 0 || ar.Height <= 0) return;
        using var ap = Rounded(ar, radius);
        var state = g.Save();
        g.SetClip(ap, CombineMode.Intersect);
        if (art is not null) g.DrawImage(art, ar);
        else using (var placeholder = new SolidBrush(Color.FromArgb(30, sub))) g.FillRectangle(placeholder, ar);
        g.Restore(state);
    }

    static void MPlayer(Graphics g, RectangleF r, MediaState st, Image? art, Color ink, Color sub, Color tint, bool ctl, bool times)
    {
        var k = Math.Clamp(r.Height / 183f, 0.7f, 1.5f);
        var p = 12 * k;
        var barY = r.Bottom - (times ? 26 : 12) * k;
        var size = Math.Min(29 * k, r.Height * 0.18f);
        var cy = barY - size - 18 * k;
        var a = Math.Max(0, Math.Min(r.Height - 2 * p, r.Width - 2 * p - 186 * k));
        var ar = new RectangleF(r.Left + p, r.Top + (r.Height - a) / 2, a, a);
        DrawArt(g, art, ar, 10 * k, sub);
        var x0 = ar.Right + 16 * k;
        var iw = r.Right - p - x0;
        Text(g, st.Title, x0, r.Top + 20 * k, 16 * k, ink, true, iw);
        if (!ctl || cy - size >= r.Top + 42 * k) Text(g, st.Artist, x0, r.Top + 38 * k, 12.5f * k, sub, false, iw);
        if (ctl) Controls(g, (ar.Right + r.Right) / 2, cy, size, Math.Min(72 * k, (r.Right - ar.Right) / 3), ink, st.Playing);
        Bar(g, x0, barY - 2 * k, iw, 4 * k, Frac(st), tint, st.CanSeek, k);
        if (times) Times(g, st, x0, iw, barY + 20 * k, sub, 11 * k);
    }

    static void MSquare(Graphics g, RectangleF r, MediaState st, Image? art, Color ink, Color sub, Color tint, bool ctl, bool times)
    {
        var k = Math.Clamp(Math.Min(r.Height / 183f, r.Width / 276f), 0.7f, 1.5f);
        var p = 12 * k;
        var barY = r.Bottom - (times ? 26 : 12) * k;
        var size = Math.Min(29 * k, r.Height * 0.16f);
        var cy = barY - size - 18 * k;
        var ctlTop = ctl ? cy - size - 12 * k : barY - 20 * k;
        var a = Math.Max(0, Math.Min(Math.Min(r.Height * 0.5f, r.Width * 0.42f), ctlTop - (r.Top + p)));
        var ar = new RectangleF(r.Left + p, r.Top + p, a, a);
        DrawArt(g, art, ar, 10 * k, sub);
        var x0 = ar.Right + 14 * k;
        var iw = r.Right - p - x0;
        var ty = r.Top + p + Math.Max(16 * k, (a - 30 * k) / 2 + 14 * k);
        Text(g, st.Title, x0, ty, 16 * k, ink, true, iw);
        Text(g, st.Artist, x0, ty + 18 * k, 12.5f * k, sub, false, iw);
        if (ctl) Controls(g, r.Left + r.Width / 2, cy, size, Math.Min(72 * k, (r.Width - 2 * p) / 4), ink, st.Playing);
        Bar(g, r.Left + p, barY - 2 * k, r.Width - 2 * p, 4 * k, Frac(st), tint, st.CanSeek, k);
        if (times) Times(g, st, r.Left + p, r.Width - 2 * p, barY + 20 * k, sub, 11 * k);
    }

    static void MTall(Graphics g, RectangleF r, Theme th, MediaState st, Image? art, Color ink, Color sub, Color tint, bool ctl)
    {
        var k = Math.Clamp(Math.Min(r.Width / 150f, r.Height / 183f), 0.7f, 1.3f);
        var p = 12 * k;
        var size = Math.Min(29 * k, r.Width * 0.13f);
        var cy = r.Bottom - p - size - 4 * k;
        var artistY = ctl ? cy - size - 12 * k : r.Bottom - p - 8 * k;
        var titleY = artistY - 16 * k;
        var a = Math.Max(0, Math.Min(r.Width - 2 * p, titleY - 18 * k - (r.Top + p)));
        var cx = r.Left + r.Width / 2;
        DrawArt(g, art, new RectangleF(cx - a / 2, r.Top + p, a, a), 10 * k, sub);
        TextCenter(g, st.Title, cx, titleY, 15 * k, ink, true, r.Width - 2 * p);
        TextCenter(g, st.Artist, cx, artistY, 12 * k, sub, false, r.Width - 2 * p);
        if (ctl) Controls(g, cx, cy, size, Math.Min(44 * k, (r.Width - 2 * p) / 2.6f), ink, st.Playing);
        EdgeBar(g, r, th.Radius, Frac(st), tint, 3 * k);
    }

    static void MStrip(Graphics g, RectangleF r, MediaState st, Image? art, Color ink, Color sub, Color tint, bool ctl)
    {
        var k = Math.Clamp(r.Height / 110f, 0.7f, 1.2f);
        var p = 10 * k;
        var a = r.Height - 2 * p;
        var ar = new RectangleF(r.Left + p, r.Top + p, a, a);
        DrawArt(g, art, ar, 8 * k, sub);
        var x0 = ar.Right + 12 * k;
        var right = r.Right - p;
        var size = Math.Min(29 * k, r.Height * 0.2f);
        var gap = 44 * k;
        var ctlW = ctl ? 2 * gap + 2 * size : 0;
        var room = right - x0;
        var inline = !ctl || room - ctlW > 150 * k;
        var iw = ctl && inline ? room - ctlW - 12 * k : room;
        Text(g, st.Title, x0, r.Top + p + 16 * k, Math.Max(13, 16 * k), ink, true, iw);
        Text(g, st.Artist, x0, r.Top + p + 33 * k, Math.Max(11, 12.5f * k), sub, false, iw);
        if (inline)
        {
            if (ctl) Controls(g, right - gap - size * 0.6f, r.Top + r.Height / 2 - 4 * k, size, gap, ink, st.Playing);
            Bar(g, x0, r.Bottom - p - 4 * k, iw, 3 * k, Frac(st), tint, false, k);
        }
        else
        {
            var s2 = size * 0.85f;
            var by = r.Bottom - p - s2;
            using (var b = new SolidBrush(ink)) g.FillEllipse(b, x0, by - s2, s2 * 2, s2 * 2);
            PlayIcon(g, x0 + s2, by, s2 * 0.85f, Color.FromArgb(11, 14, 20), st.Playing);
            Skip(g, x0 + 2 * s2 + 20 * k, by, s2 * 0.85f, ink, 1);
            AddHit("play", x0 + s2, by, s2 + 6);
            AddHit("next", x0 + 2 * s2 + 20 * k, by, Math.Max(18, s2));
            var bx = x0 + 2 * s2 + 40 * k;
            Bar(g, bx, by - 2 * k, right - bx, 3 * k, Frac(st), tint, false, k);
        }
    }

    static void MCover(Graphics g, RectangleF r, Theme th, MediaState st, Image? art, Color ink, Color tint)
    {
        var k = Math.Clamp(r.Height / 120f, 0.7f, 1.2f);
        var side = Math.Max(r.Width, r.Height);
        if (art is not null) g.DrawImage(art, r.Left + (r.Width - side) / 2, r.Top + (r.Height - side) / 2, side, side);
        using (var lg = new LinearGradientBrush(new PointF(0, r.Top + r.Height * 0.35f), new PointF(0, r.Bottom + 1), Color.FromArgb(0, 7, 9, 14), Color.FromArgb(220, 7, 9, 14)))
            g.FillRectangle(lg, r);
        var cx = r.Left + r.Width / 2; var cy = r.Top + r.Height / 2 - 8 * k;
        using (var c = new SolidBrush(Color.FromArgb(115, 7, 9, 14))) g.FillEllipse(c, cx - 24 * k, cy - 24 * k, 48 * k, 48 * k);
        PlayIcon(g, cx, cy, 22 * k, ink, st.Playing);
        _hits.Add(new Hit("play", r));
        Text(g, st.Title, r.Left + 12 * k, r.Bottom - 14 * k, Math.Max(11, 12 * k), ink, true, r.Width - 24 * k);
        EdgeBar(g, r, th.Radius, Frac(st), tint, 3 * k);
    }

    static void EdgeBar(Graphics g, RectangleF r, float radius, float f, Color tint, float h)
    {
        using var path = Rounded(r, radius);
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        using (var tb = new SolidBrush(Color.FromArgb(38, 255, 255, 255))) g.FillRectangle(tb, r.Left, r.Bottom - h, r.Width, h);
        using (var fb = new SolidBrush(Vivid(tint))) g.FillRectangle(fb, r.Left, r.Bottom - h, r.Width * f, h);
        g.Restore(state);
    }

    static void TextCenter(Graphics g, string s, float cx, float baseline, float size, Color color, bool semibold, float maxW)
    {
        using var font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;
        while (s.Length > 1 && g.MeasureString(s, font, PointF.Empty, fmt).Width > maxW) s = s[..^2] + "…";
        var w = g.MeasureString(s, font, PointF.Empty, fmt).Width;
        Text(g, s, cx - w / 2, baseline, size, color, semibold, 0);
    }

    static Image? ArtOf(string key, MediaState st)
    {
        if (st.Art is null) return null;
        if (_art is null || _artRev != st.ArtRev || _artKey != key)
        {
            _art?.Dispose();
            _art = Image.FromStream(new MemoryStream(st.Art));
            _artRev = st.ArtRev; _artKey = key;
        }
        return _art;
    }

    static float Frac(MediaState s) => s.Dur > 0 ? (float)Math.Clamp(s.PosNow / s.Dur, 0, 1) : 0;

    static void Bar(Graphics g, float x, float y, float w, float h, float f, Color tint, bool knob, float k = 1)
    {
        if (w <= 0) return;
        if (knob) _hits.Add(new Hit("seek", new RectangleF(x, y - 12 * k, w, h + 24 * k), x, x + w));
        var bar = Vivid(tint);
        y -= k; h += 2 * k;
        using (var tb = new SolidBrush(Color.FromArgb(36, 255, 255, 255))) g.FillRectangle(tb, x, y, w, h);
        using (var fb = new SolidBrush(bar)) g.FillRectangle(fb, x, y, Math.Max(4 * k, w * f), h);
        if (knob)
        {
            using (var kb = new SolidBrush(Color.White)) g.FillEllipse(kb, x + w * f - 7 * k, y + h / 2 - 7 * k, 14 * k, 14 * k);
            using (var kb = new SolidBrush(bar)) g.FillEllipse(kb, x + w * f - 3 * k, y + h / 2 - 3 * k, 6 * k, 6 * k);
        }
    }

    static Color Vivid(Color c)
    {
        if (c.GetSaturation() < 0.25f) return Deck.Accent;
        float hh = c.GetHue() / 60f, s = Math.Max(0.6f, c.GetSaturation()), v = 0.95f;

        var i = (int)Math.Floor(hh) % 6; var fr = hh - (float)Math.Floor(hh);
        float p = v * (1 - s), q = v * (1 - fr * s), t = v * (1 - (1 - fr) * s);
        var (r, g2, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromArgb((int)(r * 255), (int)(g2 * 255), (int)(b * 255));
    }

    static void Times(Graphics g, MediaState s, float x, float w, float baseline, Color sub, float size = 11)
    {
        string T(double v) { var t = (int)Math.Max(0, v); return $"{t / 60}:{t % 60:00}"; }
        Text(g, T(s.PosNow), x, baseline, size, sub, false, 80 * size / 11);
        using var f = new Font("Segoe UI", size, GraphicsUnit.Pixel);
        var rem = "-" + T(s.Dur - s.PosNow);
        var tw = g.MeasureString(rem, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        Text(g, rem, x + w - tw, baseline, size, sub, false, 80 * size / 11);
    }

    static void Controls(Graphics g, float cx, float cy, float size, float gap, Color ink, bool playing)
    {
        AddHit("prev", cx - gap, cy, Math.Max(20, gap * 0.48f));
        AddHit("next", cx + gap, cy, Math.Max(20, gap * 0.48f));
        AddHit("play", cx, cy, size + 6);
        Skip(g, cx - gap, cy, size * 0.85f, ink, -1);
        Skip(g, cx + gap, cy, size * 0.85f, ink, 1);
        using (var b = new SolidBrush(ink)) g.FillEllipse(b, cx - size, cy - size, size * 2, size * 2);
        PlayIcon(g, cx, cy, size * 0.85f, Color.FromArgb(11, 14, 20), playing);
    }

    static void PlayIcon(Graphics g, float cx, float cy, float s, Color c, bool playing)
    {
        using var b = new SolidBrush(c);
        if (playing)
        {
            g.FillRectangle(b, cx - s * 0.36f, cy - s * 0.4f, s * 0.24f, s * 0.8f);
            g.FillRectangle(b, cx + s * 0.12f, cy - s * 0.4f, s * 0.24f, s * 0.8f);
        }
        else g.FillPolygon(b, new[] { new PointF(cx - s * 0.3f, cy - s * 0.42f), new PointF(cx + s * 0.48f, cy), new PointF(cx - s * 0.3f, cy + s * 0.42f) });
    }

    static void Skip(Graphics g, float cx, float cy, float s, Color c, int dir)
    {
        using var b = new SolidBrush(c);
        g.FillPolygon(b, new[] { new PointF(cx - dir * s * 0.4f, cy - s * 0.36f), new PointF(cx + dir * s * 0.22f, cy), new PointF(cx - dir * s * 0.4f, cy + s * 0.36f) });
        var bx = dir > 0 ? cx + s * 0.24f : cx - s * 0.38f;
        g.FillRectangle(b, bx, cy - s * 0.36f, s * 0.14f, s * 0.72f);
    }

    static void Text(Graphics g, string s, float x, float baseline, float size, Color color, bool semibold, float maxW, bool bold = false)
    {
        using var font = new Font(bold ? "Segoe UI" : semibold ? "Segoe UI Semibold" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;
        if (maxW > 0)
            while (s.Length > 1 && g.MeasureString(s, font, PointF.Empty, fmt).Width > maxW) s = s[..^2] + "…";
        var ascent = size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);
        using var b = new SolidBrush(color);
        g.DrawString(s, font, b, x, baseline - ascent, fmt);
    }

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
}
