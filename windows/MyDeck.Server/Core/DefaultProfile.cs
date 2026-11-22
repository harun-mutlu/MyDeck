namespace MyDeck.Server.Core;

public static class DefaultProfile
{
    static Tile Make(int x, int y, string label, string glyph, TileAction action, int w = 1, int h = 1, string? sub = null, string? color = null) =>
        new() { X = x, Y = y, W = w, H = h, Label = label, Sub = sub, Icon = "glyph:" + glyph, Action = action, Color = color };

    static TileAction Keys(string k) => new() { Type = "keys", Keys = k };
    static TileAction Media(string k) => new() { Type = "media", Key = k };
    static TileAction Run(string p, string? a = null) => new() { Type = "run", Path = p, Args = a };
    static TileAction Go(string page) => new() { Type = "page", Page = page };
    static TileAction Folder(string page) => new() { Type = "folder", Page = page };
    static readonly TileAction Back = new() { Type = "back" };

    static Tile Graph(string id, int x, int y, int w, int h, string label, string metric, string? metric2 = null, params string[] info) => new()
    {
        Id = id, Kind = "graph", X = x, Y = y, W = w, H = h, Label = label,
        Live = new LiveSpec { Metric = metric, Metric2 = metric2, Info = info.ToList() },
    };

    public static Page SystemPage() => new()
    {
        Id = "sistem", Name = T("System"), Cols = 6, Rows = 4,
        Tiles =
        {
            Graph("sistem-cpu", 0, 0, 3, 2, "CPU", "cpu", "cpu.temp", "cpu.clock", "cpu.temp", "cpu.power", "cpu.procs"),
            Graph("sistem-gpu", 3, 0, 3, 2, "GPU", "gpu", "gpu.temp", "gpu.vram", "gpu.clock", "gpu.temp", "gpu.power"),
            Graph("sistem-ram", 0, 2, 2, 2, "RAM", "ram", null, "ram.used", "ram.free"),
            new Tile { Id = "sistem-ag", Kind = "net", X = 2, Y = 2, W = 2, H = 2, Label = T("Network"), Live = new LiveSpec() },
            new Tile { Id = "sistem-gpusicak", Kind = "gauge", X = 4, Y = 2, Label = T("GPU temperature"), Live = new LiveSpec { Metric = "gpu.temp" } },
            Graph("sistem-gpuguc", 5, 2, 1, 1, T("GPU power"), "gpu.power"),
            Graph("sistem-ramdisk", 4, 3, 2, 1, "RAM · Disk", "ram", "disk", "disk", "disk.total"),
        },
    };

    public static Page Sound() => new()
    {
        Id = "ses", Name = T("Audio"), Cols = 6, Rows = 3,
        Tiles =
        {
            new Tile { Id = "ses-ana", Kind = "slider", Target = "master", X = 0, Y = 0, H = 3, Label = T("Main volume") },
            new Tile { Id = "ses-mik", Kind = "slider", Target = "mic", X = 1, Y = 0, H = 3, Label = T("Microphone"), Color = "#FFB000" },
            new Tile { Id = "ses-discord", Kind = "slider", Target = "app:Discord", X = 2, Y = 0, H = 3, Label = "Discord", Color = "#7C8CFF" },
            new Tile
            {
                Id = "ses-mik-sessiz", Kind = "toggle", Source = "micmute", X = 3, Y = 0,
                Label = T("Mic on"), Icon = "glyph:E720", Color = "#4ADE80",
                On = new TileVariant { Label = T("Mic off"), Icon = "glyph:EC54", Color = "#FF3D5A" },
            },
            new Tile
            {
                Id = "ses-hoparlor", Kind = "toggle", Source = "mute", X = 4, Y = 0,
                Label = T("Sound on"), Icon = "glyph:E767",
                On = new TileVariant { Label = T("Muted"), Icon = "glyph:E74F", Color = "#FF3D5A" },
            },
            new Tile
            {
                Id = "ses-kayit", Kind = "toggle", Source = "local", X = 5, Y = 0,
                Label = T("Start recording"), Sub = "F13", Icon = "glyph:E7C8", Action = new TileAction { Type = "keys", Keys = "f13" },
                On = new TileVariant { Label = T("Stop recording"), Sub = "F14", Icon = "glyph:E71A", Color = "#FF3D5A", Action = new TileAction { Type = "keys", Keys = "f14" } },
            },
            new Tile { Id = "ses-ana-yatay", Kind = "slider", Target = "master", X = 3, Y = 1, W = 3, Label = T("Main volume (horizontal)") },
            new Tile { Id = "ses-zen", Kind = "slider", Target = "app:zen", X = 3, Y = 2, W = 3, Label = T("Zen browser"), Color = "#4ADE80" },
        }
    };

