using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class TabletDialog : DarkForm
{
    public TabletSettings Result { get; }

    public TabletDialog(TabletSettings s)
    {
        Result = new TabletSettings { SleepWhenIdle = s.SleepWhenIdle, IdleMinutes = s.IdleMinutes, SleepWhenPcOff = s.SleepWhenPcOff };
        Text = T("Tablet settings");
        BackColor = Ui.Panel; ForeColor = Ui.Ink; Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 330);

        var st = Ui.Stack();
        st.Location = new Point(20, 6);
        st.Controls.Add(Ui.Caption(T("Screen")));
        var idle = new ModernToggle { Text = T("Turn the tablet screen off while the computer is idle"), Checked = Result.SleepWhenIdle, Width = 420 };
        var row = Ui.Row(); row.Margin = new Padding(22, 6, 0, 0);
        var min = Ui.Number(1, 240); min.Value = Math.Clamp(Result.IdleMinutes, 1, 240);
        row.Controls.AddRange(new Control[] { min, new Label { Text = T("minutes without mouse or keyboard input"), AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(8, 4, 0, 0) } });
        idle.CheckedChanged += (_, _) => min.Enabled = idle.Checked;
        min.Enabled = idle.Checked;
        var pcoff = new ModernToggle { Text = T("Turn it off when the computer shuts down or the connection drops (after 1 min)"), Checked = Result.SleepWhenPcOff, Width = 420, Margin = new Padding(0, 14, 0, 0) };
        st.Controls.AddRange(new Control[] { idle, row, pcoff });
        st.Controls.Add(new Label
        {
            Text = T("The tablet wakes up when you use the mouse or keyboard, or when MyDeck connects. ") +
                   T("To turn the screen fully off, the tablet asks once for the \"device admin\" permission. ") +
                   T("Set a slider's target to \"Tablet brightness\" to control brightness."),
            ForeColor = Ui.Muted, Width = 420, Height = 90, Margin = new Padding(0, 16, 0, 0),
        });

        var ok = Ui.Button(T("OK"), primary: true, width: 110); ok.DialogResult = DialogResult.OK;
        var cancel = Ui.Button(T("Cancel"), width: 100); cancel.DialogResult = DialogResult.Cancel;
        var buttons = Ui.Row(); buttons.Margin = new Padding(0, 10, 0, 0);
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        st.Controls.Add(buttons);
        Controls.Add(st);
        AcceptButton = ok; CancelButton = cancel;
        FormClosing += (_, _) =>
        {
            Result.SleepWhenIdle = idle.Checked;
            Result.IdleMinutes = (int)min.Value;
            Result.SleepWhenPcOff = pcoff.Checked;
        };
    }
}
