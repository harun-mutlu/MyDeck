#pragma warning disable CS8602, CS8604
using System.Text.Json;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class EditorForm : DarkForm
{
    readonly DeckServer _server;
    Profile _profile;
    Page? _page;

    readonly DeckCanvas _canvas;
    readonly PageGrid _pages;
    readonly TextBox _pageName;
    readonly ModernStepper _cols, _rows;
    readonly ModernToggle _startPage, _smartReturn, _folderToggle;
    readonly FlowLayoutPanel _appChips;
    readonly ModernSegmented _gesture, _shape, _tabs;
    readonly Label _pageTitle, _pageSub, _tileTitle, _tileSub, _saveState, _devicePill;
    readonly FlowLayoutPanel[] _tabPages = new FlowLayoutPanel[3];
    readonly Label _kindEmpty;
    readonly ScrollPanel _inspector;
    readonly ModernTextBox _search;
    readonly Label _gridInfo;
    string _gestureKey = "tap";
    readonly Label _status;
    readonly Panel _props;
    readonly Panel _noSelection;

    readonly TextBox _label, _sub;
    readonly Button _iconButton, _colorButton, _colorClear;
    readonly ComboBox _actionType;
    readonly FlowLayoutPanel _actionPanel;
    readonly TextBox _keys, _path, _args, _url;
    readonly Button _recordKeys;
    readonly ComboBox _media, _targetPage;
    readonly ComboBox _kind, _source, _sliderTarget;
    readonly FlowLayoutPanel _toggleBox, _sliderBox, _looks;
    readonly Button _offTab, _onTab;
    readonly Label _actionCaption, _systemHint;

    readonly FlowLayoutPanel _liveBox, _presetRow, _metricRow, _metric2Row, _infoRow, _styleRow, _colorModeRow, _color2Row;
    readonly ComboBox _preset, _metric, _metric2, _style, _seconds, _colorMode;
    readonly ComboBox[] _info = new ComboBox[4];
    readonly Button _color2, _color2Clear;

    readonly FlowLayoutPanel _gifBox;
    readonly Button _gifPick, _gifClear;
    readonly ComboBox _gifFill, _gifPlay;
    static readonly (string Key, string Name)[] GifFills = { ("bg", T("Tile background (text on top)")), ("icon", T("Instead of the icon")) };
    static readonly (string Key, string Name)[] GifPlays = { ("always", T("Always play")), ("press", T("Play while pressed")), ("once", T("Play once when pressed")) };

    readonly FlowLayoutPanel _mediaBox;
    readonly ComboBox _mediaApp;
    readonly ModernToggle _mediaTint, _mediaControls, _mediaTimes;

    readonly FlowLayoutPanel _clockBox, _timerBox, _weatherBox;
    readonly ComboBox _clockStyle, _clockZone, _timerMode, _timerPreset, _weatherView;
    readonly ModernToggle _clockSec, _clockDate, _timerAnnounce;
    readonly ModernStepper _timerMin, _timerSec;
    readonly FlowLayoutPanel _timerLength;

    readonly FlowLayoutPanel _pluginBox, _pluginFields, _pluginActFields;
    readonly ComboBox _pluginData, _pluginAct;
    sealed record PluginChoice(string Plugin, PluginItem Item, string Name)
    {
        public override string ToString() => Name;
    }
    static readonly (string Key, string Name)[] ClockStyles = { ("digital", T("Digital")), ("analog", T("Analog")) };
    static readonly (string Zone, string City, string Name)[] Zones =
    {
        ("", "", T("This tablet's time")), ("Europe/Istanbul", T("Istanbul"), T("Istanbul")), ("Europe/London", T("London"), T("London")),
        ("Europe/Berlin", "Berlin", "Berlin"), ("Europe/Paris", "Paris", "Paris"), ("Europe/Moscow", T("Moscow"), T("Moscow")),
        ("Asia/Dubai", "Dubai", "Dubai"), ("Asia/Shanghai", T("Beijing"), T("Beijing")), ("Asia/Seoul", T("Seoul"), T("Seoul")),
        ("Asia/Tokyo", "Tokyo", "Tokyo"), ("Australia/Sydney", T("Sydney"), T("Sydney")), ("America/New_York", "New York", "New York"),
        ("America/Chicago", "Chicago", "Chicago"), ("America/Los_Angeles", "Los Angeles", "Los Angeles"),
    };
    static readonly (string Key, string Name)[] TimerModes = { ("countdown", T("Countdown")), ("stopwatch", T("Stopwatch")) };
    static readonly (string Name, int Seconds)[] TimerPresets =
    {
        (T("Tea"), 180), (T("Egg"), 420), ("Pomodoro", 1500), (T("Break"), 300), (T("Brew"), 900), (T("Pasta"), 600), (T("Laundry"), 3600),
    };
    static readonly (string Key, string Name)[] WeatherViews =
        { ("now", T("Now")), ("hours", T("Now + hourly (2×2 and larger)")), ("tomorrow", T("Tomorrow")) };
    readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 1000 };
    bool _editOn;
    string? _shownTileId;

    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 250 };
    readonly Stack<string> _undo = new(), _redo = new();
    string _lastUndoKey = "";
    DateTime _lastUndoAt;
    bool _loading, _recording;
    Tile? _clipboard;

    static readonly (string Type, string Name)[] ActionTypes =
    {
        ("", T("None")), ("keys", T("Keyboard shortcut")), ("media", T("Media and volume")), ("run", T("Open a program or file")),
        ("url", T("Open a web address")), ("lock", T("Lock the screen")), ("page", T("Go to page")), ("folder", T("Open a folder")), ("back", T("Go back")),
        ("plugin", T("Plugin")), ("multi", T("Several actions (in order)")), ("setvar", T("Set a variable")), ("if", T("Condition (if…)")),
    };

    static readonly (string Kind, string Name)[] Kinds =
    {
        ("button", T("Button")), ("toggle", T("On / off")), ("slider", T("Slider")),
        ("graph", T("Live graph (CPU, GPU, RAM…)")), ("ring", T("Ring gauge")), ("gauge", T("Half-circle gauge")),
        ("net", T("Network graph")), ("procs", T("Top CPU users")), ("media", T("Now playing")),
        ("clock", T("Clock and date")), ("timer", T("Timer / stopwatch")), ("weather", T("Weather")),
        ("plugin", T("Plugin data tile")),
    };

    sealed record Preset(string Name, string Label, string Metric, string? Metric2, string[] Info);

    static readonly Preset[] Presets =
    {
        new("CPU", "CPU", "cpu", "cpu.temp", new[] { "cpu.clock", "cpu.temp", "cpu.power", "cpu.procs" }),
        new(T("CPU temperature"), T("CPU temperature"), "cpu.temp", null, new[] { "cpu.hotcore", "cpu.power" }),
        new(T("CPU power"), T("CPU power"), "cpu.power", null, new[] { "cpu.temp", "cpu.clock" }),
        new("GPU", "GPU", "gpu", "gpu.temp", new[] { "gpu.vram", "gpu.clock", "gpu.temp", "gpu.power" }),
        new("RAM", "RAM", "ram", null, new[] { "ram.used", "ram.free" }),
        new("RAM + Disk", "RAM · Disk", "ram", "disk", new[] { "disk", "disk.total" }),
        new("Disk", "Disk", "disk", null, new[] { "disk.read", "disk.write" }),
        new("VRAM", "VRAM", "gpu.vrampct", null, new[] { "gpu.vram" }),
        new(T("GPU power"), T("GPU power"), "gpu.power", null, new[] { "gpu.temp", "gpu.clock" }),
        new(T("GPU temperature"), T("GPU temperature"), "gpu.temp", null, new[] { "gpu.fan", "gpu.power" }),
    };

    static readonly (string Key, string Name)[] Styles = { ("area", T("Area")), ("line", T("Line")), ("bars", T("Bars")) };
    static readonly (int Sec, string Name)[] Durations = { (30, T("30 seconds")), (60, T("1 minute")), (300, T("5 minutes")) };
    static readonly (string Key, string Name)[] ColorModes = { ("card", T("Tile color")), ("load", T("By load (green → yellow → red)")) };

    sealed record MetricItem(string? Key, string Name)
    {
        public override string ToString() => Name;
    }

    static readonly (string Source, string Name)[] Sources =
    {
        ("local", T("MyDeck remembers (two actions)")), ("mute", T("Speaker muted (Windows)")), ("micmute", T("Mic muted (Windows)")),
    };

    static readonly (string Key, string Name)[] MediaKeys =
    {
        ("playpause", T("Play / Pause")), ("next", T("Next")), ("prev", T("Previous")), ("stop", T("Stop")),
        ("volup", T("Volume +")), ("voldown", T("Volume −")), ("mute", T("Mute")),
    };

    public EditorForm(DeckServer server)
    {
        _server = server;
        _profile = Profile.Load(server.ProfilePath);

        Text = T("MyDeck · Editor");
        BackColor = Ui.Bg;
        ForeColor = Ui.Ink;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(1380, 760);
        MinimumSize = new Size(1100, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var left = new ScrollPanel { Dock = DockStyle.Left, Width = 268, BackColor = Ui.Panel, Inset = new Padding(16, 14, 0, 14) };
        var leftStack = left.Content;

        var pagesHead = new Panel { Width = 236, Height = 30, BackColor = Ui.Panel, Margin = new Padding(0, 0, 0, 6) };
        pagesHead.Controls.Add(new Label { Text = T("Pages"), Font = Ui.Strong, ForeColor = Ui.Ink2, AutoSize = true, Location = new Point(0, 6) });
        var addPageBtn = ModernButton.Icon(Fx.I.Add, T("Add a page or folder"), size: 30);
        addPageBtn.Location = new Point(206, 0);
        addPageBtn.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Items.Add(T("New page"), null, (_, _) => AddPage(folder: false));
            m.Items.Add(T("New folder"), null, (_, _) => AddPage(folder: true));
            m.Show(addPageBtn, new Point(0, addPageBtn.Height));
        };
        pagesHead.Controls.Add(addPageBtn);
        leftStack.Controls.Add(pagesHead);

        _pages = new PageGrid { Width = 236 };
        _pages.Bind(() => _profile.Pages, pg => pg.Id == _profile.Start);
        _pages.SelectedIndexChanged += (_, _) => { if (!_loading && _pages.SelectedIndex >= 0 && _pages.SelectedIndex < _profile.Pages.Count) ShowPage(_profile.Pages[_pages.SelectedIndex]); };
        _pages.ContextMenuAt += (i, pt) =>
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Items.Add(T("Move up"), null, (_, _) => MovePage(-1)).Enabled = i > 0;
            m.Items.Add(T("Move down"), null, (_, _) => MovePage(1)).Enabled = i < _profile.Pages.Count - 1;
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T("Share this page…"), null, (_, _) => SharePage());
            m.Items.Add(T("Delete page"), null, (_, _) => DeletePage());
            m.Show(_pages, pt);
        };
        leftStack.Controls.Add(_pages);

        var pageCard = new CardStack { Width = 236, AutoSize = true };
        pageCard.Controls.Add(Ui.Caption(T("Page name")));
        _pageName = Ui.TextBox(); _pageName.Width = 210;
        _pageName.TextChanged += (_, _) => { if (_loading || _page is null) return; PushUndo("pageName"); _page.Name = _pageName.Text; _pages.Invalidate(); UpdatePageHeader(); ScheduleSave(); };
        pageCard.Controls.Add(_pageName);

        pageCard.Controls.Add(Ui.Caption(T("Grid · columns × rows")));
        var gridRow = Ui.Row();
        _cols = new ModernStepper { Minimum = 1, Maximum = 15, Width = 96 };
        _rows = new ModernStepper { Minimum = 1, Maximum = 15, Width = 96 };
        _cols.ValueChanged += (_, _) => ResizeGrid();
        _rows.ValueChanged += (_, _) => ResizeGrid();
        gridRow.Controls.AddRange(new Control[] { _cols, new Label { Text = "×", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(6, 9, 6, 0) }, _rows });
        pageCard.Controls.Add(gridRow);

        pageCard.Controls.Add(Ui.Caption(T("Tile shape")));
        _shape = new ModernSegmented { Width = 210 };
        _shape.Items = new[] { T("Fill"), T("Square") };
        _shape.Glyphs = new[] { Fx.I.Expand, Fx.I.Square };
        _shape.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _page is null) return;
            PushUndo("shape");
            _page.Shape = _shape.SelectedIndex == 1 ? "square" : null;
            UpdateGridInfo();
            _canvas.Invalidate();
            ScheduleSave();
        };
        pageCard.Controls.Add(_shape);
        _gridInfo = new Label { ForeColor = Ui.Muted, AutoSize = false, Width = 210, Height = 36, Margin = new Padding(0, 2, 0, 4), Font = Ui.Small };
        pageCard.Controls.Add(_gridInfo);

        _startPage = new ModernToggle { Text = T("Start page"), Width = 210 };
        _startPage.CheckedChanged += (_, _) =>
        {
            if (_loading || _page is null) return;
            if (!_startPage.Checked) { _loading = true; _startPage.Checked = _profile.Start == _page.Id; _loading = false; return; }
            if (_page.Folder) { _loading = true; _startPage.Checked = false; _loading = false; SetStatus(T("A folder can't be the start page"), Ui.Muted); return; }
            PushUndo("start"); _profile.Start = _page.Id; _pages.Invalidate(); ScheduleSave();
        };
        pageCard.Controls.Add(_startPage);
        _folderToggle = new ModernToggle { Text = T("Folder page"), Width = 210 };
        ModernButton.SharedTip.SetToolTip(_folderToggle, T("Folders are skipped when swiping; they open from a tile."));
        _folderToggle.CheckedChanged += (_, _) =>
        {
            if (_loading || _page is null) return;
            if (_folderToggle.Checked && _profile.Start == _page.Id)
            {
                _loading = true; _folderToggle.Checked = false; _loading = false;
                SetStatus(T("The start page can't be a folder. Make another page the start page first"), Ui.Muted);
                return;
            }
            if (_folderToggle.Checked && _profile.Pages.Count(x => !x.Folder) <= 1)
            {
                _loading = true; _folderToggle.Checked = false; _loading = false;
                SetStatus(T("At least one normal page must remain"), Ui.Muted);
                return;
            }
            PushUndo("folder"); _page.Folder = _folderToggle.Checked; _startPage.Enabled = !_page.Folder; _pages.Invalidate(); ScheduleSave();
        };
        pageCard.Controls.Add(_folderToggle);
        leftStack.Controls.Add(pageCard);

        var smartCard = new CardStack { Width = 236, AutoSize = true };
        smartCard.Controls.Add(Ui.Caption(T("Smart profile · open in these apps")));
        _appChips = new FlowLayoutPanel { Width = 212, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Ui.Card, Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(212, 0) };
        smartCard.Controls.Add(_appChips);
        _smartReturn = new ModernToggle { Text = T("Go back to the previous page when I leave"), Width = 210 };
        _smartReturn.CheckedChanged += (_, _) => { if (_loading) return; PushUndo("smartReturn"); _profile.SmartReturn = _smartReturn.Checked; ScheduleSave(); };
        smartCard.Controls.Add(_smartReturn);
        leftStack.Controls.Add(smartCard);

        leftStack.Controls.Add(new Label
        {
            Text = T("Right-click a page to reorder or delete it."),
            ForeColor = Ui.Muted, Width = 236, Height = 48, Margin = new Padding(0, 2, 0, 0), Font = Ui.Small,
        });

        var right = new Panel { Dock = DockStyle.Right, Width = 316, BackColor = Ui.Panel };
        _props = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Panel };
        var p = Ui.Stack();
        p.Controls.Add(Ui.Caption(T("Tile type")));
        _kind = Ui.Combo();
        foreach (var (_, name) in Kinds) _kind.Items.Add(name);
        _kind.SelectedIndexChanged += (_, _) => ChangeKind();
        p.Controls.Add(_kind);

        _toggleBox = Ui.Stack();
        _toggleBox.Controls.Add(Ui.Caption(T("Where the state comes from")));
        _source = Ui.Combo();
        foreach (var (_, name) in Sources) _source.Items.Add(name);
        _source.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _canvas.Selected is not { } t || _source.SelectedIndex < 0) return;
            PushUndo("source");
            t.Source = Sources[_source.SelectedIndex].Source;
            ApplyKindVisibility(t);
            _canvas.Invalidate();
            ScheduleSave();
        };
        _toggleBox.Controls.Add(_source);
        _toggleBox.Controls.Add(Ui.Caption(T("Editing state")));
        var tabs = Ui.Row();
        _offTab = Ui.Button(T("Off state"), width: 127);
        _onTab = Ui.Button(T("On state"), width: 127);
        _offTab.Click += (_, _) => SetEditOn(false);
        _onTab.Click += (_, _) => SetEditOn(true);
        tabs.Controls.AddRange(new Control[] { _offTab, _onTab });
        _toggleBox.Controls.Add(tabs);
        p.Controls.Add(_toggleBox);

        _sliderBox = Ui.Stack();
        _sliderBox.Controls.Add(Ui.Caption(T("Which audio")));
        _sliderTarget = Ui.Combo();
        _sliderTarget.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _canvas.Selected is not { } t || _sliderTarget.SelectedItem is not TargetItem item) return;
            PushUndo("sliderTarget");
            t.Target = item.Target;
            if (IsDefaultLabel(t.Label, "New tile", "Yeni kart", "Main volume", "Ana ses", "Microphone", "Mikrofon") || (t.Label?.Length > 0 && SliderTargets().Any(x => x.Name == t.Label)))
            {
                t.Label = item.Name;
                _loading = true; _label.Text = item.Name; _loading = false;
            }
            _canvas.Invalidate();
            ScheduleSave();
        };
        var refresh = Ui.Button(T("Refresh list"), width: 260);
        refresh.Click += (_, _) => { if (_canvas.Selected is { } t) FillSliderTargets(t); };
        _sliderBox.Controls.AddRange(new Control[] { _sliderTarget, refresh,
            Hint(T("Apps are listed from the ones playing sound right now. The slider shows '—' while the app is closed.")) });
        p.Controls.Add(_sliderBox);

        _liveBox = Ui.Stack();
        _presetRow = Ui.Stack();
        _presetRow.Controls.Add(Ui.Caption(T("Preset")));
        _preset = Ui.Combo();
        _preset.Items.Add(T("Choose… (fills in the settings)"));
        foreach (var pr in Presets) _preset.Items.Add(pr.Name);
        _preset.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _preset.SelectedIndex <= 0 || _canvas.Selected is not { } t) return;
            PushUndo("preset");
            ApplyPreset(t, Presets[_preset.SelectedIndex - 1]);
            ShowSelection();
            _canvas.Invalidate();
            ScheduleSave();
        };
        _presetRow.Controls.Add(_preset);
        _liveBox.Controls.Add(_presetRow);

        _metricRow = Ui.Stack();
        _metricRow.Controls.Add(Ui.Caption(T("Main metric")));
        _metric = MetricCombo(includeNone: false, includeText: false);
        _metric.SelectedIndexChanged += (_, _) =>
        {
            if (_metric.SelectedItem is MetricItem m) Edit("live.metric", t => L(t).Metric = m.Key);
        };
        _metricRow.Controls.Add(_metric);
        _liveBox.Controls.Add(_metricRow);

        _metric2Row = Ui.Stack();
        _metric2Row.Controls.Add(Ui.Caption(T("Second metric (dashed)")));
        _metric2 = MetricCombo(includeNone: true, includeText: false);
        _metric2.SelectedIndexChanged += (_, _) =>
        {
            if (_metric2.SelectedItem is MetricItem m) Edit("live.metric2", t => { L(t).Metric2 = m.Key; ApplyKindVisibility(t); });
        };
        _metric2Row.Controls.Add(_metric2);
        _liveBox.Controls.Add(_metric2Row);

        _infoRow = Ui.Stack();
        _infoRow.Controls.Add(Ui.Caption(T("Info shown on the tile")));
        for (var i = 0; i < _info.Length; i++)
        {
            var cb = _info[i] = MetricCombo(includeNone: true, includeText: true);
            cb.Margin = new Padding(0, 0, 0, 4);
            cb.SelectedIndexChanged += (_, _) => Edit("live.info", t =>
                L(t).Info = _info.Select(x => (x.SelectedItem as MetricItem)?.Key).OfType<string>().ToList());
            _infoRow.Controls.Add(cb);
        }
        _infoRow.Controls.Add(Hint(T("Shown side by side on large tiles, stacked on small ones.")));
        _liveBox.Controls.Add(_infoRow);

        _styleRow = Ui.Stack();
        _styleRow.Controls.Add(Ui.Caption(T("Graph type and period")));
        var styleLine = Ui.Row();
        _style = Ui.Combo(); _style.Width = 127; _style.Margin = new Padding(0, 0, 6, 0);
        foreach (var (_, name) in Styles) _style.Items.Add(name);
        _style.SelectedIndexChanged += (_, _) => { if (_style.SelectedIndex >= 0) Edit("live.style", t => L(t).Style = Styles[_style.SelectedIndex].Key); };
        _seconds = Ui.Combo(); _seconds.Width = 127;
        foreach (var (_, name) in Durations) _seconds.Items.Add(name);
        _seconds.SelectedIndexChanged += (_, _) => { if (_seconds.SelectedIndex >= 0) Edit("live.sec", t => L(t).Seconds = Durations[_seconds.SelectedIndex].Sec); };
        styleLine.Controls.AddRange(new Control[] { _style, _seconds });
        _styleRow.Controls.Add(styleLine);
        _liveBox.Controls.Add(_styleRow);

        _colorModeRow = Ui.Stack();
        _colorModeRow.Controls.Add(Ui.Caption(T("Color")));
        _colorMode = Ui.Combo();
        foreach (var (_, name) in ColorModes) _colorMode.Items.Add(name);
        _colorMode.SelectedIndexChanged += (_, _) => { if (_colorMode.SelectedIndex >= 0) Edit("live.cm", t => L(t).ColorMode = ColorModes[_colorMode.SelectedIndex].Key); };
        _colorModeRow.Controls.Add(_colorMode);
        _liveBox.Controls.Add(_colorModeRow);

        _color2Row = Ui.Stack();
        _color2Row.Controls.Add(Ui.Caption(T("Second line color")));
        var c2Line = Ui.Row();
        _color2 = Ui.Button(T("Automatic"), width: 120); _color2.Height = 34;
        _color2.Click += (_, _) => PickColor2();
        _color2Clear = Ui.Button("×", width: 40); _color2Clear.Height = 34;
        _color2Clear.Click += (_, _) => Edit("live.color2", t => L(t).Color2 = null, refresh: true);
        c2Line.Controls.AddRange(new Control[] { _color2, _color2Clear });
        _color2Row.Controls.Add(c2Line);
        _liveBox.Controls.Add(_color2Row);

        _mediaBox = Ui.Stack();
        _mediaBox.Controls.Add(Ui.Caption(T("Which app")));
        _mediaApp = Ui.Combo();
        _mediaApp.SelectedIndexChanged += (_, _) =>
        {
            if (_mediaApp.SelectedItem is MetricItem m) Edit("media.app", t => M(t).App = m.Key);
        };
        var mediaRefresh = Ui.Button(T("Refresh list"), width: 260);
        mediaRefresh.Click += (_, _) => { if (_canvas.Selected is { } t) FillMediaApps(t); };
        ModernToggle Check(string text, string key, Action<MediaSpec, bool> set)
        {
            var cb = new ModernToggle { Text = text, Width = 262, Margin = new Padding(0, 6, 0, 0) };
            cb.CheckedChanged += (_, _) => Edit(key, t => set(M(t), cb.Checked));
            return cb;
        }
        _mediaTint = Check(T("Take the color from the cover"), "media.tint", (m, v) => m.FollowArt = v ? null : false);
        _mediaControls = Check(T("Previous / play / next buttons"), "media.ctl", (m, v) => m.Controls = v ? null : false);
        _mediaTimes = Check(T("Elapsed and remaining time"), "media.times", (m, v) => m.Times = v ? null : false);
        var mediaHint = Hint(T("Automatic means the app playing right now."));
        mediaHint.Height = 76;
        _mediaBox.Controls.AddRange(new Control[] { _mediaApp, mediaRefresh, _mediaTint, _mediaControls, _mediaTimes, mediaHint });

        _liveBox.Controls.Add(Hint(T("If the subtext is empty, the hardware name is shown.")));
        p.Controls.Add(_liveBox);
        p.Controls.Add(_mediaBox);

        ModernToggle Box(string text, Action<bool> set)
        {
            var cb = new ModernToggle { Text = text, Width = 262, Margin = new Padding(0, 6, 0, 0) };
            cb.CheckedChanged += (_, _) => set(cb.Checked);
            return cb;
        }

        _clockBox = Ui.Stack();
        _clockBox.Controls.Add(Ui.Caption(T("Style")));
        _clockStyle = Ui.Combo();
        foreach (var (_, n) in ClockStyles) _clockStyle.Items.Add(n);
        _clockStyle.SelectedIndexChanged += (_, _) => { if (_clockStyle.SelectedIndex >= 0) Edit("clock.style", t => C(t).Style = ClockStyles[_clockStyle.SelectedIndex].Key); };
        _clockBox.Controls.Add(_clockStyle);
        _clockBox.Controls.Add(Ui.Caption(T("City")));
        _clockZone = Ui.Combo();
        foreach (var z in Zones) _clockZone.Items.Add(z.Name);
        _clockZone.SelectedIndexChanged += (_, _) =>
        {
            if (_clockZone.SelectedIndex < 0) return;
            var z = Zones[_clockZone.SelectedIndex];
            Edit("clock.zone", t => { C(t).Zone = z.Zone.Length == 0 ? null : z.Zone; C(t).City = z.City.Length == 0 ? null : z.City; });
        };
        _clockBox.Controls.Add(_clockZone);
        _clockSec = Box(T("Seconds"), v => Edit("clock.sec", t => C(t).Seconds = v ? null : false));
        _clockDate = Box(T("Date"), v => Edit("clock.date", t => C(t).Date = v ? null : false));
        _clockBox.Controls.AddRange(new Control[] { _clockSec, _clockDate,
            Hint(T("The tablet keeps the time, even while the computer is off.")) });
        p.Controls.Add(_clockBox);

        _timerBox = Ui.Stack();
        _timerBox.Controls.Add(Ui.Caption(T("Type")));
        _timerMode = Ui.Combo();
        foreach (var (_, n) in TimerModes) _timerMode.Items.Add(n);
        _timerMode.SelectedIndexChanged += (_, _) =>
        {
            if (_timerMode.SelectedIndex < 0) return;
            Edit("timer.mode", t =>
            {
                var m = TimerModes[_timerMode.SelectedIndex].Key;
                Tm(t).Mode = m == "countdown" ? null : m;
                if (IsDefaultLabel(t.Label, "Timer", "Zamanlayıcı", "Stopwatch", T("Stopwatch"))) t.Label = m == "stopwatch" ? T("Stopwatch") : T("Timer");
            }, refresh: true);
            _loading = true; _label.Text = _canvas.Selected?.Label ?? ""; _loading = false;
        };
        _timerBox.Controls.Add(_timerMode);
        _timerLength = Ui.Stack();
        _timerLength.Controls.Add(Ui.Caption(T("Preset time")));
        _timerPreset = Ui.Combo();
        _timerPreset.Items.Add(T("Choose…"));
        foreach (var (n, sec) in TimerPresets) _timerPreset.Items.Add($"{n} · {(sec >= 3600 ? T("{0} h", sec / 3600) : T("{0} min", sec / 60))}");
        _timerPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _timerPreset.SelectedIndex <= 0) return;
            var (n, sec) = TimerPresets[_timerPreset.SelectedIndex - 1];
            Edit("timer.preset", t => { Tm(t).Seconds = sec; t.Label = n; });
            _loading = true;
            _label.Text = n; _timerMin.Value = sec / 60; _timerSec.Value = sec % 60; _timerPreset.SelectedIndex = 0;
            _loading = false;
        };
        _timerLength.Controls.Add(_timerPreset);
        _timerLength.Controls.Add(Ui.Caption(T("Length (minutes · seconds)")));
        var len = Ui.Row();
        _timerMin = Ui.Number(0, 1440); _timerMin.Width = 116;
        _timerSec = Ui.Number(0, 59); _timerSec.Width = 104;
        void SetLen() => Edit("timer.len", t => Tm(t).Seconds = Math.Max(5, (int)_timerMin.Value * 60 + (int)_timerSec.Value));
        _timerMin.ValueChanged += (_, _) => SetLen();
        _timerSec.ValueChanged += (_, _) => SetLen();
        len.Controls.AddRange(new Control[] { _timerMin, new Label { Text = T("min"), ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(4, 8, 12, 0) },
            _timerSec, new Label { Text = T("sec"), ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(4, 8, 0, 0) } });
        _timerLength.Controls.Add(len);
        _timerAnnounce = Box(T("Read it out when it ends"), v => Edit("timer.say", t => Tm(t).Announce = v ? null : false));
        _timerBox.Controls.Add(_timerLength);
        var timerHint = Hint(T("Tap the middle to start or pause. +1 adds a minute, ↺ resets."));
        timerHint.Height = 62;
        _timerBox.Controls.Add(timerHint);
        p.Controls.Add(_timerBox);

        _weatherBox = Ui.Stack();
        _weatherBox.Controls.Add(Ui.Caption(T("What to show")));
        _weatherView = Ui.Combo();
        foreach (var (_, n) in WeatherViews) _weatherView.Items.Add(n);
        _weatherView.SelectedIndexChanged += (_, _) => { if (_weatherView.SelectedIndex >= 0) Edit("wx.view", t => W(t).View = WeatherViews[_weatherView.SelectedIndex].Key); };
        _weatherBox.Controls.Add(_weatherView);
        var wxHint = Hint(T("Pick the city in MyDeck settings. If the text is empty, the city name is shown."));
        _weatherBox.Controls.Add(wxHint);
        p.Controls.Add(_weatherBox);

        _pluginBox = Ui.Stack();
        _pluginBox.Controls.Add(Ui.Caption(T("Which data")));
        _pluginData = Ui.Combo();
        _pluginData.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _pluginData.SelectedItem is not PluginChoice ch) return;
            Edit("plugin.data", t =>
            {
                var sp = t.Plugin ??= new PluginSpec();
                if (sp.Plugin != ch.Plugin || sp.Item != ch.Item.Id) sp.Args = null;
                sp.Plugin = ch.Plugin; sp.Item = ch.Item.Id;
                if (IsDefaultLabel(t.Label, "New tile", "Yeni kart") || PluginLabels().Contains(t.Label)) t.Label = ch.Item.Name;
            });
            if (_canvas.Selected is { } st) { _loading = true; _label.Text = st.Label ?? ""; BuildPluginFields(_pluginFields, ch.Item, st.Plugin!); _loading = false; }
        };
        _pluginBox.Controls.Add(_pluginData);
        _pluginFields = Ui.Stack();
        _pluginBox.Controls.Add(_pluginFields);
        var pluginHint = Hint(T("Manage plugins with the Plugins button in the top bar."));
        pluginHint.Height = 62;
        _pluginBox.Controls.Add(pluginHint);
        p.Controls.Add(_pluginBox);

        p.Controls.Add(Ui.Caption(T("Text")));
        _label = Ui.TextBox(); _label.TextChanged += (_, _) => Edit("label", t => Set(t, "label", Empty(_label.Text)));
        p.Controls.Add(_label);
        p.Controls.Add(Ui.Caption(T("Subtext")));
        _sub = Ui.TextBox(); _sub.TextChanged += (_, _) => Edit("sub", t => Set(t, "sub", Empty(_sub.Text)));
        p.Controls.Add(_sub);

        p.Controls.Add(Ui.Caption(T("Icon and color")));
        var looks = _looks = Ui.Row();
        _iconButton = Ui.Button(T("Choose icon…"), width: 120);
        _iconButton.ImageAlign = ContentAlignment.MiddleLeft;
        _iconButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _iconButton.Height = 40;
        _iconButton.Click += (_, _) => PickIcon();
        _colorButton = Ui.Button(T("Color"), width: 80); _colorButton.Height = 40;
        _colorButton.Click += (_, _) => PickColor();
        _colorClear = Ui.Button("×", width: 40); _colorClear.Height = 40;
        _colorClear.Click += (_, _) => Edit("color", t => Set(t, "color", null), refresh: true);
        looks.Controls.AddRange(new Control[] { _iconButton, _colorButton, _colorClear });
        p.Controls.Add(looks);

        _gifBox = Ui.Stack();
        _gifBox.Controls.Add(Ui.Caption(T("Animated GIF")));
        var gifRow = Ui.Row();
        _gifPick = Ui.Button(T("Choose GIF…"), width: 214);
        _gifPick.Click += (_, _) => PickGif();
        _gifClear = Ui.Button("×", width: 40);
        _gifClear.Click += (_, _) => Edit("gif", t => t.Gif = null, refresh: true);
        gifRow.Controls.AddRange(new Control[] { _gifPick, _gifClear });
        _gifBox.Controls.Add(gifRow);
        _gifFill = Ui.Combo();
        foreach (var (_, n) in GifFills) _gifFill.Items.Add(n);
        _gifFill.SelectedIndexChanged += (_, _) => { if (_gifFill.SelectedIndex >= 0) Edit("gif.fill", t => { if (t.Gif is not null) t.Gif.Fill = GifFills[_gifFill.SelectedIndex].Key; }); };
        _gifPlay = Ui.Combo();
        _gifPlay.Margin = new Padding(0, 4, 0, 0);
        foreach (var (_, n) in GifPlays) _gifPlay.Items.Add(n);
        _gifPlay.SelectedIndexChanged += (_, _) => { if (_gifPlay.SelectedIndex >= 0) Edit("gif.play", t => { if (t.Gif is not null) t.Gif.Play = GifPlays[_gifPlay.SelectedIndex].Key; }); };
        _gifBox.Controls.AddRange(new Control[] { _gifFill, _gifPlay });
        p.Controls.Add(_gifBox);

        _actionCaption = Ui.Caption(T("When pressed"));
        p.Controls.Add(_actionCaption);
        _gesture = new ModernSegmented { Width = 260, Margin = new Padding(0, 0, 0, 8) };
        _gesture.Items = new[] { T("Tap"), T("Hold"), T("Double tap") };
        _gesture.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _canvas.Selected is not { } st) return;
            _gestureKey = _gesture.SelectedIndex switch { 1 => "hold", 2 => "double", _ => "tap" };
            _loading = true;
            var ty = VAction(st)?.Type ?? "";
            _actionType.SelectedIndex = Math.Max(0, Array.FindIndex(ActionTypes, a => a.Type == ty));
            BuildActionPanel(st);
            ApplyKindVisibility(st);
            _loading = false;
        };
        p.Controls.Add(_gesture);
        _actionType = Ui.Combo();
        foreach (var (_, name) in ActionTypes) _actionType.Items.Add(name);
        _actionType.SelectedIndexChanged += (_, _) => ChangeActionType();
        p.Controls.Add(_actionType);

        _actionPanel = Ui.Stack();
        _actionPanel.Margin = new Padding(0, 6, 0, 0);
        _systemHint = Hint(T("Pressing it changes the setting in Windows. If you change it elsewhere, the tablet updates too."));

        _keys = Ui.TextBox();
        _keys.PlaceholderText = T("e.g. ctrl+shift+s");
        _keys.TextChanged += (_, _) => { if (!_recording) Edit("keys", t => Act(t).Keys = _keys.Text.Trim().ToLowerInvariant()); };
        _keys.KeyDown += RecordKey;
        _recordKeys = Ui.Button(T("Record by pressing keys"), width: 260);
        _recordKeys.Click += (_, _) => ToggleRecord();

        _media = Ui.Combo();
        foreach (var (_, name) in MediaKeys) _media.Items.Add(name);
        _media.SelectedIndexChanged += (_, _) => { if (_media.SelectedIndex >= 0) Edit("media", t => Act(t).Key = MediaKeys[_media.SelectedIndex].Key); };

        _path = Ui.TextBox(); _path.PlaceholderText = T("program, file or folder path");
        _path.TextChanged += (_, _) => Edit("path", t => Act(t).Path = Empty(_path.Text));
        _args = Ui.TextBox(); _args.PlaceholderText = T("arguments (optional)");
        _args.TextChanged += (_, _) => Edit("args", t => Act(t).Args = Empty(_args.Text));
        _pluginAct = Ui.Combo();
        _pluginAct.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _pluginAct.SelectedItem is not PluginChoice ch) return;
            Edit("plugin.act", t =>
            {
                var sp = Act(t).Plugin ??= new PluginSpec();
                if (sp.Plugin != ch.Plugin || sp.Item != ch.Item.Id) sp.Args = null;
                sp.Plugin = ch.Plugin; sp.Item = ch.Item.Id;
            });
            if (_canvas.Selected is { } st && VAction(st)?.Plugin is { } sp2) { _loading = true; BuildPluginFields(_pluginActFields, ch.Item, sp2); _loading = false; }
        };
        _pluginActFields = Ui.Stack();
        _url = Ui.TextBox(); _url.PlaceholderText = "https://…";
        _url.TextChanged += (_, _) => Edit("url", t => Act(t).Url = Empty(_url.Text));
        _targetPage = Ui.Combo();
        _targetPage.SelectedIndexChanged += (_, _) =>
        {
            if (_targetPage.SelectedIndex >= 0) Edit("target", t => Act(t).Page = _profile.Pages[_targetPage.SelectedIndex].Id, refresh: true);
        };
        p.Controls.Add(_actionPanel);
        p.Controls.Add(_systemHint);

        var actionSet = new HashSet<Control> { _actionCaption, _gesture, _actionType, _actionPanel, _systemHint };
        var kindSet = new HashSet<Control> { _sliderBox, _liveBox, _mediaBox, _clockBox, _timerBox, _weatherBox, _pluginBox };
        for (var i = 0; i < 3; i++) _tabPages[i] = Ui.Stack();
        foreach (var c in p.Controls.Cast<Control>().ToList())
            (actionSet.Contains(c) ? _tabPages[1] : kindSet.Contains(c) ? _tabPages[2] : _tabPages[0]).Controls.Add(c);
        _kindEmpty = new Label
        {
            Text = T("This tile type has no extra settings."),
            ForeColor = Ui.Muted, Width = 260, Height = 60, Margin = new Padding(0, 12, 0, 0), Font = Ui.Body,
        };
        _tabPages[2].Controls.Add(_kindEmpty);

        var head = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Ui.Panel, Padding = new Padding(16, 14, 16, 0) };
        _tileTitle = new Label { Font = Ui.Title, ForeColor = Ui.Ink, AutoSize = false, AutoEllipsis = true, Location = new Point(16, 12), Size = new Size(240, 28) };
        _tileSub = new Label { Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = false, Location = new Point(17, 40), Size = new Size(240, 18) };
        var tileMore = ModernButton.Icon(Fx.I.More, T("Tile menu"), size: 30);
        tileMore.Location = new Point(270, 12);
        tileMore.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Items.Add(T("Duplicate   Ctrl+D"), null, (_, _) => Duplicate());
            m.Items.Add(T("Copy   Ctrl+C"), null, (_, _) => Copy());
            m.Items.Add(T("Try on this PC"), null, async (_, _) => await TestTile());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T("Delete   Del"), null, (_, _) => DeleteTile());
            m.Show(tileMore, new Point(0, tileMore.Height));
        };
        _tabs = new ModernSegmented { Location = new Point(16, 64), Width = 284 };
        _tabs.Items = new[] { T("Appearance"), T("Action"), T("Settings") };
        _tabs.SelectedIndex = 0;
        _tabs.SelectedIndexChanged += (_, _) => ShowTab(_tabs.SelectedIndex);
        head.Controls.AddRange(new Control[] { _tileTitle, _tileSub, tileMore, _tabs });

        _inspector = new ScrollPanel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Inset = new Padding(16, 4, 0, 16) };
        _inspector.Content.Controls.AddRange(_tabPages);

        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, BackColor = Ui.Panel, Padding = new Padding(16, 12, 8, 0), WrapContents = false };
        var test = Ui.Button(T("Try on this PC"), width: 170); ((ModernButton)test).Glyph = Fx.I.Play;
        test.Click += async (_, _) => await TestTile();
        var dup = ModernButton.Icon(Fx.I.Copy, T("Duplicate (Ctrl+D)")); dup.Kind = ButtonKind.Secondary; dup.Margin = new Padding(0, 0, 6, 0);
        dup.Click += (_, _) => Duplicate();
        var del = ModernButton.Icon(Fx.I.Delete, T("Delete (Del)")); del.Kind = ButtonKind.Danger;
        del.Click += (_, _) => DeleteTile();
        test.Margin = new Padding(0, 0, 6, 0);
        foot.Controls.AddRange(new Control[] { test, dup, del });
        var footLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Ui.Border };

        _props.Controls.Add(_inspector);
        _props.Controls.Add(head);
        _props.Controls.Add(footLine);
        _props.Controls.Add(foot);
        ShowTab(0);

        _noSelection = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Padding = new Padding(22, 40, 22, 0) };
        _noSelection.Controls.Add(new Label
        {
            Text = T("Select a tile, or double-click an empty cell to add one.\n\n") +
                   T("Drag to move a tile, drag its corner to resize."),
            ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = 140, Font = Ui.Body,
        });
        _noSelection.Controls.Add(new Label
        {
            Text = Fx.I.Touch, Font = Fx.Icons(34), ForeColor = Ui.Border, Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleLeft,
        });
        right.Controls.Add(_props);
        right.Controls.Add(_noSelection);

        _canvas = new DeckCanvas { Dock = DockStyle.Fill, Profile = _profile, Device = server.LastDeviceSize, Server = server };

        _liveTimer.Tick += (_, _) =>
        {
            var gifs = _page?.Tiles.Any(t => t.Gif?.Path is not null) == true;
            var sliders = _page?.Tiles.Any(t => t.Kind == "slider") == true;
            _liveTimer.Interval = _page?.Tiles.Any(t => t.Kind == "weather") == true ? 33 : gifs ? 60 : sliders ? 250 : 1000;
            if ((gifs || sliders || _page?.Tiles.Any(t => t.IsLive || t.Kind is "media" or "clock" or "weather" or "plugin") == true) && _drag0()) _canvas.Invalidate();
        };
        _liveTimer.Start();
        FormClosed += (_, _) => _liveTimer.Dispose();
        _canvas.SelectionChanged += ShowSelection;
        _canvas.BeforeChange += PushUndo;
        _canvas.Changed += ScheduleSave;
        _canvas.EmptyCellDoubleClicked += AddTile;
        _canvas.ContextMenuAt += ShowContextMenu;

        var center = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(22, 16, 22, 12) };
        var centerHead = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Ui.Bg };
        _pageTitle = new Label { Font = new Font("Segoe UI Semibold", 17f), ForeColor = Ui.Ink, AutoSize = true, Location = new Point(0, 0) };
        _pageSub = new Label { Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Location = new Point(2, 34) };
        var showOnTablet = Ui.Button(T("Show on tablet")); ((ModernButton)showOnTablet).Glyph = Fx.I.Tablet;
        showOnTablet.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        showOnTablet.Click += (_, _) =>
        {
            if (_page is null) return;
            Save();
            _server.ShowPageOnTablets(_page.Id);
            SetStatus(T("Tablet switched to “{0}”", _page.Name), Ui.Good);
        };
        centerHead.Controls.AddRange(new Control[] { _pageTitle, _pageSub, showOnTablet });
        centerHead.Resize += (_, _) => showOnTablet.Location = new Point(centerHead.Width - showOnTablet.Width, 6);

        var lib = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, BackColor = Ui.Bg, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var libHead = new Label
        {
            Dock = DockStyle.Bottom, Height = 24, ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.BottomLeft,
            Text = T("Add a tile · click to place it in the first empty cell"),
        };
        foreach (var (kind, name, glyph) in new[]
        {
            ("button", T("Button"), Fx.I.Touch), ("toggle", T("On / off"), Fx.I.ToggleIcon), ("slider", T("Slider"), Fx.I.Sliders),
            ("graph", T("Graph"), Fx.I.Chart), ("media", T("Media"), Fx.I.Music), ("clock", T("Clock"), Fx.I.Clock),
            ("timer", T("Counter"), Fx.I.History), ("weather", T("Weather"), Fx.I.Cloud),
        })
        {
            var b = new ModernButton { Text = name, Glyph = glyph, AutoSize = true, Height = 36, Margin = new Padding(0, 0, 6, 0), Padding = new Padding(9, 0, 10, 0), Font = Ui.Small };
            b.Click += (_, _) => AddTileOfKind(kind);
            lib.Controls.Add(b);
        }
        center.Controls.Add(_canvas);
        center.Controls.Add(centerHead);
        center.Controls.Add(libHead);
        center.Controls.Add(lib);
        _canvas.BackColor = Ui.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Ui.Panel, Padding = new Padding(14, 0, 14, 0) };
        var logo = Fx.Logo(30); logo.Location = new Point(14, 12);
        var brand = new Label { Text = "MyDeck", Font = new Font("Segoe UI Semibold", 12f), ForeColor = Ui.Ink, AutoSize = true, Location = new Point(52, 15) };
        _search = (ModernTextBox)Ui.TextBox();
        _search.Width = 300;
        _search.PlaceholderText = T("Search tiles or pages · Enter  (Ctrl+K)");
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunSearch(); } else if (e.KeyCode == Keys.Escape) { _search.Text = ""; _canvas.Focus(); } };
        var searchHost = new FlowLayoutPanel { Location = new Point(140, 9), Size = new Size(300, 38), BackColor = Ui.Panel, Margin = Padding.Empty, WrapContents = false };
        _search.Margin = Padding.Empty;
        searchHost.Controls.Add(_search);
        _devicePill = new Label { AutoSize = false, Size = new Size(230, 28), Location = new Point(456, 13), Font = Ui.Small, TextAlign = ContentAlignment.MiddleCenter };
        _devicePill.Paint += PaintDevicePill;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 470, BackColor = Ui.Panel, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var virt = Ui.Button(T("Virtual deck"), primary: true); ((ModernButton)virt).Glyph = Fx.I.Monitor;
        virt.Margin = new Padding(10, 0, 0, 0);
        virt.Click += (_, _) => { Save(); VirtualDeck.Open(_server); };
        var backupBtn = ModernButton.Icon(Fx.I.Library, T("Backup and sharing"));
        backupBtn.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Items.Add(T("Back up…"), null, (_, _) => ExportBackup());
            m.Items.Add(T("Load a backup or add a package…"), null, (_, _) => ImportBackup());
            m.Items.Add(T("Share this page…"), null, (_, _) => SharePage());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(T("Restore an earlier version…"), null, (_, _) => RestoreAuto());
            m.Show(backupBtn, new Point(0, backupBtn.Height));
        };
        var sep1 = new Panel { Width = 1, Height = 22, BackColor = Ui.Border, Margin = new Padding(8, 6, 8, 0) };
        var sep2 = new Panel { Width = 1, Height = 22, BackColor = Ui.Border, Margin = new Padding(8, 6, 8, 0) };
        var tabletB = ModernButton.Icon(Fx.I.Tablet, T("Tablet settings")); tabletB.Click += (_, _) => EditTablet();
        var pluginsB = ModernButton.Icon(Fx.I.Puzzle, T("Plugins")); pluginsB.Click += (_, _) => EditPlugins();
        var eventsB = ModernButton.Icon(Fx.I.Bolt, T("Variables and events")); eventsB.Click += (_, _) => EditEvents();
        var looksB = ModernButton.Icon(Fx.I.Palette, T("Appearance and wallpaper (tablet)")); looksB.Click += (_, _) => EditAppearance();
        var redoB = ModernButton.Icon(Fx.I.Redo, T("Redo (Ctrl+Y)")); redoB.Click += (_, _) => Redo();
        var undoB = ModernButton.Icon(Fx.I.Undo, T("Undo (Ctrl+Z)")); undoB.Click += (_, _) => Undo();
        tools.Controls.AddRange(new Control[] { virt, sep1, backupBtn, tabletB, pluginsB, eventsB, looksB, sep2, redoB, undoB });
        top.Controls.AddRange(new Control[] { logo, brand, searchHost, _devicePill, tools });
        var topLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Ui.Border };

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Ui.Panel, Padding = new Padding(16, 0, 16, 0) };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Small };
        _saveState = new Label { Dock = DockStyle.Right, Width = 200, ForeColor = Ui.Good, TextAlign = ContentAlignment.MiddleRight, Font = Ui.Small, Text = T("Saved") };
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(_saveState);
        var statusLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Ui.Border };
        var leftLine = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Ui.Border };
        var rightLine = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Ui.Border };

        Controls.Add(center);
        Controls.Add(rightLine);
        Controls.Add(right);
        Controls.Add(leftLine);
        Controls.Add(left);
        Controls.Add(statusLine);
        Controls.Add(statusBar);
        Controls.Add(topLine);
        Controls.Add(top);
        server.ClientsChanged += OnClientsChanged;
        FormClosed += (_, _) => server.ClientsChanged -= OnClientsChanged;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        server.DeviceSizeChanged += OnDeviceSize;
        FormClosed += (_, _) => server.DeviceSizeChanged -= OnDeviceSize;

        RefreshPages();
        ShowPage(_profile.Pages.FirstOrDefault(pg => pg.Id == _profile.Start) ?? _profile.Pages[0]);
        SetStatus(T("Preview {0}×{1} · changes go to the tablet right away", _canvas.Device.Width, _canvas.Device.Height), Ui.Muted);
    }

    void OnDeviceSize()
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _canvas.Device = _server.LastDeviceSize;
            _canvas.Invalidate();
        });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static string? Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    bool _drag0() => MouseButtons == MouseButtons.None;

    static LiveSpec L(Tile t) => t.Live ??= new LiveSpec();

    void PickGif()
    {
        if (_canvas.Selected is not { } t) return;
        using var dlg = new OpenFileDialog { Title = T("Choose a GIF"), Filter = "GIF|*.gif" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Edit("gif", x =>
        {
            x.Gif ??= new TileGif();
            x.Gif.Path = dlg.FileName;
            x.Gif.Fill ??= "bg";
            x.Gif.Play ??= "always";
        }, refresh: true);
    }

    HashSet<string> PluginLabels() =>
        _server.Plugins.All.SelectMany(p => p.Data.Concat(p.Actions)).Select(i => i.Name).ToHashSet();

    void FillPluginChoices(ComboBox combo, bool data, PluginSpec? cur)
    {
        var was = _loading; _loading = true;
        combo.Items.Clear();
        foreach (var pl in _server.Plugins.All.Where(x => x.Enabled || x.Id == cur?.Plugin))
            foreach (var it in data ? pl.Data : pl.Actions)
                combo.Items.Add(new PluginChoice(pl.Id, it, $"{pl.Name} · {it.Name}"));
        var idx = -1;
        for (var i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is PluginChoice c && c.Plugin == cur?.Plugin && c.Item.Id == cur?.Item) idx = i;
        combo.SelectedIndex = idx;
        _loading = was;
    }

    void BuildPluginFields(FlowLayoutPanel host, PluginItem item, PluginSpec spec)
    {
        host.SuspendLayout();
        host.Controls.Clear();
        foreach (var f in item.Fields)
        {
            host.Controls.Add(Ui.Caption(f.Name));
            var box = Ui.TextBox();
            box.PlaceholderText = f.Hint ?? f.Default ?? "";
            box.Text = spec.Args?.GetValueOrDefault(f.Id) ?? f.Default ?? "";
            var fid = f.Id;
            box.TextChanged += (_, _) => Edit("plugin.f." + fid, _ =>
            {
                spec.Args ??= new();
                spec.Args[fid] = box.Text.Trim();
            });
            host.Controls.Add(box);
        }
        host.ResumeLayout();
    }

    void EditPlugins()
    {
        using var dlg = new PluginsDialog(_server);
        dlg.ShowDialog(this);
        _server.RefreshPlugins();
        if (_canvas.Selected is not null) ShowSelection();
        _canvas.Invalidate();
    }

    void ExportBackup()
    {
        Save();
        using var dlg = new SaveFileDialog
        {
            Title = T("MyDeck backup"), Filter = T("MyDeck backup|*.mydeck"), FileName = $"MyDeck-{DateTime.Now:yyyy-MM-dd}.mydeck",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var n = Backup.Export(_profile, dlg.FileName);
            SetStatus(T("Backed up · {0} ({1} files inside)", Path.GetFileName(dlg.FileName), n), Ui.Good);
        }
        catch (Exception ex) { SetStatus(T("Couldn't back up: {0}", ex.Message), Ui.Accent); }
        finally { Cursor = Cursors.Default; }
    }

    void ImportBackup()
    {
        using var dlg = new OpenFileDialog
        {
            Title = T("Load a backup"), Filter = T("MyDeck backup|*.mydeck|Profile file|*.json"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var p = dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? Profile.Load(dlg.FileName)
                : Backup.Import(dlg.FileName, _server.ConfigDir);
            var answer = MessageBox.Show(this, T("The file has {0} pages and {1} tiles.\n\n", p.Pages.Count, p.Pages.Sum(x => x.Tiles.Count)) +
                    T("Yes: ADD the pages to your current layout (for shared pages).\n") +
                    T("No: REPLACE your current layout (for a full backup; the current state is backed up too).\n") +
                    T("Cancel: do nothing."),
                    T("Backup or package"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                PushUndo("merge");
                var (pages, tiles) = Backup.Merge(_profile, p);
                RefreshPages();
                ShowPage(_profile.Pages[^pages]);
                ScheduleSave();
                SetStatus(T("Added {0} pages and {1} tiles (Ctrl+Z undoes it)", pages, tiles), Ui.Good);
                return;
            }
            ApplyProfile(p, T("Backup loaded"));
        }
        catch (Exception ex) { SetStatus(T("Couldn't open the backup: {0}", ex.Message), Ui.Accent); }
    }

    void RestoreAuto()
    {
        using var dlg = new RestoreDialog(Backup.ListAuto(_server.ConfigDir));
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result is not { } item) return;
        try { ApplyProfile(Profile.Load(item.Path), T("Restored the version from {0:d MMMM HH:mm}", item.When)); }
        catch (Exception ex) { SetStatus(T("Couldn't restore: {0}", ex.Message), Ui.Accent); }
    }

    void ApplyProfile(Profile p, string message)
    {
        Save();
        Backup.SaveBeforeRestore(_server.ConfigDir, _server.ProfilePath);
        _lastUndoKey = "";
        PushUndo("restore");
        Restore(JsonSerializer.Serialize(p, Profile.Json));
        _saveTimer.Stop();
        Save();
        SetStatus(message + T(" · sent to the tablet (Ctrl+Z undoes it)"), Ui.Good);
    }

    void EditEvents()
    {
        using var dlg = new EventsDialog(_profile, _server);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        PushUndo("events");
        _profile.Events = dlg.Events.Count > 0 ? dlg.Events : null;
        _profile.Vars = dlg.Vars.Count > 0 ? dlg.Vars : null;
        _lastUndoKey = "";
        ScheduleSave();
        SetStatus(T("{0} events on · {1} variables", dlg.Events.Count(e => e.Enabled), dlg.Vars.Count), Ui.Good);
    }

    void EditComplexAction()
    {
        if (_canvas.Selected is not { } t || VAction(t) is not { } a) return;
        TileAction? result = null;
        if (a.Type == "multi")
        {
            using var dlg = new StepsDialog(T("Several actions"), a.Steps, _server, _profile);
            if (dlg.ShowDialog(this) == DialogResult.OK) result = new TileAction { Type = "multi", Steps = dlg.Result };
        }
        else
        {
            using var dlg = new StepDialog(a, _server, _profile);
            if (dlg.ShowDialog(this) == DialogResult.OK) result = dlg.Result;
        }
        if (result is null) return;
        Edit("complex", x => SetVAction(x, result));
        _loading = true;
        var ty = result.Type;
        _actionType.SelectedIndex = Math.Max(0, Array.FindIndex(ActionTypes, x => x.Type == ty));
        BuildActionPanel(t);
        _loading = false;
    }

    void PickPageApp(Control anchor) => AppPicker.Show(anchor, AddPageApp);

    void SharePage()
    {
        if (_page is null) return;
        Save();
        var name = string.IsNullOrWhiteSpace(_page.Name) ? _page.Id : _page.Name;
        using var dlg = new SaveFileDialog
        {
            Title = T("Share page"), Filter = "MyDeck paketi|*.mydeck", FileName = $"{name}.mydeck",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var (pages, files) = Backup.ExportPages(_profile, new[] { _page }, dlg.FileName);
            SetStatus(T("Package ready · {0} pages, {1} files · import it with “Load a backup → Yes (add)”", pages, files), Ui.Good);
        }
        catch (Exception ex) { SetStatus(T("Couldn't write the package: {0}", ex.Message), Ui.Accent); }
        finally { Cursor = Cursors.Default; }
    }

    void ShowTab(int i)
    {
        for (var k = 0; k < 3; k++) _tabPages[k].Visible = k == i;
        _inspector.ScrollTo(0);
    }

    void UpdateTileHeader(Tile t)
    {
        var kind = Kinds.FirstOrDefault(k => k.Kind == (t.Kind ?? "button")).Name ?? T("Button");
        _tileTitle.Text = string.IsNullOrWhiteSpace(t.Label) ? kind : t.Label;
        _tileSub.Text = $"{kind} · {t.W} × {t.H}";
        var hasKindSettings = t.Kind is "slider" or "media" or "clock" or "timer" or "weather" or "plugin" || t.IsLive;
        _kindEmpty.Visible = !hasKindSettings;
        _tabs.SetMark(2, hasKindSettings);
        _tabs.SetMark(1, t.Action is not null || t.Hold is not null || t.Double is not null || t.On?.Action is not null);
    }

    void UpdatePageHeader()
    {
        if (_page is null) return;
        _pageTitle.Text = string.IsNullOrWhiteSpace(_page.Name) ? _page.Id : _page.Name;
        var dev = _server.LastDeviceSize;
        var shape = _page.Shape == "square" ? T("square tiles") : T("fill");
        _pageSub.Text = T("{0} tiles  ·  {1} × {2}  ·  {3}  ·  full screen {4} × {5}", _page.Tiles.Count, _page.Cols, _page.Rows, shape, dev.Width, dev.Height) +
                        (_page.Folder ? T("  ·  folder") : _page.Id == _profile.Start ? T("  ·  start page") : "");
    }

    void OnClientsChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => _devicePill.Invalidate());
    }

    void PaintDevicePill(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Panel);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var devices = _server.ConnectedDevices;
        var on = devices.Count > 0;
        var text = on ? T("{0} · connected", string.Join(", ", devices)) : T("Waiting for a tablet");
        var tw = TextRenderer.MeasureText(text, Ui.Small).Width;
        var w = Math.Min(_devicePill.Width - 2, tw + 34);
        var r = new RectangleF(0.5f, 0.5f, w, _devicePill.Height - 1.5f);
        var fg = on ? Ui.Good : Ui.Muted;
        using (var path = Fx.Round(r, r.Height / 2))
        using (var b = new SolidBrush(Color.FromArgb(on ? 30 : 0, fg)))
        {
            g.FillPath(b, path);
            if (!on) using (var p = new Pen(Ui.Border)) g.DrawPath(p, path);
        }
        using (var d = new SolidBrush(fg)) g.FillEllipse(d, 12, r.Height / 2 - 3, 7, 7);
        TextRenderer.DrawText(g, text, Ui.Small, new Rectangle(26, 0, (int)w - 30, _devicePill.Height), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    void RefreshAppChips()
    {
        _appChips.SuspendLayout();
        foreach (Control c in _appChips.Controls) c.Dispose();
        _appChips.Controls.Clear();
        foreach (var app in _page?.Apps ?? new())
        {
            var chip = new ModernButton { Text = app + "  ×", Height = 28, AutoSize = true, Margin = new Padding(0, 0, 6, 6), Radius = 14, Font = Ui.Small, BackColor = Ui.Field, ForeColor = Ui.Ink2 };
            ModernButton.SharedTip.SetToolTip(chip, T("Remove"));
            var name = app;
            chip.Click += (_, _) =>
            {
                if (_page?.Apps is null) return;
                PushUndo("apps");
                _page.Apps.Remove(name);
                if (_page.Apps.Count == 0) _page.Apps = null;
                RefreshAppChips();
                ScheduleSave();
            };
            _appChips.Controls.Add(chip);
        }
        var add = new ModernButton { Text = T("Add app"), Glyph = Fx.I.Add, Height = 28, AutoSize = true, Margin = new Padding(0, 0, 6, 6), Radius = 14, Font = Ui.Small, Kind = ButtonKind.Ghost };
        add.Click += (_, _) => PickPageApp(add);
        _appChips.Controls.Add(add);
        _appChips.ResumeLayout();
    }

    void AddPageApp(string name)
    {
        if (_page is null) return;
        name = DeckEvent.AppName(name);
        if (name.Length == 0) return;
        _page.Apps ??= new();
        if (_page.Apps.Contains(name)) return;
        PushUndo("apps");
        _page.Apps.Add(name);
        RefreshAppChips();
        ScheduleSave();
    }

    void AddTileOfKind(string kind)
    {
        if (_page is null) return;
        if (FreeCellNear(_page, 1, 1, Point.Empty) is not { } cell) { SetStatus(T("No empty cell on this page. Make the grid bigger or delete a tile"), Ui.Muted); return; }
        AddTile(cell);
        if (kind != "button")
        {
            var idx = Array.FindIndex(Kinds, k => k.Kind == kind);
            if (idx >= 0) _kind.SelectedIndex = idx;
        }
        _tabs.SelectedIndex = 0;
    }

    void RunSearch()
    {
        var q = _search.Text.Trim();
        if (q.Length == 0) return;
        var tr = new System.Globalization.CultureInfo("tr-TR");
        bool Hit(string? s) => s is not null && tr.CompareInfo.IndexOf(s, q, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
        var m = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var pg in _profile.Pages)
        {
            if (Hit(pg.Name) || Hit(pg.Id))
            {
                var page = pg;
                m.Items.Add(T("Page · {0}", pg.Name), null, (_, _) => ShowPage(page));
            }
            foreach (var t in pg.Tiles.Where(t => Hit(t.Label) || Hit(t.Sub) || Hit(t.Action?.Text) || Hit(t.Action?.Keys)))
            {
                var page = pg; var tile = t;
                m.Items.Add($"{t.Label ?? T("(untitled)")}  ·  {pg.Name}", null, (_, _) => { ShowPage(page); _canvas.Selected = tile; });
            }
            if (m.Items.Count >= 20) break;
        }
        if (m.Items.Count == 0) m.Items.Add(T("No results")).Enabled = false;
        m.Show(_search, new Point(0, _search.Height + 8));
    }

    void EditTablet()
    {
        using var dlg = new TabletDialog(_profile.Tablet);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        PushUndo("tablet");
        _profile.Tablet = dlg.Result;
        ScheduleSave();
    }

    void EditAppearance()
    {
        if (_page is null) return;
        var original = JsonSerializer.Deserialize<Theme>(JsonSerializer.Serialize(_profile.Theme, Profile.Json), Profile.Json)!;
        var originalPageWall = _page.Wallpaper;
        using var dlg = new AppearanceDialog(_profile.Theme, _page);

        dlg.Preview += (t, pw) => { _profile.Theme = t; _page.Wallpaper = pw; _canvas.Invalidate(); };
        var ok = dlg.ShowDialog(this) == DialogResult.OK;
        _profile.Theme = original;
        _page.Wallpaper = originalPageWall;
        if (ok)
        {
            PushUndo("appearance");
            _profile.Theme = dlg.Result;
            _page.Wallpaper = dlg.PageWallpaper;
            _lastUndoKey = "";
            ScheduleSave();
            var any = dlg.Result.Wallpaper ?? dlg.PageWallpaper;
            if (any is not null && Assets.IsVideo(any) && (any != original.Wallpaper && any != originalPageWall))
                SetStatus(T("Preparing the live wallpaper for the tablet. The first time, the video converter is downloaded too."), Ui.Cyan);
        }
        _canvas.Invalidate();
    }
    static MediaSpec M(Tile t) => t.Media ??= new MediaSpec();
    static ClockSpec C(Tile t) => t.Clock ??= new ClockSpec();
    static bool IsDefaultLabel(string? label, params string[] defaults) =>
        label is null || defaults.Contains(label) || defaults.Any(d => T(d) == label);

    static TimerSpec Tm(Tile t) => t.Timer ??= new TimerSpec();
    static WeatherSpec W(Tile t) => t.Weather ??= new WeatherSpec();

    void FillMediaApps(Tile t)
    {
        var was = _loading;
        _loading = true;
        var cur = t.Media?.App ?? "";
        var items = new List<MetricItem> { new(null, T("Automatic (whatever is playing)")) };
        foreach (var (id, name) in _server.Media.ListApps())
        {
            var key = MediaKey(id);
            if (!items.Any(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))) items.Add(new(key, name));
        }
        if (cur.Length > 0 && !items.Any(i => string.Equals(i.Key, cur, StringComparison.OrdinalIgnoreCase)))
            items.Add(new(cur, T("{0} (closed now)", MediaWatcher.FriendlyApp(cur))));
        _mediaApp.Items.Clear();
        foreach (var i in items) _mediaApp.Items.Add(i);
        _mediaApp.SelectedIndex = Math.Max(0, items.FindIndex(i => string.Equals(i.Key ?? "", cur, StringComparison.OrdinalIgnoreCase)));
        _loading = was;
    }

    static string MediaKey(string id)
    {
        var l = id.ToLowerInvariant();
        foreach (var k in new[] { "youtube-music", "spotify", "chrome", "msedge", "firefox", "zen", "vlc" })
            if (l.Contains(k)) return k;
        var name = id.Split('!')[0].Split('\\', '/').Last();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    ComboBox MetricCombo(bool includeNone, bool includeText)
    {
        var cb = Ui.Combo();
        cb.MaxDropDownItems = 20;
        if (includeNone) cb.Items.Add(new MetricItem(null, T("— none —")));
        foreach (var m in Sensors.Catalog)
        {
            if (!includeText && m.Unit == "süre") continue;
            cb.Items.Add(new MetricItem(m.Key, m.Name + (m.Unit is { Length: > 0 } u && u != "süre" ? $"  ({u})" : "")));
        }
        return cb;
    }

    static void SelectMetric(ComboBox cb, string? key)
    {
        for (var i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is MetricItem m && m.Key == key) { cb.SelectedIndex = i; return; }
        cb.SelectedIndex = 0;
    }

    void ApplyPreset(Tile t, Preset pr)
    {
        var l = L(t);
        l.Metric = pr.Metric;
        l.Metric2 = pr.Metric2;
        l.Info = pr.Info.ToList();
        l.Color2 = null;
        t.Color = null;
        if (IsDefaultLabel(t.Label, "New tile", "Yeni kart") || Presets.Any(x => x.Label == t.Label)) t.Label = pr.Label;
    }

    void FillLive(Tile t)
    {
        var l = L(t);
        _preset.SelectedIndex = 0;
        SelectMetric(_metric, l.Metric ?? "cpu");
        SelectMetric(_metric2, l.Metric2);
        for (var i = 0; i < _info.Length; i++) SelectMetric(_info[i], l.Info?.ElementAtOrDefault(i));
        _style.SelectedIndex = Math.Max(0, Array.FindIndex(Styles, x => x.Key == (l.Style ?? "area")));
        _seconds.SelectedIndex = Math.Max(0, Array.FindIndex(Durations, x => x.Sec == l.Seconds));
        _colorMode.SelectedIndex = Math.Max(0, Array.FindIndex(ColorModes, x => x.Key == (l.ColorMode ?? "card")));
        var c2 = Ui.ParseColor(l.Color2);
        _color2.BackColor = c2 ?? Ui.Card;
        _color2.ForeColor = c2 is { } cc && cc.GetBrightness() > 0.6f ? Color.Black : Ui.Ink;
        _color2.Text = c2 is null ? T("Automatic") : Ui.Hex(c2.Value);
    }

    void PickColor2()
    {
        if (_canvas.Selected is not { } t) return;
        using var dlg = new ColorDialog { FullOpen = true, Color = Ui.ParseColor(L(t).Color2) ?? ColorTranslator.FromHtml("#FFB000") };
        if (dlg.ShowDialog(this) == DialogResult.OK) Edit("live.color2", x => L(x).Color2 = Ui.Hex(dlg.Color), refresh: true);
    }

    TileAction Act(Tile t)
    {
        var a = VAction(t);
        if (a is null) { a = new TileAction(); SetVAction(t, a); }
        return a;
    }

    TileAction? VAction(Tile t) => _gestureKey switch
    {
        "hold" => t.Hold,
        "double" => t.Double,
        _ => _editOn ? t.On?.Action : t.Action,
    };

    void SetVAction(Tile t, TileAction? a)
    {
        switch (_gestureKey)
        {
            case "hold": t.Hold = a; break;
            case "double": t.Double = a; break;
            default: if (_editOn) (t.On ??= new TileVariant()).Action = a; else t.Action = a; break;
        }
    }

    string? Get(Tile t, string field) => field switch
    {
        "label" => _editOn ? t.On?.Label : t.Label,
        "sub" => _editOn ? t.On?.Sub : t.Sub,
        "icon" => _editOn ? t.On?.Icon : t.Icon,
        "color" => _editOn ? t.On?.Color : t.Color,
        _ => null,
    };

    void Set(Tile t, string field, string? value)
    {
        if (_editOn)
        {
            var o = t.On ??= new TileVariant();
            switch (field) { case "label": o.Label = value; break; case "sub": o.Sub = value; break; case "icon": o.Icon = value; break; case "color": o.Color = value; break; }
        }
        else
        {
            switch (field) { case "label": t.Label = value; break; case "sub": t.Sub = value; break; case "icon": t.Icon = value; break; case "color": t.Color = value; break; }
        }
    }

    sealed record TargetItem(string Target, string Name)
    {
        public override string ToString() => Name;
    }

    static List<TargetItem> SliderTargets()
    {
        var list = new List<TargetItem> { new("master", T("Main volume")), new("mic", "Mikrofon"), new("tablet", T("Tablet brightness")) };
        foreach (var app in Audio.ListApps()) list.Add(new($"app:{app}", app));
        return list;
    }

    void FillSliderTargets(Tile t)
    {
        var was = _loading;
        _loading = true;
        var items = SliderTargets();
        var cur = t.Target ?? "master";
        if (!items.Any(i => i.Target.Equals(cur, StringComparison.OrdinalIgnoreCase)))
            items.Add(new TargetItem(cur, cur.StartsWith("app:") ? T("{0} (closed now)", cur[4..]) : cur));
        _sliderTarget.Items.Clear();
        foreach (var i in items) _sliderTarget.Items.Add(i);
        _sliderTarget.SelectedIndex = items.FindIndex(i => i.Target.Equals(cur, StringComparison.OrdinalIgnoreCase));
        _loading = was;
    }

    void ChangeKind()
    {
        if (_loading || _canvas.Selected is not { } t || _kind.SelectedIndex < 0) return;
        var kind = Kinds[_kind.SelectedIndex].Kind;
        if ((t.Kind ?? "button") == kind) return;
        PushUndo("kind");
        t.Kind = kind == "button" ? null : kind;
        if (kind == "toggle")
        {
            t.Source ??= "local";
            t.On ??= new TileVariant { Color = "#FF3D5A" };
        }
        if (kind == "slider")
        {
            t.Target ??= "master";
            t.Action = null;
            if (IsDefaultLabel(t.Label, "New tile", "Yeni kart")) t.Label = T("Main volume");

            if (t.W == 1 && t.H == 1 && _page is not null)
            {
                if (_canvas.Fits(_page, new Rectangle(t.X, t.Y, 1, 2), t)) t.H = 2;
                else if (_canvas.Fits(_page, new Rectangle(t.X, t.Y, 2, 1), t)) t.W = 2;
            }
        }
        if (kind == "media")
        {
            t.Action = null; t.Icon = null; t.Source = null; t.On = null; t.Target = null; t.Live = null;
            t.Media ??= new MediaSpec();
            if (t.Label == T("New tile")) t.Label = null;
            if (_page is not null && t.W * t.H == 1)
                foreach (var (w, h) in new[] { (4, 2), (3, 2), (2, 2), (2, 1) })
                    if (_canvas.Fits(_page, new Rectangle(t.X, t.Y, w, h), t)) { t.W = w; t.H = h; break; }
        }
        else t.Media = null;
        if (kind is "clock" or "timer" or "weather")
        {
            t.Action = null; t.Icon = null; t.Source = null; t.On = null; t.Target = null; t.Live = null; t.Gif = null;
            if (t.Label == T("New tile")) t.Label = kind == "timer" ? T("Timer") : null;
            if (kind == "clock") t.Clock ??= new ClockSpec();
            if (kind == "timer") t.Timer ??= new TimerSpec();
            if (kind == "weather") t.Weather ??= new WeatherSpec();
            var sizes = kind switch
            {
                "clock" => new[] { (2, 1) },
                "weather" => new[] { (2, 2) },
                _ => Array.Empty<(int, int)>(),
            };
            if (_page is not null && t.W * t.H == 1)
                foreach (var (w, h) in sizes)
                    if (_canvas.Fits(_page, new Rectangle(t.X, t.Y, w, h), t)) { t.W = w; t.H = h; break; }
        }
        if (kind == "plugin")
        {
            t.Action = null; t.Icon = null; t.Source = null; t.On = null; t.Target = null; t.Live = null; t.Gif = null;
            if (t.Plugin is null)
            {
                var first = _server.Plugins.All.Where(x => x.Enabled).SelectMany(x => x.Data.Select(i => (x, i))).FirstOrDefault();
                t.Plugin = new PluginSpec { Plugin = first.x?.Id, Item = first.i?.Id };
                if (first.i is not null && IsDefaultLabel(t.Label, "New tile", "Yeni kart")) t.Label = first.i.Name;
            }
        }
        else t.Plugin = null;
        if (kind != "clock") t.Clock = null;
        if (kind != "timer") t.Timer = null;
        if (kind != "weather") t.Weather = null;
        if (t.IsLive)
        {
            t.Action = null; t.Icon = null; t.Source = null; t.On = null; t.Target = null;
            var l = L(t);
            if (t.Label == T("New tile")) t.Label = null;
            switch (kind)
            {
                case "graph":
                    if (l.Metric is null) ApplyPreset(t, Presets[0]);
                    break;
                case "ring":
                    if (l.Metric is null or "cpu" or "uptime") l.Metric = "ram.used";
                    break;
                case "gauge":
                    if (l.Metric is null or "uptime") l.Metric = "gpu.temp";
                    break;
            }

            var sizes = kind switch
            {
                "graph" or "net" => new[] { (3, 2), (2, 2), (2, 1) },
                "procs" => new[] { (2, 1) },
                _ => Array.Empty<(int, int)>(),
            };
            if (_page is not null && t.W * t.H == 1)
                foreach (var (w, h) in sizes)
                    if (_canvas.Fits(_page, new Rectangle(t.X, t.Y, w, h), t)) { t.W = w; t.H = h; break; }
        }
        else t.Live = null;
        _editOn = false;
        ShowSelection();
        _canvas.Invalidate();
        ScheduleSave();
    }

    void SetEditOn(bool on)
    {
        _editOn = on;
        if (_canvas.Selected is { } t) ShowSelection();
        _canvas.PreviewOn = on ? _canvas.Selected?.Id : null;
        _canvas.Invalidate();
    }

    void ApplyKindVisibility(Tile t)
    {
        var kind = t.Kind ?? "button";
        var system = kind == "toggle" && t.Source is "mute" or "micmute"
            ;
        _systemHint.Text = T("Pressing it changes the setting in Windows. If you change it elsewhere, the tablet updates too.");
        _systemHint.Height = 46;
        var widget = kind is "clock" or "timer" or "weather" or "plugin";
        _pluginBox.Visible = kind == "plugin";
        _clockBox.Visible = kind == "clock";
        _timerBox.Visible = kind == "timer";
        _timerLength.Visible = kind == "timer" && t.Timer?.Mode != "stopwatch";
        _weatherBox.Visible = kind == "weather";
        _toggleBox.Visible = kind == "toggle";
        _sliderBox.Visible = kind == "slider";
        var live = t.IsLive;
        _liveBox.Visible = live;
        _mediaBox.Visible = kind == "media";
        _gifBox.Visible = kind is "button" or "toggle";
        if (widget) live = true;
        _gifClear.Visible = _gifFill.Visible = _gifPlay.Visible = t.Gif?.Path is not null;
        _gifPick.Text = t.Gif?.Path is { } gp ? Path.GetFileName(gp) : T("Choose GIF…");
        if (kind == "media") live = true;
        if (live && !widget)
        {
            _presetRow.Visible = kind == "graph";
            _metricRow.Visible = kind is "graph" or "ring" or "gauge";
            _metric2Row.Visible = _infoRow.Visible = kind == "graph";
            _styleRow.Visible = kind is "graph" or "net";
            _colorModeRow.Visible = kind is not "net";
            _color2Row.Visible = kind == "net" || (kind == "graph" && t.Live?.Metric2 is not null);
        }
        _iconButton.Visible = kind != "slider" && !live;
        var gestures = kind is "button" or "toggle";
        var tapOnly = _gestureKey == "tap";
        _gesture.Visible = gestures;
        _actionCaption.Visible = gestures || (kind != "slider" && !live);
        _actionType.Visible = _actionPanel.Visible = kind != "slider" && !live && (!system || !tapOnly);
        _systemHint.Visible = system && tapOnly;
        _actionCaption.Text = !tapOnly ? T("What this gesture does")
            : kind == "toggle" ? (_editOn ? T("When pressed while on") : T("When pressed while off")) : T("When pressed");
        _offTab.BackColor = !_editOn ? Ui.Cyan : Ui.Card;
        _offTab.ForeColor = !_editOn ? Color.Black : Ui.Ink;
        _onTab.BackColor = _editOn ? Ui.Accent : Ui.Card;
        _onTab.ForeColor = _editOn ? Color.White : Ui.Ink;
        if (kind == "slider") FillSliderTargets(t);
    }

    void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    void RefreshPages()
    {
        _loading = true;
        var sel = _page;
        _pages.RefreshItems();
        if (sel is not null) _pages.SetSilently(_profile.Pages.IndexOf(sel));
        _loading = false;

        _targetPage.Items.Clear();
        foreach (var pg in _profile.Pages) _targetPage.Items.Add($"{(pg.Folder ? T("Folder") : T("Page"))} · {pg.Name}");
    }

    void ShowPage(Page pg)
    {
        _page = pg;
        _loading = true;
        _pages.SetSilently(_profile.Pages.IndexOf(pg));
        _pageName.Text = pg.Name;

        _cols.Minimum = Math.Max(1, pg.Tiles.Select(t => t.X + t.W).DefaultIfEmpty(1).Max());
        _rows.Minimum = Math.Max(1, pg.Tiles.Select(t => t.Y + t.H).DefaultIfEmpty(1).Max());
        _cols.Value = pg.Cols;
        _rows.Value = pg.Rows;
        _shape.SetSilently(pg.Shape == "square" ? 1 : 0);
        _folderToggle.Checked = pg.Folder;
        _startPage.Checked = _profile.Start == pg.Id;
        RefreshAppChips();
        _smartReturn.Checked = _profile.SmartReturn;
        _startPage.Enabled = !pg.Folder;
        _loading = false;
        _canvas.Profile = _profile;
        _canvas.Page = pg;
        _canvas.Selected = null;
        _canvas.Invalidate();
        UpdateGridInfo();
        UpdatePageHeader();
    }

    void UpdateGridInfo()
    {
        if (_page is null) return;
        var dev = _server.LastDeviceSize;
        var g = Grid.Layout(dev.Width, dev.Height, _page, _profile.Theme);
        var shape = Math.Abs(g.Cw - g.Ch) < 0.5f ? T("square") : T("ratio 1 : {0:0.00}", g.Ch / g.Cw);
        _gridInfo.Text = T("One tile {0:0} × {1:0} px · {2}\nFull-screen grid ({3}×{4})", g.Cw, g.Ch, shape, dev.Width, dev.Height);
    }

    void ResizeGrid()
    {
        if (_loading || _page is null) return;
        PushUndo("grid");
        _page.Cols = (int)_cols.Value;
        _page.Rows = (int)_rows.Value;
        UpdateGridInfo();
        _pages.Invalidate();
        _canvas.Invalidate();
        ScheduleSave();
    }

    string NewPageId(string name)
    {
        var baseId = new string(name.ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (baseId.Length == 0) baseId = T("page");
        var id = baseId;
        for (var i = 2; _profile.Pages.Any(p => p.Id == id); i++) id = $"{baseId}-{i}";
        return id;
    }

    void AddPage(bool folder)
    {
        PushUndo("addPage");
        var name = folder ? T("New folder") : T("New page");
        var pg = new Page { Id = NewPageId(name), Name = name, Cols = 5, Rows = 3, Folder = folder, Shape = "square" };
        if (folder) pg.Tiles.Add(new Tile { Id = pg.Id + "-geri", X = 0, Y = 0, Label = T("Back"), Icon = "glyph:E72B", Color = "#3CC8F0", Action = new TileAction { Type = "back" } });
        _profile.Pages.Add(pg);
        RefreshPages();
        ShowPage(pg);
        ScheduleSave();
        _pageName.Focus();
        _pageName.SelectAll();
    }

    void MovePage(int dir)
    {
        if (_page is null) return;
        var i = _profile.Pages.IndexOf(_page);
        var j = i + dir;
        if (j < 0 || j >= _profile.Pages.Count) return;
        PushUndo("movePage");
        (_profile.Pages[i], _profile.Pages[j]) = (_profile.Pages[j], _profile.Pages[i]);
        RefreshPages();
        ScheduleSave();
    }

    void DeletePage()
    {
        if (_page is null) return;
        if (_profile.Pages.Count(p => !p.Folder) <= 1 && !_page.Folder)
        {
            MessageBox.Show(this, T("At least one normal page must remain."), "MyDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, T("Delete '{0}' and its {1} tiles?", _page.Name, _page.Tiles.Count), "MyDeck",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        PushUndo("deletePage");
        var id = _page.Id;
        _profile.Pages.Remove(_page);

        foreach (var t in _profile.Pages.SelectMany(p => p.Tiles))
        {
            if (t.Action?.Type is "page" or "folder" && t.Action.Page == id) t.Action = null;
            if (t.On?.Action?.Type is "page" or "folder" && t.On.Action.Page == id) t.On.Action = null;
        }
        if (_profile.Start == id) _profile.Start = _profile.Pages.First(p => !p.Folder).Id;
        _page = null;
        RefreshPages();
        ShowPage(_profile.Pages[0]);
        ScheduleSave();
    }

    string NewTileId() => $"{_page!.Id}-{Guid.NewGuid().ToString("N")[..6]}";

    void AddTile(Point cell)
    {
        if (_page is null) return;
        PushUndo("addTile");
        var t = new Tile { Id = NewTileId(), X = cell.X, Y = cell.Y, Label = T("New tile"), Icon = "glyph:E710" };
        _page.Tiles.Add(t);
        _canvas.Selected = t;
        ScheduleSave();
        _label.Focus();
        _label.SelectAll();
    }

    Point? FreeCellNear(Page pg, int w, int h, Point from)
    {
        for (var dy = 0; dy < pg.Rows; dy++)
            for (var dx = 0; dx < pg.Cols; dx++)
            {
                var r = new Rectangle((from.X + dx) % pg.Cols, (from.Y + dy) % pg.Rows, w, h);
                if (_canvas.Fits(pg, r, null)) return r.Location;
            }
        return null;
    }

    Tile Clone(Tile t) => JsonSerializer.Deserialize<Tile>(JsonSerializer.Serialize(t, Profile.Json), Profile.Json)!;

    void Duplicate()
    {
        if (_page is null || _canvas.Selected is not { } t) return;
        PasteTile(Clone(t), new Point(t.X + t.W, t.Y));
    }

    void Copy()
    {
        if (_canvas.Selected is { } t) { _clipboard = Clone(t); SetStatus(T("Copied '{0}'", t.Label), Ui.Muted); }
    }

    void PasteTile(Tile t, Point at)
    {
        if (_page is null) return;
        var spot = FreeCellNear(_page, t.W, t.H, at) ?? FreeCellNear(_page, 1, 1, at);
        if (spot is null) { SetStatus(T("No room on this page"), Ui.Accent); return; }
        PushUndo("paste");
        t.Id = NewTileId();
        t.X = spot.Value.X; t.Y = spot.Value.Y;
        if (!_canvas.Fits(_page, new Rectangle(t.X, t.Y, t.W, t.H), null)) { t.W = 1; t.H = 1; }
        _page.Tiles.Add(t);
        _canvas.Selected = t;
        ScheduleSave();
    }

    void DeleteTile()
    {
        if (_page is null || _canvas.Selected is not { } t) return;
        PushUndo("deleteTile");
        _page.Tiles.Remove(t);
        _canvas.Selected = null;
        ScheduleSave();
    }

    void Nudge(int dx, int dy)
    {
        if (_page is null || _canvas.Selected is not { } t) return;
        var r = new Rectangle(t.X + dx, t.Y + dy, t.W, t.H);
        if (!_canvas.Fits(_page, r, t)) return;
        PushUndo("nudge");
        t.X = r.X; t.Y = r.Y;
        _canvas.Invalidate();
        ScheduleSave();
    }

    async Task TestTile()
    {
        if ((_canvas.Selected is { } sel ? VAction(sel) : null) is not { } a) { SetStatus(T("This tile has no action for this gesture"), Ui.Muted); return; }
        if (a.Type is "page" or "folder" or "back") { SetStatus(T("Navigation tiles only work on the tablet"), Ui.Muted); return; }
        try
        {
            SetStatus(T("Runs in 3 seconds, switch to the target window…"), Ui.Cyan);
            await Task.Delay(3000);
            await Actions.RunAsync(a);
            SetStatus(T("Done"), Ui.Good);
        }
        catch (Exception ex) { SetStatus(T("Couldn't run: {0}", ex.Message), Ui.Accent); }
    }

    void ShowContextMenu(Point screen, Point cell)
    {
        var m = new ContextMenuStrip { BackColor = Ui.Card, ForeColor = Ui.Ink, ShowImageMargin = false };
        var has = _canvas.Selected is not null && _page!.Tiles.Contains(_canvas.Selected);
        if (has)
        {
            m.Items.Add(T("Duplicate"), null, (_, _) => Duplicate());
            m.Items.Add(T("Copy"), null, (_, _) => Copy());
            m.Items.Add(T("Delete"), null, (_, _) => DeleteTile());
        }
        else m.Items.Add(T("New tile here"), null, (_, _) => AddTile(cell));
        if (_clipboard is not null) m.Items.Add(T("Paste here"), null, (_, _) => PasteTile(Clone(_clipboard), cell));
        m.Show(_canvas, screen);
    }

    void ShowSelection()
    {
        var t = _canvas.Selected;
        _props.Visible = t is not null;
        _noSelection.Visible = t is null;
        if (t is null) return;
        _loading = true;
        if (t.Kind != "toggle" || t.Id != _shownTileId) _editOn = false;
        if (t.Id != _shownTileId) _gestureKey = "tap";
        _gesture.SetMark(0, (t.Kind == "toggle" ? t.Action ?? t.On?.Action : t.Action) is not null);
        _gesture.SetMark(1, t.Hold is not null);
        _gesture.SetMark(2, t.Double is not null);
        _gesture.SetSilently(_gestureKey switch { "hold" => 1, "double" => 2, _ => 0 });
        _shownTileId = t.Id;
        _kind.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == (t.Kind ?? "button")));
        _source.SelectedIndex = Math.Max(0, Array.FindIndex(Sources, k => k.Source == (t.Source ?? "local")));
        _label.Text = Get(t, "label") ?? "";
        _sub.Text = Get(t, "sub") ?? "";
        _label.PlaceholderText = _editOn ? (t.Label ?? "") + T("  (same as the off state)") : "";
        _sub.PlaceholderText = _editOn ? (t.Sub ?? "") + T("  (same as the off state)") : "";
        UpdateLookButtons(t);
        var type = VAction(t)?.Type ?? "";
        _actionType.SelectedIndex = Math.Max(0, Array.FindIndex(ActionTypes, a => a.Type == type));
        BuildActionPanel(t);
        if (t.IsLive) FillLive(t);
        _gifFill.SelectedIndex = Math.Max(0, Array.FindIndex(GifFills, x => x.Key == (t.Gif?.Fill ?? "bg")));
        _gifPlay.SelectedIndex = Math.Max(0, Array.FindIndex(GifPlays, x => x.Key == (t.Gif?.Play ?? "always")));
        if (t.Kind == "media")
        {
            FillMediaApps(t);
            _mediaTint.Checked = t.Media?.FollowArt ?? true;
            _mediaControls.Checked = t.Media?.Controls ?? true;
            _mediaTimes.Checked = t.Media?.Times ?? true;
        }
        if (t.Kind == "clock")
        {
            _clockStyle.SelectedIndex = Math.Max(0, Array.FindIndex(ClockStyles, x => x.Key == (t.Clock?.Style ?? "digital")));
            _clockZone.SelectedIndex = Math.Max(0, Array.FindIndex(Zones, z => z.Zone == (t.Clock?.Zone ?? "")));
            _clockSec.Checked = t.Clock?.Seconds ?? true;
            _clockDate.Checked = t.Clock?.Date ?? true;
        }
        if (t.Kind == "timer")
        {
            _timerMode.SelectedIndex = t.Timer?.Mode == "stopwatch" ? 1 : 0;
            var sec = t.Timer?.Seconds ?? 300;
            _timerMin.Value = Math.Min(1440, sec / 60); _timerSec.Value = sec % 60;
            _timerPreset.SelectedIndex = 0;
            _timerAnnounce.Checked = t.Timer?.Announce ?? true;
        }
        if (t.Kind == "plugin")
        {
            FillPluginChoices(_pluginData, data: true, t.Plugin);
            if (_pluginData.SelectedItem is PluginChoice ch && t.Plugin is not null) BuildPluginFields(_pluginFields, ch.Item, t.Plugin);
            else _pluginFields.Controls.Clear();
        }
        if (t.Kind == "weather")
            _weatherView.SelectedIndex = Math.Max(0, Array.FindIndex(WeatherViews, x => x.Key == (t.Weather?.View ?? (t.W >= 2 && t.H >= 2 ? "hours" : "now"))));
        ApplyKindVisibility(t);
        _loading = false;
        _canvas.PreviewOn = _editOn ? t.Id : null;
        UpdateTileHeader(t);
    }

    void UpdateLookButtons(Tile t)
    {
        var icon = Get(t, "icon") ?? t.Icon;
        var color = Get(t, "color") ?? (_editOn ? t.Color : null);
        var png = IconRenderer.Render(icon, 24, color ?? t.IconColor ?? _profile.Theme.Text);
        _iconButton.Image?.Dispose();
        _iconButton.Image = png is null ? null : Image.FromStream(new MemoryStream(png));
        _iconButton.Text = png is null ? T("Choose icon…") : T("Change");
        var c = Ui.ParseColor(Get(t, "color"));
        _colorButton.BackColor = c ?? Ui.Card;
        _colorButton.ForeColor = c is { } cc && cc.GetBrightness() > 0.6f ? Color.Black : Ui.Ink;
        _colorButton.Text = c is null ? T("Color") : Ui.Hex(c.Value);
    }

    void Edit(string key, Action<Tile> change, bool refresh = false)
    {
        if (_loading || _canvas.Selected is not { } t) return;
        PushUndo(key);
        change(t);
        if (refresh)
        {
            UpdateLookButtons(t);
            if (t.IsLive) { _loading = true; FillLive(t); _loading = false; }
            ApplyKindVisibility(t);
        }
        _canvas.Invalidate();
        UpdateTileHeader(t);
        ScheduleSave();
    }

    void PickIcon()
    {
        if (_canvas.Selected is not { } t) return;
        using var dlg = new IconPicker(Get(t, "icon"), Get(t, "color") ?? t.Color ?? _profile.Theme.Text);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is { } icon)
            Edit("icon", x => Set(x, "icon", icon.Length == 0 ? null : icon), refresh: true);
    }

    void PickColor()
    {
        if (_canvas.Selected is not { } t) return;
        using var dlg = new ColorDialog { FullOpen = true, Color = Ui.ParseColor(Get(t, "color")) ?? Ui.Accent, CustomColors = new[]
        {
            0x5A3DFF, 0xF0C83C, 0x80DE4A, 0x00B0FF, 0xFF4DA6, 0xFFFFFF, 0x80604C,
        } };
        if (dlg.ShowDialog(this) == DialogResult.OK) Edit("color", x => Set(x, "color", Ui.Hex(dlg.Color)), refresh: true);
    }

    void ChangeActionType()
    {
        if (_loading || _canvas.Selected is not { } t) return;
        var type = ActionTypes[Math.Max(0, _actionType.SelectedIndex)].Type;
        if ((VAction(t)?.Type ?? "") == type) return;
        PushUndo("actionType");
        var na = type.Length == 0 ? null : new TileAction { Type = type };
        SetVAction(t, na);
        if (type == "media") na!.Key = "playpause";
        if (type is "multi" or "setvar" or "if") { na = ActionText.NewStep(type); SetVAction(t, na); }
        if (type == "plugin")
        {
            var first = _server.Plugins.All.Where(x => x.Enabled).SelectMany(x => x.Actions.Select(i => (x, i))).FirstOrDefault();
            if (first.i is not null) na!.Plugin = new PluginSpec { Plugin = first.x.Id, Item = first.i.Id };
        }
        if (type is "page" or "folder")
        {
            var target = _profile.Pages.FirstOrDefault(p => p != _page && p.Folder == (type == "folder")) ?? _profile.Pages.FirstOrDefault(p => p != _page);
            na!.Page = target?.Id;
            if (target is null) SetVAction(t, null);
        }
        _loading = true;
        BuildActionPanel(t);
        _loading = false;
        _canvas.Invalidate();
        ScheduleSave();
    }

    void BuildActionPanel(Tile t)
    {
        _actionPanel.SuspendLayout();
        _actionPanel.Controls.Clear();
        var a = VAction(t);
        switch (a?.Type)
        {
            case "keys":
                _keys.Text = a.Keys ?? "";
                _actionPanel.Controls.Add(_keys);
                _actionPanel.Controls.Add(_recordKeys);
                _actionPanel.Controls.Add(Hint(T("Join keys with +: ctrl, shift, alt, win, a–z, 0–9, f1–f24, enter, esc, tab, space, arrows…")));
                break;
            case "media":
                _media.SelectedIndex = Math.Max(0, Array.FindIndex(MediaKeys, m => m.Key == a.Key));
                _actionPanel.Controls.Add(_media);
                break;
            case "run":
                _path.Text = a.Path ?? "";
                _args.Text = a.Args ?? "";
                var browse = Ui.Button(T("Browse…"), width: 260);
                browse.Click += (_, _) => BrowseProgram();
                _actionPanel.Controls.AddRange(new Control[] { _path, browse, _args });
                break;
            case "url":
                _url.Text = a.Url ?? "";
                _actionPanel.Controls.Add(_url);
                break;
            case "page" or "folder":
                _targetPage.SelectedIndex = _profile.Pages.FindIndex(p => p.Id == a.Page);
                _actionPanel.Controls.Add(_targetPage);
                break;
            case "lock":
                _actionPanel.Controls.Add(Hint(T("Locks Windows (Win + L).")));
                break;
            case "back":
                _actionPanel.Controls.Add(Hint(T("Goes back to the previous page from a folder.")));
                break;
            case "plugin":
                FillPluginChoices(_pluginAct, data: false, a.Plugin);
                _actionPanel.Controls.Add(_pluginAct);
                _actionPanel.Controls.Add(_pluginActFields);
                if (_pluginAct.SelectedItem is PluginChoice pc && a.Plugin is not null) BuildPluginFields(_pluginActFields, pc.Item, a.Plugin);
                else _pluginActFields.Controls.Clear();
                if (_pluginAct.Items.Count == 0) _actionPanel.Controls.Add(Hint(T("No plugin with actions is turned on.")));
                else if (_pluginAct.SelectedItem is PluginChoice pd && pd.Item.Description is { Length: > 0 } desc) _actionPanel.Controls.Add(Hint(desc));
                break;
            case "multi" or "setvar" or "if":
                var summary = new Label
                {
                    Text = ActionText.Describe(a, _profile), ForeColor = Ui.Ink, Width = 260, Height = 40,
                    Margin = new Padding(0, 2, 0, 4), AutoEllipsis = true,
                };
                var editBtn = Ui.Button(a.Type == "multi" ? T("Edit steps ({0})…", a.Steps?.Count ?? 0) : T("Edit…"), width: 260);
                editBtn.Click += (_, _) => EditComplexAction();
                _actionPanel.Controls.AddRange(new Control[] { summary, editBtn });
                _actionPanel.Controls.Add(Hint(a.Type switch
                {
                    "multi" => T("Steps run in order. You can add waits, conditions and variables."),
                    "setvar" => T("Show the variable on a tile as {name}, or use it in conditions and events."),
                    _ => T("If the condition holds, the “If true” steps run; otherwise the “Otherwise” steps."),
                }));
                break;
        }
        _actionPanel.ResumeLayout();
    }

    static Label Hint(string text) => new() { Text = text, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(262, 0), Margin = new Padding(0, 4, 0, 6), Font = Ui.Small };

    void BrowseProgram()
    {
        using var dlg = new OpenFileDialog { Title = T("Program or file to open"), Filter = T("Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*") };
        if (dlg.ShowDialog(this) != DialogResult.OK || _canvas.Selected is not { } t) return;
        _path.Text = dlg.FileName;

        if (Get(t, "icon") is null or "glyph:E710") Edit("icon", x => Set(x, "icon", dlg.FileName), refresh: true);
        if (IsDefaultLabel(Get(t, "label"), "New tile", "Yeni kart")) _label.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    void ToggleRecord()
    {
        _recording = !_recording;
        _recordKeys.Text = _recording ? T("Recording… press the key combination") : T("Record by pressing keys");
        _recordKeys.BackColor = _recording ? Ui.Accent : Ui.Card;
        if (_recording) _keys.Focus();
    }

    void RecordKey(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        var name = KeyName(e.KeyCode);
        if (name is null) return;
        var parts = new List<string>();
        if (e.Control) parts.Add("ctrl");
        if (e.Shift) parts.Add("shift");
        if (e.Alt) parts.Add("alt");
        if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) parts.Add("win");
        parts.Add(name);
        var chord = string.Join("+", parts);
        _keys.Text = chord;
        _recording = false;
        _recordKeys.Text = T("Record by pressing keys");
        _recordKeys.BackColor = Ui.Card;
        Edit("keys", t => Act(t).Keys = chord);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetKeyState(int key);

    static string? KeyName(Keys k) => k switch
    {
        >= Keys.A and <= Keys.Z => ((char)('a' + (k - Keys.A))).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => ((char)('0' + (k - Keys.NumPad0))).ToString(),
        >= Keys.F1 and <= Keys.F24 => "f" + (k - Keys.F1 + 1),
        Keys.Enter => "enter", Keys.Escape => "esc", Keys.Tab => "tab", Keys.Space => "space",
        Keys.Back => "backspace", Keys.Delete => "delete", Keys.Insert => "insert",
        Keys.Home => "home", Keys.End => "end", Keys.PageUp => "pgup", Keys.PageDown => "pgdn",
        Keys.Up => "up", Keys.Down => "down", Keys.Left => "left", Keys.Right => "right",
        Keys.PrintScreen => "printscreen", Keys.Pause => "pause",
        Keys.Oemplus => "=", Keys.OemMinus => "-", Keys.OemPeriod => ".", Keys.Oemcomma => ",",
        Keys.MediaPlayPause => "media_play_pause", Keys.MediaNextTrack => "media_next", Keys.MediaPreviousTrack => "media_prev",
        Keys.VolumeUp => "volume_up", Keys.VolumeDown => "volume_down", Keys.VolumeMute => "volume_mute",
        _ => null,
    };

    static Control? FindFocused(Control c)
    {
        foreach (Control x in c.Controls)
        {
            if (x.Focused) return x;
            if (x.ContainsFocus && FindFocused(x) is { } inner) return inner;
        }
        return null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_recording) return base.ProcessCmdKey(ref msg, keyData);
        var typing = ActiveControl is TextBox or ModernStepper or ComboBox || FindFocused(this) is TextBox or ComboBox;
        switch (keyData)
        {
            case Keys.Control | Keys.K: _search.Focus(); _search.SelectAll(); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y: Redo(); return true;
            case Keys.Control | Keys.S: _saveTimer.Stop(); Save(); return true;
        }
        if (!typing)
        {
            switch (keyData)
            {
                case Keys.Delete: DeleteTile(); return true;
                case Keys.Control | Keys.C: Copy(); return true;
                case Keys.Control | Keys.V when _clipboard is not null:
                    PasteTile(Clone(_clipboard), _canvas.Selected is { } s ? new Point(s.X, s.Y) : Point.Empty); return true;
                case Keys.Control | Keys.D: Duplicate(); return true;
                case Keys.Left: Nudge(-1, 0); return true;
                case Keys.Right: Nudge(1, 0); return true;
                case Keys.Up: Nudge(0, -1); return true;
                case Keys.Down: Nudge(0, 1); return true;
                case Keys.Escape: _canvas.Selected = null; return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void PushUndo(string key)
    {
        var now = DateTime.Now;
        if (key == _lastUndoKey && (now - _lastUndoAt).TotalMilliseconds < 1500) { _lastUndoAt = now; return; }
        _lastUndoKey = key;
        _lastUndoAt = now;
        _undo.Push(JsonSerializer.Serialize(_profile, Profile.Json));
        if (_undo.Count > 200) { var keep = _undo.Take(150).Reverse().ToList(); _undo.Clear(); foreach (var s in keep) _undo.Push(s); }
        _redo.Clear();
    }

    void Restore(string json)
    {
        var pageId = _page?.Id;
        var tileId = _canvas.Selected?.Id;
        _profile = JsonSerializer.Deserialize<Profile>(json, Profile.Json)!;
        _page = null;
        RefreshPages();
        var pg = _profile.Pages.FirstOrDefault(p => p.Id == pageId) ?? _profile.Pages[0];
        ShowPage(pg);
        _canvas.Selected = pg.Tiles.FirstOrDefault(t => t.Id == tileId);
        _lastUndoKey = "";
        ScheduleSave();
    }

    void Undo()
    {
        if (_undo.Count == 0) { SetStatus(T("Nothing to undo"), Ui.Muted); return; }
        _redo.Push(JsonSerializer.Serialize(_profile, Profile.Json));
        Restore(_undo.Pop());
        SetStatus(T("Undone"), Ui.Muted);
    }

    void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(JsonSerializer.Serialize(_profile, Profile.Json));
        Restore(_redo.Pop());
        SetStatus(T("Redone"), Ui.Muted);
    }

    void ScheduleSave()
    {
        _saveState.Text = T("Saving…");
        _saveState.ForeColor = Ui.Muted;
        UpdatePageHeader();
        _pages.Invalidate();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void Save()
    {
        try
        {
            var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(_profile, Profile.Json), Profile.Json)!;
            copy.Validate();
            copy.Save(_server.ProfilePath);
            SetStatus(T("Saved {0:HH:mm:ss} · sent to the tablet", DateTime.Now), Ui.Good);
        }
        catch (Exception ex)
        {
            SetStatus(T("Couldn't save: {0}", ex.Message), Ui.Accent);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_saveTimer.Enabled) { _saveTimer.Stop(); Save(); }
        base.OnFormClosing(e);
    }
}
