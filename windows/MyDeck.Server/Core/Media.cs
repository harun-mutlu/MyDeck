using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WinMedia = Windows.Media.Control;

namespace MyDeck.Server.Core;

public sealed class MediaState
{
    public string Key = "";
    public string App = "", AppId = "", Title = "", Artist = "", Album = "";
    public bool Playing, CanSeek, CanNext, CanPrev;
    public double Pos, Dur;
    public DateTime At = DateTime.UtcNow;
    public string Tint = "#FF3D5A";
    public byte[]? Art;
    public int ArtRev;
    public byte[]? PreviousSongArt;
    public string Sig = "";
    public int ArtTries;

    public DateTime SongAt = DateTime.UtcNow, ArtCheckAt = DateTime.MinValue;

    public double PosNow => Playing ? Math.Min(Dur > 0 ? Dur : double.MaxValue, Pos + (DateTime.UtcNow - At).TotalSeconds) : Pos;
    public bool Empty => Title.Length == 0;
}

public sealed class MediaWatcher : IDisposable
{
    public event Action<MediaState>? Changed;

    readonly object _lock = new();
    readonly Dictionary<string, MediaState> _states = new();
    HashSet<string> _keys = new() { "" };
    WinMedia.GlobalSystemMediaTransportControlsSessionManager? _mgr;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _poke = new(0);
    int _artRev;

    public MediaWatcher() => _ = Task.Run(Loop);

    public void SetKeys(IEnumerable<string> keys)
    {
        lock (_lock) _keys = new HashSet<string>(keys.Append(""), StringComparer.OrdinalIgnoreCase);
        Poke();
    }

    public MediaState? Get(string key) { lock (_lock) return _states.GetValueOrDefault(key); }
    public List<MediaState> All() { lock (_lock) return _states.Values.ToList(); }

    void Poke() { if (_poke.CurrentCount == 0) _poke.Release(); }

    public event Action<string>? Warn;
    string? _lastError;

