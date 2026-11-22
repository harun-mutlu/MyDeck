using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyDeck.Server.Core;

public sealed class Profile
{
    public string Start { get; set; } = "ana";
    public Theme Theme { get; set; } = new();

    public TabletSettings Tablet { get; set; } = new();
    public List<Page> Pages { get; set; } = new();

    public Dictionary<string, string>? Vars { get; set; }

    public List<DeckEvent>? Events { get; set; }

    public bool SmartReturn { get; set; } = true;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Profile Load(string path)
    {
        var text = File.ReadAllText(path);
        var p = JsonSerializer.Deserialize<Profile>(text, Json) ?? throw new InvalidDataException(T("The profile is empty."));
        p.Validate();
        return p;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public void Validate()
    {
        if (Pages.Count == 0) throw new InvalidDataException(T("The profile has no pages."));
        var ids = new HashSet<string>();
        foreach (var page in Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id)) throw new InvalidDataException(T("A page has an empty 'id'."));
            if (!ids.Add(page.Id)) throw new InvalidDataException(T("The page id '{0}' is used twice.", page.Id));
            if (page.Shape is not (null or "fill" or "square")) page.Shape = null;
            if (page.Cols is < 1 or > 15 || page.Rows is < 1 or > 15)
                throw new InvalidDataException(T("The grid of page '{0}' must be 1–15 (now {1}×{2}).", page.Id, page.Cols, page.Rows));
            var taken = new bool[page.Cols, page.Rows];
            var tileIds = new HashSet<string>();
            for (var n = 0; n < page.Tiles.Count; n++)
            {
                var t = page.Tiles[n];
                if (string.IsNullOrWhiteSpace(t.Id)) t.Id = $"{page.Id}-{n + 1}";
                if (!tileIds.Add(t.Id)) throw new InvalidDataException(T("Page '{0}' has the id '{1}' twice.", page.Id, t.Id));
                if (t.Kind is not (null or "button" or "toggle" or "slider" or "graph" or "ring" or "gauge" or "net" or "procs" or "media" or "clock" or "timer" or "weather" or "plugin"))
                    throw new InvalidDataException(T("Unknown tile type for '{0}': '{1}'.", t.Label ?? t.Id, t.Kind));
                if (t.Kind == "slider" && string.IsNullOrWhiteSpace(t.Target)) t.Target = "master";
                if (t.Kind == "toggle" && string.IsNullOrWhiteSpace(t.Source)) t.Source = "local";
                if (t.Kind == "media") t.Media ??= new MediaSpec();
                if (t.Kind == "clock") t.Clock ??= new ClockSpec();
                if (t.Kind == "timer") { t.Timer ??= new TimerSpec(); t.Timer.Seconds = Math.Clamp(t.Timer.Seconds, 5, 24 * 3600); }
                if (t.Kind == "weather") t.Weather ??= new WeatherSpec();
                if (t.Kind == "plugin") t.Plugin ??= new PluginSpec();
                if (t.IsLive)
                {
                    t.Live ??= new LiveSpec();
                    if (string.IsNullOrWhiteSpace(t.Live.Metric))
                        t.Live.Metric = t.Kind switch { "ring" => "ram.used", "gauge" => "gpu.temp", _ => "cpu" };
                    if (t.Live.Seconds is not (30 or 60 or 300)) t.Live.Seconds = 60;
                }
                if (t.W < 1) t.W = 1;
                if (t.H < 1) t.H = 1;
                if (t.X < 0 || t.Y < 0 || t.X + t.W > page.Cols || t.Y + t.H > page.Rows)
                    throw new InvalidDataException(T("On page '{0}', the tile '{1}' goes outside the grid.", page.Id, t.Label ?? t.Id));
                for (var x = t.X; x < t.X + t.W; x++)
                    for (var y = t.Y; y < t.Y + t.H; y++)
                    {
                        if (taken[x, y]) throw new InvalidDataException(T("On page '{0}', the tile '{1}' overlaps another tile ({2},{3}).", page.Id, t.Label ?? t.Id, x, y));
                        taken[x, y] = true;
                    }
            }
        }
        foreach (var page in Pages)
            foreach (var t in page.Tiles)
            {
                if (t.Action?.Type is "page" or "folder" && !ids.Contains(t.Action.Page ?? ""))
                    throw new InvalidDataException(T("The tile '{0}' goes to a page that doesn't exist ('{1}').", t.Label ?? t.Id, t.Action.Page));
                if (t.Kind is not (null or "button" or "toggle")) { t.Hold = null; t.Double = null; }
            }
        foreach (var page in Pages)
            if (page.Apps is { } apps)
            {
                page.Apps = apps.Select(DeckEvent.AppName).Where(a => a.Length > 0).Distinct().ToList();
                if (page.Apps.Count == 0) page.Apps = null;
            }
        if (!ids.Contains(Start)) Start = Pages[0].Id;
    }
}

public sealed class TabletSettings
{
    public bool SleepWhenIdle { get; set; } = true;
    public int IdleMinutes { get; set; } = 10;

    public bool SleepWhenPcOff { get; set; } = true;
}

public sealed class Theme
{
    public string Background { get; set; } = "#07090E";
    public string Tile { get; set; } = "#11151F";
    public string TileBorder { get; set; } = "#222938";
    public string Text { get; set; } = "#E8ECF4";
    public string SubText { get; set; } = "#8D97AB";
    public string Accent { get; set; } = "#FF3D5A";
    public string Press { get; set; } = "#FF3D5A";
    public int Radius { get; set; } = 16;
    public int Gap { get; set; } = 12;

