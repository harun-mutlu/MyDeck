using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class VirtualDeck : DarkForm
{
    static VirtualDeck? _open;

    public static void Open(DeckServer server)
    {
        if (_open is { IsDisposed: false }) { _open.WindowState = FormWindowState.Normal; _open.Activate(); return; }
        _open = new VirtualDeck(server);
        _open.Show();
    }

    readonly DeckServer _server;
    readonly DeckCanvas _canvas;
    readonly Label _title, _status;
    readonly ModernButton _pin, _prev, _next;
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };
    readonly List<string> _folders = new();
    string? _pageId;
    string? _smartFrom;

    VirtualDeck(DeckServer server)
    {
        _server = server;
        Text = T("MyDeck · Virtual deck");
        BackColor = Ui.Bg; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.Manual;
        var dev = server.LastDeviceSize;
        var w = 720;
        ClientSize = new Size(w, (int)(w * dev.Height / (float)dev.Width) + 38);
        MinimumSize = new Size(360, 260);
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
        Icon = Application.OpenForms.Cast<Form>().FirstOrDefault()?.Icon;

        var bar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Ui.Panel, Padding = new Padding(10, 4, 8, 4) };
        _prev = BarButton("", T("Previous page")); _prev.Click += (_, _) => Swipe(-1);
        _next = BarButton("", T("Next page")); _next.Click += (_, _) => Swipe(1);
        _title = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Ink, Font = new Font("Segoe UI Semibold", 10f), Padding = new Padding(8, 0, 0, 0) };
        _status = new Label { AutoSize = false, Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleRight, ForeColor = Ui.Muted };
        _pin = BarButton(Fx.I.Pin, T("Always on top"));
        _pin.Click += (_, _) => { TopMost = !TopMost; StylePin(); };
        _pin.Dock = DockStyle.Right;
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 76, BackColor = Color.Transparent, Margin = new Padding(0), WrapContents = false };
        left.Controls.AddRange(new Control[] { _prev, _next });
        bar.Controls.Add(_title);
        bar.Controls.Add(_status);
        bar.Controls.Add(_pin);
        bar.Controls.Add(left);

        _canvas = new DeckCanvas { Dock = DockStyle.Fill, PlayMode = true, Server = server, Device = dev, BackColor = Ui.Bg };
        _canvas.IsOn = server.IsOn;
        _canvas.Pressed += OnPressed;
        _canvas.Slid += (t, v) => _server.SetSlider(t, v);
        _canvas.SlideMuteClicked += t => _server.ToggleSliderMute(t);
        _canvas.MediaCommand += (t, cmd, v) => { _server.MediaCommand(t, cmd, v); _canvas.Invalidate(); };
        _canvas.TimerFinished += t => _server.TimerFinished(t);
        Controls.Add(_canvas);
        Controls.Add(bar);

        TopMost = true;
        StylePin();
        LoadProfile();

        _tick.Tick += (_, _) => { _canvas.TickTimers(); _canvas.Invalidate(); };
        _tick.Start();
        server.ProfileReloaded += OnProfileReloaded;
        server.ExternalGoTo += OnExternalGoTo;
        server.SmartPage += OnSmartPage;
        server.VarChanged += OnVarChanged;
        server.DeviceSizeChanged += OnDeviceSize;
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            server.ProfileReloaded -= OnProfileReloaded;
            server.ExternalGoTo -= OnExternalGoTo;
            server.SmartPage -= OnSmartPage;
            server.VarChanged -= OnVarChanged;
            server.DeviceSizeChanged -= OnDeviceSize;
            _open = null;
        };
    }

    static ModernButton BarButton(string glyph, string tip)
    {
        var b = ModernButton.Icon(glyph, tip, size: 30);
        b.Margin = new Padding(0, 0, 2, 0);
        b.TabStop = false;
        return b;
    }

    void StylePin()
    {
        _pin.Kind = TopMost ? ButtonKind.Secondary : ButtonKind.Ghost;
        _pin.Glyph = TopMost ? Fx.I.Pinned : Fx.I.Pin;
    }

    Profile P => _server.CurrentProfile;

    void LoadProfile()
    {
        var p = P;
        _canvas.Profile = p;
        var page = p.Pages.FirstOrDefault(x => x.Id == _pageId) ?? p.Pages.FirstOrDefault(x => x.Id == p.Start) ?? p.Pages[0];
        ShowPage(page);
    }

    void ShowPage(Page page)
    {
        _pageId = page.Id;
        _canvas.Page = page;
        _title.Text = (page.Folder ? "📁 " : "") + (page.Name.Length > 0 ? page.Name : page.Id);
        var swipe = SwipePages();
        _prev.Enabled = _next.Enabled = !page.Folder && swipe.Count > 1;

        var fast = page.Tiles.Any(t => t.Gif is not null || t.Kind is "clock" or "timer" or "media" or "slider" || t.IsLive);
        _tick.Interval = page.Tiles.Any(t => t.Gif is not null) ? 70 : fast ? 250 : 1000;
        _canvas.Invalidate();
    }

    List<Page> SwipePages() => P.Pages.Where(x => !x.Folder).ToList();

    void GoTo(string? id, bool folder)
    {
        var p = P;
        if (p.Pages.FirstOrDefault(x => x.Id == id) is not { } target) return;
        if (folder && _pageId is not null) _folders.Add(_pageId);
        if (!target.Folder && !folder) _folders.Clear();
        ShowPage(target);
    }

    void Back()
    {
        var prev = _folders.Count > 0 ? _folders[^1] : P.Start;
        if (_folders.Count > 0) _folders.RemoveAt(_folders.Count - 1);
        GoTo(prev, false);
    }

    void Swipe(int dir)
    {
        var list = SwipePages();
        var i = list.FindIndex(x => x.Id == _pageId);
        if (i < 0 || i + dir < 0 || i + dir >= list.Count) return;
        _folders.Clear();
        ShowPage(list[i + dir]);
    }

    void OnPressed(Tile t, string gesture)
    {
        if (gesture == "tap" && t.Kind is null or "button")
            switch (t.Action?.Type)
            {
                case "page": GoTo(t.Action.Page, false); return;
                case "folder": GoTo(t.Action.Page, true); return;
                case "back": Back(); return;
            }
        _server.Press(t, gesture, text => BeginInvoke(() => Flash(text)));
        if (gesture != "tap" || t.Kind == "toggle") _ = RedrawSoon();
    }

    async Task RedrawSoon()
    {
        await Task.Delay(120);
        if (!IsDisposed) _canvas.Invalidate();
    }

    void Flash(string text)
    {
        _status.Text = text;
        _status.ForeColor = Ui.Accent;
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) => { t.Dispose(); if (!IsDisposed && _status.Text == text) _status.Text = ""; };
        t.Start();
    }

    void Ui_(Action a) { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }

    void OnProfileReloaded() => Ui_(LoadProfile);

    void OnExternalGoTo(string? id, bool back) => Ui_(() =>
    {
        if (back) Back();
        else GoTo(id, P.Pages.FirstOrDefault(x => x.Id == id)?.Folder == true);
    });

    void OnSmartPage(string? id) => Ui_(() =>
    {
        if (id is not null)
        {
            if (id == _pageId) return;
            _smartFrom ??= _pageId;
            GoTo(id, false);
        }
        else if (_smartFrom is { } from)
        {
            _smartFrom = null;
            if (P.SmartReturn) GoTo(from, false);
        }
    });

    void OnVarChanged(string _) => Ui_(_canvas.Invalidate);

    void OnDeviceSize() => Ui_(() => { _canvas.Device = _server.LastDeviceSize; _canvas.Invalidate(); });
}