    void Report(string where, Exception ex)
    {
        var m = $"{where}: {ex.GetType().Name}: {ex.Message}";
        if (m == _lastError) return;
        _lastError = m;
        Warn?.Invoke(m);
    }

    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Poll(); }
            catch (Exception ex) { Report(T("Media manager"), ex); _mgr = null; }
            try { await _poke.WaitAsync(700, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    async Task Poll()
    {
        _mgr ??= await WinMedia.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = _mgr.GetSessions().ToList();
        string[] keys;
        lock (_lock) keys = _keys.ToArray();
        foreach (var key in keys)
        {
            try
            {
                var s = Pick(key, sessions);
                MediaState? old;
                lock (_lock) old = _states.GetValueOrDefault(key);
                var now = await Read(key, s, old);
                if (now is null) continue;
                lock (_lock) _states[key] = now;
                Changed?.Invoke(now);
            }
            catch (Exception ex) { Report(T("Couldn't read media ({0})", key.Length > 0 ? key : T("automatic")), ex); }
        }
    }

    WinMedia.GlobalSystemMediaTransportControlsSession? Pick(string key, List<WinMedia.GlobalSystemMediaTransportControlsSession> sessions)
    {
        if (key.Length > 0)
            return sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains(key, StringComparison.OrdinalIgnoreCase));
        static bool Playing(WinMedia.GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo().PlaybackStatus == WinMedia.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        return sessions.FirstOrDefault(Playing) ?? _mgr?.GetCurrentSession() ?? sessions.FirstOrDefault();
    }

    async Task<MediaState?> Read(string key, WinMedia.GlobalSystemMediaTransportControlsSession? s, MediaState? old)
    {
        if (s is null)
        {
            if (old is { Empty: true }) return null;
            return new MediaState { Key = key, ArtRev = old?.ArtRev ?? 0 };
        }
        var props = await s.TryGetMediaPropertiesAsync();
        var info = s.GetPlaybackInfo();
        var tl = s.GetTimelineProperties();
        var st = new MediaState
        {
            Key = key,
            AppId = s.SourceAppUserModelId,
            App = FriendlyApp(s.SourceAppUserModelId),
            Title = props?.Title ?? "",
            Artist = props?.Artist ?? "",
            Album = props?.AlbumTitle ?? "",
            Playing = info.PlaybackStatus == WinMedia.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            CanSeek = info.Controls.IsPlaybackPositionEnabled,
            CanNext = info.Controls.IsNextEnabled,
            CanPrev = info.Controls.IsPreviousEnabled,
            Dur = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds),
        };
        st.Sig = $"{st.AppId}|{st.Title}|{st.Artist}|{st.Album}";

        var pos = (tl.Position - tl.StartTime).TotalSeconds;
        if (st.Playing && tl.LastUpdatedTime.Year > 2000)
            pos += Math.Max(0, (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds);
        st.Pos = st.Dur > 0 ? Math.Clamp(pos, 0, st.Dur) : Math.Max(0, pos);
        st.At = DateTime.UtcNow;

        var sameSong = old is not null && old.Sig == st.Sig;
        st.ArtRev = sameSong ? old!.ArtRev : Interlocked.Increment(ref _artRev);
        st.SongAt = sameSong ? old!.SongAt : DateTime.UtcNow;
        if (sameSong && old!.Art is not null)
        {
            st.Art = old.Art;
            st.Tint = old.Tint;
            st.ArtTries = old.ArtTries;
            st.PreviousSongArt = old.PreviousSongArt;
            st.ArtCheckAt = old.ArtCheckAt;

            var now = DateTime.UtcNow;
            if ((now - st.SongAt).TotalSeconds < 15 || (now - st.ArtCheckAt).TotalSeconds >= 5)
            {
                st.ArtCheckAt = now;
                var (art, tint) = await LoadArt(props);
                if (art is not null && !art.AsSpan().SequenceEqual(old.Art)
                    && (st.PreviousSongArt is null || !art.AsSpan().SequenceEqual(st.PreviousSongArt)))
                {
                    st.Art = art;
                    st.Tint = tint;
                    st.ArtRev = Interlocked.Increment(ref _artRev);
                }
            }
        }
        else
        {
            st.ArtTries = sameSong ? Math.Min(old!.ArtTries + 1, 6) : 1;
            st.PreviousSongArt = sameSong ? old!.PreviousSongArt : old?.Art ?? old?.PreviousSongArt;
            var (art, tint) = await LoadArt(props);
            if (art is not null && (st.ArtTries >= 6 || st.PreviousSongArt is null
                || !art.AsSpan().SequenceEqual(st.PreviousSongArt)))
            {
                st.Art = art;
                st.Tint = tint;
                st.ArtRev = Interlocked.Increment(ref _artRev);
                if (st.ArtTries >= 6) st.PreviousSongArt = null;
            }
        }
        if (old is not null && sameSong && old.Playing == st.Playing && old.ArtRev == st.ArtRev
            && old.ArtTries == st.ArtTries
            && Math.Abs(old.Dur - st.Dur) < 1 && old.CanSeek == st.CanSeek && old.CanNext == st.CanNext && old.CanPrev == st.CanPrev
            && Math.Abs(old.PosNow - st.Pos) < 3.5)
            return null;
        return st;
    }

    public async Task<string> Command(string key, string cmd, double value = 0)
    {
        _mgr ??= await WinMedia.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = _mgr.GetSessions().ToList();
        var s = Pick(key, sessions);
        if (s is null)
            return $"no session for '{key}'";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = false;
        switch (cmd)
        {
            case "play": ok = await s.TryTogglePlayPauseAsync(); break;
            case "next": ok = await s.TrySkipNextAsync(); break;
            case "prev": ok = await s.TrySkipPreviousAsync(); break;
            case "seek":
                var start = s.GetTimelineProperties().StartTime;
                ok = await s.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(Math.Max(0, value))).Ticks);
                break;
        }
        var took = sw.ElapsedMilliseconds;
        await Task.Delay(150);
        Poke();
        return $"{s.SourceAppUserModelId}: {(ok ? "ok" : "rejected")} in {took} ms";
    }

    public List<(string Id, string Name)> ListApps()
    {
        try
        {
            var mgr = _mgr;
            if (mgr is null) return new();
            return mgr.GetSessions().Select(s => (s.SourceAppUserModelId, FriendlyApp(s.SourceAppUserModelId)))
                .DistinctBy(x => x.SourceAppUserModelId).ToList();
        }
        catch { return new(); }
    }

    public static string FriendlyApp(string id)
    {
        var l = id.ToLowerInvariant();
        if (l.Contains("youtube-music") || l.Contains("youtubemusic")) return "YouTube Music";
        if (l.Contains("spotify")) return "Spotify";
        if (l.Contains("chrome")) return "Chrome";
        if (l.Contains("msedge") || l.Contains("microsoftedge")) return "Edge";
        if (l.Contains("firefox")) return "Firefox";
        if (l.Contains("zen")) return "Zen";
        if (l.Contains("vlc")) return "VLC";
        if (l.Contains("zunemusic") || l.Contains("media.player")) return T("Media Player");
        var name = id.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.Split('\\', '/').Last();
        var parts = name.Split('.', '_');
        name = parts.LastOrDefault(p => p.Length > 2) ?? name;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : id;
    }

    static async Task<(byte[]? Art, string Tint)> LoadArt(WinMedia.GlobalSystemMediaTransportControlsSessionMediaProperties? p)
    {
        if (p?.Thumbnail is null) return (null, "#FF3D5A");
        try
        {
            using var ras = await p.Thumbnail.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var src = Image.FromStream(ms);

            var side = Math.Min(src.Width, src.Height);
            var size = Math.Min(512, side);
            using var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size),
                    new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            }
            using var outMs = new MemoryStream();
            var enc = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var prm = new EncoderParameters(1);
            prm.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 86L);
            bmp.Save(outMs, enc, prm);
            return (outMs.ToArray(), Tint(bmp));
        }
        catch { return (null, "#FF3D5A"); }
    }

    static string Tint(Bitmap bmp)
    {
        using var small = new Bitmap(24, 24);
        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(bmp, 0, 0, 24, 24); }
        double r = 0, gr = 0, b = 0, w = 0, ar = 0, ag = 0, ab = 0;
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x++)
            {
                var c = small.GetPixel(x, y);
                var sat = c.GetSaturation(); var bri = c.GetBrightness();
                var weight = sat * sat * (1 - Math.Abs(bri - 0.55) * 1.4);
                if (weight > 0) { r += c.R * weight; gr += c.G * weight; b += c.B * weight; w += weight; }
                ar += c.R; ag += c.G; ab += c.B;
            }
        Color col = w > 2 ? Color.FromArgb((int)(r / w), (int)(gr / w), (int)(b / w)) : Color.FromArgb((int)(ar / 576), (int)(ag / 576), (int)(ab / 576));

        var h = col.GetHue(); var s = Math.Max(col.GetSaturation(), w > 2 ? 0.55f : 0.15f); var l = Math.Clamp(col.GetBrightness(), 0.5f, 0.62f);
        return Hsl(h, s, l);
    }

    static string Hsl(float h, float s, float l)
    {
        double C = (1 - Math.Abs(2 * l - 1)) * s, X = C * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - C / 2;
        var (r, g, b) = h switch
        {
            < 60 => (C, X, 0d), < 120 => (X, C, 0d), < 180 => (0d, C, X),
            < 240 => (0d, X, C), < 300 => (X, 0d, C), _ => (C, 0d, X),
        };
        return $"#{(int)((r + m) * 255):X2}{(int)((g + m) * 255):X2}{(int)((b + m) * 255):X2}";
    }

    public void Dispose() => _cts.Cancel();
}
