using System.Diagnostics;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class RestoreDialog : DarkForm
{
    public Backup.AutoItem? Result { get; private set; }

    public RestoreDialog(List<Backup.AutoItem> items)
    {
        Text = T("Restore an earlier version");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 430);

        var st = Ui.Stack();
        st.Location = new Point(20, 6);
        st.Controls.Add(Ui.Caption(T("Automatic backups")));
        var list = new ModernList { Width = 480, Height = 270, ItemHeight = 30, AccentColor = Ui.Accent };
        foreach (var i in items) list.Items.Add(i);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        st.Controls.Add(list);
        st.Controls.Add(new Label
        {
            Text = items.Count == 0
                ? T("No automatic backups yet. MyDeck saves one each day the profile changes.")
                : T("MyDeck saves a backup each day the profile changes and keeps the last 20. The current state is backed up before restoring."),
            ForeColor = Ui.Muted, Width = 480, Height = 40, Margin = new Padding(0, 8, 0, 0),
        });

        var ok = Ui.Button(T("Restore this one"), primary: true, width: 130); ok.DialogResult = DialogResult.OK;
        ok.Enabled = items.Count > 0;
        var folder = Ui.Button(T("Open folder"), width: 110);
        folder.Click += (_, _) =>
        {
            if (items.FirstOrDefault() is { } f) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(f.Path)}\""));
        };
        folder.Enabled = items.Count > 0;
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel;
        var buttons = Ui.Row(); buttons.Margin = new Padding(0, 6, 0, 0);
        buttons.Controls.AddRange(new Control[] { ok, folder, cancel });
        st.Controls.Add(buttons);
        Controls.Add(st);
        AcceptButton = ok; CancelButton = cancel;
        list.DoubleClick += (_, _) => { if (list.SelectedItem is not null) { DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, _) => { if (DialogResult == DialogResult.OK) Result = list.SelectedItem as Backup.AutoItem; };
    }
}

