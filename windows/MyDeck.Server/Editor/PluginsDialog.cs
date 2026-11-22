using System.Diagnostics;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class PluginsDialog : DarkForm
{
    readonly DeckServer _server;
    readonly ScrollPanel _list;

    public PluginsDialog(DeckServer server)
    {
        _server = server;
        Text = T("Plugins");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = Ui.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 560);
        AllowDrop = true;

        var head = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Ui.Panel };
        head.Controls.Add(new Label { Text = T("Plugins"), Font = Ui.Title, ForeColor = Ui.Ink, AutoSize = true, Location = new Point(22, 16) });
        head.Controls.Add(new Label
        {
            Text = T("Install a .mydeckplugin or .zip file, or drop it on this window."),
            ForeColor = Ui.Muted, AutoSize = true, Location = new Point(24, 50),
        });
        var install = new ModernButton { Text = T("Install plugin"), Glyph = Fx.I.Add, Kind = ButtonKind.Primary, AutoSize = true, Height = 34 };
        install.Click += (_, _) => Browse();
        var folder = ModernButton.Icon(Fx.I.Folder, T("Open plugins folder"));
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{server.Plugins.Folder}\""));
        var refresh = ModernButton.Icon(Fx.I.Refresh, T("Reload"));
        refresh.Click += (_, _) => Fill();
        head.Controls.AddRange(new Control[] { install, folder, refresh });
        head.Resize += (_, _) =>
        {
            install.Location = new Point(head.Width - 22 - install.Width, 18);
            folder.Location = new Point(install.Left - 8 - folder.Width, 18);
            refresh.Location = new Point(folder.Left - 4 - refresh.Width, 18);
        };

        _list = new ScrollPanel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Inset = new Padding(22, 4, 22, 12) };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Ui.Panel };
        var ok = Ui.Button(T("Close"), primary: true, width: 110);
        ok.DialogResult = DialogResult.OK;
        ok.Location = new Point(ClientSize.Width - 22 - 110, 10);
        footer.Controls.Add(ok);
        footer.Controls.Add(new Label
        {
            Text = T("Plugins run commands on your PC. Only install plugins you trust."),
            ForeColor = Ui.Muted, Font = Ui.Small, AutoSize = true, Location = new Point(22, 20),
        });

        Controls.Add(_list);
        Controls.Add(footer);
        Controls.Add(head);
        AcceptButton = ok; CancelButton = ok;

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files) InstallFrom(f);
        };
        Fill();
    }

    void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = T("Install plugin"),
            Filter = T("MyDeck plugin") + "|*.mydeckplugin;*.zip|" + T("All files") + "|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) InstallFrom(dlg.FileName);
    }

    void InstallFrom(string path)
    {
        PluginHost.Package pkg;
        try { pkg = _server.Plugins.Inspect(path); }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, T("Install plugin"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var i = pkg.Info;
        var lines = new List<string> { i.Name + (i.Version is { Length: > 0 } v ? "  " + v : "") };
        if (i.Author is { Length: > 0 } a) lines.Add(T("By {0}", a));
        if (i.Description is { Length: > 0 } d) lines.Add(d);
        lines.Add("");
        if (i.Actions.Count > 0) lines.Add(T("Actions: {0}", string.Join(", ", i.Actions.Select(x => x.Name))));
        if (i.Data.Count > 0) lines.Add(T("Data tiles: {0}", string.Join(", ", i.Data.Select(x => x.Name))));
        lines.Add("");
        if (pkg.Existing is { } old) lines.Add(T("This replaces the installed version {0}.", old.Version ?? "?"));
        lines.Add(T("Plugins run commands on your PC. Only install plugins you trust."));
        var answer = MessageBox.Show(this, string.Join(Environment.NewLine, lines), T("Install plugin"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) { _server.Plugins.Discard(pkg); return; }
        try { _server.Plugins.Install(pkg); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, T("The plugin couldn't be installed: {0}", ex.Message), T("Install plugin"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Fill();
    }

    void Fill()
    {
        var c = _list.Content;
        c.SuspendLayout();
        foreach (Control x in c.Controls.Cast<Control>().ToList()) x.Dispose();
        c.Controls.Clear();
        _server.Plugins.Reload();
        const int W = 556;
        if (_server.Plugins.All.Count == 0)
            c.Controls.Add(new Label { Text = T("No plugins installed yet."), ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(2, 12, 0, 0) });
        foreach (var pl in _server.Plugins.All)
        {
            var card = new CardStack { MinimumSize = new Size(W, 0), MaximumSize = new Size(W, 0), Padding = new Padding(14, 10, 14, 12) };
            var top = new Panel { Width = W - 28, Height = 30, BackColor = Ui.Card, Margin = Padding.Empty };
            var name = new Label
            {
                Text = pl.Name + (pl.Version is { Length: > 0 } v ? "   " + v : ""), Font = Ui.Strong, ForeColor = Ui.Ink,
                AutoSize = false, Location = new Point(0, 4), Size = new Size(W - 28 - 130, 22), AutoEllipsis = true,
            };
            var toggle = new ModernToggle { Checked = pl.Enabled, Width = 44, Location = new Point(W - 28 - 84, 2) };
            var id = pl.Id;
            toggle.CheckedChanged += (_, _) => _server.Plugins.SetEnabled(id, toggle.Checked);
            ModernButton.SharedTip.SetToolTip(toggle, T("On / off"));
            var remove = ModernButton.Icon(Fx.I.Delete, T("Remove"));
            remove.Location = new Point(W - 28 - 34, 0);
            var plName = pl.Name;
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(this, T("Remove the plugin \"{0}\"? Tiles that use it will stop working.", plName), T("Remove"),
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                try { _server.Plugins.Uninstall(id); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "MyDeck"); }
                Fill();
            };
            top.Controls.AddRange(new Control[] { name, toggle, remove });
            card.Controls.Add(top);
            if (pl.Description is { Length: > 0 } d)
                card.Controls.Add(new Label { Text = d, ForeColor = Ui.Ink2, MaximumSize = new Size(W - 28, 0), AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
            var parts = new List<string>();
            if (pl.Author is { Length: > 0 } au) parts.Add(T("By {0}", au));
            if (pl.Actions.Count > 0) parts.Add(T("Actions: {0}", string.Join(", ", pl.Actions.Select(a => a.Name))));
            if (pl.Data.Count > 0) parts.Add(T("Data tiles: {0}", string.Join(", ", pl.Data.Select(a => a.Name))));
            card.Controls.Add(new Label { Text = string.Join("   ·   ", parts), ForeColor = Ui.Muted, Font = Ui.Small, MaximumSize = new Size(W - 28, 0), AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            c.Controls.Add(card);
        }
        c.ResumeLayout();
    }
}
