using System.Diagnostics;
using System.Drawing.Drawing2D;
using MyDeck.Server.Core;
using MyDeck.Server.Editor;

namespace MyDeck.Server;

public sealed class MainForm : DarkForm
{
    static readonly Color Warn = ColorTranslator.FromHtml("#FFC53D");

    readonly DeckServer _server;
    readonly Updater _updater = new();
    readonly NotifyIcon _tray;
    readonly ModernList _log;
    readonly Label _status, _address;
    readonly Panel _banner;
    readonly Label _bannerText;
    bool _quitting, _hintShown, _startHidden;

    public MainForm(string root, bool startHidden = false)
    {
        _startHidden = startHidden;
        Text = "MyDeck";
        Icon = Fx.AppIcon;
        BackColor = Ui.Bg;
        ForeColor = Ui.Ink;
        Font = Ui.Body;
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Ui.Bg };
        var logo = Fx.Logo(40); logo.Location = new Point(22, 20);
        var title = new Label { Text = "MyDeck", Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, ForeColor = Ui.Ink, Location = new Point(72, 14) };
        _status = new Label { AutoSize = true, ForeColor = Ui.Muted, Location = new Point(75, 48), Text = T("Waiting for a tablet…") };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 120, BackColor = Ui.Bg, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 22, 18, 0) };
        var settings = ModernButton.Icon(Fx.I.Settings, T("Settings"));
        settings.Click += (_, _) => OpenSettings();
        var feedback = ModernButton.Icon(Fx.I.Feedback, T("Send feedback"));
        feedback.Click += (_, _) => OpenFeedback();
        tools.Controls.AddRange(new Control[] { settings, feedback });
        header.Controls.AddRange(new Control[] { logo, title, _status, tools });

        _banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Ui.Bg, Padding = new Padding(20, 4, 20, 8), Visible = false };
        var bannerCard = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card };
        bannerCard.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Ui.Good);
            e.Graphics.FillEllipse(b, 14, bannerCard.Height / 2 - 4, 8, 8);
        };
        _bannerText = new Label { AutoSize = false, ForeColor = Ui.Ink, Location = new Point(30, 0), TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Body };
        var install = new ModernButton { Text = T("Restart and update"), Kind = ButtonKind.Primary, AutoSize = true, Height = 30 };
        install.Click += (_, _) => InstallUpdate();
        bannerCard.Controls.AddRange(new Control[] { _bannerText, install });
        bannerCard.Resize += (_, _) =>
        {
            install.Location = new Point(bannerCard.Width - install.Width - 6, (bannerCard.Height - install.Height) / 2);
            _bannerText.SetBounds(30, 0, Math.Max(10, install.Left - 40), bannerCard.Height);
        };
        _banner.Controls.Add(bannerCard);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(20, 0, 20, 0) };
        _address = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.TopLeft };
        _log = new ModernList { Dock = DockStyle.Fill, ItemHeight = 26, Font = new Font("Segoe UI", 9.5f), EmptyText = T("Nothing yet.") };
        _log.DrawItem += DrawLogItem;
        body.Controls.Add(_log);
        body.Controls.Add(_address);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, BackColor = Ui.Bg, Padding = new Padding(20, 14, 20, 0), WrapContents = false };
        bottom.Controls.Add(MakeButton(T("Open editor"), true, (_, _) => OpenEditor()));
        bottom.Controls.Add(MakeButton(T("Virtual deck"), false, (_, _) => VirtualDeck.Open(_server!)));
        bottom.Controls.Add(MakeButton(T("Reload profile"), false, (_, _) => _server!.ReloadProfile()));
        bottom.Controls.Add(MakeButton(T("Open folder"), false, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_server!.Root}\""))));

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(_banner);
        Controls.Add(header);

        var menu = new ContextMenuStrip();
        menu.Items.Add(T("Show MyDeck"), null, (_, _) => ShowWindow());
        menu.Items.Add(T("Open editor"), null, (_, _) => OpenEditor());
        menu.Items.Add(T("Virtual deck"), null, (_, _) => VirtualDeck.Open(_server!));
        menu.Items.Add(T("Settings"), null, (_, _) => { ShowWindow(); OpenSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Quit"), null, (_, _) => { _quitting = true; Close(); });
        _tray = new NotifyIcon { Icon = Fx.AppIcon, Text = "MyDeck", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();

        _server = new DeckServer(root);
        _server.Log += (text, kind) => UI(() => AddLog(text, kind));
        _server.Log += FileLog;
        _server.Log += (text, _) => Feedback.Note(text);
        _server.ClientsChanged += () => UI(UpdateStatus);
        _server.ConfirmPairing = ConfirmPairing;
        var ips = DeckServer.LocalIPv4().Select(ip => $"http://{ip}:{DeckServer.HttpPort}").ToList();
        _address.Text = ips.Count > 0
            ? T("To install MyDeck on a tablet, open this address in its browser: {0}", string.Join("  ·  ", ips))
            : T("No network connection.");
        _server.Start();
        try
        {
            if (Autostart.EnsureDefault(_server.Root, _server.ConfigDir))
                AddLog(T("MyDeck will start with Windows. You can turn this off in Settings."), LogKind.Good);
        }
        catch (Exception ex) { AddLog(T("Couldn't set up start with Windows: {0}", ex.Message), LogKind.Warn); }

        _updater.Changed += () => UI(OnUpdaterChanged);
        _updater.StartAutomatic();
        _ = Feedback.Flush();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    Button MakeButton(string text, bool primary, EventHandler onClick)
    {
        var b = new ModernButton
        {
            Text = text,
            Kind = primary ? ButtonKind.Primary : ButtonKind.Secondary,
            AutoSize = true,
            Height = 38,
            Margin = new Padding(0, 0, 8, 0),
        };
        b.Click += onClick;
        return b;
    }

    sealed record LogItem(DateTime At, string Text, LogKind Kind);

    void AddLog(string text, LogKind kind)
    {
        var atEnd = _log.TopIndex + _log.Height / _log.ItemHeight >= _log.Items.Count - 1;
        _log.BeginUpdate();
        _log.Items.Add(new LogItem(DateTime.Now, text, kind));
        while (_log.Items.Count > 500) _log.Items.RemoveAt(0);
        _log.EndUpdate();
        if (atEnd) _log.ScrollToEnd();
        if (kind == LogKind.Error && !Visible)
            _tray.ShowBalloonTip(4000, "MyDeck", text, ToolTipIcon.Warning);
    }

    readonly object _fileLogLock = new();

    void FileLog(string text, LogKind kind)
    {
        try
        {
            lock (_fileLogLock)
            {
                var path = Path.Combine(_server.Root, "logs", "server.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kind}] {text}{Environment.NewLine}");
            }
        }
        catch { }
    }

    void DrawLogItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _log.Items[e.Index] is not LogItem item) return;
        var dot = item.Kind switch
        {
            LogKind.Good => Ui.Good, LogKind.Warn => Warn, LogKind.Error => Ui.Bad, LogKind.Muted => Ui.Border, _ => Ui.Cyan
        };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(dot)) e.Graphics.FillEllipse(b, e.Bounds.X + 12, e.Bounds.Y + 10, 7, 7);
        TextRenderer.DrawText(e.Graphics, item.At.ToString("HH:mm:ss"), _log.Font, new Point(e.Bounds.X + 28, e.Bounds.Y + 4), Ui.Muted);
        TextRenderer.DrawText(e.Graphics, item.Text, _log.Font,
            new Rectangle(e.Bounds.X + 96, e.Bounds.Y + 4, e.Bounds.Width - 104, e.Bounds.Height - 4),
            item.Kind == LogKind.Muted ? Ui.Muted : Ui.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void UpdateStatus()
    {
        var devices = _server.ConnectedDevices;
        _status.Text = devices.Count == 0 ? T("Waiting for a tablet…") : T("Connected: {0}", string.Join(", ", devices));
        _status.ForeColor = devices.Count == 0 ? Ui.Muted : Ui.Good;
        var tip = devices.Count == 0 ? T("MyDeck · waiting for a tablet") : T("MyDeck · {0} connected", devices.Count);
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    void UI(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }

    Task<bool> ConfirmPairing(string device, string code)
    {
        var tcs = new TaskCompletionSource<bool>();
        UI(() =>
        {
            ShowWindow();
            var answer = MessageBox.Show(this,
                T("{0} wants to connect to MyDeck.\n\nCode on the tablet:  {1}\n\nAllow it if the codes match.", device, code),
                T("MyDeck · New device"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            tcs.TrySetResult(answer == DialogResult.Yes);
        });
        return tcs.Task;
    }

    EditorForm? _editor;

    void OpenEditor()
    {
        try
        {
            if (_editor is { IsDisposed: false }) { _editor.Activate(); return; }
            _editor = new EditorForm(_server) { Icon = Fx.AppIcon };
            _editor.Show();
        }
        catch (Exception ex)
        {
            AddLog(T("Couldn't open the editor: {0}", ex.Message), LogKind.Error);
            try { File.AppendAllText(Path.Combine(_server.Root, "logs", "error.log"), $"{DateTime.Now:s} {ex}{Environment.NewLine}{Environment.NewLine}"); } catch { }
        }
    }

    void OpenSettings()
    {
        using var dlg = new SettingsDialog(_server, _updater);
        dlg.ShowDialog(this);
        if (dlg.RestartNeeded) Restart(hidden: false);
    }

    void OpenFeedback()
    {
        using var dlg = new FeedbackDialog(_server);
        dlg.ShowDialog(this);
    }

    void OnUpdaterChanged()
    {
        var ready = _updater.State == Updater.Phase.Ready;
        _bannerText.Text = ready ? T("MyDeck {0} is ready to install.", _updater.Latest) : "";
        if (ready && !_banner.Visible)
        {
            _banner.Visible = true;
            if (AppSettings.Current.AutoUpdate && !Visible && _editor is not { IsDisposed: false })
                InstallUpdate();
            else if (!Visible)
                _tray.ShowBalloonTip(5000, "MyDeck", T("Version {0} is ready. Click to install.", _updater.Latest), ToolTipIcon.Info);
        }
        if (_updater.State == Updater.Phase.Failed && _banner.Visible) _banner.Visible = false;
    }

    void InstallUpdate()
    {
        _editor?.Close();
        if (_editor is { IsDisposed: false }) return;
        if (!_updater.Install(_server.Root, hidden: !Visible))
        {
            MessageBox.Show(this, T("The update couldn't be installed: {0}\n\nYou can download it from {1}.", _updater.Error, Updater.ReleasesPage),
                "MyDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _quitting = true;
        Close();
    }

    void Restart(bool hidden)
    {
        _editor?.Close();
        if (_editor is { IsDisposed: false }) return;
        var args = $"\"{_server.Root}\" --updated" + (hidden ? " --tepside" : "");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false });
        _quitting = true;
        Close();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            _startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_hintShown)
            {
                _tray.ShowBalloonTip(3000, T("MyDeck is still running"), T("Double-click the tray icon to open it."), ToolTipIcon.Info);
                _hintShown = true;
            }
            return;
        }
        _tray.Visible = false;
        _server.Dispose();
        base.OnFormClosing(e);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
