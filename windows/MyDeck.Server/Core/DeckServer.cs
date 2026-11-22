using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyDeck.Server.Core;

public sealed class DeckServer : IDisposable
{
    public const int DiscoveryPort = 47800;
    public const int TcpPort = 47801;
    public const int HttpPort = 47802;

    public string Root { get; }
    public string ConfigDir => Path.Combine(Root, "config");
    public string ProfilePath => Path.Combine(ConfigDir, "profile.json");
    string DevicesPath => Path.Combine(ConfigDir, "devices.json");
    string? _apkPath;
    string ApkPath => _apkPath ??= FindApk();

    string FindApk()
    {
        var dev = Path.Combine(Root, "out", "MyDeck.apk");
        var bundled = Path.Combine(AppContext.BaseDirectory, "MyDeck.apk");
        return !File.Exists(dev) && File.Exists(bundled) ? bundled : dev;
    }

    public event Action<string, LogKind>? Log;
    public event Action? ClientsChanged;

    public Func<string, string, Task<bool>>? ConfirmPairing;

    readonly CancellationTokenSource _cts = new();
    readonly ConcurrentDictionary<Client, byte> _clients = new();
    readonly object _profileLock = new();
    Profile _profile;
    int _rev = 1;
    FileSystemWatcher? _watcher, _apkWatcher;
    System.Threading.Timer? _apkTimer;
    System.Threading.Timer? _reloadTimer;
    TcpListener? _tcp, _http;

    readonly ConcurrentDictionary<string, bool> _local = new();
    readonly ConcurrentDictionary<string, string> _lastSent = new();

    public Sensors Sensors { get; } = new();

    public MediaWatcher Media { get; } = new();

    public WeatherWatcher Weather { get; } = new();

    public PluginHost Plugins { get; }

    public Assets Assets { get; }

    public Variables Vars { get; }

    public DeckServer(string root)
    {
        Root = root;
        Assets = new Assets(root);
        Assets.Log += (t, k) => Log?.Invoke(t, k);
        Assets.Ready += () => { foreach (var c in _clients.Keys.Where(c => c.Paired)) SendLayout(c); };
        Directory.CreateDirectory(ConfigDir);
        if (!File.Exists(ProfilePath)) DefaultProfile.Create().Save(ProfilePath);
        _profile = Profile.Load(ProfilePath);
        Vars = new Variables(ConfigDir);
        Vars.Seed(_profile.Vars);
        Vars.Changed += OnVarChanged;
        Actions.Vars = Vars;
        Actions.GoTo = GoToAll;
        Sensors.NeedProcesses = HasKind(_profile, "procs");
        Sensors.Sampled += OnSampled;
        Media.SetKeys(MediaKeys(_profile));
        Media.Changed += OnMediaChanged;
        Media.Warn += m => Log?.Invoke(m, LogKind.Warn);
        Plugins = new PluginHost(root, ConfigDir);
        Plugins.Log += (t, k) => Log?.Invoke(t, k == "warn" ? LogKind.Warn : LogKind.Info);
        Actions.Plugins = Plugins;
        Plugins.SetJobs(_profile);
        Plugins.ValueChanged += OnPluginValue;
        Backup.AutoDaily(ConfigDir, ProfilePath);
        Weather.Changed += wx =>
        {
            Profile p; lock (_profileLock) p = _profile;
            if (HasKind(p, "weather")) Broadcast(wx);
        };
    }

    static IEnumerable<string> MediaKeys(Profile p) =>
        p.Pages.SelectMany(pg => pg.Tiles).Where(t => t.Kind == "media").Select(t => t.Media?.App?.Trim() ?? "").Distinct();

    static bool HasKind(Profile p, string kind) => p.Pages.Any(pg => pg.Tiles.Any(t => t.Kind == kind));
    static bool HasLive(Profile p) => p.Pages.Any(pg => pg.Tiles.Any(t => t.IsLive));

    public Size LastDeviceSize { get; private set; } = new(1024, 600);
    public event Action? DeviceSizeChanged;

    public event Action? ProfileReloaded;

    public string DeviceSummary() => string.Join("; ", _clients.Keys.Where(c => c.Paired)
        .Select(c => $"{c.Name}, Android {c.Os} (API {c.Sdk}), {c.Width}x{c.Height} @{c.Density:0.##}"));

    public IReadOnlyCollection<string> ConnectedDevices => _clients.Keys.Where(c => c.Paired).Select(c => c.Name).ToList();

