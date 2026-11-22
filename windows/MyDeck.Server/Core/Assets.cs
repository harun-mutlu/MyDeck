using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MyDeck.Server.Core;

public sealed record Asset(string Name, string Type);

public sealed record GifAsset(string Name, int Frames, int W, int H, int[] Delays);

public sealed class Assets
{
    public static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp" };
    public static readonly string[] VideoExts = { ".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v", ".gif" };

    readonly string _root;
    public string CacheDir { get; }
    string FfmpegPath => Path.Combine(_root, "tools", "ffmpeg", "ffmpeg.exe");

    public event Action<string, LogKind>? Log;

    public event Action? Ready;

    readonly ConcurrentDictionary<string, Task> _jobs = new();
    readonly ConcurrentDictionary<string, GifAsset> _gifs = new();
    readonly ConcurrentDictionary<string, (List<Bitmap> Frames, int[] Delays)> _previewGifs = new();
    readonly SemaphoreSlim _ffmpegLock = new(1);

    public Assets(string root)
    {
        _root = root;
        CacheDir = Path.Combine(root, "config", "cache");
        Directory.CreateDirectory(CacheDir);
    }

    public static bool IsVideo(string path) => VideoExts.Contains(Path.GetExtension(path).ToLowerInvariant());

    static string Hash(params object[] parts)
    {
        var s = string.Join("|", parts);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();
    }

    static string FileKey(string path)
    {
        var fi = new FileInfo(path);
        return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
    }

    public string? Resolve(string name)
    {
        if (name.Length == 0 || name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var p = Path.Combine(CacheDir, name);
        return File.Exists(p) ? p : null;
    }

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg", ".png" => "image/png", ".mp4" => "video/mp4", _ => "application/octet-stream",
    };

