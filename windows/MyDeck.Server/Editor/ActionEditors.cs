using System.Text.Json;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

static class ActionText
{
    public static readonly (string Type, string Name)[] StepTypes =
    {
        ("keys", T("Keyboard shortcut")), ("media", T("Media and volume")), ("run", T("Open a program or file")), ("url", T("Open a web address")),
        ("delay", T("Wait")), ("setvar", T("Set a variable")), ("if", T("Condition (if…)")), ("multi", T("Several actions")),
        ("page", T("Switch the tablet to a page")), ("back", T("Send the tablet back")), ("lock", T("Lock the screen")),
    };

    public static readonly (string Key, string Name)[] MediaKeys =
    {
        ("playpause", T("Play / Pause")), ("next", T("Next")), ("prev", T("Previous")), ("stop", T("Stop")),
        ("volup", T("Volume +")), ("voldown", T("Volume −")), ("mute", T("Mute")),
    };

    static string Name((string Key, string Name)[] list, string? key, string def = "") =>
        list.FirstOrDefault(x => x.Key == key).Name ?? def;

    static string Short(string? s, int n = 28) =>
        string.IsNullOrEmpty(s) ? "…" : s.Length <= n ? s : s[..(n - 1)] + "…";

    public static string Describe(TileAction? a, Profile? p)
    {
        if (a is null) return T("Nothing");
        string PageName(string? id) => p?.Pages.FirstOrDefault(x => x.Id == id) is { } pg && pg.Name.Length > 0 ? pg.Name : id ?? "…";
        return a.Type switch
        {
            "keys" => T("Keys · {0}", Short(a.Keys)),
            "media" => $"{T("Media")} · {Name(MediaKeys, a.Key, a.Key ?? "")}",
            "run" => T("Open · {0}", Short(Path.GetFileName(a.Path ?? ""))),
            "url" => T("Web · {0}", Short(a.Url)),
            "lock" => T("Lock the screen"),
            "delay" => T("Wait · {0} ms", a.Ms),
            "page" or "folder" => T("Go to page · {0}", PageName(a.Page)),
            "back" => T("Go back"),
            "plugin" => T("Plugin action"),
            "setvar" => a.Op switch
            {
                "add" => $"{a.Var} + {(string.IsNullOrEmpty(a.Value) ? "1" : a.Value)}",
                "sub" => $"{a.Var} − {(string.IsNullOrEmpty(a.Value) ? "1" : a.Value)}",
                "toggle" => T("{0} on ↔ off", a.Var),
                "append" => T("{0}: append “{1}”", a.Var, Short(a.Value, 16)),
                _ => $"{a.Var} = {Short(a.Value, 20)}",
            },
            "if" => T("If {0} → {1} steps", Condition(a), a.Steps?.Count ?? 0) + (a.Else is { Count: > 0 } e ? T(", else {0}", e.Count) : ""),
            "multi" => a.Steps is not { Count: > 0 } st ? T("No steps yet") : T("{0} steps: ", st.Count) + string.Join(" → ", st.Take(3).Select(s => Describe(s, p))),
            _ => a.Type,
        };
    }

    public static string Condition(TileAction a)
    {
        var op = Name(Variables.TestOps, a.Op ?? "eq", T("equals"));
        return a.Op is "empty" or "notempty" or "true" or "false" ? $"{a.Var} {op}" : $"{a.Var} “{a.Value}” {op}";
    }

    public static TileAction Clone(TileAction a) =>
        JsonSerializer.Deserialize<TileAction>(JsonSerializer.Serialize(a, Profile.Json), Profile.Json)!;

    public static TileAction NewStep(string type) => type switch
    {
        "media" => new TileAction { Type = type, Key = "playpause" },
        "delay" => new TileAction { Type = type, Ms = 300 },
        "setvar" => new TileAction { Type = type, Op = "set" },
        "if" => new TileAction { Type = type, Op = "eq", Steps = new(), Else = new() },
        "multi" => new TileAction { Type = type, Steps = new() },
        _ => new TileAction { Type = type },
    };

    public static string[] VarNames(DeckServer server, Profile p) =>
        server.Vars.All.Keys.Concat(p.Vars?.Keys ?? Enumerable.Empty<string>()).Concat(L.Turkish ? Variables.BuiltIn[4..] : Variables.BuiltIn[..4])
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
}

sealed class StepsDialog : DarkForm
{
    public List<TileAction> Result { get; }
    readonly ModernList _list;
    readonly DeckServer _server;
    readonly Profile _profile;

