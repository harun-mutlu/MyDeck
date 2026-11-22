using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MyDeck.Server.Core;

public static class IconRenderer
{
    static readonly ConcurrentDictionary<string, byte[]?> Cache = new();
    static readonly string GlyphFont = PickGlyphFont();

    static string PickGlyphFont()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public static byte[]? Render(string? icon, int size, string color)
    {
        if (string.IsNullOrWhiteSpace(icon) || size < 8) return null;
        size = Math.Clamp(size, 8, 256);
        return Cache.GetOrAdd($"{icon}|{size}|{color}", _ =>
        {
            try
            {
                using var bmp = icon.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase)
                    ? Glyph(icon[6..], size, ColorTranslator.FromHtml(color))
                    : FromFile(Environment.ExpandEnvironmentVariables(icon), size);
                if (bmp is null) return null;
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch { return null; }
        });
    }

    public static void Clear() => Cache.Clear();

    static Bitmap Glyph(string hex, int size, Color color)
    {
        var text = char.ConvertFromUtf32(Convert.ToInt32(hex.Trim(), 16));
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var family = new FontFamily(GlyphFont);
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Regular, size, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return bmp;

        var scale = size * 0.92f / Math.Max(b.Width, b.Height);
        using var m = new Matrix();
        m.Translate(size / 2f, size / 2f);
        m.Scale(scale, scale);
        m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
        path.Transform(m);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        return bmp;
    }

    static Bitmap? FromFile(string path, int size)
    {
        if (!File.Exists(path)) return null;
        Image? src = null;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".exe" or ".lnk" or ".ico" or ".dll")
            {
                using var ico = Icon.ExtractAssociatedIcon(path);
                if (ico is null) return null;
                src = ico.ToBitmap();
            }
            else src = Image.FromFile(path);

            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var s = Math.Min((float)size / src.Width, (float)size / src.Height);
            var w = src.Width * s; var h = src.Height * s;
            g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
            return bmp;
        }
        finally { src?.Dispose(); }
    }
}
