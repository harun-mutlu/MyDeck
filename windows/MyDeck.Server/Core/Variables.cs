using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyDeck.Server.Core;

public sealed partial class Variables
{
    readonly ConcurrentDictionary<string, string> _v = new(StringComparer.OrdinalIgnoreCase);
    readonly string _path;
    readonly System.Threading.Timer _saveTimer;
    static readonly CultureInfo Tr = new("tr-TR");

    public event Action<string, string, string>? Changed;

    public static readonly string[] BuiltIn = { "app", "time", "date", "day", "uygulama", "saat", "tarih", "gün" };

    public Variables(string configDir)
    {
        _path = Path.Combine(configDir, "vars.json");
        _saveTimer = new System.Threading.Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) is { } d)
                foreach (var (k, v) in d) _v[k] = v;
        }
        catch {  }
    }

    public void Seed(Dictionary<string, string>? initial)
    {
        if (initial is null) return;
        foreach (var (k, v) in initial)
            if (IsValidName(k)) _v.TryAdd(k, v ?? "");
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NameRx().IsMatch(name) && !BuiltIn.Contains(name, StringComparer.OrdinalIgnoreCase);

    public string App { get; private set; } = "";

    internal void SetApp(string app)
    {
        if (app == App) return;
        var old = App;
        App = app;
        Changed?.Invoke("app", old, app);
        Changed?.Invoke("uygulama", old, app);
    }

    public string Get(string name) => name.ToLower(Tr) switch
    {
        "app" or "uygulama" => App,
        "time" or "saat" => DateTime.Now.ToString("HH:mm"),
        "date" or "tarih" => DateTime.Now.ToString("d MMMM yyyy", L.Culture),
        "day" or "gün" => DateTime.Now.ToString("dddd", L.Culture),
        _ => _v.GetValueOrDefault(name, ""),
    };

    public bool Has(string name) => _v.ContainsKey(name) || BuiltIn.Contains(name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> All => new SortedDictionary<string, string>(_v, StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value)
    {
        if (!IsValidName(name)) return;
        value ??= "";
        var old = _v.GetValueOrDefault(name, "");
        _v[name] = value;
        _saveTimer.Change(500, Timeout.Infinite);
        if (old != value) Changed?.Invoke(name, old, value);
    }

    public bool Remove(string name)
    {
        if (!_v.TryRemove(name, out _)) return false;
        _saveTimer.Change(500, Timeout.Infinite);
        return true;
    }

    void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(new SortedDictionary<string, string>(_v), Profile.Json)); }
        catch {  }
    }

    public string? Expand(string? s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('{')) return s;
        return RefRx().Replace(s, m => Has(m.Groups[1].Value) ? Get(m.Groups[1].Value) : m.Value);
    }

    public static bool Uses(string? s, string name) =>
        s is not null && s.Contains("{" + name + "}", StringComparison.OrdinalIgnoreCase);

    public static bool Truthy(string? s) =>
        !string.IsNullOrWhiteSpace(s) && s.Trim().ToLower(Tr) is not ("0" or "false" or "hayır" or "kapalı" or "off" or "no");

    static bool Num(string? s, out double d) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    static string Fmt(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

    public bool Test(string? name, string? op, string? value)
    {
        var a = Get(name ?? "");
        var b = Expand(value) ?? "";
        int Cmp() => Num(a, out var x) && Num(b, out var y) ? x.CompareTo(y) : string.Compare(a, b, true, Tr);
        return (op ?? "eq") switch
        {
            "ne" => Cmp() != 0,
            "gt" => Cmp() > 0,
            "lt" => Cmp() < 0,
            "ge" => Cmp() >= 0,
            "le" => Cmp() <= 0,
            "has" => a.Contains(b, StringComparison.CurrentCultureIgnoreCase),
            "empty" => string.IsNullOrWhiteSpace(a),
            "notempty" => !string.IsNullOrWhiteSpace(a),
            "true" => Truthy(a),
            "false" => !Truthy(a),
            _ => Cmp() == 0,
        };
    }

    public void Apply(string? name, string? op, string? value)
    {
        if (!IsValidName(name)) throw new InvalidDataException(T("Invalid variable name: '{0}'. Use letters, digits, _ and -.", name));
        var cur = Get(name!);
        var v = Expand(value) ?? "";
        var next = (op ?? "set") switch
        {
            "add" => Fmt((Num(cur, out var a) ? a : 0) + (Num(v, out var b) ? b : 1)),
            "sub" => Fmt((Num(cur, out var c) ? c : 0) - (Num(v, out var d) ? d : 1)),
            "toggle" => Truthy(cur) ? "0" : "1",
            "append" => cur + v,
            _ => v,
        };
        Set(name!, next);
    }

    public static readonly (string Key, string Name)[] SetOps =
    {
        ("set", T("= set")), ("add", T("+ add")), ("sub", T("− subtract")), ("toggle", T("on ↔ off (1/0)")), ("append", T("append")),
    };

    public static readonly (string Key, string Name)[] TestOps =
    {
        ("eq", T("equals")), ("ne", T("doesn't equal")), ("gt", T("is greater than")), ("lt", T("is less than")), ("ge", T("is greater or equal")),
        ("le", T("is less or equal")), ("has", T("contains")), ("true", T("is on (1)")), ("false", T("is off (0)")),
        ("empty", T("is empty")), ("notempty", T("isn't empty")),
    };

    [GeneratedRegex(@"^[\p{L}\p{N}_\-]{1,40}$")] private static partial Regex NameRx();
    [GeneratedRegex(@"\{([\p{L}\p{N}_\-]{1,40})\}")] private static partial Regex RefRx();
}

public static class Foreground
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    static readonly int Self = Environment.ProcessId;

    public static string? Current()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == 0 || pid == Self) return null;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.ToLowerInvariant();
        }
        catch { return null; }
    }

    public static List<(string Name, string Title)> Running()
    {
        var list = new List<(string, string)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Self || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                list.Add((p.ProcessName.ToLowerInvariant(), p.MainWindowTitle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.GroupBy(x => x.Item1).Select(g => g.First()).OrderBy(x => x.Item1).ToList();
    }
}