    public StepsDialog(string title, IEnumerable<TileAction>? steps, DeckServer server, Profile profile)
    {
        _server = server; _profile = profile;
        Result = (steps ?? Enumerable.Empty<TileAction>()).Select(ActionText.Clone).ToList();
        Text = title;
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 420);

        _list = new ModernList { Location = new Point(16, 16), Size = new Size(400, 340), ItemHeight = 30 };
        _list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            ModernList.DrawSelection(e, Ui.Cyan);
            TextRenderer.DrawText(e.Graphics, $"{e.Index + 1}.", _list.Font, new Point(e.Bounds.X + 10, e.Bounds.Y + 6), Ui.Muted);
            TextRenderer.DrawText(e.Graphics, ActionText.Describe(Result[e.Index], _profile), _list.Font,
                new Rectangle(e.Bounds.X + 36, e.Bounds.Y + 6, e.Bounds.Width - 40, 20), Ui.Ink, TextFormatFlags.EndEllipsis);
        };
        _list.DoubleClick += (_, _) => EditSelected();
        Controls.Add(_list);

        var side = Ui.Stack();
        side.Location = new Point(428, 16);
        var add = Ui.Button(T("+ Add step"), primary: true, width: 116);
        add.Click += (_, _) => AddStep(add);
        var edit = Ui.Button(T("Edit"), width: 116); edit.Click += (_, _) => EditSelected();
        var dup = Ui.Button(T("Duplicate"), width: 116); dup.Click += (_, _) => { if (Sel() is { } i) { Result.Insert(i + 1, ActionText.Clone(Result[i])); Refresh(i + 1); } };
        var del = Ui.Button(T("Delete"), width: 116); del.Click += (_, _) => { if (Sel() is { } i) { Result.RemoveAt(i); Refresh(Math.Min(i, Result.Count - 1)); } };
        var up = Ui.Button(T("▲ Up"), width: 116); up.Click += (_, _) => Move(-1);
        var down = Ui.Button(T("▼ Down"), width: 116); down.Click += (_, _) => Move(1);
        side.Controls.AddRange(new Control[] { add, edit, dup, del, up, down });
        Controls.Add(side);

        var hint = new Label
        {
            Text = T("Steps run from top to bottom. Add a “Wait” step to give programs time to open."),
            ForeColor = Ui.Muted, Location = new Point(16, 362), Size = new Size(320, 50),
        };
        var ok = Ui.Button(T("OK"), primary: true, width: 100); ok.DialogResult = DialogResult.OK; ok.Location = new Point(338, 374);
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(444, 374);
        Controls.AddRange(new Control[] { hint, ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
        Refresh(Result.Count > 0 ? 0 : -1);
    }

    int? Sel() => _list.SelectedIndex >= 0 && _list.SelectedIndex < Result.Count ? _list.SelectedIndex : null;

    void Refresh(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var _ in Result) _list.Items.Add("");
        _list.EndUpdate();
        if (select >= 0 && select < Result.Count) _list.SelectedIndex = select;
    }

    new void Move(int d)
    {
        if (Sel() is not { } i || i + d < 0 || i + d >= Result.Count) return;
        (Result[i], Result[i + d]) = (Result[i + d], Result[i]);
        Refresh(i + d);
    }

    void AddStep(Control anchor)
    {
        var m = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var (type, name) in ActionText.StepTypes)
            m.Items.Add(name, null, (_, _) =>
            {
                using var dlg = new StepDialog(ActionText.NewStep(type), _server, _profile);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var at = Sel() is { } i ? i + 1 : Result.Count;
                Result.Insert(at, dlg.Result);
                Refresh(at);
            });
        m.Show(anchor, new Point(0, anchor.Height));
    }

    void EditSelected()
    {
        if (Sel() is not { } i) return;
        using var dlg = new StepDialog(Result[i], _server, _profile);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Result[i] = dlg.Result;
        Refresh(i);
    }
}

sealed class StepDialog : DarkForm
{
    public TileAction Result { get; private set; }
    readonly DeckServer _server;
    readonly Profile _profile;
    readonly ComboBox _type;
    readonly FlowLayoutPanel _fields;

    public StepDialog(TileAction action, DeckServer server, Profile profile)
    {
        _server = server; _profile = profile;
        Result = ActionText.Clone(action);
        Text = T("Step");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 400);