    public int? Margin { get; set; }

    public string? Wallpaper { get; set; }

    public int WallpaperDim { get; set; } = 20;

    public int CardGlass { get; set; } = 40;
}

public sealed class Page
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Cols { get; set; } = 5;
    public int Rows { get; set; } = 3;

    public bool Folder { get; set; }

    public string? Wallpaper { get; set; }

    public string? Shape { get; set; }

    public List<string>? Apps { get; set; }
    public List<Tile> Tiles { get; set; } = new();
}

public sealed class Tile
{
    public string Id { get; set; } = "";

    public string? Kind { get; set; }

    public string? Source { get; set; }

    public TileVariant? On { get; set; }

    public string? Target { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 1;
    public int H { get; set; } = 1;
    public string? Label { get; set; }
    public string? Sub { get; set; }

    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? IconColor { get; set; }
    public string? Background { get; set; }
    public TileAction? Action { get; set; }

    public TileAction? Hold { get; set; }

    public TileAction? Double { get; set; }

    public LiveSpec? Live { get; set; }

    public MediaSpec? Media { get; set; }

    public TileGif? Gif { get; set; }

    public ClockSpec? Clock { get; set; }

    public TimerSpec? Timer { get; set; }

    public WeatherSpec? Weather { get; set; }

    public PluginSpec? Plugin { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLive => Kind is "graph" or "ring" or "gauge" or "net" or "procs";
}

public sealed class LiveSpec
{
    public string? Metric { get; set; }

    public string? Metric2 { get; set; }

    public List<string>? Info { get; set; }

    public string? Style { get; set; }

    public string? ColorMode { get; set; }

    public int Seconds { get; set; } = 60;

    public string? Color2 { get; set; }
}

public sealed class ClockSpec
{
    public string? Style { get; set; }
    public bool? Seconds { get; set; }
    public bool? Date { get; set; }

    public string? Zone { get; set; }

    public string? City { get; set; }
}

public sealed class TimerSpec
{
    public string? Mode { get; set; }

    public int Seconds { get; set; } = 300;

    public bool? Announce { get; set; }
}

public sealed class WeatherSpec
{
    public string? View { get; set; }
}

public sealed class PluginSpec
{
    public string? Plugin { get; set; }
    public string? Item { get; set; }
    public Dictionary<string, string>? Args { get; set; }
}

public sealed class TileGif
{
    public string? Path { get; set; }

    public string? Fill { get; set; }

    public string? Play { get; set; }
}

public sealed class MediaSpec
{
    public string? App { get; set; }

    public bool? FollowArt { get; set; }

    public bool? Controls { get; set; }

    public bool? Times { get; set; }
}

public sealed class TileVariant
{
    public string? Label { get; set; }
    public string? Sub { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public TileAction? Action { get; set; }
}

public sealed class TileAction
{
    public string Type { get; set; } = "";

    public string? Var { get; set; }

    public string? Op { get; set; }

    public string? Value { get; set; }

    public List<TileAction>? Else { get; set; }

    public string? Text { get; set; }

    public PluginSpec? Plugin { get; set; }
    public string? Keys { get; set; }
    public string? Key { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Url { get; set; }
    public string? Page { get; set; }
    public int Ms { get; set; }
    public List<TileAction>? Steps { get; set; }
}

public sealed class DeckEvent
{
    public string When { get; set; } = "start";

    public string? Arg { get; set; }
    public bool Enabled { get; set; } = true;
    public TileAction? Do { get; set; }

    public static readonly (string Key, string Name)[] Triggers =
    {
        ("start", T("When MyDeck starts")), ("connect", T("When a tablet connects")), ("app", T("When an app comes to the front")),
        ("appleave", T("When you leave an app")), ("var", T("When a variable changes")), ("time", T("Every day at")),
    };

    public static string AppName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var n = Path.GetFileName(s.Trim().Trim('"'));
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.ToLowerInvariant();
    }
}

public readonly record struct GridGeo(float Cw, float Ch, float Ox, float Oy, float Gx, float Gy)
{
    public System.Drawing.RectangleF Rect(int x, int y, int w, int h) =>
        new(Ox + x * (Cw + Gx), Oy + y * (Ch + Gy), Cw * w + Gx * (w - 1), Ch * h + Gy * (h - 1));
}

public static class Grid
{
    public static GridGeo Layout(float width, float height, int cols, int rows, float gap, float margin, bool square)
    {
        cols = Math.Max(1, cols); rows = Math.Max(1, rows);
        float aw = width - 2 * margin, ah = height - 2 * margin;
        var cw = (aw - gap * (cols - 1)) / cols;
        var ch = (ah - gap * (rows - 1)) / rows;
        if (!square) return new GridGeo(cw, ch, margin, margin, gap, gap);
        var s = Math.Max(1f, Math.Min(cw, ch));
        float w, gx, h, gy;
        if (cols > 1) { w = s; gx = (aw - cols * s) / (cols - 1); } else { w = aw; gx = gap; }
        if (rows > 1) { h = s; gy = (ah - rows * s) / (rows - 1); } else { h = ah; gy = gap; }
        return new GridGeo(w, h, margin, margin, gx, gy);
    }

    public static GridGeo Layout(float width, float height, Page page, Theme theme, float density = 1f) =>
        Layout(width, height, page.Cols, page.Rows, theme.Gap * density, (theme.Margin ?? theme.Gap) * density, page.Shape == "square");
}
