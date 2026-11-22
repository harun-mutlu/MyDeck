using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MyDeck.Server.Core;

public sealed class Updater
{
    public const string Repo = "harun-mutlu/MyDeck";
    public const string Asset = "MyDeck-win-x64.zip";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases";

    public static bool Supported => true;

    public static Version Current { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));
    public static string CurrentText => Current.ToString(3);

    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MyDeck-Updater/" + CurrentText);
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public enum Phase { Idle, Checking, UpToDate, Downloading, Ready, Failed }

    public Phase State { get; private set; }
    public string? Latest { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    string? _staging;
    readonly SemaphoreSlim _gate = new(1, 1);
    System.Threading.Timer? _timer;

    public void StartAutomatic()
    {
        if (!Supported) return;
        _timer = new System.Threading.Timer(_ => { if (AppSettings.Current.AutoUpdate) _ = CheckAsync(); }, null,
            TimeSpan.FromSeconds(20), TimeSpan.FromHours(6));
    }

    void Set(Phase p, string? error = null)
    {
        State = p;
        Error = error;
        Changed?.Invoke();
    }

    public async Task CheckAsync()
    {
        if (!Supported || !await _gate.WaitAsync(0)) return;
        try
        {
            if (State == Phase.Ready) return;
            Set(Phase.Checking);
            var root = JsonNode.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var tag = root?["tag_name"]?.GetValue<string>() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) { Set(Phase.Failed, T("The release has no version number.")); return; }
            v = Normalize(v);
            Latest = v.ToString(3);
            if (v <= Current) { Set(Phase.UpToDate); return; }

            var assets = root?["assets"] as JsonArray ?? new JsonArray();
            string? Url(string name) => assets.FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)?["browser_download_url"]?.GetValue<string>();
            var zipUrl = Url(Asset);
            if (zipUrl is null) { Set(Phase.Failed, T("The release has no {0} file.", Asset)); return; }

            Set(Phase.Downloading);
            var dir = Path.Combine(Path.GetTempPath(), "MyDeck-update", Latest);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, Asset);
            await using (var src = await Http.GetStreamAsync(zipUrl))
            await using (var dst = File.Create(zip))
                await src.CopyToAsync(dst);

            if (Url(Asset + ".sha256") is { } shaUrl)
            {
                var expected = (await Http.GetStringAsync(shaUrl)).Trim().Split(' ', '\t')[0];
                await using var f = File.OpenRead(zip);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(f));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) { Set(Phase.Failed, T("The downloaded file is damaged.")); return; }
            }

            var staging = Path.Combine(dir, "files");
            ZipFile.ExtractToDirectory(zip, staging);
            if (!File.Exists(Path.Combine(staging, "MyDeck.exe"))) { Set(Phase.Failed, T("The update package is incomplete.")); return; }
            _staging = staging;
            Set(Phase.Ready);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Set(Phase.Failed, ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? T("No release has been published yet.") : ex.Message);
        }
        finally { _gate.Release(); }
    }

    public bool Install(string root, bool hidden)
    {
        if (_staging is null) return false;
        var app = AppContext.BaseDirectory;
        var moved = new List<string>();
        try
        {
            foreach (var src in Directory.EnumerateFiles(_staging, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_staging, src);
                var dst = Path.Combine(app, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                if (File.Exists(dst))
                {
                    var old = dst + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(dst, old);
                    moved.Add(dst);
                }
                File.Copy(src, dst);
            }
        }
        catch (Exception ex)
        {
            foreach (var dst in moved)
                try { if (File.Exists(dst)) File.Delete(dst); File.Move(dst + ".old", dst); } catch { }
            Set(Phase.Failed, ex is UnauthorizedAccessException ? T("No permission to write to the MyDeck folder.") : ex.Message);
            return false;
        }
        var args = $"\"{root}\" --updated" + (hidden ? " --tepside" : "");
        Process.Start(new ProcessStartInfo(Path.Combine(app, "MyDeck.exe"), args) { UseShellExecute = false, WorkingDirectory = app });
        return true;
    }

    public static void CleanUp()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.old", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
            var temp = Path.Combine(Path.GetTempPath(), "MyDeck-update");
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
        catch { }
    }

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
