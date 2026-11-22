using System.IO.Compression;
using System.Text.Json;

namespace MyDeck.Server.Core;

public static class Backup
{
    public const int Keep = 20;
    public static string AutoDir(string configDir) => Path.Combine(configDir, "yedekler");

    public sealed record AutoItem(string Path, DateTime When, int Pages, int Tiles, bool Manual)
    {
        public override string ToString() =>
            When.ToString("d MMMM yyyy, HH:mm", L.Culture) + "  ·  " + T("{0} pages, {1} tiles", Pages, Tiles) + (Manual ? "  ·  " + T("before loading a backup") : "");
    }

    public static void AutoDaily(string configDir, string profilePath)
    {
        try
        {
            var dir = AutoDir(configDir);
            Directory.CreateDirectory(dir);
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (Directory.EnumerateFiles(dir, $"profil-{today}-*.json").Any()) return;
            File.Copy(profilePath, Path.Combine(dir, $"profil-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
            Trim(dir);
        }
        catch {  }
    }

    public static void SaveBeforeRestore(string configDir, string profilePath)
    {
        var dir = AutoDir(configDir);
        Directory.CreateDirectory(dir);
        if (File.Exists(profilePath))
            File.Copy(profilePath, Path.Combine(dir, $"once-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
        Trim(dir);
    }

    static void Trim(string dir)
    {
        foreach (var old in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
            try { old.Delete(); } catch { }
    }

    public static List<AutoItem> ListAuto(string configDir)
    {
        var dir = AutoDir(configDir);
        var list = new List<AutoItem>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(f.FullName), Profile.Json);
                if (p is null) continue;
                list.Add(new AutoItem(f.FullName, f.LastWriteTime, p.Pages.Count, p.Pages.Sum(x => x.Tiles.Count), f.Name.StartsWith("once-")));
            }
            catch { }
        }
        return list;
    }

    public static int Export(Profile profile, string target)
    {
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, Profile.Json), Profile.Json)!;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tmp = target + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            string? Pack(string? path)
            {
                if (string.IsNullOrWhiteSpace(path) || path.StartsWith("glyph:") || path.StartsWith("dosyalar/")) return path;
                var full = Environment.ExpandEnvironmentVariables(path);
                if (!File.Exists(full)) return path;
                if (!map.TryGetValue(full, out var name))
                {
                    name = $"dosyalar/{map.Count + 1}-{Path.GetFileName(full)}";
                    zip.CreateEntryFromFile(full, name, CompressionLevel.Optimal);
                    map[full] = name;
                }
                return name;
            }
            Rewrite(copy, Pack);
            var entry = zip.CreateEntry("profile.json");
            using (var w = new StreamWriter(entry.Open())) w.Write(JsonSerializer.Serialize(copy, Profile.Json));
        }
        File.Move(tmp, target, overwrite: true);
        return map.Count;
    }

    public static Profile Import(string source, string configDir)
    {
        using var zip = ZipFile.OpenRead(source);
        var pj = zip.GetEntry("profile.json") ?? throw new InvalidDataException(T("This isn't a MyDeck backup (it has no profile)."));
        Profile p;
        using (var r = new StreamReader(pj.Open())) p = JsonSerializer.Deserialize<Profile>(r.ReadToEnd(), Profile.Json)
            ?? throw new InvalidDataException(T("The profile in the backup is empty."));
        var outDir = Path.Combine(configDir, "yedekten", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        string? Unpack(string? path)
        {
            if (path is null || !path.StartsWith("dosyalar/")) return path;
            var e = zip.GetEntry(path);
            if (e is null) return null;
            var dest = Path.GetFullPath(Path.Combine(outDir, Path.GetFileName(path)));
            if (!dest.StartsWith(Path.GetFullPath(outDir), StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(dest)) { Directory.CreateDirectory(outDir); e.ExtractToFile(dest); }
            return dest;
        }
        Rewrite(p, Unpack);
        p.Validate();
        return p;
    }

    public static IEnumerable<TileAction> AllActions(Tile t)
    {
        foreach (var root in new[] { t.Action, t.Hold, t.Double, t.On?.Action })
            if (root is not null)
                foreach (var a in Walk(root)) yield return a;
    }

    static IEnumerable<TileAction> Walk(TileAction a)
    {
        yield return a;
        foreach (var s in (a.Steps ?? new()).Concat(a.Else ?? new()))
            foreach (var x in Walk(s)) yield return x;
    }

    public static (int pages, int files) ExportPages(Profile profile, IEnumerable<Page> pages, string target)
    {
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, Profile.Json), Profile.Json)!;
        var want = new HashSet<string>(pages.Select(x => x.Id));

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var pg in copy.Pages.Where(x => want.Contains(x.Id)).ToList())
                foreach (var a in pg.Tiles.SelectMany(AllActions))
                    if (a.Type is "page" or "folder" && a.Page is { } id && copy.Pages.Any(x => x.Id == id) && want.Add(id)) changed = true;
        }
        copy.Pages = copy.Pages.Where(x => want.Contains(x.Id)).ToList();
        foreach (var t in copy.Pages.SelectMany(x => x.Tiles))
        {
            if (t.Action?.Type is "page" or "folder" && !want.Contains(t.Action.Page ?? "")) t.Action = null;
            foreach (var a in AllActions(t).ToList())
                if (a.Type is "page" or "folder" && !want.Contains(a.Page ?? "")) { a.Type = "back"; a.Page = null; }
        }
        var used = copy.Pages.SelectMany(x => x.Tiles).SelectMany(AllActions).Select(a => a.Var).Where(v => v is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        copy.Vars = copy.Vars?.Where(kv => used.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (copy.Vars is { Count: 0 }) copy.Vars = null;
        copy.Events = null;
        copy.Start = pages.First().Id;
        copy.Validate();
        return (copy.Pages.Count, Export(copy, target));
    }

    public static (int pages, int tiles) Merge(Profile into, Profile pkg)
    {
        var map = new Dictionary<string, string>();
        var taken = new HashSet<string>(into.Pages.Select(x => x.Id));
        foreach (var pg in pkg.Pages)
        {
            var id = pg.Id;
            for (var n = 2; taken.Contains(id); n++) id = $"{pg.Id}-{n}";
            taken.Add(id);
            map[pg.Id] = id;
        }
        var tileIds = new HashSet<string>(into.Pages.SelectMany(x => x.Tiles).Select(t => t.Id));
        var tiles = 0;
        foreach (var pg in pkg.Pages)
        {
            pg.Id = map[pg.Id];
            var n = 0;
            foreach (var t in pg.Tiles)
            {
                foreach (var a in AllActions(t))
                    if (a.Page is { } target && map.TryGetValue(target, out var nt)) a.Page = nt;
                if (tileIds.Contains(t.Id))
                {
                    string id;
                    do id = $"{pg.Id}-{++n}"; while (tileIds.Contains(id));
                    t.Id = id;
                }
                tileIds.Add(t.Id);
                tiles++;
            }
            into.Pages.Add(pg);
        }
        if (pkg.Vars is { Count: > 0 })
        {
            into.Vars ??= new();
            foreach (var (k, v) in pkg.Vars) into.Vars.TryAdd(k, v);
        }
        into.Validate();
        return (pkg.Pages.Count, tiles);
    }

    static void Rewrite(Profile p, Func<string?, string?> f)
    {
        p.Theme.Wallpaper = f(p.Theme.Wallpaper);
        foreach (var page in p.Pages)
        {
            page.Wallpaper = f(page.Wallpaper);
            foreach (var t in page.Tiles)
            {
                if (t.Icon is { } i && !i.StartsWith("glyph:") && !IsProgram(i)) t.Icon = f(i);
                if (t.On?.Icon is { } oi && !oi.StartsWith("glyph:") && !IsProgram(oi)) t.On.Icon = f(oi);
                if (t.Gif is not null) t.Gif.Path = f(t.Gif.Path);
            }
        }
    }

    static bool IsProgram(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".lnk" or ".dll";
}
