using System.Text.Json;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class AppearanceDialog : DarkForm
{
    public event Action<Theme, string?>? Preview;

    public Theme Result { get; private set; }
    public string? PageWallpaper { get; private set; }

    sealed record Preset(string Name, string Bg, string Tile, string Border, string Text, string Sub, string Accent, string Press, int Radius, int Gap);

    static readonly Preset[] Presets =
    {
        new(T("MyDeck night"), "#07090E", "#11151F", "#222938", "#E8ECF4", "#8D97AB", "#FF3D5A", "#FF3D5A", 16, 12),
        new(T("Red neon"), "#0B0306", "#1A070C", "#45101E", "#FFE9EC", "#C48A93", "#FF2E4D", "#FF5C74", 10, 10),
        new(T("Ocean"), "#04121C", "#0A2130", "#16405A", "#E2F4FF", "#7FA6BC", "#2BB3D9", "#45D0F5", 18, 12),
        new(T("Graphite"), "#121316", "#1C1E22", "#2E3137", "#F1F1F2", "#9A9DA5", "#D4D8DE", "#AEB5C0", 6, 8),
        new(T("Forest"), "#06100B", "#0F1F17", "#1F3A2A", "#E6F4EA", "#86A893", "#4ADE80", "#7BEFA4", 20, 14),
        new(T("Light"), "#E9EDF3", "#FFFFFF", "#D3D9E3", "#141821", "#5B6577", "#E8304C", "#E8304C", 14, 12),
    };

    static readonly (string Key, string Name)[] ColorRows =
    {
        ("Background", T("Background")), ("Tile", T("Tile")), ("TileBorder", T("Tile border")), ("Text", T("Text")),
        ("SubText", T("Subtext")), ("Accent", T("Accent (tiles that are on, sliders)")), ("Press", T("Press color")),
    };

    static readonly string Filter = T("Images and videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.mov;*.webm;*.mkv;*.avi;*.m4v|") +
                          T("Image|*.jpg;*.jpeg;*.png;*.bmp|Live (video or GIF)|*.gif;*.mp4;*.mov;*.webm;*.mkv;*.avi;*.m4v");

    readonly Theme _t;
    readonly Dictionary<string, Button> _swatches = new();
    readonly List<Button> _presetButtons = new();
    readonly FlatSlider _radius, _gap, _dim, _glass;
    readonly Label _radiusV, _gapV, _dimV, _glassV, _wallName, _pageName;
    readonly FlatSlider _margin;
    readonly Label _marginV;
    bool _loading;

    public AppearanceDialog(Theme theme, Page page)
    {
        _t = JsonSerializer.Deserialize<Theme>(JsonSerializer.Serialize(theme, Profile.Json), Profile.Json)!;
        Result = _t;
        PageWallpaper = page.Wallpaper;

        Text = T("Appearance");
        BackColor = Ui.Panel;
        ForeColor = Ui.Ink;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 630);

        var left = Ui.Stack();
        left.Location = new Point(20, 6);
        left.Controls.Add(Ui.Caption(T("Themes")));
        var grid = new FlowLayoutPanel { Width = 350, Height = 104, Margin = new Padding(0), BackColor = Color.Transparent };
        foreach (var p in Presets)
        {
            var b = new Button
            {
                Text = p.Name, Width = 110, Height = 44, Margin = new Padding(0, 0, 6, 6), FlatStyle = FlatStyle.Flat,
                BackColor = Ui.ParseColor(p.Tile)!.Value, ForeColor = Ui.ParseColor(p.Text)!.Value, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9f), Tag = p,
            };
            b.FlatAppearance.BorderColor = Ui.ParseColor(p.Accent)!.Value;
            b.FlatAppearance.BorderSize = 2;
            b.Click += (_, _) => ApplyPreset(p);
            _presetButtons.Add(b);
            grid.Controls.Add(b);
        }
        left.Controls.Add(grid);

        left.Controls.Add(Ui.Caption(T("Colors")));
        foreach (var (key, name) in ColorRows)
        {
            var row = Ui.Row();
            row.Margin = new Padding(0, 0, 0, 5);
            var sw = new Button { Width = 44, Height = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), Tag = key };
            sw.FlatAppearance.BorderColor = Ui.Border;
            sw.Click += (_, _) => PickColor(key);
            var lbl = new Label { Text = name, AutoSize = false, Width = 290, Height = 26, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Ink, Cursor = Cursors.Hand };
            lbl.Click += (_, _) => PickColor(key);
            row.Controls.AddRange(new Control[] { sw, lbl });
            _swatches[key] = sw;
            left.Controls.Add(row);
        }
        Controls.Add(left);

        var right = Ui.Stack();
        right.Location = new Point(400, 6);
        right.Controls.Add(Ui.Caption(T("Corner radius")));
        (_radius, _radiusV) = Slider(0, 30, right, " px");
        right.Controls.Add(Ui.Caption(T("Gap between tiles")));
        (_gap, _gapV) = Slider(0, 24, right, " px");
        right.Controls.Add(Ui.Caption(T("Screen edge margin")));
        (_margin, _marginV) = Slider(0, 40, right, " px");

        right.Controls.Add(Ui.Caption(T("Wallpaper · all pages")));
        _wallName = NameLabel(_t.Wallpaper, T("None (solid color)"));
        right.Controls.Add(_wallName);
        var row1 = Ui.Row();
        var pick = Ui.Button(T("Choose file…"), width: 130);
        pick.Click += (_, _) => { if (Pick() is { } f) { _t.Wallpaper = f; _wallName.Text = Describe(f); Changed(); } };
        var clear = Ui.Button(T("Remove"), width: 90);
        clear.Click += (_, _) => { _t.Wallpaper = null; _wallName.Text = T("None (solid color)"); Changed(); };
        row1.Controls.AddRange(new Control[] { pick, clear });
        right.Controls.Add(row1);

        right.Controls.Add(Ui.Caption(T("Wallpaper · {0}", (string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name))));
        _pageName = NameLabel(PageWallpaper, T("Uses the general wallpaper"));
        right.Controls.Add(_pageName);
        var row2 = Ui.Row();
        var ppick = Ui.Button(T("Only for this page…"), width: 150);
        ppick.Click += (_, _) => { if (Pick() is { } f) { PageWallpaper = f; _pageName.Text = Describe(f); Changed(); } };
        var pclear = Ui.Button(T("Use the general one"), width: 110);
        pclear.Click += (_, _) => { PageWallpaper = null; _pageName.Text = T("Uses the general wallpaper"); Changed(); };
        row2.Controls.AddRange(new Control[] { ppick, pclear });
        right.Controls.Add(row2);

        right.Controls.Add(Ui.Caption(T("Dimming")));
        (_dim, _dimV) = Slider(0, 80, right, "", "%");
        right.Controls.Add(Ui.Caption(T("Tile opacity (with a wallpaper)")));
        (_glass, _glassV) = Slider(0, 80, right, "", "%");
        Controls.Add(right);

        var ok = Ui.Button(T("OK"), primary: true, width: 110);
        ok.DialogResult = DialogResult.OK;
        ok.Location = new Point(ClientSize.Width - 230, ClientSize.Height - 48);
        var cancel = Ui.Button(T("Cancel"), width: 100);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Location = new Point(ClientSize.Width - 114, ClientSize.Height - 48);
        var hint = new Label
        {
            Text = T("Changes show in the preview right away. OK sends them to the tablet; Cancel undoes everything."),
            ForeColor = Ui.Muted, Location = new Point(20, ClientSize.Height - 52), Size = new Size(500, 40),
        };
        Controls.AddRange(new Control[] { ok, cancel, hint });
        AcceptButton = ok; CancelButton = cancel;

        Sync();
        _radius.ValueChanged += (_, _) => { if (_loading) return; _t.Radius = _radius.Value; Changed(); };
        _gap.ValueChanged += (_, _) => { if (_loading) return; _t.Gap = _gap.Value; Changed(); };
        _dim.ValueChanged += (_, _) => { if (_loading) return; _t.WallpaperDim = _dim.Value; Changed(); };
        _glass.ValueChanged += (_, _) => { if (_loading) return; _t.CardGlass = _glass.Value; Changed(); };
        _margin.ValueChanged += (_, _) => { if (_loading) return; _t.Margin = _margin.Value; Changed(); };
    }

    void Changed()
    {
        Sync();
        Preview?.Invoke(_t, PageWallpaper);
    }

    void Sync()
    {
        _loading = true;
        foreach (var (key, _) in ColorRows)
        {
            var c = Ui.ParseColor(Get(key)) ?? Color.Black;
            _swatches[key].BackColor = c;
            _swatches[key].FlatAppearance.MouseOverBackColor = c;
        }
        _radius.Value = Math.Clamp(_t.Radius, 0, 30); _radiusV.Text = $"{_radius.Value} px";
        _gap.Value = Math.Clamp(_t.Gap, 0, 24); _gapV.Text = $"{_gap.Value} px";
        _dim.Value = Math.Clamp(_t.WallpaperDim, 0, 80); _dimV.Text = $"%{_dim.Value}";
        _glass.Value = Math.Clamp(_t.CardGlass, 0, 80); _glassV.Text = $"%{_glass.Value}";
        _margin.Value = Math.Clamp(_t.Margin ?? _t.Gap, 0, 40); _marginV.Text = $"{_margin.Value} px";
        foreach (var b in _presetButtons)
        {
            var p = (Preset)b.Tag!;
            var on = Same(p);
            b.FlatAppearance.BorderSize = on ? 3 : 1;
            b.FlatAppearance.BorderColor = on ? Ui.Cyan : Ui.ParseColor(p.Accent)!.Value;
        }
        _loading = false;
    }

    bool Same(Preset p) =>
        Eq(p.Bg, _t.Background) && Eq(p.Tile, _t.Tile) && Eq(p.Border, _t.TileBorder) && Eq(p.Text, _t.Text) &&
        Eq(p.Sub, _t.SubText) && Eq(p.Accent, _t.Accent) && Eq(p.Press, _t.Press) && p.Radius == _t.Radius && p.Gap == _t.Gap;

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    void ApplyPreset(Preset p)
    {
        _t.Background = p.Bg; _t.Tile = p.Tile; _t.TileBorder = p.Border; _t.Text = p.Text; _t.SubText = p.Sub;
        _t.Accent = p.Accent; _t.Press = p.Press; _t.Radius = p.Radius; _t.Gap = p.Gap;
        Changed();
    }

    string Get(string key) => key switch
    {
        "Background" => _t.Background, "Tile" => _t.Tile, "TileBorder" => _t.TileBorder, "Text" => _t.Text,
        "SubText" => _t.SubText, "Accent" => _t.Accent, _ => _t.Press,
    };

    void Set(string key, string v)
    {
        switch (key)
        {
            case "Background": _t.Background = v; break;
            case "Tile": _t.Tile = v; break;
            case "TileBorder": _t.TileBorder = v; break;
            case "Text": _t.Text = v; break;
            case "SubText": _t.SubText = v; break;
            case "Accent": _t.Accent = v; break;
            default: _t.Press = v; break;
        }
    }

    void PickColor(string key)
    {
        using var dlg = new ColorDialog { FullOpen = true, Color = Ui.ParseColor(Get(key)) ?? Color.Black };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Set(key, Ui.Hex(dlg.Color));
        Changed();
    }

    static Label NameLabel(string? path, string empty) => new()
    {
        Text = string.IsNullOrWhiteSpace(path) ? empty : Describe(path),
        ForeColor = Ui.Ink, Width = 340, Height = 22, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 6),
    };

    static string Describe(string path) => $"{(Assets.IsVideo(path) ? T("Live") : T("Image"))} · {Path.GetFileName(path)}";

    (FlatSlider, Label) Slider(int min, int max, Control parent, string suffix, string prefix = "")
    {
        var row = Ui.Row();
        var tb = new FlatSlider { Minimum = min, Maximum = max, Width = 280, Margin = new Padding(0, 4, 0, 4) };
        var lbl = new Label { ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
        row.Controls.AddRange(new Control[] { tb, lbl });
        parent.Controls.Add(row);
        return (tb, lbl);
    }

    string? Pick()
    {
        using var dlg = new OpenFileDialog { Title = T("Choose a wallpaper"), Filter = Filter };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }
}
