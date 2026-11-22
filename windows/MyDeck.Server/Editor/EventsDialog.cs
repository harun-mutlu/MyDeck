using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class EventsDialog : DarkForm
{
    public new List<DeckEvent> Events { get; }
    public Dictionary<string, string> Vars { get; }

    readonly DeckServer _server;
    readonly Profile _profile;
    readonly ModernList _events, _vars;
    readonly ComboBox _when;
    readonly TextBox _arg;
    readonly Label _argCaption;
    readonly Button _do, _pickApp;
    readonly ModernToggle _enabled;
    readonly FlowLayoutPanel _editor;
    bool _loading;

    public EventsDialog(Profile profile, DeckServer server)
    {
        _server = server; _profile = profile;
        Events = (profile.Events ?? new()).Select(e => new DeckEvent
        {
            When = e.When, Arg = e.Arg, Enabled = e.Enabled, Do = e.Do is null ? null : ActionText.Clone(e.Do),
        }).ToList();
        Vars = new Dictionary<string, string>(profile.Vars ?? new(), StringComparer.OrdinalIgnoreCase);

        Text = T("Variables and events");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 540);

        var left = Ui.Stack();
        left.Location = new Point(18, 4);
        left.Controls.Add(Ui.Caption(T("Events · when something happens")));
        _events = MakeList(400, 190, i => Events[i].Enabled ? Ui.Ink : Ui.Muted, i => Describe(Events[i]));
        _events.SelectedIndexChanged += (_, _) => ShowEvent();
        left.Controls.Add(_events);
        var evButtons = Ui.Row();
        evButtons.Margin = new Padding(0, 8, 0, 0);
        var add = Ui.Button(T("+ Event"), primary: true, width: 96);
        add.Click += (_, _) =>
        {
            Events.Add(new DeckEvent { When = "start", Do = new TileAction { Type = "multi", Steps = new() } });
            RefreshEvents(Events.Count - 1);
        };
        var del = Ui.Button(T("Delete"), width: 80);
        del.Click += (_, _) => { if (SelEvent() is { } i) { Events.RemoveAt(i); RefreshEvents(Math.Min(i, Events.Count - 1)); } };
        evButtons.Controls.AddRange(new Control[] { add, del });
        left.Controls.Add(evButtons);

        _editor = Ui.Stack();
        _editor.Controls.Add(Ui.Caption(T("When")));
        _when = Ui.Combo(); _when.Width = 400;
        foreach (var (_, n) in DeckEvent.Triggers) _when.Items.Add(n);
        _when.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || SelEvent() is not { } i) return;
            Events[i].When = DeckEvent.Triggers[Math.Max(0, _when.SelectedIndex)].Key;
            UpdateArg(Events[i]);
            _events.Invalidate();
        };
        _editor.Controls.Add(_when);
        _argCaption = Ui.Caption(T("App"));
        _editor.Controls.Add(_argCaption);
        var argRow = Ui.Row();
        _arg = Ui.TextBox(); _arg.Width = 300;
        _arg.TextChanged += (_, _) => { if (_loading || SelEvent() is not { } i) return; Events[i].Arg = _arg.Text.Trim(); _events.Invalidate(); };
        _pickApp = Ui.Button(T("Choose…"), width: 92);
        _pickApp.Margin = new Padding(8, 0, 0, 0);
        _pickApp.Click += (_, _) => PickApp();
        argRow.Controls.AddRange(new Control[] { _arg, _pickApp });
        _editor.Controls.Add(argRow);
        _do = Ui.Button(T("What it does…"), width: 400);
        _do.Margin = new Padding(0, 12, 0, 0);
        _do.Click += (_, _) => EditDo();
        _editor.Controls.Add(_do);
        _enabled = new ModernToggle { Text = T("On"), Width = 400, Margin = new Padding(0, 10, 0, 0) };
        _enabled.CheckedChanged += (_, _) => { if (_loading || SelEvent() is not { } i) return; Events[i].Enabled = _enabled.Checked; _events.Invalidate(); };
        _editor.Controls.Add(_enabled);
        left.Controls.Add(_editor);
        Controls.Add(left);

        var right = Ui.Stack();
        right.Location = new Point(448, 4);
        right.Controls.Add(Ui.Caption(T("Variables · current values")));
        _vars = MakeList(354, 250, _ => Ui.Ink, i => VarLine(i));
        _vars.DoubleClick += (_, _) => EditVar();
        right.Controls.Add(_vars);
        var varButtons = Ui.Row();
        varButtons.Margin = new Padding(0, 8, 0, 0);
        var addVar = Ui.Button(T("+ Variable"), width: 110); addVar.Click += (_, _) => AddVar();
        var editVar = Ui.Button(T("Change"), width: 100); editVar.Click += (_, _) => EditVar();
        var delVar = Ui.Button(T("Delete"), width: 80); delVar.Click += (_, _) => DeleteVar();
        varButtons.Controls.AddRange(new Control[] { addVar, editVar, delVar });
        right.Controls.Add(varButtons);
        right.Controls.Add(new Label
        {
            Text = T("Write {name} on a tile to show its value: “Count: {counter}”. Built-in: {app} the app in front, {time}, {date}, {day}.\n\n") +
                   T("Changing a value here also makes it the starting value."),
            ForeColor = Ui.Muted, Width = 354, Height = 110, Margin = new Padding(0, 10, 0, 0),
        });
        Controls.Add(right);

        var ok = Ui.Button(T("OK"), primary: true, width: 110); ok.DialogResult = DialogResult.OK; ok.Location = new Point(ClientSize.Width - 236, ClientSize.Height - 46);
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(ClientSize.Width - 118, ClientSize.Height - 46);
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok; CancelButton = cancel;

        RefreshEvents(Events.Count > 0 ? 0 : -1);
        RefreshVars();
    }

    static ModernList MakeList(int w, int h, Func<int, Color> color, Func<int, string> text)
    {
        var l = new ModernList { Width = w, Height = h, ItemHeight = 28 };
        l.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            ModernList.DrawSelection(e, Ui.Cyan);
            TextRenderer.DrawText(e.Graphics, text(e.Index), l.Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 5, e.Bounds.Width - 16, 20),
                color(e.Index), TextFormatFlags.EndEllipsis);
        };
        return l;
    }

    int? SelEvent() => _events.SelectedIndex >= 0 && _events.SelectedIndex < Events.Count ? _events.SelectedIndex : null;

    string Describe(DeckEvent e)
    {
        var when = DeckEvent.Triggers.FirstOrDefault(t => t.Key == e.When).Name ?? e.When;
        var arg = string.IsNullOrWhiteSpace(e.Arg) || e.When is "start" or "connect" ? "" : $" · {e.Arg}";
        return $"{when}{arg}  →  {ActionText.Describe(e.Do, _profile)}";
    }

    void RefreshEvents(int select)
    {
        _events.BeginUpdate();
        _events.Items.Clear();
        foreach (var _ in Events) _events.Items.Add("");
        _events.EndUpdate();
        if (select >= 0 && select < Events.Count) _events.SelectedIndex = select;
        ShowEvent();
    }

    void ShowEvent()
    {
        var i = SelEvent();
        _editor.Visible = i is not null;
        if (i is null) return;
        _loading = true;
        var e = Events[i.Value];
        _when.SelectedIndex = Math.Max(0, Array.FindIndex(DeckEvent.Triggers, t => t.Key == e.When));
        _arg.Text = e.Arg ?? "";
        _enabled.Checked = e.Enabled;
        UpdateArg(e);
        _loading = false;
    }

    void UpdateArg(DeckEvent e)
    {
        var needs = e.When is not ("start" or "connect");
        _argCaption.Visible = _arg.Visible = needs;
        _pickApp.Visible = e.When is "app" or "appleave" or "var";
        _argCaption.Text = e.When switch { "var" => T("VARIABLE"), "time" => T("TIME (HH:MM, E.G. 08:30)"), _ => T("APP (PROCESS NAME)") };
        _arg.PlaceholderText = e.When switch { "var" => "sayac", "time" => "08:30", _ => "photoshop" };
        _do.Text = T("What it does… ({0} steps)", e.Do?.Steps?.Count ?? (e.Do is null ? 0 : 1));
    }

    void PickApp()
    {
        if (SelEvent() is not { } i) return;
        if (Events[i].When != "var") { AppPicker.Show(_pickApp, name => _arg.Text = name); return; }
        var m = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var n in ActionText.VarNames(_server, _profile).Where(Variables.IsValidName))
            m.Items.Add(n, null, (_, _) => _arg.Text = n);
        if (m.Items.Count == 0) m.Items.Add(T("(no variables yet)")).Enabled = false;
        m.Show(_pickApp, new Point(0, _pickApp.Height));
    }

    void EditDo()
    {
        if (SelEvent() is not { } i) return;
        var e = Events[i];
        var steps = e.Do is null ? new List<TileAction>() : e.Do.Type == "multi" ? e.Do.Steps ?? new() : new List<TileAction> { e.Do };
        using var dlg = new StepsDialog(T("On event"), steps, _server, _profile);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        e.Do = new TileAction { Type = "multi", Steps = dlg.Result };
        UpdateArg(e);
        _events.Invalidate();
    }

    List<string> _varNames = new();

    string VarLine(int i)
    {
        var n = _varNames[i];
        var cur = _server.Vars.Has(n) ? _server.Vars.Get(n) : Vars.GetValueOrDefault(n, "");
        return $"{n}  =  {(cur.Length == 0 ? T("(empty)") : cur)}";
    }

    void RefreshVars()
    {
        _varNames = _server.Vars.All.Keys.Concat(Vars.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        _vars.BeginUpdate();
        _vars.Items.Clear();
        foreach (var _ in _varNames) _vars.Items.Add("");
        _vars.EndUpdate();
    }

    void AddVar()
    {
        var name = Prompt.Ask(this, T("New variable"), T("Name (letters, digits, _ and -):"))?.Trim();
        if (name is null) return;
        if (!Variables.IsValidName(name))
        {
            MessageBox.Show(this, T("This name can't be used. Use letters, digits, _ and -. app, time, date and day are built-in."), T("Variable"));
            return;
        }
        var value = Prompt.Ask(this, name, T("Starting value:"), "0");
        if (value is null) return;
        Vars[name] = value;
        _server.Vars.Set(name, value);
        RefreshVars();
    }

    void EditVar()
    {
        if (_vars.SelectedIndex < 0) return;
        var name = _varNames[_vars.SelectedIndex];
        var value = Prompt.Ask(this, name, T("Value:"), _server.Vars.Get(name));
        if (value is null) return;
        Vars[name] = value;
        _server.Vars.Set(name, value);
        RefreshVars();
    }

    void DeleteVar()
    {
        if (_vars.SelectedIndex < 0) return;
        var name = _varNames[_vars.SelectedIndex];
        Vars.Remove(name);
        _server.Vars.Remove(name);
        RefreshVars();
    }
}