    public Asset? Wallpaper(string? path, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || w < 16 || h < 16) return null;
        try
        {
            if (!IsVideo(path))
            {
                var name = $"wall_{Hash(FileKey(path), w, h)}.jpg";
                var outPath = Path.Combine(CacheDir, name);
                if (!File.Exists(outPath))
                {
                    using var src = LoadImage(path);
                    using var bmp = Cover(src, w, h);
                    SaveJpeg(bmp, outPath, 90);
                }
                return new Asset(name, "image");
            }
            var vname = $"wall_{Hash(FileKey(path), w, h)}.mp4";
            var vout = Path.Combine(CacheDir, vname);
            if (File.Exists(vout)) return new Asset(vname, "video");
            var key = vname;
            _jobs.GetOrAdd(key, k0 => Task.Run(async () =>
            {
                try
                {
                    await ConvertVideo(path, vout, w, h);
                    Log?.Invoke(T("Live wallpaper ready: {0}", Path.GetFileName(path)), LogKind.Good);
                    Ready?.Invoke();
                }
                catch (Exception ex) { Log?.Invoke(T("Couldn't prepare the live wallpaper: {0}", ex.Message), LogKind.Error); }
                finally { _jobs.TryRemove(key, out _); }
            }));
            return null;
        }
        catch (Exception ex)
        {
            Log?.Invoke(T("Couldn't read the wallpaper ({0}): {1}", Path.GetFileName(path), ex.Message), LogKind.Error);
            return null;
        }
    }

    readonly ConcurrentDictionary<string, Image> _posters = new();

    public Image? Poster(string? path, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var name = $"poster_{Hash(FileKey(path), w, h)}.jpg";
            if (_posters.TryGetValue(name, out var img)) return img;
            var p = Path.Combine(CacheDir, name);
            if (!File.Exists(p))
            {
                if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase) || !IsVideo(path))
                {
                    using var src = LoadImage(path);
                    using var bmp = Cover(src, w, h);
                    SaveJpeg(bmp, p, 85);
                }
                else
                {
                    if (!File.Exists(FfmpegPath)) return null;
                    _jobs.GetOrAdd(name, k0 => Task.Run(async () =>
                    {
                        try { await RunFfmpeg($"-y -ss 0.5 -i \"{path}\" -frames:v 1 -vf \"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}\" \"{p}\""); }
                        finally { _jobs.TryRemove(name, out _); }
                    }));
                    return null;
                }
            }
            if (_posters.Count > 16) _posters.Clear();
            return _posters[name] = Image.FromStream(new MemoryStream(File.ReadAllBytes(p)));
        }
        catch { return null; }
    }

    async Task ConvertVideo(string input, string output, int w, int h)
    {
        await EnsureFfmpeg();
        Log?.Invoke(T("Preparing the live wallpaper for the tablet: {0}…", Path.GetFileName(input)), LogKind.Info);
        var tmp = output + ".tmp.mp4";

        var args = $"-y -i \"{input}\" -t 60 -an " +
                   $"-vf \"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},fps=30,format=yuv420p\" " +
                   $"-c:v libx264 -profile:v baseline -level 3.1 -preset veryfast -crf 24 -movflags +faststart \"{tmp}\"";
        var code = await RunFfmpeg(args);
        if (code != 0 || !File.Exists(tmp)) throw new InvalidOperationException(T("ffmpeg couldn't convert it (the file is damaged or not supported)"));
        File.Move(tmp, output, true);
    }

    async Task<int> RunFfmpeg(string args)
    {
        var psi = new ProcessStartInfo(FfmpegPath, "-hide_banner -loglevel error " + args)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    static readonly string[] FfmpegSources =
    {
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
    };

    async Task EnsureFfmpeg()
    {
        if (File.Exists(FfmpegPath)) return;
        await _ffmpegLock.WaitAsync();
        try
        {
            if (File.Exists(FfmpegPath)) return;
            var dir = Path.GetDirectoryName(FfmpegPath)!;
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.zip*")) try { File.Delete(old); } catch { }
            Exception? last = null;
            foreach (var url in FfmpegSources)
            {
                var part = Path.Combine(dir, "ffmpeg.zip.part");
                try
                {
                    Log?.Invoke(T("Downloading the video converter (ffmpeg) from {0}. This happens once…", new Uri(url).Host), LogKind.Info);
                    await Download(url, part);
                    using (var a = ZipFile.OpenRead(part))
                    {
                        var e = a.Entries.FirstOrDefault(x => x.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidDataException(T("ffmpeg.exe wasn't found in the package"));
                        e.ExtractToFile(FfmpegPath + ".tmp", true);
                    }
                    File.Move(FfmpegPath + ".tmp", FfmpegPath, true);
                    File.Delete(part);
                    Log?.Invoke(T("ffmpeg is ready"), LogKind.Good);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    try { File.Delete(part); } catch { }
                    Log?.Invoke(T("Couldn't download ffmpeg ({0}): {1}", new Uri(url).Host, ex.Message), LogKind.Warn);
                }
            }
            throw new InvalidOperationException("ffmpeg indirilemedi: " + last?.Message);
        }
        finally { _ffmpegLock.Release(); }
    }

    async Task Download(string url, string target)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MyDeck");
        using var head = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, head.Token);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs = File.Create(target);
        var buf = new byte[1 << 16];
        long got = 0; var nextLog = 25;
        while (true)
        {
            using var stall = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            int n;
            try { n = await src.ReadAsync(buf, stall.Token); }
            catch (OperationCanceledException) { throw new TimeoutException("indirme 30 saniyedir ilerlemiyor"); }
            if (n == 0) break;
            await fs.WriteAsync(buf.AsMemory(0, n));
            got += n;
            if (total > 0 && got * 100 / total >= nextLog)
            {
                Log?.Invoke(T("Downloading ffmpeg · {0}%", nextLog), LogKind.Muted);
                nextLog += 25;
            }
        }
        if (total > 0 && got < total) throw new IOException(T("the download was interrupted"));
    }

    public GifAsset? Gif(string? path, int w, int h, bool cover, string bgHex)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || w < 8 || h < 8) return null;
        var key = Hash(FileKey(path), w, h, cover, bgHex);
        if (_gifs.TryGetValue(key, out var cached) && File.Exists(Path.Combine(CacheDir, cached.Name))) return cached;
        try
        {
            var bpp = cover ? 2 : 4;
            const int budget = 6 * 1024 * 1024;
            var maxFrames = Math.Clamp(budget / Math.Max(1, w * h * bpp), 1, 60);
            var fw = w; var fh = h;
            if (maxFrames < 8)
            {
                var k = Math.Sqrt(maxFrames / 8.0 + 1e-6);
                fw = Math.Max(8, (int)(w * k)); fh = Math.Max(8, (int)(h * k));
                maxFrames = Math.Clamp(budget / Math.Max(1, fw * fh * bpp), 1, 60);
            }
            var (frames, delays) = DecodeGif(path, fw, fh, cover, maxFrames, cover ? Ui(bgHex) : Color.Transparent);
            try
            {
                using var sheet = new Bitmap(fw, fh * frames.Count, cover ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(sheet))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    for (var i = 0; i < frames.Count; i++) g.DrawImageUnscaled(frames[i], 0, i * fh);
                }
                var name = $"gif_{key}.{(cover ? "jpg" : "png")}";
                var outPath = Path.Combine(CacheDir, name);
                if (cover) SaveJpeg(sheet, outPath, 86); else sheet.Save(outPath, ImageFormat.Png);
                var asset = new GifAsset(name, frames.Count, fw, fh, delays);
                _gifs[key] = asset;
                return asset;
            }
            finally { foreach (var f in frames) f.Dispose(); }
        }
        catch (Exception ex)
        {
            Log?.Invoke(T("Couldn't read the GIF ({0}): {1}", Path.GetFileName(path), ex.Message), LogKind.Error);
            return null;
        }
    }

    public (List<Bitmap> Frames, int[] Delays)? PreviewGif(string? path, int w, int h, bool cover)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || w < 8 || h < 8) return null;
        var key = Hash(FileKey(path), w, h, cover);
        if (_previewGifs.TryGetValue(key, out var v)) return v;
        try
        {
            if (_previewGifs.Count > 24) { foreach (var x in _previewGifs.Values) x.Frames.ForEach(f => f.Dispose()); _previewGifs.Clear(); }
            var r = DecodeGif(path, w, h, cover, 60, Color.Transparent);
            _previewGifs[key] = r;
            return r;
        }
        catch { return null; }
    }

    static Color Ui(string hex) { try { return ColorTranslator.FromHtml(hex); } catch { return Color.Black; } }

    static (List<Bitmap>, int[]) DecodeGif(string path, int w, int h, bool cover, int maxFrames, Color bg)
    {
        using var img = LoadImage(path);
        var count = 1;
        FrameDimension? dim = null;
        if (img.FrameDimensionsList.Length > 0)
        {
            dim = new FrameDimension(img.FrameDimensionsList[0]);
            count = Math.Max(1, img.GetFrameCount(dim));
        }
        var raw = new int[count];
        try
        {
            var prop = img.GetPropertyItem(0x5100);
            for (var i = 0; i < count && prop?.Value is { } v && i * 4 + 3 < v.Length; i++) raw[i] = BitConverter.ToInt32(v, i * 4) * 10;
        }
        catch { }
        for (var i = 0; i < count; i++) if (raw[i] < 20) raw[i] = 100;

        var pick = new List<int>();
        var step = Math.Max(1.0, (double)count / maxFrames);
        for (var x = 0.0; x < count && pick.Count < maxFrames; x += step) pick.Add((int)x);
        var frames = new List<Bitmap>(pick.Count);
        var delays = new int[pick.Count];
        for (var n = 0; n < pick.Count; n++)
        {
            var from = pick[n];
            var to = n + 1 < pick.Count ? pick[n + 1] : count;
            for (var i = from; i < to; i++) delays[n] += raw[i];
            delays[n] = Math.Max(40, delays[n]);
            if (dim is not null) img.SelectActiveFrame(dim, from);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, Fit(img.Width, img.Height, w, h, cover));
            }
            frames.Add(bmp);
        }
        return (frames, delays);
    }

    static Image LoadImage(string path) => Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));

    static RectangleF Fit(int sw, int sh, int w, int h, bool cover)
    {
        var s = cover ? Math.Max((float)w / sw, (float)h / sh) : Math.Min((float)w / sw, (float)h / sh);
        var dw = sw * s; var dh = sh * s;
        return new RectangleF((w - dw) / 2, (h - dh) / 2, dw, dh);
    }

    public static Bitmap Cover(Image src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, Fit(src.Width, src.Height, w, h, true));
        return bmp;
    }

    static void SaveJpeg(Image bmp, string path, long quality)
    {
        var enc = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var prm = new EncoderParameters(1);
        prm.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        var tmp = path + ".tmp";
        bmp.Save(tmp, enc, prm);
        File.Move(tmp, path, true);
    }
}
