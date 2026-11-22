using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MyDeck.Server.Editor;

static class WeatherArtwork
{
    static readonly string[] Kinds = ["sun", "moon", "cloudsun", "cloudmoon", "cloud", "rain", "sleet", "snow", "storm", "fog", "wind"];
    static readonly Lazy<Atlas?> Assets = new(Load);
    static readonly Dictionary<string, Particle[]> Schedules = new();
    static long _nextFlash;

    sealed record Atlas(Bitmap[] Icons, Bitmap[] Drops, Bitmap Bolt, Bitmap[] Leaves);
    sealed record Particle(bool Snow, long Start, int Duration);

    static Atlas? Load()
    {
        using var stream = typeof(WeatherArtwork).Assembly.GetManifestResourceStream("weather-art.png");
        if (stream is null) return null;
        using var image = new Bitmap(stream);
        var icons = Kinds.Select((kind, i) =>
        {
            int[] tops = [0, 366, 708], heights = [287, 258, 270];
            var cutoff = kind == "storm" ? 158 : 147;
            return Layer(image, i % 4 * 362, tops[i / 4], 362, heights[i / 4], (x, y, color) => kind switch
            {
                "sun" => Math.Clamp((132 - MathF.Sqrt((x - 194) * (x - 194) + (y - 174) * (y - 174))) / 47, 0, 1),
                "rain" or "sleet" or "snow" or "storm" => Math.Clamp((cutoff + 11f - y) / 20, 0, 1),
                "wind" => 1 - Warm(color),
                _ => 1
            });
        }).ToArray();
        return new Atlas(icons,
            [Layer(image, 553, 546, 16, 32), Layer(image, 602, 514, 16, 34), Layer(image, 432, 574, 20, 34)],
            Layer(image, 0, 708, 362, 270, (_, y, color) => y < 138 ? 0 : Warm(color)),
            [Layer(image, 942, 764, 84, 86, (_, _, color) => Warm(color)),
             Layer(image, 801, 857, 85, 70, (_, _, color) => Warm(color))]);
    }

    static float Warm(Color color) => Math.Clamp((color.R - color.B - 8) / 28f, 0, 1);

