using Microsoft.Win32;

namespace MyDeck.Server.Core;

public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "MyDeck";

    static string Command(string root) => $"\"{Environment.ProcessPath}\" \"{root}\" --tepside";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }

    public static void Set(bool on, string root)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(Name, Command(root));
        else k.DeleteValue(Name, false);
    }

    public static bool EnsureDefault(string root, string configDir)
    {
        var marker = Path.Combine(configDir, "autostart.set");
        if (!File.Exists(marker))
        {
            Set(true, root);
            File.WriteAllText(marker, DateTime.Now.ToString("s"));
            return true;
        }
        if (IsEnabled())
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(Name) as string != Command(root)) Set(true, root);
        }
        return false;
    }
}