        var stack = Ui.Stack();
        stack.Location = new Point(18, 4);
        stack.Controls.Add(Ui.Caption(T("What it does")));
        _type = Ui.Combo(); _type.Width = 380;
        foreach (var (_, n) in ActionText.StepTypes) _type.Items.Add(n);
        var idx = Array.FindIndex(ActionText.StepTypes, x => x.Type == (Result.Type == "folder" ? "page" : Result.Type));
        if (idx < 0) { _type.Items.Add(ActionText.Describe(Result, profile)); idx = _type.Items.Count - 1; }
        _type.SelectedIndex = idx;
        _type.SelectedIndexChanged += (_, _) =>
        {
            if (_type.SelectedIndex < ActionText.StepTypes.Length)
            {
                var t = ActionText.StepTypes[_type.SelectedIndex].Type;
                if (t != Result.Type) Result = ActionText.NewStep(t);
            }
            Build();
        };
        stack.Controls.Add(_type);
        _fields = Ui.Stack();
        stack.Controls.Add(_fields);
        Controls.Add(stack);

        var ok = Ui.Button(T("OK"), primary: true, width: 100); ok.DialogResult = DialogResult.OK; ok.Location = new Point(198, 354);
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(304, 354);
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
        Build();
    }

    TextBox Field(string caption, string? value, string placeholder, Action<string?> set)
    {
        _fields.Controls.Add(Ui.Caption(caption));
        var tb = Ui.TextBox(); tb.Width = 380; tb.Text = value ?? ""; tb.PlaceholderText = placeholder;
        tb.TextChanged += (_, _) => set(string.IsNullOrWhiteSpace(tb.Text) ? null : tb.Text);
        _fields.Controls.Add(tb);
        return tb;
    }

    void Hint(string text) =>
        _fields.Controls.Add(new Label { Text = text, ForeColor = Ui.Muted, Width = 380, Height = 44, Margin = new Padding(0, 6, 0, 0) });

    ComboBox Choice((string Key, string Name)[] list, string? key, Action<string> set, string caption)
    {
        _fields.Controls.Add(Ui.Caption(caption));
        var cb = Ui.Combo(); cb.Width = 380;
        foreach (var (_, n) in list) cb.Items.Add(n);
        cb.SelectedIndex = Math.Max(0, Array.FindIndex(list, x => x.Key == key));
        cb.SelectedIndexChanged += (_, _) => set(list[Math.Max(0, cb.SelectedIndex)].Key);
        _fields.Controls.Add(cb);
        return cb;
    }

    void VarField()
    {
        _fields.Controls.Add(Ui.Caption(T("Variable")));
        var cb = new ComboBox
        {
            BackColor = Ui.Field, ForeColor = Ui.Ink, FlatStyle = FlatStyle.Flat, Width = 380,
            DropDownStyle = ComboBoxStyle.DropDown, Text = Result.Var ?? "",
        };
        cb.Items.AddRange(ActionText.VarNames(_server, _profile));
        cb.Text = Result.Var ?? "";
        cb.TextChanged += (_, _) => Result.Var = cb.Text.Trim();
        _fields.Controls.Add(cb);
    }

    Button StepsButton(string text, Func<List<TileAction>?> get, Action<List<TileAction>> set)
    {
        var b = Ui.Button(T("{0} ({1} steps)…", text, get()?.Count ?? 0), width: 380);
        b.Margin = new Padding(0, 8, 0, 0);
        b.Click += (_, _) =>
        {
            using var dlg = new StepsDialog(text, get(), _server, _profile);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(dlg.Result);
            b.Text = T("{0} ({1} steps)…", text, dlg.Result.Count);
        };
        _fields.Controls.Add(b);
        return b;
    }

    void Build()
    {
        _fields.SuspendLayout();
        _fields.Controls.Clear();
        var a = Result;
        switch (a.Type)
        {
            case "keys":
                Field(T("Keys"), a.Keys, T("e.g. ctrl+shift+s"), v => a.Keys = v?.Trim().ToLowerInvariant());
                Hint(T("Join keys with +: ctrl, shift, alt, win, a–z, 0–9, f1–f24, enter, esc, tab, space, arrows…"));
                break;
            case "media":
                Choice(ActionText.MediaKeys, a.Key, v => a.Key = v, T("Key"));
                break;
            case "run":
                var path = Field(T("Program, file or folder"), a.Path, @"C:\…\program.exe", v => a.Path = v);
                var browse = Ui.Button(T("Browse…"), width: 380);
                browse.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Title = T("Choose a program or file"), Filter = T("Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*") };
                    if (dlg.ShowDialog(this) == DialogResult.OK) path.Text = dlg.FileName;
                };
                _fields.Controls.Add(browse);
                Field(T("Arguments"), a.Args, T("optional"), v => a.Args = v);
                break;
            case "url":
                Field(T("Address"), a.Url, "https://…", v => a.Url = v);
                Hint(T("You can use {variable} in the address, e.g. https://site.com/search?q={query}"));
                break;
            case "delay":
                _fields.Controls.Add(Ui.Caption(T("Duration (milliseconds)")));
                var ms = Ui.Number(0, 10000); ms.Width = 120; ms.Increment = 100; ms.Value = Math.Clamp(a.Ms, 0, 10000);
                ms.ValueChanged += (_, _) => a.Ms = (int)ms.Value;
                _fields.Controls.Add(ms);
                Hint(T("1000 ms = 1 second. Up to 10 seconds."));
                break;
            case "page" or "folder":
                _fields.Controls.Add(Ui.Caption(T("Page")));
                var pg = Ui.Combo(); pg.Width = 380;
                foreach (var x in _profile.Pages) pg.Items.Add((x.Folder ? "📁 " : "") + (x.Name.Length > 0 ? x.Name : x.Id));
                pg.SelectedIndex = _profile.Pages.FindIndex(x => x.Id == a.Page);
                if (pg.SelectedIndex < 0 && _profile.Pages.Count > 0) { pg.SelectedIndex = 0; a.Page = _profile.Pages[0].Id; }
                pg.SelectedIndexChanged += (_, _) => { if (pg.SelectedIndex >= 0) { var target = _profile.Pages[pg.SelectedIndex]; a.Page = target.Id; a.Type = target.Folder ? "folder" : "page"; } };
                _fields.Controls.Add(pg);
                Hint(T("All connected tablets (and the virtual deck) switch to this page."));
                break;
            case "setvar":
                VarField();
                Choice(Variables.SetOps, a.Op ?? "set", v => a.Op = v, T("Operation"));
                Field(T("Value"), a.Value, T("empty means 1 when adding"), v => a.Value = v);
                Hint(T("Show the variable on a tile as {name}. The value can include other variables: {counter}."));
                break;
            case "if":
                VarField();
                Choice(Variables.TestOps, a.Op ?? "eq", v => a.Op = v, T("Condition"));
                Field(T("Value"), a.Value, T("value to compare"), v => a.Value = v);
                StepsButton(T("If true"), () => a.Steps, v => a.Steps = v);
                StepsButton(T("Otherwise"), () => a.Else, v => a.Else = v);
                break;
            case "multi":
                StepsButton(T("Steps"), () => a.Steps, v => a.Steps = v);
                break;
            case "lock":
                Hint(T("Locks Windows (Win + L)."));
                break;
            case "back":
                Hint(T("Goes to the previous page if the tablet is in a folder, otherwise to the start page."));
                break;
            case "plugin":
                Hint(T("Plugin actions are edited from the tile's own panel."));
                break;
        }
        _fields.ResumeLayout();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK) return;
        if (Result.Type is not ("setvar" or "if")) return;
        var builtIn = Variables.BuiltIn.Contains(Result.Var ?? "", StringComparer.OrdinalIgnoreCase);
        var valid = Variables.IsValidName(Result.Var) || (Result.Type == "if" && builtIn);
        if (valid) return;
        MessageBox.Show(this, builtIn
                ? T("Built-in variables (app, time, date, day) can't be changed. Use them in conditions.")
                : T("The variable name is empty or invalid. Use letters, digits, _ and - (up to 40 characters)."),
            T("Variable"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        e.Cancel = true;
    }
}

sealed class Prompt : DarkForm
{
    readonly TextBox _box;
    public string Value => _box.Text;

    Prompt(string title, string label, string value)
    {
        Text = title;
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 140);
        var l = new Label { Text = label, ForeColor = Ui.Muted, Location = new Point(16, 14), Size = new Size(350, 20) };
        _box = Ui.TextBox(); _box.Location = new Point(16, 40); _box.Width = 348; _box.Text = value;
        var ok = Ui.Button(T("OK"), primary: true, width: 100); ok.DialogResult = DialogResult.OK; ok.Location = new Point(158, 92);
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(264, 92);
        Controls.AddRange(new Control[] { l, _box, ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
    }

    public static string? Ask(IWin32Window owner, string title, string label, string value = "")
    {
        using var p = new Prompt(title, label, value);
        return p.ShowDialog(owner) == DialogResult.OK ? p.Value : null;
    }
}
