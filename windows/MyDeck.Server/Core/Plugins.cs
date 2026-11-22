using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyDeck.Server.Core;

public sealed class PluginInfo
{
    [JsonIgnore] public string Id { get; set; } = "";
    [JsonIgnore] public string Dir { get; set; } = "";
    [JsonIgnore] public bool Enabled { get; set; } = true;
    [JsonPropertyName("id")] public string? ManifestId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("actions")] public List<PluginItem> Actions { get; set; } = new();
    [JsonPropertyName("data")] public List<PluginItem> Data { get; set; } = new();
}

public sealed class PluginItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("seconds")] public int Seconds { get; set; } = 10;
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("fields")] public List<PluginField> Fields { get; set; } = new();
}

public sealed class PluginField
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("default")] public string? Default { get; set; }
    [JsonPropertyName("hint")] public string? Hint { get; set; }
}

public static class PluginManifest
{
    public static readonly string[] FileNames = { "plugin.json", "eklenti.json" };

    static readonly Dictionary<string, string> Legacy = new()
    {
        ["ad"] = "name", ["aciklama"] = "description", ["surum"] = "version", ["yazar"] = "author",
        ["eylemler"] = "actions", ["veriler"] = "data", ["komut"] = "command", ["saniye"] = "seconds",
        ["ikon"] = "icon", ["alanlar"] = "fields", ["varsayilan"] = "default", ["ipucu"] = "hint",
    };

    public static PluginInfo Parse(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidDataException(T("The manifest is empty."));
        Normalize(node);
        var p = node.Deserialize<PluginInfo>(Profile.Json) ?? throw new InvalidDataException(T("The manifest is empty."));
        if (string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException(T("The manifest has no name."));
        if (p.Actions.Count == 0 && p.Data.Count == 0) throw new InvalidDataException(T("The plugin has no actions or data tiles."));
        foreach (var it in p.Actions.Concat(p.Data))
            if (string.IsNullOrWhiteSpace(it.Id) || string.IsNullOrWhiteSpace(it.Command))
                throw new InvalidDataException(T("Every action and data tile needs an id and a command."));
        return p;
    }

    static void Normalize(JsonNode node)
    {
        if (node is JsonArray arr) { foreach (var x in arr) if (x is not null) Normalize(x); return; }
        if (node is not JsonObject obj) return;
        foreach (var key in obj.Select(k => k.Key).ToList())
        {
            if (Legacy.TryGetValue(key, out var en) && !obj.ContainsKey(en))
            {
                var v = obj[key];
                obj.Remove(key);
                obj[en] = v;
            }
        }
        foreach (var kv in obj.ToList()) if (kv.Value is not null) Normalize(kv.Value);
    }

    public static string? Find(string dir) =>
        FileNames.Select(f => Path.Combine(dir, f)).FirstOrDefault(File.Exists);
}

public sealed record PluginValue(string Value, string? Sub, string? Color, double? Percent, DateTime At);

public sealed class PluginHost : IDisposable
{
    readonly string _root;
    readonly string _stateFile;
    readonly object _lock = new();
    List<PluginInfo> _plugins = new();
    readonly ConcurrentDictionary<string, PluginValue> _values = new();
    readonly ConcurrentDictionary<string, DateTime> _due = new();
    readonly ConcurrentDictionary<string, bool> _running = new();
    readonly CancellationTokenSource _cts = new();
    List<(string Key, PluginInfo P, PluginItem Item, Dictionary<string, string> Args)> _jobs = new();

    public string Folder => _root;
    public event Action<string, string>? Log;

    public event Action<string, PluginValue>? ValueChanged;

    public PluginHost(string root, string configDir)
    {
        _root = Path.Combine(root, "plugins");
        _stateFile = Path.Combine(configDir, "plugins.json");
        var legacy = Path.Combine(root, "eklentiler");
        if (!Directory.Exists(_root) && Directory.Exists(legacy))
            try { Directory.Move(legacy, _root); } catch { _root = legacy; }
        var legacyState = Path.Combine(configDir, "eklentiler.json");
        if (!File.Exists(_stateFile) && File.Exists(legacyState))
            try { File.Move(legacyState, _stateFile); } catch { }
        Directory.CreateDirectory(_root);
        Reload();
        _ = Task.Run(Loop);
    }

    public IReadOnlyList<PluginInfo> All { get { lock (_lock) return _plugins.ToList(); } }
    public PluginInfo? Find(string? id) { lock (_lock) return _plugins.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); }