    static Bitmap Layer(Bitmap image, int left, int top, int width, int height, Func<int, int, Color, float>? mask = null)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = image.GetPixel(left + x, top + y);
            var strength = Math.Max(color.R - 13, Math.Max(color.G - 20, color.B - 33));
            var alpha = Math.Clamp((strength - 3) / 35f, 0, 1) * (mask?.Invoke(x, y, color) ?? 1);
            result.SetPixel(x, y, Color.FromArgb((int)(alpha * 255), color));
        }
        return result;
    }

    public static bool Draw(Graphics g, string kind, float cx, float cy, float size, float opacity)
    {
        if (Assets.Value is not { } atlas) return false;
        if (size <= 0) return true;
        var index = Array.IndexOf(Kinds, kind);
        var image = atlas.Icons[index < 0 ? 4 : index];
        var scale = size / image.Width;
        var height = image.Height * scale;
        var left = cx - size / 2;
        var top = cy - height / 2;
        var now = Environment.TickCount64;
        var state = g.Save();
        g.SetClip(new RectangleF(left, cy - size / 2, size, size), CombineMode.Intersect);
        var imageState = g.Save();
        if (kind == "sun") Rotate(g, left + 194 * scale, top + 174 * scale, now % 22000 * 360f / 22000);
        var pulse = kind is "moon" or "cloudsun" or "cloudmoon" or "cloud" or "fog" or "wind"
            ? .89f + .11f * MathF.Cos(now % 7000 * 2 * MathF.PI / 7000) : 1;
        Picture(g, image, left, top, size, height, opacity * pulse);
        g.Restore(imageState);

        if (kind is "rain" or "sleet" or "snow" or "storm")
        {
            if (kind == "storm")
            {
                if (_nextFlash == 0 || now > _nextFlash + 240) _nextFlash = now + Random.Shared.Next(2300, 5700);
                var flash = (now - _nextFlash) switch { >= 0 and < 105 => 1f, >= 155 and < 225 => .63f, _ => 0f };
                if (flash > 0) Picture(g, atlas.Bolt, left - size * .09f, top - height * .15f, size * 1.18f, height * 1.15f, opacity * flash);
            }
            Precipitation(g, atlas, kind, left, top, size, scale, opacity, now, image.Height);
        }
        else if (kind == "wind")
        {
            for (var i = 0; i < atlas.Leaves.Length; i++)
            {
                var leaf = atlas.Leaves[i];
                var phase = (now + i * 1700) % 3900 / 3900f;
                var x = left + size * (-.12f + 1.24f * phase);
                var y = top + scale * (95 + i * 46 - phase * 30);
                var w = leaf.Width * scale * .7f;
                var h = leaf.Height * scale * .7f;
                var leafState = g.Save();
                Rotate(g, x, y, phase * 170 + i * 50);
                Picture(g, leaf, x - w / 2, y - h / 2, w, h, opacity * Fade(phase, 14, 14));
                g.Restore(leafState);
            }
        }
        g.Restore(state);
        return true;
    }

    static void Rotate(Graphics g, float x, float y, float angle)
    {
        g.TranslateTransform(x, y);
        g.RotateTransform(angle);
        g.TranslateTransform(-x, -y);
    }

    static float Fade(float phase, float entry, float exit) => Math.Min(1, Math.Min(phase * entry, (1 - phase) * exit));

    static Particle NewParticle(bool snow, long start) =>
        new(snow, start, snow ? Random.Shared.Next(1300, 2200) : Random.Shared.Next(470, 1020));

    static void Precipitation(Graphics g, Atlas atlas, string kind, float left, float top, float size, float scale, float opacity, long now, int height)
    {
        if (!Schedules.TryGetValue(kind, out var particles))
        {
            var count = kind switch { "rain" => 11, "sleet" => 10, _ => 8 };
            particles = Enumerable.Range(0, count)
                .Select(i => NewParticle(kind == "snow" || kind == "sleet" && i % 2 == 0, now + Random.Shared.Next(1100))).ToArray();
            Schedules.Add(kind, particles);
        }
        var start = kind == "storm" ? 158 : 147;
        for (var i = 0; i < particles.Length; i++)
        {
            var particle = particles[i];
            var phase = (now - particle.Start) / (float)particle.Duration;
            if (phase < 0) continue;
            if (phase >= 1)
            {
                particles[i] = NewParticle(particle.Snow, now + Random.Shared.Next(100, 1100));
                continue;
            }
            var x = left + size * (.16f + .68f * (i + .5f) / particles.Length);
            var y = top + scale * (start - 5 + phase * (height - start - 8));
            var alpha = opacity * Fade(phase, 16, 10);
            if (particle.Snow)
            {
                var radius = scale * (13.65f + i % 3 * 1.575f);
                using var pen = new Pen(Color.FromArgb((int)(alpha * 255), 242, 251, 255), Math.Max(.8f, radius * .14f))
                    { StartCap = LineCap.Round, EndCap = LineCap.Round };
                for (var arm = 0; arm < 6; arm++)
                {
                    var angle = arm * MathF.PI / 3;
                    var dx = MathF.Cos(angle) * radius;
                    var dy = MathF.Sin(angle) * radius;
                    g.DrawLine(pen, x, y, x + dx, y + dy);
                    for (var joint = .48f; joint < .8f; joint += .28f)
                    {
                        var jx = x + dx * joint;
                        var jy = y + dy * joint;
                        g.DrawLine(pen, jx, jy, jx + dx * .18f - dy * .22f, jy + dy * .18f + dx * .22f);
                        g.DrawLine(pen, jx, jy, jx + dx * .18f + dy * .22f, jy + dy * .18f - dx * .22f);
                    }
                }
            }
            else
            {
                var drop = atlas.Drops[i % atlas.Drops.Length];
                var factor = scale * (.94f + i % 3 * .08f);
                Picture(g, drop, x - drop.Width * factor / 2, y, drop.Width * factor, drop.Height * factor, alpha);
            }
        }
    }

    static void Picture(Graphics g, Bitmap image, float x, float y, float w, float h, float opacity)
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
        var points = new PointF[] { new(x, y), new(x + w, y), new(x, y + h) };
        g.DrawImage(image, points, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, attributes);
    }
}
