using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class FeedbackDialog : DarkForm
{
    readonly DeckServer _server;
    readonly ModernSegmented _kind;
    readonly ModernTextBox _message, _email;
    readonly ModernToggle _technical;
    readonly Label _count, _error;
    readonly Button _send;
    readonly FlowLayoutPanel _form, _done;
    readonly Label _doneTitle, _doneText;

    public FeedbackDialog(DeckServer server)
    {
        _server = server;
        Text = T("Send feedback");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = Ui.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 520);

        _form = Ui.Stack();
        _form.Location = new Point(24, 18);
        _form.Controls.Add(new Label { Text = T("Send feedback"), Font = Ui.Title, ForeColor = Ui.Ink, AutoSize = true });
        _form.Controls.Add(new Label { Text = T("Goes straight to the developer. No account needed."), ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 2, 0, 12) });

        _form.Controls.Add(Ui.Caption(T("Topic")));
        _kind = new ModernSegmented { Items = new[] { T("Bug"), T("Suggestion"), T("Other") }, Width = 452 };
        _form.Controls.Add(_kind);

        _form.Controls.Add(Ui.Caption(T("Your message")));
        _message = new ModernTextBox { Multiline = true, Width = 452, Height = 150, MaxLength = Feedback.MaxLength, PlaceholderText = T("What happened? What were you doing?") };
        _form.Controls.Add(_message);
        _count = new Label { ForeColor = Ui.Muted, Font = Ui.Small, Width = 452, TextAlign = ContentAlignment.MiddleRight, Height = 18 };
        _form.Controls.Add(_count);

        _form.Controls.Add(Ui.Caption(T("Email (optional, so the developer can reply)")));
        _email = new ModernTextBox { Width = 452 };
        _form.Controls.Add(_email);

        _technical = new ModernToggle { Text = T("Include technical details"), Checked = true, Width = 452, Margin = new Padding(0, 10, 0, 0) };
        _form.Controls.Add(_technical);
        _form.Controls.Add(new Label
        {
            Text = T("App version, Windows version, connected tablets and recent connection events. No files or profile data."),
            ForeColor = Ui.Muted, Font = Ui.Small, MaximumSize = new Size(452, 0), AutoSize = true, Margin = new Padding(0, 2, 0, 6),
        });
        _error = new Label { ForeColor = Ui.Bad, AutoSize = true, MaximumSize = new Size(452, 0), Margin = new Padding(0, 0, 0, 4) };
        _form.Controls.Add(_error);

        _send = Ui.Button(T("Send"), primary: true, width: 110);
        _send.Click += async (_, _) => await SendAsync();
        var cancel = Ui.Button(T("Cancel"), width: 100);
        cancel.Click += (_, _) => Close();
        var row = Ui.Row();
        row.Controls.AddRange(new Control[] { _send, cancel });
        _form.Controls.Add(row);
        Controls.Add(_form);

        _done = Ui.Stack();
        _done.Location = new Point(24, 160);
        _done.Visible = false;
        _doneTitle = new Label { Font = Ui.Title, ForeColor = Ui.Good, AutoSize = true };
        _doneText = new Label { ForeColor = Ui.Ink2, AutoSize = true, MaximumSize = new Size(452, 0), Margin = new Padding(0, 6, 0, 18) };
        var ok = Ui.Button(T("OK"), primary: true, width: 100);
        ok.Click += (_, _) => Close();
        _done.Controls.AddRange(new Control[] { _doneTitle, _doneText, ok });
        Controls.Add(_done);

        _message.TextChanged += (_, _) => { _error.Text = ""; UpdateCount(); };
        UpdateCount();
        AcceptButton = null;
        CancelButton = cancel;
        Shown += (_, _) => _message.Focus();
    }

    void UpdateCount() => _count.Text = $"{_message.TextLength}/{Feedback.MaxLength}";

    async Task SendAsync()
    {
        var message = _message.Text.Trim();
        if (message.Length < 3) { _error.Text = T("Write something before sending."); return; }
        if (!Feedback.CanSend(out var wait))
        {
            _error.Text = T("You've sent a lot of feedback recently. Try again in {0} min.", Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)));
            return;
        }
        _send.Enabled = false;
        _send.Text = T("Sending…");
        var technical = _technical.Checked;
        var email = _email.Text.Trim();
        var details = new Dictionary<string, string>
        {
            ["Sürüm"] = Updater.CurrentText,
            ["Bilgisayar"] = Feedback.OsName,
        };
        var devices = _server.ConnectedDevices;
        if (devices.Count > 0) details["Tablet"] = string.Join(", ", devices);
        if (technical) details["Tablet bilgisi"] = _server.DeviceSummary();
        var item = new FeedbackItem((FeedbackKind)Math.Max(0, _kind.SelectedIndex), message, email.Length == 0 ? null : email, details,
            technical ? Feedback.RecentEvents() : null, technical, L.Lang, DateTime.UtcNow);

        var result = await Feedback.Submit(item);
        if (result == FeedbackResult.Failed)
        {
            _error.Text = T("Couldn't send it. Try again later.");
            _send.Enabled = true;
            _send.Text = T("Send");
            return;
        }
        _doneTitle.Text = result == FeedbackResult.Sent ? T("Thanks, it arrived") : T("Thanks, it's saved");
        _doneText.Text = result == FeedbackResult.Sent
            ? T("The developer will read it soon.")
            : T("You're offline. It will be sent on its own when you're back online.");
        _form.Visible = false;
        _done.Visible = true;
    }
}