    public void Reload()
    {
        var off = Disabled();
        var list = new List<PluginInfo>();
        foreach (var dir in Directory.GetDirectories(_root))
        {
            if (PluginManifest.Find(dir) is not { } file) continue;
            try
            {
                var p = PluginManifest.Parse(File.ReadAllText(file));
                p.Id = Path.GetFileName(dir);
                p.Dir = dir;
                p.Enabled = !off.Contains(p.Id);
                list.Add(p);
            }
            catch (Exception ex) { Log?.Invoke(T("Couldn't read the plugin '{0}': {1}", Path.GetFileName(dir), ex.Message), "warn"); }
        }
        lock (_lock) _plugins = list.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    public sealed record Package(PluginInfo Info, string Id, string Source, string Temp, PluginInfo? Existing);

    public static readonly string[] ForeignExtensions = { ".streamdeckplugin", ".tpp", ".lbplugin" };

    public Package Inspect(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ForeignExtensions.Contains(ext))
            throw new InvalidDataException(T("This is a plugin for another app. MyDeck can only install its own plugins (.mydeckplugin or .zip with a plugin.json file)."));
        var temp = Path.Combine(Path.GetTempPath(), "MyDeck-plugin-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Directory.Exists(path)) CopyDir(path, temp);
            else System.IO.Compression.ZipFile.ExtractToDirectory(path, temp);
            var dir = temp;
            if (PluginManifest.Find(dir) is null)
            {
                var subs = Directory.GetDirectories(dir);
                if (subs.Length == 1 && Directory.GetFiles(dir).Length == 0) dir = subs[0];
            }
            if (PluginManifest.Find(dir) is not { } manifest)
            {
                if (Directory.EnumerateFiles(temp, "manifest.json", SearchOption.AllDirectories).Any())
                    throw new InvalidDataException(T("This is a plugin for another app. MyDeck can only install its own plugins (.mydeckplugin or .zip with a plugin.json file)."));
                throw new InvalidDataException(T("No plugin.json file found in the package."));
            }
            var info = PluginManifest.Parse(File.ReadAllText(manifest));
            var id = SafeId(info.ManifestId ?? info.Name);
            return new Package(info, id, dir, temp, Find(id));
        }
        catch (InvalidDataException) { TryDelete(temp); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            TryDelete(temp);
            throw new InvalidDataException(T("The file couldn't be read as a plugin: {0}", ex.Message));
        }
    }

    public void Install(Package pkg)
    {
        var target = Path.Combine(_root, pkg.Id);
        var backup = target + ".old";
        try
        {
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            CopyDir(pkg.Source, target);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
            throw;
        }
        finally { TryDelete(pkg.Temp); }
        Reload();
        Log?.Invoke(T("Plugin installed: {0}", pkg.Info.Name), "info");
    }

    public void Discard(Package pkg) => TryDelete(pkg.Temp);

    public void Uninstall(string id)
    {
        if (Find(id) is not { } p) return;
        Directory.Delete(p.Dir, true);
        Reload();
        Log?.Invoke(T("Plugin removed: {0}", p.Name), "info");
    }

