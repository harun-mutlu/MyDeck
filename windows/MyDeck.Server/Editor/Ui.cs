namespace MyDeck.Server.Editor;

static class Ui
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0A0B0E");
    public static readonly Color Panel = ColorTranslator.FromHtml("#101216");
    public static readonly Color Card = ColorTranslator.FromHtml("#16191F");
    public static readonly Color Field = ColorTranslator.FromHtml("#1F232B");
    public static readonly Color Border = ColorTranslator.FromHtml("#272C35");
    public static readonly Color Ink = ColorTranslator.FromHtml("#EDEFF3");
    public static readonly Color Ink2 = ColorTranslator.FromHtml("#C3C8D2");
    public static readonly Color Muted = ColorTranslator.FromHtml("#858C99");

    public static readonly Color Accent = ColorTranslator.FromHtml("#FF6A5C");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#FF8274");

    public static readonly Color Cyan = ColorTranslator.FromHtml("#5EEAD4");
    public static readonly Color Good = ColorTranslator.FromHtml("#6EE7A1");
    public static readonly Color Bad = ColorTranslator.FromHtml("#FF6B6B");

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 14f);

    public static Label Caption(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        Font = Small,
        AutoSize = true,
        Margin = new Padding(0, 10, 0, 5),
    };

    public static TextBox TextBox() => new ModernTextBox { Width = 260, Margin = new Padding(0, 0, 0, 4) };

    public static ComboBox Combo() => new ModernCombo { Width = 260, Margin = new Padding(0, 0, 0, 4) };

    public static ModernStepper Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 120 };

    public static Button Button(string text, bool primary = false, int width = 0)
    {
        var b = new ModernButton
        {
            Text = text,
            Kind = primary ? ButtonKind.Primary : ButtonKind.Secondary,
            Height = 34,
            Margin = new Padding(0, 0, 6, 6),
        };
        if (width > 0) b.Width = width; else b.AutoSize = true;
        return b;
    }

    public static FlowLayoutPanel Stack() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0),
        BackColor = Color.Transparent,
    };

    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0),
        BackColor = Color.Transparent,
    };

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return ColorTranslator.FromHtml(hex); } catch { return null; }
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

static class Deck
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#07090E");
    public static readonly Color Panel = ColorTranslator.FromHtml("#0C1018");
    public static readonly Color Card = ColorTranslator.FromHtml("#11151F");
    public static readonly Color Field = ColorTranslator.FromHtml("#161B27");
    public static readonly Color Border = ColorTranslator.FromHtml("#222938");
    public static readonly Color Ink = ColorTranslator.FromHtml("#E8ECF4");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8D97AB");
    public static readonly Color Accent = ColorTranslator.FromHtml("#FF3D5A");
    public static readonly Color Cyan = ColorTranslator.FromHtml("#3CC8F0");
    public static readonly Color Good = ColorTranslator.FromHtml("#4ADE80");
}
