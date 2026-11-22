using MyDeck.Server;
using MyDeck.Server.Core;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var root = ResolveRoot(args.Where(a => !a.StartsWith("--")).ToArray());
        var configDir = Path.Combine(root, "config");
        var firstRun = !File.Exists(Path.Combine(configDir, "app.json"));
        AppSettings.Load(configDir);
        if (firstRun && File.Exists(Path.Combine(configDir, "profile.json")))
        {
            AppSettings.Current.Language = "tr";
            AppSettings.Save();
        }
        L.Init(AppSettings.Current.Language);
        Feedback.Init(configDir);

        var restarting = args.Contains("--updated");
        using var single = new Mutex(false, "MyDeck.Server.Single");
        var owned = false;
        try { owned = single.WaitOne(restarting ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            MessageBox.Show(T("MyDeck is already running. Its icon is in the bottom-right corner of the taskbar."), "MyDeck",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Updater.CleanUp();
        ApplicationConfiguration.Initialize();
        MyDeck.Server.Editor.Chrome.InstallMenuRenderer();
        var hidden = args.Contains("--tepside") || (!restarting && AppSettings.Current.StartMinimized);
        Application.Run(new MainForm(root, startHidden: hidden));
        single.ReleaseMutex();
    }

    static string ResolveRoot(string[] args)
    {
        if (args.Length > 0 && Directory.Exists(args[0])) return Path.GetFullPath(args[0]);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "scripts")) && Directory.Exists(Path.Combine(dir.FullName, "android")))
                return dir.FullName;
            dir = dir.Parent;
        }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyDeck");
        Directory.CreateDirectory(data);
        return data;
    }
}