    public static Profile Create()
    {
        var main = new Page
        {
            Id = "ana", Name = T("Main"), Cols = 5, Rows = 3,
            Tiles =
            {
                Make(0, 0, "Oynat / Duraklat", "E768", Media("playpause"), color: "#FF3D5A"),
                Make(1, 0, T("Previous"), "E892", Media("prev")),
                Make(2, 0, T("Next"), "E893", Media("next")),
                Make(3, 0, T("Volume −"), "E993", Media("voldown")),
                Make(4, 0, T("Volume +"), "E995", Media("volup")),

                Make(0, 1, T("Mute"), "E74F", Media("mute")),
                Make(1, 1, T("Screenshot"), "E722", Keys("ctrl+shift+s"), sub: "Ctrl + Shift + S"),
                Make(2, 1, T("Task Manager"), "E9D9", Keys("ctrl+shift+esc")),
                Make(3, 1, T("Desktop"), "E7F4", Keys("win+d")),
                Make(4, 1, T("Lock"), "E72E", new TileAction { Type = "lock" }),

                Make(0, 2, T("File Explorer"), "E8B7", Run("explorer.exe")),
                Make(1, 2, T("Calculator"), "E8EF", Run("calc.exe")),
                Make(2, 2, T("Shortcuts"), "E8F1", Folder("kisayollar"), w: 2, sub: T("Folder · 6 × 4")),
                Make(4, 2, T("Macro"), "E945", Go("makro"), sub: T("Page 2")),
            }
        };

        var shortcuts = new Page
        {
            Id = "kisayollar", Name = T("Shortcuts"), Cols = 6, Rows = 4, Folder = true,
            Tiles =
            {
                Make(0, 0, T("Back"), "E72B", Back, color: "#3CC8F0"),
                Make(1, 0, T("Copy"), "E8C8", Keys("ctrl+c")),
                Make(2, 0, T("Paste"), "E77F", Keys("ctrl+v")),
                Make(3, 0, T("Cut"), "E8C6", Keys("ctrl+x")),
                Make(4, 0, T("Select all"), "E8B3", Keys("ctrl+a")),
                Make(5, 0, T("Save"), "E74E", Keys("ctrl+s")),

                Make(0, 1, T("Undo"), "E7A7", Keys("ctrl+z")),
                Make(1, 1, T("Redo"), "E7A6", Keys("ctrl+y")),
                Make(2, 1, T("Find"), "E721", Keys("ctrl+f")),
                Make(3, 1, T("New tab"), "E710", Keys("ctrl+t")),
                Make(4, 1, T("Close tab"), "E711", Keys("ctrl+w")),
                Make(5, 1, T("Reopen tab"), "E72C", Keys("ctrl+shift+t")),

                Make(0, 2, "Alt + Tab", "E7C4", Keys("alt+tab")),
                Make(1, 2, T("Task view"), "E7C4", Keys("win+tab")),
                Make(2, 2, T("Clipboard history"), "E77F", Keys("win+v")),
                Make(3, 2, "Emoji", "E76E", Keys("win+.")),
                Make(4, 2, T("Settings"), "E713", Keys("win+i")),
                Make(5, 2, T("Run"), "E756", Keys("win+r")),

                Make(0, 3, T("Zoom in"), "E8A3", Keys("ctrl+=")),
                Make(1, 3, T("Zoom out"), "E71F", Keys("ctrl+-")),
                Make(2, 3, T("Refresh"), "E72C", Keys("f5")),
                Make(3, 3, T("Full screen"), "E740", Keys("f11")),
                Make(4, 3, T("Close window"), "E8BB", Keys("alt+f4")),
                Make(5, 3, T("Explorer"), "E8B7", Keys("win+e")),
            }
        };

        var macro = new Page { Id = "makro", Name = T("Macro"), Cols = 6, Rows = 3 };
        for (var i = 0; i < 12; i++)
            macro.Tiles.Add(Make(i % 6, i / 6, $"F{13 + i}", "E765", Keys($"f{13 + i}"), sub: T("Assign a shortcut")));
        macro.Tiles.Add(Make(0, 2, T("Volume −"), "E993", Media("voldown"), w: 2));
        macro.Tiles.Add(Make(2, 2, T("Mute"), "E74F", Media("mute"), w: 2, color: "#FF3D5A"));
        macro.Tiles.Add(Make(4, 2, T("Volume +"), "E995", Media("volup"), w: 2));

        var p = new Profile { Start = "ana", Pages = { main, macro, Sound(), SystemPage(), shortcuts } };
        p.Validate();
        return p;
    }
}
