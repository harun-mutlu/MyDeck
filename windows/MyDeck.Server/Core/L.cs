using System.Globalization;

namespace MyDeck.Server.Core;

public static class L
{
    public static string Lang { get; private set; } = "en";
    public static bool Turkish => Lang == "tr";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static readonly (string Code, string Name)[] Languages = { ("en", "English"), ("tr", "Türkçe") };

    public static void Init(string? lang)
    {
        Lang = lang == "tr" ? "tr" : "en";
        Culture = CultureInfo.GetCultureInfo(Turkish ? "tr-TR" : "en-US");
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    public static string T(string en) =>
        Turkish && Lang_tr.Map.TryGetValue(en, out var tr) ? tr : en;

    public static string T(string en, params object?[] args) =>
        string.Format(Culture, T(en), args);
}