    static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in n.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask is null) continue;
                var ip = a.Address.GetAddressBytes();
                var mask = a.IPv4Mask.GetAddressBytes();
                var b = new byte[4];
                for (var i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(b);
            }
        }
    }

    async Task AnnounceLoop()
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var msg = Encoding.UTF8.GetBytes($"MYDECK! {TcpPort} {Environment.MachineName}");
        while (!_cts.IsCancellationRequested)
        {
            foreach (var addr in BroadcastAddresses().Distinct())
                    try { await udp.SendAsync(msg, msg.Length, new IPEndPoint(addr, DiscoveryPort)); } catch { }
            try { await Task.Delay(3000, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    public static IEnumerable<IPAddress> LocalIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address);

    public void Start()
    {
        _ = Task.Run(DiscoveryLoop);
        _ = Task.Run(AnnounceLoop);
        _ = Task.Run(TcpLoop);
        _ = Task.Run(HttpLoop);
        _ = Task.Run(StateLoop);
        _watcher = new FileSystemWatcher(ConfigDir, "profile.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onChange = (_, _) => _reloadTimer?.Change(350, Timeout.Infinite);
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (_, _) => _reloadTimer?.Change(350, Timeout.Infinite);
        _reloadTimer = new System.Threading.Timer(_ => ReloadProfile(), null, Timeout.Infinite, Timeout.Infinite);
        var outDir = Path.GetDirectoryName(ApkPath)!;
        Directory.CreateDirectory(outDir);
        _apkWatcher = new FileSystemWatcher(outDir, "MyDeck.apk")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _apkWatcher.Changed += (_, _) => _apkTimer?.Change(1000, Timeout.Infinite);
        _apkWatcher.Created += (_, _) => _apkTimer?.Change(1000, Timeout.Infinite);
        _apkTimer = new System.Threading.Timer(_ => OnApkChanged(), null, Timeout.Infinite, Timeout.Infinite);
        Log?.Invoke(T("Server started · profile: {0} pages", _profile.Pages.Count), LogKind.Info);
        RunEvents("start", null);
    }

    public void ReloadProfile()
    {
        try
        {
            Profile p = null!;

            for (var i = 0; ; i++)
            {
                try { p = Profile.Load(ProfilePath); break; }
                catch (IOException) when (i < 5) { Thread.Sleep(120); }
            }
            lock (_profileLock) { _profile = p; _rev++; }
            Vars.Seed(p.Vars);
            Sensors.NeedProcesses = HasKind(p, "procs");
            Media.SetKeys(MediaKeys(p));
            Plugins.SetJobs(p);
            Backup.AutoDaily(ConfigDir, ProfilePath);
            Log?.Invoke(T("Profile reloaded · {0} pages · sending to tablets", p.Pages.Count), LogKind.Good);
            foreach (var c in _clients.Keys.Where(c => c.Paired)) SendLayout(c);
            ProfileReloaded?.Invoke();
        }
        catch (Exception ex)
        {
            Log?.Invoke(T("Couldn't read the profile: {0}", ex.Message), LogKind.Error);
            Broadcast(new JsonObject { ["t"] = "toast", ["text"] = T("There's an error in the profile. Check the computer.") });
        }
    }

    async Task DiscoveryLoop()
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort)) { EnableBroadcast = true };
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult res;
                try { res = await udp.ReceiveAsync(_cts.Token); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                if (Encoding.UTF8.GetString(res.Buffer).Trim() != "MYDECK?") continue;
                var reply = Encoding.UTF8.GetBytes($"MYDECK! {TcpPort} {Environment.MachineName}");
                await udp.SendAsync(reply, reply.Length, res.RemoteEndPoint);
            }
        }
        catch (SocketException ex)
        {
            Log?.Invoke(T("Couldn't open discovery port {0} ({1}). Another MyDeck may be running.", DiscoveryPort, ex.SocketErrorCode), LogKind.Error);
        }
    }

    async Task TcpLoop()
    {
        try { _tcp = new TcpListener(IPAddress.Any, TcpPort); _tcp.Start(); }
        catch (SocketException ex) { Log?.Invoke(T("Couldn't open connection port {0} ({1}).", TcpPort, ex.SocketErrorCode), LogKind.Error); return; }

        while (!_cts.IsCancellationRequested)
        {
            TcpClient tc;
            try { tc = await _tcp.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => HandleClient(tc));
        }
    }

    sealed class Client
    {
        public required TcpClient Tcp;
        public required StreamWriter Writer;
        public readonly object WriteLock = new();
        public string Id = "", Name = "Tablet";
        public int Width = 1024, Height = 600;
        public float Density = 1f;
        public bool Paired;
        public string Address = "";
        public string LocalAddress = "";
        public string ApkHash = "";
        public string Os = "";
        public int Sdk;

        public string? Page;

        public string? AutoPage, AutoFrom;
    }

    async Task HandleClient(TcpClient tc)
    {
        tc.NoDelay = true;
        var stream = tc.GetStream();
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        var c = new Client
        {
            Tcp = tc,
            Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" },
            Address = (tc.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?",
            LocalAddress = (tc.Client.LocalEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? "",
        };
        _clients[c] = 0;
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
            {
                JsonNode? msg;
                try { msg = JsonNode.Parse(line); } catch (JsonException) { continue; }
                var type = msg?["t"]?.GetValue<string>();
                switch (type)
                {
                    case "hello": await OnHello(c, msg!); break;
                    case "ping": Send(c, new JsonObject { ["t"] = "pong", ["n"] = msg!["n"]?.DeepClone() }); break;
                    case "press" when c.Paired: OnPress(c, msg!); break;
                    case "slide" when c.Paired: OnSlide(msg!); break;
                    case "slidemute" when c.Paired: OnSlideMute(c, msg!); break;
                    case "media" when c.Paired: OnMediaCommand(msg!); break;
                    case "timerdone" when c.Paired: OnTimerDone(msg!); break;
                    case "page" when c.Paired:
                        c.Page = msg!["id"]?.GetValue<string>();

                        if (c.AutoPage is not null && c.Page != c.AutoPage) { c.AutoPage = null; c.AutoFrom = null; }
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally
        {
            _clients.TryRemove(c, out _);
            tc.Dispose();
            if (c.Paired)
            {
                Log?.Invoke(T("{0} disconnected", c.Name), LogKind.Muted);
                ClientsChanged?.Invoke();
            }
        }
    }

    async Task OnHello(Client c, JsonNode msg)
    {
        c.Id = msg["id"]?.GetValue<string>() ?? "";
        c.Name = msg["name"]?.GetValue<string>() ?? "Tablet";
        c.Width = msg["w"]?.GetValue<int>() ?? 1024;
        c.Height = msg["h"]?.GetValue<int>() ?? 600;
        c.Density = msg["density"]?.GetValue<float>() ?? 1f;
        c.ApkHash = msg["apk"]?.GetValue<string>() ?? "";
        c.Os = msg["os"]?.GetValue<string>() ?? "";
        c.Sdk = msg["sdk"]?.GetValue<int>() ?? 0;

        var devices = LoadDevices();
        var keys = LoadKeys();

        var keyField = msg["key"]?.GetValue<string>();
        var known = devices.ContainsKey(c.Id);

        if (known && keys.TryGetValue(c.Id, out var saved) && !SameKey(saved, keyField ?? ""))
        {
            known = false;
            Log?.Invoke(T("{0} ({1}) came with a wrong key · needs approval again", c.Name, c.Address), LogKind.Warn);
        }
        if (!known)
        {
            if (Interlocked.CompareExchange(ref _pairingBusy, 1, 0) != 0)
            {
                Send(c, new JsonObject { ["t"] = "toast", ["text"] = T("Another pairing is waiting on the computer. Try again in a moment.") });
                c.Tcp.Close();
                return;
            }
            try
            {
                var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000).ToString();
                Send(c, new JsonObject { ["t"] = "pair", ["code"] = code });
                Log?.Invoke(T("{0} ({1}) wants to pair · code {2}", c.Name, c.Address, code), LogKind.Warn);
                var ok = ConfirmPairing is not null && await ConfirmPairing(c.Name, code);
                if (!ok)
                {
                    Send(c, new JsonObject { ["t"] = "denied" });
                    Log?.Invoke(T("Pairing with {0} declined", c.Name), LogKind.Muted);
                    c.Tcp.Close();
                    return;
                }
            }
            finally { Interlocked.Exchange(ref _pairingBusy, 0); }
            devices[c.Id] = c.Name;
            SaveDevices(devices);
            if (keys.Remove(c.Id)) SaveKeys(keys);
        }
        if (keyField is not null)
        {
            if (!keys.TryGetValue(c.Id, out var key))
            {
                key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                keys[c.Id] = key;
                SaveKeys(keys);
            }
            if (!SameKey(key, keyField)) Send(c, new JsonObject { ["t"] = "key", ["k"] = key });
        }

        c.Paired = true;
        if (LastDeviceSize != new Size(c.Width, c.Height))
        {
            LastDeviceSize = new Size(c.Width, c.Height);
            DeviceSizeChanged?.Invoke();
        }
        Log?.Invoke(T("{0} connected ({1}) · {2}×{3}", c.Name, c.Address, c.Width, c.Height), LogKind.Good);
        ClientsChanged?.Invoke();
        SendLayout(c);
        OfferUpdate(c);
        RunEvents("connect", null);
    }

    string? _apkHash;
    DateTime _apkHashTime;

    string? CurrentApkHash()
    {
        try
        {
            if (!File.Exists(ApkPath)) return null;
            var t = File.GetLastWriteTimeUtc(ApkPath);
            if (_apkHash is null || t != _apkHashTime)
            {
                using var f = File.OpenRead(ApkPath);
                _apkHash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(f)).ToLowerInvariant();
                _apkHashTime = t;
            }
            return _apkHash;
        }
        catch (IOException) { return null; }
    }

    void OfferUpdate(Client c)
    {
        var hash = CurrentApkHash();
        if (hash is null || c.ApkHash.Length == 0 || string.Equals(hash, c.ApkHash, StringComparison.OrdinalIgnoreCase)) return;
        var host = c.LocalAddress.Length > 0 ? c.LocalAddress : LocalIPv4().FirstOrDefault()?.ToString();
        Send(c, new JsonObject { ["t"] = "update", ["url"] = $"http://{host}:{HttpPort}/MyDeck.apk" });
        Log?.Invoke(T("New version ready for {0} · offered to the tablet", c.Name), LogKind.Info);
    }

    void OnApkChanged()
    {
        Thread.Sleep(1500);
        foreach (var c in _clients.Keys.Where(c => c.Paired)) OfferUpdate(c);
    }

    void OnPluginValue(string key, PluginValue v)
    {
        Profile p; lock (_profileLock) p = _profile;
        var s = new JsonObject();
        foreach (var t in p.Pages.SelectMany(pg => pg.Tiles))
            if (t.Kind == "plugin" && t.Plugin is not null && PluginHost.Key(t.Plugin) == key) s[t.Id] = PluginHost.ToJson(v);
        if (s.Count > 0) Broadcast(new JsonObject { ["t"] = "pv", ["s"] = s });
    }

    public void RefreshPlugins()
    {
        Plugins.Reload();
        Profile p; lock (_profileLock) p = _profile;
        Plugins.SetJobs(p);
    }

    void OnTimerDone(JsonNode msg)
    {
        var id = msg["tile"]?.GetValue<string>();
        Tile? tile;
        lock (_profileLock) tile = _profile.Pages.SelectMany(p => p.Tiles).FirstOrDefault(t => t.Id == id);
        if (tile?.Kind == "timer") TimerFinished(tile);
    }

    public void TimerFinished(Tile tile)
    {
        var name = string.IsNullOrWhiteSpace(tile.Label) ? T("Timer") : tile.Label!.Trim();
        Log?.Invoke(T("{0}: time is up", name), LogKind.Info);
    }


    void OnPress(Client c, JsonNode msg)
    {
        var tileId = msg["tile"]?.GetValue<string>();
        Tile? tile;
        lock (_profileLock) tile = _profile.Pages.SelectMany(p => p.Tiles).FirstOrDefault(t => t.Id == tileId);
        if (tile is null) return;
        Press(tile, msg["g"]?.GetValue<string>() ?? "tap", text => Send(c, new JsonObject { ["t"] = "toast", ["text"] = text }));
    }

    public void Press(Tile tile, string gesture, Action<string>? toast = null)
    {
        if (gesture == "tap" && tile.Kind == "toggle")
        {
            _ = Task.Run(() => OnToggle(tile, toast));
            return;
        }
        var action = gesture switch
        {
            "hold" => tile.Hold,
            "double" => tile.Double,
            _ => tile.Kind == "slider" ? null : tile.Action,
        };
        if (action is null) return;
        _ = Task.Run(async () =>
        {
            try { await Actions.RunAsync(action); }
            catch (Exception ex)
            {
                Log?.Invoke(T("Couldn't run '{0}': {1}", tile.Label ?? tile.Id, ex.Message), LogKind.Error);
                toast?.Invoke(T("{0}: couldn't run", tile.Label));
            }
        });
    }

    public bool IsOn(Tile t) => t.Kind == "toggle" && (t.Source switch
    {
        "mute" => Audio.GetMute(false),
        "micmute" => Audio.GetMute(true),
        _ => _local.GetValueOrDefault(t.Id),
    } ?? false);

    public event Action<string?, bool>? ExternalGoTo;

    public event Action<string>? VarChanged;

    public void ShowPageOnTablets(string pageId) => GoToAll(pageId, false);

    void GoToAll(string? pageId, bool back)
    {
        if (!back && string.IsNullOrWhiteSpace(pageId)) return;
        foreach (var c in _clients.Keys.Where(c => c.Paired))
        {
            if (!back) { c.Page = pageId; c.AutoPage = null; c.AutoFrom = null; }
            Send(c, new JsonObject { ["t"] = "goto", ["page"] = back ? null : pageId, ["back"] = back });
        }
        ExternalGoTo?.Invoke(pageId, back);
    }

    string? _lastApp;
    long _nextFgCheck;
    string _lastMinute = "";
    System.Threading.Timer? _relayoutTimer;

    void AutomationTick()
    {
        var now = Environment.TickCount64;
        if (now >= _nextFgCheck)
        {
            _nextFgCheck = now + 400;
            var app = Foreground.Current();
            if (app is not null && app != _lastApp)
            {
                var prev = _lastApp;
                _lastApp = app;
                Vars.SetApp(app);
                OnAppChanged(prev, app);
            }
        }
        var minute = DateTime.Now.ToString("HH:mm");
        if (minute != _lastMinute)
        {
            var first = _lastMinute.Length == 0;
            _lastMinute = minute;
            if (!first)
            {
                RunEvents("time", minute);
                if (LabelsUse("saat") || LabelsUse("tarih") || LabelsUse("gün") || LabelsUse("time") || LabelsUse("date") || LabelsUse("day")) ScheduleRelayout();
            }
        }
    }

    void OnAppChanged(string? prev, string app)
    {
        Profile p;
        lock (_profileLock) p = _profile;
        if (prev is not null) RunEvents("appleave", prev);
        RunEvents("app", app);
        var match = p.Pages.FirstOrDefault(pg => pg.Apps?.Contains(app) == true);
        foreach (var c in _clients.Keys.Where(c => c.Paired))
        {
            if (match is not null)
            {
                if (c.Page == match.Id) continue;
                if (c.AutoPage is null) c.AutoFrom = c.Page;
                c.AutoPage = match.Id;
                c.Page = match.Id;
                Send(c, new JsonObject { ["t"] = "goto", ["page"] = match.Id });
            }
            else if (c.AutoPage is not null)
            {
                var back = p.SmartReturn && c.AutoFrom is { } from && p.Pages.Any(pg => pg.Id == from) ? c.AutoFrom : null;
                c.AutoPage = null; c.AutoFrom = null;
                if (back is not null && back != c.Page)
                {
                    c.Page = back;
                    Send(c, new JsonObject { ["t"] = "goto", ["page"] = back });
                }
            }
        }
        if (match is not null) Log?.Invoke(T("Smart profile · {0} → {1}", app, (match.Name.Length > 0 ? match.Name : match.Id)), LogKind.Muted);
        SmartPage?.Invoke(match?.Id);
        if (LabelsUse("uygulama") || LabelsUse("app")) ScheduleRelayout();
    }

    public event Action<string?>? SmartPage;

    void OnVarChanged(string name, string old, string value)
    {
        if (name is not ("uygulama" or "app"))
        {
            RunEvents("var", name);
            if (LabelsUse(name)) ScheduleRelayout();
        }
        VarChanged?.Invoke(name);
    }

    bool LabelsUse(string name)
    {
        Profile p;
        lock (_profileLock) p = _profile;
        return p.Pages.Any(pg => pg.Tiles.Any(t =>
            Variables.Uses(t.Label, name) || Variables.Uses(t.Sub, name) || Variables.Uses(t.On?.Label, name) || Variables.Uses(t.On?.Sub, name)));
    }

    void ScheduleRelayout()
    {
        _relayoutTimer ??= new System.Threading.Timer(_ =>
        {
            foreach (var c in _clients.Keys.Where(c => c.Paired)) SendLayout(c);
        }, null, Timeout.Infinite, Timeout.Infinite);
        _relayoutTimer.Change(150, Timeout.Infinite);
    }

    void RunEvents(string when, string? arg)
    {
        Profile p;
        lock (_profileLock) p = _profile;
        if (p.Events is null) return;
        foreach (var ev in p.Events)
        {
            if (!ev.Enabled || ev.When != when || ev.Do is null) continue;
            var match = when switch
            {
                "app" or "appleave" => DeckEvent.AppName(ev.Arg) == arg,
                "var" => string.Equals(ev.Arg?.Trim(), arg, StringComparison.OrdinalIgnoreCase),
                "time" => ev.Arg?.Trim() == arg,
                _ => true,
            };
            if (!match) continue;
            var action = ev.Do;
            _ = Task.Run(async () =>
            {
                try { await Actions.RunAsync(action); }
                catch (Exception ex) { Log?.Invoke(T("Event ({0}{1}) couldn't run: {2}", when, (arg is null ? "" : " · " + arg), ex.Message), LogKind.Error); }
            });
        }
    }

    public Profile CurrentProfile { get { lock (_profileLock) return _profile; } }

    Dictionary<string, string> LoadDevices()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(DevicesPath)) ?? new(); }
        catch { return new(); }
    }

    void SaveDevices(Dictionary<string, string> d) =>
        File.WriteAllText(DevicesPath, JsonSerializer.Serialize(d, Profile.Json));

    int _pairingBusy;
    string KeysPath => Path.Combine(ConfigDir, "device-keys.json");

    Dictionary<string, string> LoadKeys()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KeysPath)) ?? new(); }
        catch { return new(); }
    }

    void SaveKeys(Dictionary<string, string> d) =>
        File.WriteAllText(KeysPath, JsonSerializer.Serialize(d, Profile.Json));

    static bool SameKey(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    bool IsPairedAddress(IPAddress? ip)
    {
        if (ip is null) return false;
        var s = ip.MapToIPv4().ToString();
        return _clients.Keys.Any(c => c.Paired && IPAddress.TryParse(c.Address, out var a) && a.MapToIPv4().ToString() == s);
    }

    void Send(Client c, JsonObject msg)
    {
        try { lock (c.WriteLock) c.Writer.WriteLine(msg.ToJsonString()); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
    }

    void Broadcast(JsonObject msg)
    {
        foreach (var c in _clients.Keys.Where(c => c.Paired)) Send(c, msg);
    }

    void SendLayout(Client c)
    {
        Profile p; int rev;
        lock (_profileLock) { p = _profile; rev = _rev; }
        var th = p.Theme;
        var gap = th.Gap * c.Density;
        var pad = (th.Margin ?? th.Gap) * c.Density;

        var pages = new JsonArray();
        foreach (var page in p.Pages)
        {
            var geo = Grid.Layout(c.Width, c.Height, page, th, c.Density);
            var (cellW, cellH) = (geo.Cw, geo.Ch);
            var tiles = new JsonArray();
            foreach (var t in page.Tiles)
            {
                var tr = geo.Rect(t.X, t.Y, t.W, t.H);
                var tw = tr.Width;
                var tht = tr.Height;
                var hasText = !string.IsNullOrWhiteSpace(t.Label);
                var iconPx = (int)Math.Round(Math.Min(tw * 0.42, tht * (hasText ? 0.36 : 0.55)));
                var iconColor = t.IconColor ?? t.Color ?? th.Text;
                var png = IconRenderer.Render(t.Icon, iconPx, iconColor);
                var nav = t.Kind is null or "button" && t.Action?.Type is "page" or "folder" or "back" ? t.Action!.Type : null;
                JsonObject? on = null;
                if (t.Kind == "toggle")
                {
                    var v = t.On ?? new TileVariant();
                    var onIcon = IconRenderer.Render(v.Icon ?? t.Icon, iconPx, v.Color ?? t.IconColor ?? t.Color ?? th.Text);
                    on = new JsonObject
                    {
                        ["label"] = Vars.Expand(v.Label ?? t.Label),
                        ["sub"] = Vars.Expand(v.Sub ?? t.Sub),
                        ["color"] = v.Color ?? t.Color,
                        ["icon"] = onIcon is null ? null : Convert.ToBase64String(onIcon),
                    };
                }
                JsonObject? gif = null;
                if (t.Gif?.Path is { Length: > 0 } gp && t.Kind is null or "button" or "toggle")
                {
                    var bgMode = t.Gif.Fill != "icon";
                    var gw = bgMode ? (int)Math.Round(tw) : iconPx;
                    var gh = bgMode ? (int)Math.Round(tht) : iconPx;
                    var ga = Assets.Gif(gp, gw, gh, bgMode, t.Background ?? th.Tile);
                    if (ga is not null)
                    {
                        var d = new JsonArray();
                        foreach (var x in ga.Delays) d.Add(x);
                        gif = new JsonObject
                        {
                            ["url"] = AssetUrl(c, ga.Name), ["n"] = ga.Frames, ["w"] = ga.W, ["h"] = ga.H, ["d"] = d,
                            ["fill"] = bgMode ? "bg" : "icon", ["play"] = t.Gif.Play ?? "always",
                        };
                    }
                }
                tiles.Add(new JsonObject
                {
                    ["gif"] = gif,
                    ["live"] = LiveJson(t, th),
                    ["media"] = t.Kind == "media" ? new JsonObject
                    {
                        ["k"] = t.Media?.App?.Trim() ?? "",
                        ["tint"] = t.Media?.FollowArt ?? true,
                        ["ctl"] = t.Media?.Controls ?? true,
                        ["times"] = t.Media?.Times ?? true,
                    } : null,
                    ["clock"] = t.Kind == "clock" ? new JsonObject
                    {
                        ["style"] = t.Clock?.Style ?? "digital",
                        ["sec"] = t.Clock?.Seconds ?? true,
                        ["date"] = t.Clock?.Date ?? true,
                        ["tz"] = string.IsNullOrWhiteSpace(t.Clock?.Zone) ? null : t.Clock!.Zone,
                        ["city"] = t.Clock?.City ?? t.Label,
                    } : null,
                    ["timer"] = t.Kind == "timer" ? new JsonObject
                    {
                        ["mode"] = t.Timer?.Mode ?? "countdown",
                        ["secs"] = t.Timer?.Seconds ?? 300,
                    } : null,
                    ["wx"] = t.Kind == "weather" ? new JsonObject
                    {
                        ["view"] = t.Weather?.View ?? (t.W >= 2 && t.H >= 2 ? "hours" : "now"),
                    } : null,
                    ["pv"] = t.Kind == "plugin" ? new JsonObject
                    {
                        ["name"] = Plugins.Find(t.Plugin?.Plugin)?.Data.FirstOrDefault(d => d.Id == t.Plugin?.Item)?.Name ?? T("Plugin"),
                    } : null,
                    ["kind"] = t.Kind ?? "button",
                    ["on"] = on,
                    ["id"] = t.Id,
                    ["x"] = t.X, ["y"] = t.Y, ["w"] = t.W, ["h"] = t.H,
                    ["label"] = Vars.Expand(t.Label),
                    ["sub"] = Vars.Expand(t.Sub),
                    ["hold"] = t.Hold is not null ? true : null,
                    ["dbl"] = t.Double is not null ? true : null,
                    ["color"] = t.Color,
                    ["bg"] = t.Background,
                    ["nav"] = nav,
                    ["target"] = nav is "page" or "folder" ? t.Action!.Page : null,
                    ["sm"] = t.Kind == "slider" ? (t.Target switch { "mic" => "mic", "tablet" => "bri", _ => "spk" }) : null,
                    ["icon"] = png is null ? null : Convert.ToBase64String(png),
                });
            }
            pages.Add(new JsonObject
            {
                ["wall"] = string.IsNullOrWhiteSpace(page.Wallpaper) ? null : WallJson(c, page.Wallpaper),
                ["id"] = page.Id, ["name"] = page.Name, ["cols"] = page.Cols, ["rows"] = page.Rows,
                ["sq"] = page.Shape == "square" ? true : null,
                ["folder"] = page.Folder, ["tiles"] = tiles,
            });
        }

        Send(c, new JsonObject
        {
            ["t"] = "layout",
            ["lang"] = L.Lang,
            ["rev"] = rev,
            ["start"] = p.Start,
            ["theme"] = new JsonObject
            {
                ["bg"] = th.Background, ["tile"] = th.Tile, ["border"] = th.TileBorder, ["text"] = th.Text,
                ["sub"] = th.SubText, ["accent"] = th.Accent, ["press"] = th.Press,
                ["radius"] = th.Radius * c.Density, ["gap"] = gap, ["pad"] = pad,
                ["wall"] = WallJson(c, th.Wallpaper),
                ["dim"] = Math.Clamp(th.WallpaperDim, 0, 80) / 100.0,
                ["glass"] = Math.Clamp(th.CardGlass, 0, 80) / 100.0,
            },
            ["pages"] = pages,
            ["tablet"] = new JsonObject
            {
                ["idle"] = p.Tablet.SleepWhenIdle ? Math.Clamp(p.Tablet.IdleMinutes, 1, 240) * 60 : 0,
                ["pcoff"] = p.Tablet.SleepWhenPcOff,
            },
        });
        Send(c, new JsonObject { ["t"] = "screen", ["idle"] = _pcIdle });

        if (HasKind(p, "media"))
            foreach (var st in Media.All()) Send(c, MediaJson(st, withArt: true));

        var pvs = new JsonObject();
        foreach (var t in p.Pages.SelectMany(pg => pg.Tiles))
            if (t.Kind == "plugin" && t.Plugin is not null && Plugins.ValueJson(t.Plugin) is { } pvj) pvs[t.Id] = pvj;
        if (pvs.Count > 0) Send(c, new JsonObject { ["t"] = "pv", ["s"] = pvs });

        if (HasKind(p, "weather") && Weather.Current is { } wx) Send(c, (JsonObject)wx.DeepClone());

        if (HasLive(p)) SendHistory(c);

        var all = ComputeStates(p);
        foreach (var (id, st) in all) _lastSent[id] = st.ToJsonString();
        if (all.Count > 0) Send(c, new JsonObject { ["t"] = "state", ["s"] = ToJson(all) });
    }

    static readonly Dictionary<string, string> GroupColors = new()
    {
        ["cpu"] = "#FF3D5A", ["gpu"] = "#3CC8F0", ["ram"] = "#A78BFA", ["disk"] = "#FFB000", ["net"] = "#3CC8F0",
    };

    public string DefaultColor(string? metric) =>
        GroupColors.GetValueOrDefault(Sensors.Info(metric)?.Group ?? "", "#FF3D5A");

    public string AutoDetail(string? metric) => Sensors.Info(metric)?.Group switch
    {
        "cpu" => Sensors.CpuName,
        "gpu" => Sensors.GpuName,
        "ram" => Sensors.RamTotalGb > 0 ? $"{Math.Round(Sensors.RamTotalGb)} GB" : "",
        _ => "",
    };

    JsonObject Series(string key, string color, bool dash = false)
    {
        var m = Sensors.Info(key);
        return new JsonObject
        {
            ["k"] = key, ["name"] = m?.Short ?? key, ["unit"] = m?.Unit ?? "", ["dec"] = m?.Dec ?? 0,
            ["max"] = m?.Max ?? 0, ["min"] = m?.GaugeMin ?? 0, ["color"] = color, ["dash"] = dash, ["load"] = m?.Loadable ?? false,
        };
    }

    JsonObject? LiveJson(Tile t, Theme th)
    {
        if (!t.IsLive) return null;
        var l = t.Live ?? new LiveSpec();
        var s = new JsonArray();
        string color;
        if (t.Kind == "net")
        {
            color = t.Color ?? "#3CC8F0";
            s.Add(Series("net.down", color));
            s.Add(Series("net.up", l.Color2 ?? "#FF3D5A"));
        }
        else
        {
            color = t.Color ?? DefaultColor(l.Metric);
            if (t.Kind != "procs") s.Add(Series(l.Metric ?? "cpu", color));
            if (t.Kind == "graph" && !string.IsNullOrWhiteSpace(l.Metric2))
            {
                var c2 = l.Color2 ?? DefaultColor(l.Metric2);
                if (l.Color2 is null && c2 == color) c2 = "#FFB000";
                s.Add(Series(l.Metric2, c2, dash: true));
            }
        }
        var info = new JsonArray();
        foreach (var key in l.Info ?? new())
        {
            var m = Sensors.Info(key);
            if (m is null) continue;
            info.Add(new JsonObject { ["k"] = key, ["name"] = m.Short, ["unit"] = m.Unit, ["dec"] = m.Dec, ["max"] = m.Max });
        }
        return new JsonObject
        {
            ["style"] = l.Style ?? "area",
            ["cm"] = l.ColorMode ?? "card",
            ["sec"] = l.Seconds,
            ["detail"] = DetailFor(t),
            ["s"] = s,
            ["info"] = info,
        };
    }

    public string? DetailFor(Tile t)
    {
        if (!string.IsNullOrWhiteSpace(t.Sub)) return t.Sub;
        if (t.Kind is "net" or "procs") return null;
        var a = AutoDetail(t.Live?.Metric);
        return a.Length > 0 ? a : null;
    }

    string AssetUrl(Client c, string name)
    {
        var host = c.LocalAddress.Length > 0 ? c.LocalAddress : LocalIPv4().FirstOrDefault()?.ToString();
        return $"http://{host}:{HttpPort}/a/{name}";
    }

    JsonObject? WallJson(Client c, string? path)
    {
        var a = Assets.Wallpaper(path, (int)c.Width, (int)c.Height);
        return a is null ? null : new JsonObject { ["type"] = a.Type, ["url"] = AssetUrl(c, a.Name), ["name"] = a.Name };
    }

    void SendHistory(Client c)
    {
        var h = new JsonObject();
        foreach (var m in Sensors.Catalog)
        {
            var arr = new JsonArray();
            foreach (var v in Sensors.History(m.Key)) arr.Add(Math.Round(v, 2));
            h[m.Key] = arr;
        }
        Send(c, new JsonObject { ["t"] = "sysh", ["h"] = h });
    }

    string _hwLogged = "";

    void OnSampled()
    {
        var hw = Sensors.HwinfoState;
        if (hw != _hwLogged && hw.Length > 0)
        {
            _hwLogged = hw;
            Log?.Invoke(hw switch
                {
                    "gadget" => T("HWiNFO Gadget connected · reading CPU temperature and power"),
                    "bağlı" => T("HWiNFO connected (shared memory) · reading CPU temperature and power"),
                    "etiket" => T("CPU temperature/power not found in HWiNFO Gadget · visible sensors: {0}", Sensors.HwinfoLabels),
                    _ => T("HWiNFO not found · in HWiNFO, right-click CPU Package temperature and CPU Package Power and tick 'Report value in Gadget'"),
                },
                hw is "gadget" or "bağlı" ? LogKind.Good : LogKind.Warn);
        }
        if (!_clients.Keys.Any(c => c.Paired)) return;
        Profile p;
        lock (_profileLock) p = _profile;
        if (!HasLive(p)) return;
        var v = new JsonObject();
        foreach (var (k, val) in Sensors.Latest) v[k] = Math.Round(val, 2);
        var msg = new JsonObject { ["t"] = "sys", ["v"] = v };
        if (Sensors.NeedProcesses)
        {
            var arr = new JsonArray();
            foreach (var (name, cpu) in Sensors.TopProcesses) arr.Add(new JsonArray(name, cpu));
            msg["p"] = arr;
        }
        Broadcast(msg);
    }

    readonly ConcurrentDictionary<string, int> _artSent = new();

    JsonObject MediaJson(MediaState s, bool withArt) => new()
    {
        ["t"] = "media", ["k"] = s.Key, ["app"] = s.App, ["title"] = s.Title, ["artist"] = s.Artist, ["album"] = s.Album,
        ["playing"] = s.Playing, ["pos"] = Math.Round(s.PosNow, 2), ["dur"] = Math.Round(s.Dur, 2),
        ["seek"] = s.CanSeek, ["next"] = s.CanNext, ["prev"] = s.CanPrev, ["tint"] = s.Tint, ["rev"] = s.ArtRev,
        ["art"] = withArt && s.Art is not null ? Convert.ToBase64String(s.Art) : null,
    };

    void OnMediaChanged(MediaState s)
    {
        if (!_clients.Keys.Any(c => c.Paired)) return;
        Profile p;
        lock (_profileLock) p = _profile;
        if (!HasKind(p, "media")) return;
        var newArt = _artSent.GetValueOrDefault(s.Key) != s.ArtRev;
        _artSent[s.Key] = s.ArtRev;
        Broadcast(MediaJson(s, withArt: newArt));
    }

    void OnMediaCommand(JsonNode msg)
    {
        var id = msg["tile"]?.GetValue<string>();
        Tile? tile;
        lock (_profileLock) tile = _profile.Pages.SelectMany(p => p.Tiles).FirstOrDefault(t => t.Id == id);
        if (tile is not null) MediaCommand(tile, msg["cmd"]?.GetValue<string>() ?? "", msg["v"]?.GetValue<double>() ?? 0);
    }

    public void MediaCommand(Tile tile, string cmd, double v)
    {
        if (tile.Kind != "media" || cmd is not ("play" or "next" or "prev" or "seek")) return;
        _ = Task.Run(async () =>
        {
            try { await Media.Command(tile.Media?.App?.Trim() ?? "", cmd, v); }
            catch (Exception ex) { Log?.Invoke(T("The media command didn't work: {0}", ex.Message), LogKind.Warn); }
        });
    }


    static JsonObject ToJson(Dictionary<string, JsonObject> states)
    {
        var o = new JsonObject();
        foreach (var (id, st) in states) o[id] = st;
        return o;
    }

    Dictionary<string, JsonObject> ComputeStates(Profile p)
    {
        var result = new Dictionary<string, JsonObject>();
        bool? master = null, mic = null; var masterRead = false; var micRead = false;
        bool? Master() { if (!masterRead) { master = Audio.GetMute(false); masterRead = true; } return master; }
        bool? Mic() { if (!micRead) { mic = Audio.GetMute(true); micRead = true; } return mic; }
        foreach (var t in p.Pages.SelectMany(pg => pg.Tiles))
        {
            if (t.Kind == "toggle")
            {
                bool? on = t.Source switch
                {
                    "mute" => Master(),
                    "micmute" => Mic(),
                    _ => _local.GetValueOrDefault(t.Id),
                };
                if (on is not null) result[t.Id] = new JsonObject { ["on"] = on.Value };
            }
            else if (t.Kind == "slider" && t.Target != "tablet")
            {
                var v = Audio.GetVolume(t.Target ?? "master");
                var m = Audio.GetMuteOf(t.Target ?? "master");
                result[t.Id] = new JsonObject { ["v"] = v is null ? -1 : Math.Round(v.Value, 3), ["m"] = m ?? false };
            }
        }
        return result;
    }

    void PushStateChanges()
    {
        Profile p;
        lock (_profileLock) p = _profile;
        var diff = new Dictionary<string, JsonObject>();
        foreach (var (id, st) in ComputeStates(p))
        {
            var text = st.ToJsonString();
            if (_lastSent.TryGetValue(id, out var old) && old == text) continue;
            _lastSent[id] = text;
            diff[id] = st;
        }
        if (diff.Count > 0) Broadcast(new JsonObject { ["t"] = "state", ["s"] = ToJson(diff) });
    }

    bool _pcIdle;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public static double IdleSeconds()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li)) return 0;
        return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
    }

    void CheckIdle()
    {
        Profile p;
        lock (_profileLock) p = _profile;
        var limit = p.Tablet.SleepWhenIdle ? Math.Clamp(p.Tablet.IdleMinutes, 1, 240) * 60 : double.MaxValue;
        var idle = IdleSeconds() >= limit;
        if (idle == _pcIdle) return;
        _pcIdle = idle;
        Log?.Invoke(idle ? T("Computer is idle · turning the tablet screen off") : T("Computer in use · waking the tablet"), LogKind.Muted);
        Broadcast(new JsonObject { ["t"] = "screen", ["idle"] = idle });
    }

    async Task StateLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                AutomationTick();
                if (_clients.Keys.Any(c => c.Paired)) { PushStateChanges(); CheckIdle(); }
            }
            catch (Exception ex) { Log?.Invoke(T("Couldn't read the state: {0}", ex.Message), LogKind.Error); await Task.Delay(3000); }
            try { await Task.Delay(200, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    async Task OnToggle(Tile t, Action<string>? toast)
    {
        try
        {
            switch (t.Source)
            {
                case "mute":
                    Audio.SetMute(false, !(Audio.GetMute(false) ?? false));
                    break;
                case "micmute":
                    Audio.SetMute(true, !(Audio.GetMute(true) ?? false));
                    break;
                default:
                    var on = _local.GetValueOrDefault(t.Id);
                    var action = on ? t.On?.Action : t.Action;
                    _local[t.Id] = !on;
                    PushStateChanges();
                    if (action is not null) await Actions.RunAsync(action);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(T("Couldn't change '{0}': {1}", t.Label ?? t.Id, ex.Message), LogKind.Error);
            toast?.Invoke(T("{0}: couldn't change", t.Label));
        }
        PushStateChanges();
    }

    readonly ConcurrentDictionary<string, (double? V, bool M, long At)> _volCache = new();

    public (double? Value, bool Muted) SliderState(Tile t)
    {
        var target = t.Target ?? "master";
        if (target == "tablet") return (null, false);
        var now = Environment.TickCount64;
        if (_volCache.TryGetValue(target, out var c) && now - c.At < 150) return (c.V, c.M);
        double? v = null; var m = false;
        try { v = Audio.GetVolume(target); m = Audio.GetMuteOf(target) ?? false; } catch { }
        _volCache[target] = (v, m, now);
        return (v, m);
    }

    public void SetSlider(Tile t, double v)
    {
        if (t.Kind != "slider" || t.Target == "tablet") return;
        var target = t.Target ?? "master";
        try
        {
            if (Audio.GetMuteOf(target) == true) Audio.SetMuteOf(target, false);
            Audio.SetVolume(target, (float)Math.Clamp(v, 0, 1));
            _volCache[target] = (Math.Clamp(v, 0, 1), false, Environment.TickCount64);
            if (_clients.Keys.Any(c => c.Paired)) PushStateChanges();
        }
        catch (Exception ex) { Log?.Invoke(T("Couldn't set the volume: {0}", ex.Message), LogKind.Error); }
    }

    public void ToggleSliderMute(Tile t)
    {
        if (t.Kind != "slider" || t.Target == "tablet") return;
        var target = t.Target ?? "master";
        try
        {
            Audio.SetMuteOf(target, !(Audio.GetMuteOf(target) ?? false));
            _volCache.TryRemove(target, out _);
            if (_clients.Keys.Any(c => c.Paired)) PushStateChanges();
        }
        catch (Exception ex) { Log?.Invoke(T("Couldn't mute: {0}", ex.Message), LogKind.Error); }
    }

    void OnSlide(JsonNode msg)
    {
        var id = msg["tile"]?.GetValue<string>();
        var v = msg["v"]?.GetValue<double>() ?? -1;
        if (v < 0) return;
        Tile? tile;
        lock (_profileLock) tile = _profile.Pages.SelectMany(p => p.Tiles).FirstOrDefault(t => t.Id == id);
        if (tile?.Kind != "slider" || tile.Target == "tablet") return;
        try
        {
            var target = tile.Target ?? "master";

            if (Audio.GetMuteOf(target) == true) Audio.SetMuteOf(target, false);
            Audio.SetVolume(target, (float)v);
            _volCache[target] = (v, false, Environment.TickCount64);

            _lastSent[tile.Id] = new JsonObject { ["v"] = Math.Round(v, 3), ["m"] = false }.ToJsonString();

            var tileId = tile.Id;
            _ = Task.Delay(1500).ContinueWith(_ => _lastSent.TryRemove(tileId, out var _));
        }
        catch (Exception ex) { Log?.Invoke(T("Couldn't set the volume: {0}", ex.Message), LogKind.Error); }
    }

    void OnSlideMute(Client c, JsonNode msg)
    {
        var id = msg["tile"]?.GetValue<string>();
        Tile? tile;
        lock (_profileLock) tile = _profile.Pages.SelectMany(p => p.Tiles).FirstOrDefault(t => t.Id == id);
        if (tile?.Kind != "slider" || tile.Target == "tablet") return;
        try
        {
            var target = tile.Target ?? "master";
            Audio.SetMuteOf(target, !(Audio.GetMuteOf(target) ?? false));
            _volCache.TryRemove(target, out _);
            PushStateChanges();
        }
        catch (Exception ex)
        {
            Log?.Invoke(T("Couldn't mute: {0}", ex.Message), LogKind.Error);
            Send(c, new JsonObject { ["t"] = "toast", ["text"] = T("{0}: couldn't mute", tile.Label) });
        }
    }

    async Task HttpLoop()
    {
        try { _http = new TcpListener(IPAddress.Any, HttpPort); _http.Start(); }
        catch (SocketException ex) { Log?.Invoke(T("Couldn't open download port {0} ({1}).", HttpPort, ex.SocketErrorCode), LogKind.Error); return; }
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tc;
            try { tc = await _http.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => ServeHttp(tc));
        }
    }

    async Task ServeHttp(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_cts.Token) ?? "";
                string? h;
                while (!string.IsNullOrEmpty(h = await reader.ReadLineAsync(_cts.Token))) { }
                var path = requestLine.Split(' ').ElementAtOrDefault(1) ?? "/";

                if (path.StartsWith("/a/", StringComparison.Ordinal))
                {
                    if (!IsPairedAddress((client.Client.RemoteEndPoint as IPEndPoint)?.Address))
                    {
                        await WriteHttp(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(T("forbidden")));
                        return;
                    }
                    var name = Uri.UnescapeDataString(path[3..].Split('?')[0]);
                    var file = Assets.Resolve(name);
                    if (file is null) { await WriteHttp(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(T("not found"))); return; }
                    await WriteHttp(stream, 200, Assets.ContentType(name), await File.ReadAllBytesAsync(file, _cts.Token));
                    return;
                }
                if (path.StartsWith("/MyDeck.apk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(ApkPath)) { await WriteHttp(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(T("APK not found"))); return; }
                    var bytes = await File.ReadAllBytesAsync(ApkPath, _cts.Token);
                    Log?.Invoke(T("Downloading the APK → {0}", (client.Client.RemoteEndPoint as IPEndPoint)?.Address), LogKind.Info);
                    await WriteHttp(stream, 200, "application/vnd.android.package-archive", bytes,
                        "Content-Disposition: attachment; filename=\"MyDeck.apk\"\r\n");
                    return;
                }

                var info = File.Exists(ApkPath)
                    ? $"{new FileInfo(ApkPath).Length / 1024} KB · {File.GetLastWriteTime(ApkPath):dd.MM.yyyy HH:mm}"
                    : T("not built yet");
                var html = $$"""
<!doctype html><html lang="{{L.Lang}}"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{T("Install MyDeck")}}</title>
<style>
body{margin:0;background:#07090E;color:#E8ECF4;font-family:sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center}
.box{background:#11151F;border:1px solid #222938;border-radius:20px;padding:36px 40px;max-width:520px;text-align:center}
h1{margin:0 0 6px;font-size:28px} p{color:#9AA4B8;font-size:16px;line-height:1.5;margin:8px 0}
a.btn{display:block;margin:24px 0 12px;padding:20px;border-radius:14px;background:#FF3D5A;color:#fff;font-size:22px;font-weight:bold;text-decoration:none}
ol{text-align:left;color:#C9D2E3;font-size:15px;line-height:1.7;padding-left:22px}
</style></head><body><div class="box">
<h1>MyDeck</h1><p>{{info}}</p>
<a class="btn" href="/MyDeck.apk">{{T("Download MyDeck")}}</a>
<ol><li>{{T("When the download finishes, tap the file in the notification.")}}</li>
<li>{{T("Install → Open.")}}</li></ol>
</div></body></html>
""";
                await WriteHttp(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }

    static async Task WriteHttp(NetworkStream s, int code, string type, byte[] body, string extra = "")
    {
        var status = code switch { 200 => "200 OK", 403 => "403 Forbidden", _ => "404 Not Found" };
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n{extra}Connection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(body);
        await s.FlushAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        Sensors.Dispose();
        Media.Dispose();
        Weather.Dispose();
        Plugins.Dispose();
        _watcher?.Dispose();
        _apkWatcher?.Dispose();
        _apkTimer?.Dispose();
        _reloadTimer?.Dispose();
        _tcp?.Stop();
        _http?.Stop();
        foreach (var c in _clients.Keys) c.Tcp.Dispose();
    }
}

public enum LogKind { Info, Good, Warn, Error, Muted }
