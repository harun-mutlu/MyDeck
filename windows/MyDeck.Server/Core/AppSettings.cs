using System.Text.Json;

namespace MyDeck.Server.Core;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public bool StartMinimized { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public string? SkippedVersion { get; set; }
    public string? WeatherCity { get; set; }
    public double? WeatherLat { get; set; }
    public double? WeatherLon { get; set; }

    static string? _path;
    public static AppSettings Current { get; private set; } = new();

    public static void Load(string configDir)
    {
        _path = Path.Combine(configDir, "app.json");
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
