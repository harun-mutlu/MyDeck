using System.Diagnostics;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class SettingsDialog : DarkForm
{
    readonly DeckServer _server;
    readonly Updater _updater;
    readonly Label _updateStatus;
    readonly Button _check;
    readonly ModernTextBox _city;
    readonly Label _cityStatus;

    public bool RestartNeeded { get; private set; }

    public SettingsDialog(DeckServer server, Updater updater)
    {
        _server = server;
        _updater = updater;
        var s = AppSettings.Current;
        Text = T("Settings");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = Ui.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 600);

        var scroll = new ScrollPanel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Inset = new Padding(22, 16, 22, 16) };
        var c = scroll.Content;
        const int W = 460;

        CardStack Card(string title)
        {
            c.Controls.Add(new Label { Text = title, Font = Ui.Strong, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(2, 4, 0, 6) });
            var card = new CardStack { MinimumSize = new Size(W, 0), Margin = new Padding(0, 0, 0, 16) };
            c.Controls.Add(card);
            return card;
        }

        var general = Card(T("General"));
        general.Controls.Add(Ui.Caption(T("Language")));
        var lang = Ui.Combo(); lang.Width = W - 24;
        foreach (var (_, name) in L.Languages) lang.Items.Add(name);
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(L.Languages, l => l.Code == L.Lang));
        var langNote = new Label { ForeColor = Ui.Muted, Font = Ui.Small, AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
        lang.SelectedIndexChanged += (_, _) =>
        {
            var code = L.Languages[Math.Max(0, lang.SelectedIndex)].Code;
            AppSettings.Current.Language = code;
            AppSettings.Save();
            RestartNeeded = code != L.Lang;
            langNote.Text = RestartNeeded ? T("MyDeck will restart to change the language.") : "";
        };
        general.Controls.AddRange(new Control[] { lang, langNote });

        var autostart = new ModernToggle { Text = T("Start with Windows"), Width = W - 24, Checked = Autostart.IsEnabled() };
        autostart.CheckedChanged += (_, _) =>
        {
            try { Autostart.Set(autostart.Checked, _server.Root); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "MyDeck"); }
        };
        var minimized = new ModernToggle { Text = T("Start minimized to the tray"), Width = W - 24, Checked = s.StartMinimized };
        minimized.CheckedChanged += (_, _) => { AppSettings.Current.StartMinimized = minimized.Checked; AppSettings.Save(); };
        general.Controls.AddRange(new Control[] { autostart, minimized });
        general.Controls.Add(new Label
        {
            Text = T("When Windows starts, MyDeck always opens in the tray."),
            ForeColor = Ui.Muted, Font = Ui.Small, MaximumSize = new Size(W - 24, 0), AutoSize = true, Margin = new Padding(0, 2, 0, 4),
        });

        var updates = Card(T("Updates"));
        updates.Controls.Add(new Label { Text = T("Version {0}", Updater.CurrentText), ForeColor = Ui.Ink2, AutoSize = true, Margin = new Padding(0, 6, 0, 4) });
        _updateStatus = new Label { ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(W - 24, 0), Margin = new Padding(0, 0, 0, 6) };
        _check = Ui.Button(T("Check now"), width: 140);
        _check.Click += async (_, _) => await _updater.CheckAsync();
        if (Updater.Supported)
        {
            var auto = new ModernToggle { Text = T("Install updates automatically"), Width = W - 24, Checked = s.AutoUpdate };
            auto.CheckedChanged += (_, _) => { AppSettings.Current.AutoUpdate = auto.Checked; AppSettings.Save(); };
            updates.Controls.Add(auto);
            updates.Controls.Add(_updateStatus);
            updates.Controls.Add(_check);
        }
        else
        {
            _updateStatus.Text = T("This is a personal build. Updates are installed by hand.");
            updates.Controls.Add(_updateStatus);
        }

        var weather = Card(T("Weather"));
        weather.Controls.Add(Ui.Caption(T("City for weather tiles")));
        var cityRow = Ui.Row();
        _city = new ModernTextBox { Width = W - 24 - 110, Text = s.WeatherCity ?? "", PlaceholderText = T("e.g. Istanbul") };
        var find = Ui.Button(T("Find"), width: 100);
        find.Margin = new Padding(10, 0, 0, 0);
        cityRow.Controls.AddRange(new Control[] { _city, find });
        _cityStatus = new Label
        {
            ForeColor = Ui.Muted, Font = Ui.Small, AutoSize = true, MaximumSize = new Size(W - 24, 0), Margin = new Padding(0, 4, 0, 4),
            Text = s.WeatherLat is null ? T("Not set. Weather tiles stay empty until you pick a city.") : T("Data from Open-Meteo."),
        };
        find.Click += async (_, _) => await FindCity(find);
        _city.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await FindCity(find); } };
        weather.Controls.AddRange(new Control[] { cityRow, _cityStatus });

        var folders = Card(T("Files"));
        var open = Ui.Button(T("Open data folder"), width: 200);
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_server.Root}\""));
        var profile = Ui.Button(T("Open profile file"), width: 200);
        profile.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_server.ProfilePath}\"")); } catch { } };
        var frow = Ui.Row(); frow.Margin = new Padding(0, 6, 0, 2);
        frow.Controls.AddRange(new Control[] { open, profile });
        folders.Controls.Add(frow);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Ui.Panel };
        var close = Ui.Button(T("Close"), primary: true, width: 110);
        close.DialogResult = DialogResult.OK;
        close.Location = new Point(ClientSize.Width - 22 - 110, 12);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        footer.Controls.Add(close);
        Controls.Add(scroll);
        Controls.Add(footer);
        AcceptButton = close; CancelButton = close;

        _updater.Changed += OnUpdater;
        FormClosed += (_, _) => _updater.Changed -= OnUpdater;
        OnUpdater();
    }

    void OnUpdater()
    {
        if (!Updater.Supported || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(OnUpdater); return; }
        _check.Enabled = _updater.State is not (Updater.Phase.Checking or Updater.Phase.Downloading);
        _updateStatus.ForeColor = _updater.State == Updater.Phase.Failed ? Ui.Bad : _updater.State == Updater.Phase.Ready ? Ui.Good : Ui.Muted;
        _updateStatus.Text = _updater.State switch
        {
            Updater.Phase.Checking => T("Checking for updates…"),
            Updater.Phase.UpToDate => T("You have the latest version."),
            Updater.Phase.Downloading => T("Downloading version {0}…", _updater.Latest),
            Updater.Phase.Ready => T("Version {0} is ready. Restart MyDeck from the main window to install it.", _updater.Latest),
            Updater.Phase.Failed => T("Couldn't check for updates: {0}", _updater.Error),
            _ => T("Updates come from GitHub ({0}).", Updater.Repo),
        };
    }

    async Task FindCity(Control anchor)
    {
        var q = _city.Text.Trim();
        if (q.Length < 2) return;
        _cityStatus.ForeColor = Ui.Muted;
        _cityStatus.Text = T("Searching…");
        List<WeatherWatcher.Place> places;
        try { places = await WeatherWatcher.Search(q); }
        catch (Exception ex) { _cityStatus.ForeColor = Ui.Bad; _cityStatus.Text = T("Couldn't search: {0}", ex.Message); return; }
        if (places.Count == 0) { _cityStatus.Text = T("No city found with that name."); return; }
        var m = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var p in places)
        {
            var place = p;
            m.Items.Add(p.Detail.Length > 0 ? $"{p.Name}  ·  {p.Detail}" : p.Name, null, (_, _) => Pick(place));
        }
        m.Show(anchor, new Point(0, anchor.Height));
        _cityStatus.Text = T("Pick one from the list.");
    }

    void Pick(WeatherWatcher.Place p)
    {
        var s = AppSettings.Current;
        s.WeatherCity = p.Name;
        s.WeatherLat = p.Lat;
        s.WeatherLon = p.Lon;
        AppSettings.Save();
        _city.Text = p.Name;
        _cityStatus.ForeColor = Ui.Good;
        _cityStatus.Text = T("Saved: {0}. Data from Open-Meteo.", p.Detail.Length > 0 ? $"{p.Name}, {p.Detail}" : p.Name);
        _server.Weather.Refresh();
    }
}
