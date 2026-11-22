using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace MyDeck.Server.Core;

public sealed class WeatherWatcher : IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly System.Threading.Timer _timer;
    public JsonObject? Current { get; private set; }
    public event Action<JsonObject>? Changed;

    public WeatherWatcher()
    {
        _timer = new System.Threading.Timer(_ => _ = Poll(), null, 1000, 15 * 60_000);
    }

    public void Refresh() => _timer.Change(0, 15 * 60_000);

    async Task Poll()
    {
        var s = AppSettings.Current;
        if (s.WeatherLat is not { } lat || s.WeatherLon is not { } lon) return;
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(inv) + "&longitude=" + lon.ToString(inv) +
                      "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code,is_day" +
                      "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
                      "&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max" +
                      "&timezone=auto&forecast_days=2";
            var root = JsonNode.Parse(await Http.GetStringAsync(url));
            if (root is null) return;
            var data = Build(root, s.WeatherCity ?? "");
            Current = data;
            Changed?.Invoke(data);
        }
        catch { }
    }

    static JsonObject Build(JsonNode root, string place)
    {
        int? I(JsonNode? n) => n is null ? null : (int?)Math.Round(n.GetValue<double>());
        var cur = root["current"]!;
        var code = I(cur["weather_code"]) ?? 3;
        var night = I(cur["is_day"]) == 0;
        var daily = root["daily"];
        JsonNode? D(string k, int i) => (daily?[k] as JsonArray) is { } a && a.Count > i ? a[i] : null;

        var now = new JsonObject
        {
            ["t"] = I(cur["temperature_2m"]),
            ["feels"] = I(cur["apparent_temperature"]),
            ["hum"] = I(cur["relative_humidity_2m"]),
            ["wind"] = I(cur["wind_speed_10m"]),
            ["cond"] = Condition(code),
            ["icon"] = Icon(code, night),
            ["hi"] = I(D("temperature_2m_max", 0)),
            ["lo"] = I(D("temperature_2m_min", 0)),
        };
        var tcode = I(D("weather_code", 1)) ?? 3;
        var tomorrowDate = D("time", 1)?.GetValue<string>();
        var tomorrow = new JsonObject
        {
            ["hi"] = I(D("temperature_2m_max", 1)),
            ["lo"] = I(D("temperature_2m_min", 1)),
            ["icon"] = Icon(tcode, false),
            ["cond"] = Condition(tcode),
            ["rain"] = I(D("precipitation_probability_max", 1)),
            ["day"] = DateTime.TryParse(tomorrowDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var td)
                ? L.Culture.TextInfo.ToTitleCase(td.ToString("dddd", L.Culture)) : T("Tomorrow"),
        };

        var hours = new JsonArray();
        var hourly = root["hourly"];
        var curTime = cur["time"]?.GetValue<string>() ?? "";
        if (hourly?["time"] is JsonArray times)
        {
            var start = 0;
            for (var i = 0; i < times.Count; i++)
                if (string.CompareOrdinal(times[i]!.GetValue<string>(), curTime) > 0) { start = i; break; }
            for (var i = start; i < times.Count && hours.Count < 8; i++)
            {
                var time = times[i]!.GetValue<string>();
                var hc = I((hourly["weather_code"] as JsonArray)?[i]) ?? 3;
                hours.Add(new JsonObject
                {
                    ["h"] = time.Length >= 13 ? time.Substring(11, 2) : "",
                    ["t"] = I((hourly["temperature_2m"] as JsonArray)?[i]),
                    ["icon"] = Icon(hc, I((hourly["is_day"] as JsonArray)?[i]) == 0),
                    ["pc"] = I((hourly["precipitation_probability"] as JsonArray)?[i]),
                });
            }
        }

        return new JsonObject
        {
            ["t"] = "wx",
            ["place"] = place,
            ["at"] = curTime.Length >= 16 ? curTime.Substring(11, 5) : "",
            ["now"] = now,
            ["hours"] = hours,
            ["tomorrow"] = tomorrow,
        };
    }

    public static string Icon(int code, bool night) => code switch
    {
        0 => night ? "moon" : "sun",
        1 or 2 => night ? "cloudmoon" : "cloudsun",
        45 or 48 => "fog",
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => "rain",
        56 or 57 or 66 or 67 => "sleet",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        95 or 96 or 99 => "storm",
        _ => "cloud",
    };

    static string Condition(int code) => code switch
    {
        0 => T("Clear"),
        1 => T("Mostly clear"),
        2 => T("Partly cloudy"),
        3 => T("Cloudy"),
        45 or 48 => T("Fog"),
        51 or 53 or 55 => T("Drizzle"),
        56 or 57 or 66 or 67 => T("Freezing rain"),
        61 or 63 or 80 or 81 => T("Rain"),
        65 or 82 => T("Heavy rain"),
        71 or 73 or 75 or 77 or 85 or 86 => T("Snow"),
        95 or 96 or 99 => T("Thunderstorm"),
        _ => "",
    };

    public sealed record Place(string Name, string Detail, double Lat, double Lon);

    public static async Task<List<Place>> Search(string name)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=6&format=json&language=" + L.Lang + "&name=" + Uri.EscapeDataString(name);
        var root = JsonNode.Parse(await Http.GetStringAsync(url));
        var list = new List<Place>();
        if (root?["results"] is JsonArray arr)
            foreach (var r in arr)
            {
                if (r is null) continue;
                var detail = string.Join(", ", new[] { r["admin1"]?.GetValue<string>(), r["country"]?.GetValue<string>() }.Where(x => !string.IsNullOrEmpty(x)));
                list.Add(new Place(r["name"]?.GetValue<string>() ?? "", detail, r["latitude"]!.GetValue<double>(), r["longitude"]!.GetValue<double>()));
            }
        return list;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
