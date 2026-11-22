using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

static class AppPicker
{
    public static void Show(Control anchor, Action<string> pick)
    {
        var m = new ContextMenuStrip { ShowImageMargin = false };
        m.Items.Add(T("Choose a program (.exe)…"), null, (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = T("Choose a program"),
                Filter = T("Programs") + " (*.exe)|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dlg.ShowDialog(anchor.FindForm()) == DialogResult.OK) pick(DeckEvent.AppName(dlg.FileName));
        });
        var running = Foreground.Running().ToList();
        if (running.Count > 0)
        {
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripLabel(T("Open now")) { ForeColor = Ui.Muted });
            foreach (var (name, title) in running)
                m.Items.Add($"{name}  ·  {(title.Length > 40 ? title[..40] + "…" : title)}", null, (_, _) => pick(name));
        }
        m.Show(anchor, new Point(0, anchor.Height));
    }
}
