using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json.Nodes;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

static class WidgetPreview
{
    public static int CardAlpha = 255;
    static readonly CultureInfo Tr = new("tr-TR");
    static Color _ink, _sub;

    public static void Draw(Graphics g, Tile t, RectangleF r, Theme th, DeckServer? srv)
    {
        _ink = Ui.ParseColor(th.Text) ?? Deck.Ink;
        _sub = Ui.ParseColor(th.SubText) ?? Deck.Muted;
        var accent = Ui.ParseColor(t.Color);
        var themeAccent = Ui.ParseColor(th.Accent) ?? Deck.Accent;
        switch (t.Kind)
        {
            case "clock":
                Frame(g, r, th, accent, 0.12f);
                var c = t.Clock ?? new ClockSpec();
                var now = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(c.Zone))
                    try { now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, c.Zone); } catch { }
                if (c.Style == "analog") Analog(g, r, c, now, accent ?? themeAccent);
                else Digital(g, r, c, now, accent ?? themeAccent);
                break;
            case "timer":
                Timer(g, t, r, th, accent);
                break;
            case "plugin":
                PluginCard(g, t, r, th, srv);
                break;
            case "weather":
                Frame(g, r, th, accent, 0.1f);
                Weather(g, t, r, srv?.Weather.Current);
                break;
        }
    }

    static void Frame(Graphics g, RectangleF r, Theme th, Color? accent, float k)
    {
        var tile = Ui.ParseColor(th.Tile) ?? Deck.Card;
        var border = Ui.ParseColor(th.TileBorder) ?? Deck.Border;
        using var path = Rounded(r, th.Radius);
        using (var b = new SolidBrush(Color.FromArgb(CardAlpha, accent is { } a ? Ui.Mix(tile, a, k) : tile))) g.FillPath(b, path);
        using var pen = new Pen(accent is { } a2 ? Ui.Mix(border, a2, 0.75f) : border, 1);
        g.DrawPath(pen, path);
    }

    static void Caption(Graphics g, string s, float x, float y, bool right = false, float maxW = 0, float size = 11) =>
        Text(g, s.ToUpper(Tr), x, y, size, _sub, FontStyle.Bold, right ? StringAlignment.Far : StringAlignment.Near, maxW);

    static void Text(Graphics g, string s, float x, float baseline, float size, Color color, FontStyle style = FontStyle.Regular,
        StringAlignment align = StringAlignment.Near, float maxW = 0, string family = "Segoe UI")
    {
        using var font = new Font(family, size, style, GraphicsUnit.Pixel);
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        if (maxW > 0)
            while (s.Length > 1 && g.MeasureString(s, font, PointF.Empty, fmt).Width > maxW) s = s[..^2] + "…";
        var ascent = size * font.FontFamily.GetCellAscent(style) / font.FontFamily.GetEmHeight(style);
        var w = g.MeasureString(s, font, PointF.Empty, fmt).Width;
        var left = align switch { StringAlignment.Center => x - w / 2, StringAlignment.Far => x - w, _ => x };
        using var b = new SolidBrush(color);
        g.DrawString(s, font, b, left, baseline - ascent, fmt);
    }

    static float Measure(Graphics g, string s, float size, string family = "Segoe UI Light", FontStyle style = FontStyle.Regular)
    {
        using var font = new Font(family, size, style, GraphicsUnit.Pixel);
        return g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width;
    }

    static Color A(Color c, float a) => Color.FromArgb((int)(Math.Clamp(a, 0, 1) * 255), c);

    static void Digital(Graphics g, RectangleF r, ClockSpec c, DateTime now, Color accent)
    {
        float w = r.Width, h = r.Height, p = w < 200 ? 12 : 18;
        var sec = c.Seconds ?? true; var date = c.Date ?? true;
        if (!string.IsNullOrWhiteSpace(c.City)) Caption(g, c.City!, r.Left + p, r.Top + p + 10);
        var hm = now.ToString("HH:mm"); var ss = now.ToString("ss");
        var top = r.Top + p + (string.IsNullOrWhiteSpace(c.City) ? 0 : 14);
        var dateH = date ? Math.Max(14, Math.Min(18, h * 0.1f)) + 10 : 0;
        var barH = sec ? 8f : 0;
        var bottom = r.Bottom - p - dateH - barH;
        var size = Math.Min((bottom - top) * 0.92f, h * 0.5f);
        float Width(float s) => Measure(g, hm, s) + (sec ? 6 + Measure(g, ss, s * 0.3f, "Segoe UI") : 0);
        while (size > 12 && Width(size) > w - 2 * p) size *= 0.94f;
        var base0 = (top + bottom) / 2 + size * 0.36f;
        Text(g, hm, r.Left + p - size * 0.03f, base0, size, _ink, family: "Segoe UI Light");
        if (sec) Text(g, ss, r.Left + p + Measure(g, hm, size) + 6, base0, size * 0.3f, _sub);
        if (date)
        {
            var ds = Math.Max(12, Math.Min(16, h * 0.09f));
            Text(g, now.ToString(w < 220 ? "d MMM ddd" : "d MMMM dddd", Tr), r.Left + p, r.Bottom - p - barH, ds, _ink, FontStyle.Regular, maxW: w - 2 * p, family: "Segoe UI Semibold");
        }
        if (sec)
        {
            var y = r.Bottom - p * 0.75f;
            using (var b = new SolidBrush(A(_sub, 0.15f))) g.FillRectangle(b, r.Left + p, y, w - 2 * p, 2);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, r.Left + p, y, (w - 2 * p) * (now.Second + now.Millisecond / 1000f) / 60f, 2);
        }
    }

    static void Analog(Graphics g, RectangleF r, ClockSpec c, DateTime now, Color accent)
    {
        const float labelH = 26;
        float cx = r.Left + r.Width / 2, cy = r.Top + (r.Height - labelH) / 2 + 4;
        var rad = Math.Min(r.Width, r.Height - labelH) * 0.4f;
        using (var pen = new Pen(A(_sub, 0.25f), 1)) g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
        for (var i = 0; i < 12; i++)
        {
            var a = i * Math.PI / 6; var major = i % 3 == 0;
            var l = (major ? 0.12f : 0.06f) * rad; var r0 = rad - 3;
            using var pen = new Pen(major ? _ink : A(_sub, 0.6f), major ? 2 : 1.2f);
            g.DrawLine(pen, cx + (float)Math.Sin(a) * r0, cy - (float)Math.Cos(a) * r0, cx + (float)Math.Sin(a) * (r0 - l), cy - (float)Math.Cos(a) * (r0 - l));
        }
        float s = now.Second, m = now.Minute + s / 60f, hr = now.Hour % 12 + m / 60f;
        void Hand(double ang, float len, float w, Color col)
        {
            using var pen = new Pen(col, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, cx - (float)Math.Sin(ang) * 6, cy + (float)Math.Cos(ang) * 6, cx + (float)Math.Sin(ang) * len, cy - (float)Math.Cos(ang) * len);
        }
        Hand(hr * Math.PI / 6, rad * 0.5f, Math.Max(3, rad * 0.06f), _ink);
        Hand(m * Math.PI / 30, rad * 0.76f, Math.Max(2.4f, rad * 0.045f), _ink);
        if (c.Seconds ?? true) Hand(s * Math.PI / 30, rad * 0.86f, 1.4f, accent);
        using (var b = new SolidBrush(accent)) g.FillEllipse(b, cx - 3.5f, cy - 3.5f, 7, 7);
        var name = c.City ?? "";
        var line = !string.IsNullOrWhiteSpace(c.Zone) ? $"{name}  {now:HH:mm}".Trim() : (c.Date ?? true) ? now.ToString("d MMM ddd", Tr) : name;
        if (line.Length > 0) Text(g, line, cx, r.Bottom - 14, 13, _ink, align: StringAlignment.Center, maxW: r.Width - 16, family: "Segoe UI Semibold");
    }

    public sealed class TimerRun
    {
        public bool Running;
        public double Base;
        public DateTime Since;
        public bool Done;
        public int Total;
    }

    public static readonly Dictionary<string, TimerRun> Timers = new();

    static bool IsStopwatch(Tile t) => t.Timer?.Mode == "stopwatch";

    public static double TimerValue(Tile t, TimerRun run)
    {
        var passed = run.Running ? (DateTime.UtcNow - run.Since).TotalSeconds : 0;
        return IsStopwatch(t) ? run.Base + passed : Math.Max(0, run.Base - passed);
    }

    public static string TimerHit(Tile t, RectangleF r, PointF p)
    {
        const float pad = 14;
        if (new RectangleF(r.Left, r.Bottom - pad - 26, 40, 40).Contains(p)) return "reset";
        if (!IsStopwatch(t) && new RectangleF(r.Right - 52, r.Top, 52, 44).Contains(p)) return "plus";
        return "toggle";
    }

    public static void TimerAction(Tile t, string action)
    {
        var total = Math.Max(5, t.Timer?.Seconds ?? 300);
        if (!Timers.TryGetValue(t.Id, out var run)) Timers[t.Id] = run = new TimerRun { Base = IsStopwatch(t) ? 0 : total, Total = total };
        var now = TimerValue(t, run);
        switch (action)
        {
            case "reset":
                Timers.Remove(t.Id);
                break;
            case "plus":
                run.Base = now + 60; run.Since = DateTime.UtcNow; run.Total = Math.Max(run.Total, (int)run.Base); run.Done = false;
                break;
            default:
                if (run.Done) { Timers.Remove(t.Id); break; }
                run.Base = now; run.Since = DateTime.UtcNow; run.Running = !run.Running;
                break;
        }
    }

    public static bool TickTimers(IEnumerable<Tile> tiles, Action<Tile> finished)
    {
        var any = false;
        foreach (var t in tiles)
        {
            if (!Timers.TryGetValue(t.Id, out var run) || !run.Running) continue;
            any = true;
            if (IsStopwatch(t) || TimerValue(t, run) > 0) continue;
            run.Running = false; run.Base = 0; run.Done = true;
            finished(t);
        }
        return any;
    }

    static void Timer(Graphics g, Tile t, RectangleF r, Theme th, Color? accent)
    {
        var spec = t.Timer ?? new TimerSpec();
        var sw = spec.Mode == "stopwatch";
        Timers.TryGetValue(t.Id, out var run);
        var color = accent ?? (sw ? Deck.Cyan : Ui.ParseColor(th.Accent) ?? Deck.Accent);
        Frame(g, r, th, color, 0.1f);
        const float p = 14;
        Caption(g, t.Label ?? (sw ? T("Stopwatch") : T("Timer")), r.Left + p, r.Top + p + 10, maxW: r.Width - 2 * p - 24);
        if (!sw) Text(g, "+1", r.Right - p, r.Top + p + 10, 13, _sub, align: StringAlignment.Far, family: "Segoe UI Semibold");
        float cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2 + 6;
        var big = r.Width > 220 && r.Height > 220;
        var rad = Math.Min(r.Width, r.Height - 40) * (big ? 0.36f : 0.33f);
        var ring = Math.Max(5, rad * 0.11f);
        var value = run is null ? (sw ? 0 : spec.Seconds) : TimerValue(t, run);
        var frac = sw ? (float)(value % 60 / 60) : run is null ? 1f : (float)Math.Clamp(value / Math.Max(1, run.Total), 0, 1);
        using (var pen = new Pen(A(_sub, 0.14f), ring)) g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
        if (frac > 0.002f)
            using (var pen = new Pen(run?.Done == true ? Deck.Good : color, ring) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, -90, 359.9f * frac);
        var secs = (int)Math.Ceiling(value - (sw ? 0.999 : 0));
        var txt = secs >= 3600 ? $"{secs / 3600}:{secs % 3600 / 60:00}:{secs % 60:00}" : $"{secs / 60:00}:{secs % 60:00}";
        var ts = rad * (secs >= 3600 ? 0.42f : 0.52f);
        Text(g, txt, cx, cy + ts * 0.3f, ts, _ink, align: StringAlignment.Center, family: "Segoe UI Semibold");
        var state = run is null ? T("start") : run.Done ? T("done") : run.Running ? T("pause") : T("resume");
        Text(g, state, cx, cy + rad * 0.48f, Math.Max(10, rad * 0.2f), _sub, align: StringAlignment.Center);
        using (var pen = new Pen(_sub, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(pen, r.Left + p - 1, r.Bottom - p - 12, 14, 14, -60, 300);
    }

    static void PluginCard(Graphics g, Tile t, RectangleF r, Theme th, DeckServer? srv)
    {
        var v = t.Plugin is null ? null : srv?.Plugins.Value(t.Plugin);
        var item = srv?.Plugins.Find(t.Plugin?.Plugin)?.Data.FirstOrDefault(d => d.Id == t.Plugin?.Item);
        var color = Ui.ParseColor(v?.Color) ?? Ui.ParseColor(t.Color) ?? Ui.ParseColor(th.Accent) ?? Deck.Accent;
        Frame(g, r, th, color, 0.1f);
        float w = r.Width, h = r.Height, p = w < 200 ? 14 : 18;
        Caption(g, t.Label ?? item?.Name ?? T("Plugin"), r.Left + p, r.Top + p + 10, maxW: w - 2 * p);
        var value = v?.Value ?? (item is null ? T("no plugin") : "…");
        var size = Math.Min(h * 0.3f, 56);
        while (size > 12 && Measure(g, value, size, "Segoe UI Semibold") > w - 2 * p) size *= 0.92f;
        var hasBar = v?.Percent is not null;
        var cy = r.Top + h / 2 + size * 0.3f - (hasBar ? 4 : 0);
        Text(g, value, r.Left + p, cy, size, _ink, maxW: w - 2 * p, family: "Segoe UI Semibold");
        if (v?.Sub is { Length: > 0 } sub) Text(g, sub, r.Left + p, cy + 20, 12, _sub, maxW: w - 2 * p);
        if (hasBar)
        {
            var y = r.Bottom - p - 2;
            using (var b = new SolidBrush(A(_sub, 0.15f))) g.FillRectangle(b, r.Left + p, y, w - 2 * p, 4);
            using (var b = new SolidBrush(color)) g.FillRectangle(b, r.Left + p, y, (w - 2 * p) * (float)v!.Percent!.Value, 4);
        }
    }

    static string Deg(JsonNode? n) => n is null ? "–" : $"{n.GetValue<int>()}°";

    static void Weather(Graphics g, Tile t, RectangleF r, JsonObject? x)
    {
        float w = r.Width, h = r.Height, p = w < 200 ? 14 : 18;
        if (x is null)
        {
            Icon(g, "cloud", r.Left + w / 2, r.Top + h / 2 - 14, Math.Min(w, h) * 0.4f, 0.35f);
            Text(g, T("No weather data"), r.Left + w / 2, r.Bottom - p - 14, 13, _ink, align: StringAlignment.Center, maxW: w - 2 * p, family: "Segoe UI Semibold");
            Text(g, T("Pick a city in MyDeck settings"), r.Left + w / 2, r.Bottom - p, 11, _sub, align: StringAlignment.Center, maxW: w - 2 * p);
            return;
        }
        var n = x["now"]!;
        var view = t.Weather?.View ?? (t.W >= 2 && t.H >= 2 ? "hours" : "now");
        var place = x["place"]?.GetValue<string>() ?? "";
        if (view == "tomorrow")
        {
            var m = x["tomorrow"]!;
            var kt = Wk(r); var pt = 14 * kt; var bigT = Math.Min(h * 0.14f, w * 0.15f);
            Caption(g, t.Label ?? m["day"]?.GetValue<string>() ?? T("Tomorrow"), r.Left + pt, r.Top + pt + 10 * kt, maxW: w * 0.6f, size: 11 * kt);
            WeatherCardIcon(g, r, m["icon"]?.GetValue<string>() ?? "cloud", pt, kt, bigT);
            Text(g, $"{Deg(m["hi"])} / {Deg(m["lo"])}", r.Left + w / 2, r.Bottom - pt - 18 * kt, bigT, _ink, align: StringAlignment.Center, maxW: w - 2 * pt, family: "Segoe UI Semibold");
            Text(g, m["rain"] is { } rn ? T("Rain {0}%", rn) : m["cond"]?.GetValue<string>() ?? "", r.Left + w / 2, r.Bottom - pt, 11.5f * kt, _sub, align: StringAlignment.Center, maxW: w - 2 * pt);
            return;
        }
        var icon = n["icon"]?.GetValue<string>() ?? "cloud";
        var cond = n["cond"]?.GetValue<string>() ?? "";
        if (view == "hours" && h >= 260 && w >= 260)
        {
            Caption(g, t.Label ?? (place.Length > 0 ? place : T("Weather")), r.Left + p, r.Top + p + 10);
            if (x["at"]?.GetValue<string>() is { Length: > 0 } at) Text(g, T("updated ") + at, r.Right - p, r.Top + p + 10, 12, _sub, align: StringAlignment.Far);
            var s = w < h * 1.3f ? Math.Min(h * 0.46f, 145) : Math.Min(h * 0.36f, 100);
            var iy = r.Top + p + 24 + s * 0.45f;
            Icon(g, icon, r.Left + p + s / 2, iy, s);
            var big = Math.Min(h * 0.24f, 64);
            Text(g, Deg(n["t"]), r.Left + p + s + 12, iy + big * 0.36f, big, _ink, family: "Segoe UI Light");
            Text(g, $"↑{Deg(n["hi"])}  ↓{Deg(n["lo"])}", r.Right - p, iy + big * 0.36f, 13, _ink, align: StringAlignment.Far, family: "Segoe UI Semibold");
            var ty = iy + s * 0.5f + 26;
            Text(g, cond, r.Left + p, ty, 15, _ink, maxW: w - 2 * p, family: "Segoe UI Semibold");
            Text(g, T("Feels like {0}  ·  Humidity {1}%  ·  Wind {2} km/h", Deg(n["feels"]), n["hum"], n["wind"]), r.Left + p, ty + 20, 12.5f, _sub, maxW: w - 2 * p);
            var y0 = ty + 36;
            var avail = r.Bottom - p - y0;
            if (avail < 70 || x["hours"] is not JsonArray hours || hours.Count == 0) return;
            using (var b = new SolidBrush(A(_sub, 0.12f))) g.FillRectangle(b, r.Left + p, y0, w - 2 * p, 1);
            var count = Math.Min(hours.Count, Math.Max(3, (int)((w - 2 * p) / 58)));
            var cw = (w - 2 * p) / count;
            var isz = Math.Min(cw * 0.8f, avail * 0.45f);
            for (var i = 0; i < count; i++)
            {
                var hr = hours[i]!;
                var cx = r.Left + p + cw * i + cw / 2;
                Text(g, hr["h"]?.GetValue<string>() ?? "", cx, y0 + 18, 12, _sub, align: StringAlignment.Center);
                Icon(g, hr["icon"]?.GetValue<string>() ?? "cloud", cx, y0 + 26 + isz / 2, isz);
                Text(g, Deg(hr["t"]), cx, r.Bottom - p, 14, _ink, align: StringAlignment.Center, family: "Segoe UI Semibold");
            }
            return;
        }

        var k = Wk(r);
        p = 14 * k;
        var vertical = w <= h * 1.25f;
        Caption(g, t.Label ?? (place.Length > 0 ? place : T("Now")), r.Left + p, r.Top + p + 10 * k,
            maxW: vertical || w <= 200 ? w - 2 * p : w * 0.6f, size: 11 * k);
        if (vertical)
        {
            var bigV = Math.Min(h * 0.2f, w * 0.24f);
            WeatherCardIcon(g, r, icon, p, k, bigV);
            Text(g, Deg(n["t"]), r.Left + w / 2, r.Bottom - p - 18 * k, bigV, _ink, align: StringAlignment.Center, family: "Segoe UI Semibold");
            Text(g, cond, r.Left + w / 2, r.Bottom - p, 11.5f * k, _sub, align: StringAlignment.Center, maxW: w - 2 * p);
            return;
        }
        var top = r.Top + p + 16 * k;
        var hh = r.Bottom - p - top;
        var iconSize = Math.Min(hh / 0.79f, w * 0.45f);
        Icon(g, icon, r.Left + p + iconSize / 2, top + hh / 2, iconSize);
        var x0 = r.Left + p + iconSize + 12 * k;
        var tw = r.Right - p - x0;
        var bigW = Math.Min(Math.Min(h * 0.3f, tw * 0.5f), 64 * k);
        var showCond = tw > 60 * k;
        var extra = showCond && h > 140 * k && tw > 110 * k;
        var lines = !showCond ? 0 : extra ? 2 : 1;
        var y = top + hh / 2 - (bigW * 0.75f + lines * 18 * k) / 2 + bigW * 0.75f;
        Text(g, Deg(n["t"]), x0, y, bigW, _ink, family: "Segoe UI Light");
        if (showCond) { y += 18 * k; Text(g, cond, x0, y, 13 * k, _ink, maxW: tw, family: "Segoe UI Semibold"); }
        if (extra) { y += 18 * k; Text(g, T("Feels like {0} · Humidity {1}%", Deg(n["feels"]), n["hum"]), x0, y, 12 * k, _sub, maxW: tw); }
        if (w > 200) Text(g, $"↑{Deg(n["hi"])}  ↓{Deg(n["lo"])}", r.Right - p, r.Top + p + 10 * k, 12 * k, _sub, align: StringAlignment.Far, family: "Segoe UI Semibold");
    }

    static float Wk(RectangleF r) => Math.Clamp(Math.Min(r.Width / 131f, r.Height / 183f), 0.8f, 1.5f);

    static void WeatherCardIcon(Graphics g, RectangleF r, string kind, float p, float k, float big)
    {
        var top = r.Top + p + 14 * k;
        var bottom = r.Bottom - p - 18 * k - big * .80f;
        var available = Math.Max(0, bottom - top);
        Icon(g, kind, r.Left + r.Width / 2, top + available / 2, Math.Min(r.Width - 4 * k, available / .74f));
    }

    static void Icon(Graphics g, string kind, float cx, float cy, float s, float alpha = 1)
    {
        if (WeatherArtwork.Draw(g, kind, cx, cy, s, alpha)) return;
        var sunC = Color.FromArgb(0xFF, 0xC5, 0x3D);
        void Sun(float x, float y, float r)
        {
            using (var b = new SolidBrush(A(sunC, alpha))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            using var pen = new Pen(A(sunC, alpha), Math.Max(1.5f, r * 0.18f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                g.DrawLine(pen, x + (float)Math.Cos(a) * r * 1.35f, y + (float)Math.Sin(a) * r * 1.35f, x + (float)Math.Cos(a) * r * 1.75f, y + (float)Math.Sin(a) * r * 1.75f);
            }
        }
        void Moon(float x, float y, float r)
        {
            using var full = new GraphicsPath(); full.AddEllipse(x - r, y - r, r * 2, r * 2);
            using var cut = new GraphicsPath(); cut.AddEllipse(x + r * 0.45f - r * 0.85f, y - r * 0.3f - r * 0.85f, r * 1.7f, r * 1.7f);
            using var region = new Region(full); region.Exclude(cut);
            using var b = new SolidBrush(A(Color.FromArgb(0xE8, 0xE2, 0xC8), alpha));
            g.FillRegion(b, region);
        }
        void Cloud(float x, float y, float w, Color col)
        {
            using var b = new SolidBrush(A(col, alpha));
            g.FillEllipse(b, x - w * 0.22f - w * 0.2f, y + w * 0.04f - w * 0.2f, w * 0.4f, w * 0.4f);
            g.FillEllipse(b, x + w * 0.02f - w * 0.27f, y - w * 0.08f - w * 0.27f, w * 0.54f, w * 0.54f);
            g.FillEllipse(b, x + w * 0.26f - w * 0.19f, y + w * 0.05f - w * 0.19f, w * 0.38f, w * 0.38f);
            using var rr = Rounded(new RectangleF(x - w * 0.42f, y + w * 0.02f, w * 0.86f, w * 0.22f), w * 0.11f);
            g.FillPath(b, rr);
        }
        void Drops(Color col, int n = 3)
        {
            using var pen = new Pen(A(col, alpha), Math.Max(1.5f, s * 0.04f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var i = 0; i < n; i++) { var ox = (i - (n - 1) / 2f) * s * 0.18f; g.DrawLine(pen, cx + ox, cy + s * 0.22f, cx + ox - s * 0.06f, cy + s * 0.36f); }
        }
        void Flakes(int n = 3)
        {
            using var b = new SolidBrush(A(Color.White, alpha));
            var rr = Math.Max(1.5f, s * 0.035f);
            for (var i = 0; i < n; i++) g.FillEllipse(b, cx + (i - (n - 1) / 2f) * s * 0.18f - rr, cy + s * 0.3f + (i % 2 == 0 ? 0 : s * 0.06f) - rr, rr * 2, rr * 2);
        }
        Color H(string hex) => ColorTranslator.FromHtml(hex);
        switch (kind)
        {
            case "sun": Sun(cx, cy, s * 0.22f); break;
            case "moon": Moon(cx, cy, s * 0.24f); break;
            case "cloudsun": Sun(cx - s * 0.14f, cy - s * 0.14f, s * 0.17f); Cloud(cx + s * 0.06f, cy + s * 0.08f, s * 0.8f, H("#D9E0EA")); break;
            case "cloudmoon": Moon(cx - s * 0.14f, cy - s * 0.16f, s * 0.19f); Cloud(cx + s * 0.06f, cy + s * 0.08f, s * 0.8f, H("#C9D2DE")); break;
            case "rain": Cloud(cx, cy - s * 0.08f, s * 0.8f, H("#AFC0D3")); Drops(Deck.Cyan); break;
            case "sleet": Cloud(cx, cy - s * 0.08f, s * 0.8f, H("#AFC0D3")); Drops(Deck.Cyan, 2); Flakes(1); break;
            case "snow": Cloud(cx, cy - s * 0.08f, s * 0.8f, H("#C9D2DE")); Flakes(); break;
            case "storm":
                Cloud(cx, cy - s * 0.1f, s * 0.8f, H("#8E9BB0"));
                using (var b = new SolidBrush(A(sunC, alpha)))
                    g.FillPolygon(b, new[] { new PointF(cx + s * 0.04f, cy + s * 0.12f), new PointF(cx - s * 0.1f, cy + s * 0.3f), new PointF(cx, cy + s * 0.3f),
                        new PointF(cx - s * 0.06f, cy + s * 0.44f), new PointF(cx + s * 0.12f, cy + s * 0.24f), new PointF(cx + s * 0.02f, cy + s * 0.24f) });
                break;
            case "fog":
                Cloud(cx, cy - s * 0.12f, s * 0.72f, H("#9AA6B8"));
                using (var pen = new Pen(A(H("#C9D2DE"), alpha), Math.Max(1.5f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - s * 0.32f, cy + s * 0.2f, cx + s * 0.28f, cy + s * 0.2f);
                    g.DrawLine(pen, cx - s * 0.24f, cy + s * 0.32f, cx + s * 0.34f, cy + s * 0.32f);
                }
                break;
            case "wind":
                using (var pen = new Pen(A(H("#C9D2DE"), alpha), Math.Max(1.5f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var lens = new[] { 0.6f, 0.75f, 0.5f };
                    for (var i = 0; i < 3; i++) { var y = cy - s * 0.16f + i * s * 0.16f; g.DrawLine(pen, cx - s * 0.36f, y, cx - s * 0.36f + s * lens[i], y); }
                }
                break;
            default: Cloud(cx, cy, s * 0.85f, H("#C9D2DE")); break;
        }
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