    static string SafeId(string s)
    {
        var chars = s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
        var id = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return id.Length == 0 ? "plugin" : id.Length > 40 ? id[..40] : id;
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    static void TryDelete(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }

    HashSet<string> Disabled()
    {
        try { return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_stateFile)) ?? new(); }
        catch { return new(); }
    }

    public void SetEnabled(string id, bool on)
    {
        var off = Disabled();
        if (on) off.Remove(id); else off.Add(id);
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(off));
        if (Find(id) is { } p) p.Enabled = on;
    }

    public async Task RunActionAsync(string? pluginId, string? itemId, Dictionary<string, string>? args)
    {
        var p = Find(pluginId) ?? throw new InvalidOperationException(T("There's no plugin '{0}'.", pluginId));
        if (!p.Enabled) throw new InvalidOperationException(T("The plugin '{0}' is turned off.", p.Name));
        var item = p.Actions.FirstOrDefault(a => a.Id == itemId) ?? throw new InvalidOperationException(T("The plugin '{0}' has no such action.", p.Name));
        var (code, _, err) = await Exec(p, item, args, 30_000);
        if (code != 0) throw new InvalidOperationException(err.Length > 0 ? err : T("exit code {0}", code));
    }

    static string Fill(string command, PluginItem item, Dictionary<string, string>? args)
    {
        var s = command;
        foreach (var f in item.Fields)
        {
            var v = args?.GetValueOrDefault(f.Id) ?? f.Default ?? "";
            v = v.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Replace("&", " ").Replace("|", " ").Replace("<", " ").Replace(">", " ").Replace("^", " ");
            s = s.Replace("{" + f.Id + "}", v);
        }
        return s;
    }

    async Task<(int Code, string Out, string Err)> Exec(PluginInfo p, PluginItem item, Dictionary<string, string>? args, int timeoutMs)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + Fill(item.Command, item, args) + "\"")
        {
            WorkingDirectory = p.Dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException(T("couldn't start the command"));
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(T("the command didn't finish in time"));
        }
        return (proc.ExitCode, (await outTask).Trim(), (await errTask).Trim());
    }

    public static string Key(PluginSpec s) =>
        $"{s.Plugin}|{s.Item}|{string.Join("&", (s.Args ?? new()).OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}";

    public PluginValue? Value(PluginSpec s) => _values.GetValueOrDefault(Key(s));

    public void SetJobs(Profile profile)
    {
        var jobs = new List<(string, PluginInfo, PluginItem, Dictionary<string, string>)>();
        var seen = new HashSet<string>();
        foreach (var t in profile.Pages.SelectMany(p => p.Tiles).Where(t => t.Kind == "plugin" && t.Plugin is not null))
        {
            var key = Key(t.Plugin!);
            if (!seen.Add(key)) continue;
            var p = Find(t.Plugin!.Plugin);
            var item = p?.Data.FirstOrDefault(d => d.Id == t.Plugin.Item);
            if (p is null || item is null || !p.Enabled) continue;
            jobs.Add((key, p, item, t.Plugin.Args ?? new()));
        }
        lock (_lock) _jobs = jobs;
        foreach (var j in jobs) _due.TryAdd(j.Item1, DateTime.MinValue);
    }

    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            List<(string Key, PluginInfo P, PluginItem Item, Dictionary<string, string> Args)> jobs;
            lock (_lock) jobs = _jobs;
            var now = DateTime.UtcNow;
            foreach (var j in jobs)
            {
                if (_due.GetValueOrDefault(j.Key) > now || _running.ContainsKey(j.Key)) continue;
                _due[j.Key] = now.AddSeconds(Math.Max(2, j.Item.Seconds));
                _running[j.Key] = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var (code, output, err) = await Exec(j.P, j.Item, j.Args, Math.Max(5000, j.Item.Seconds * 1000));
                        var v = code == 0 ? Parse(output) : new PluginValue("—", err.Length > 0 ? err.Split('\n')[0] : T("error ({0})", code), "#FF3D5A", null, DateTime.Now);
                        var old = _values.GetValueOrDefault(j.Key);
                        _values[j.Key] = v;
                        if (old is null || old with { At = v.At } != v) ValueChanged?.Invoke(j.Key, v);
                    }
                    catch (Exception ex)
                    {
                        var v = new PluginValue("—", ex.Message, "#FF3D5A", null, DateTime.Now);
                        _values[j.Key] = v;
                        ValueChanged?.Invoke(j.Key, v);
                    }
                    finally { _running.TryRemove(j.Key, out _); }
                });
            }
            try { await Task.Delay(500, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    static PluginValue Parse(string output)
    {
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        if (line.StartsWith("{"))
            try
            {
                var o = JsonNode.Parse(line)!.AsObject();
                double? pct = null;
                JsonNode? Get(string en, string old) => o[en] ?? o[old];
                if (Get("percent", "yuzde") is JsonValue pv && pv.TryGetValue<double>(out var d)) pct = Math.Clamp(d, 0, 1);
                return new PluginValue(Get("value", "deger")?.ToString() ?? "", Get("sub", "alt")?.ToString(), Get("color", "renk")?.ToString(), pct, DateTime.Now);
            }
            catch { }
        return new PluginValue(line.Length > 60 ? line[..60] : line, null, null, null, DateTime.Now);
    }

    public JsonObject? ValueJson(PluginSpec s) => Value(s) is { } v ? ToJson(v) : null;

    public static JsonObject ToJson(PluginValue v) => new()
    {
        ["v"] = v.Value, ["sub"] = v.Sub, ["c"] = v.Color, ["p"] = v.Percent,
    };

    public void Dispose() => _cts.Cancel();
}
