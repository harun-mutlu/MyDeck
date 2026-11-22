using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyDeck.Server.Core;

public static class Actions
{
    public static PluginHost? Plugins { get; set; }

    public static Variables? Vars { get; set; }

    public static Action<string?, bool>? GoTo { get; set; }

    static string? X(string? s) => Vars?.Expand(s) ?? s;

    public static Task RunAsync(TileAction action) => RunAsync(action, 0);

    static async Task RunAsync(TileAction action, int depth)
    {
        if (depth > 16) throw new InvalidOperationException(T("Actions are nested too deeply (a loop calling itself?)."));
        switch (action.Type)
        {
            case "keys":
                SendChord(action.Keys ?? throw new InvalidDataException(T("The 'keys' action has no keys.")));
                break;
            case "media":
                SendChord(action.Key switch
                {
                    "playpause" => "media_play_pause",
                    "next" => "media_next",
                    "prev" => "media_prev",
                    "stop" => "media_stop",
                    "volup" => "volume_up",
                    "voldown" => "volume_down",
                    "mute" => "volume_mute",
                    _ => throw new InvalidDataException(T("Unknown media key: {0}", action.Key))
                });
                break;
            case "run":
                if (string.IsNullOrWhiteSpace(action.Path)) throw new InvalidDataException(T("The 'run' action has no path."));
                Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(X(action.Path)!))
                {
                    Arguments = X(action.Args) ?? "",
                    UseShellExecute = true,
                });
                break;
            case "url":
                Process.Start(new ProcessStartInfo(X(action.Url) ?? "") { UseShellExecute = true });
                break;
            case "lock":
                if (!LockWorkStation()) throw new InvalidOperationException(T("Couldn't lock the screen."));
                break;
            case "delay":
                await Task.Delay(Math.Clamp(action.Ms, 0, 10_000));
                break;
            case "multi":
                foreach (var step in action.Steps ?? new()) await RunAsync(step, depth + 1);
                break;
            case "setvar":
                (Vars ?? throw new InvalidOperationException(T("Variables aren't ready."))).Apply(action.Var, action.Op, action.Value);
                break;
            case "if":
                if (Vars is null) throw new InvalidOperationException(T("Variables aren't ready."));
                foreach (var step in (Vars.Test(action.Var, action.Op, action.Value) ? action.Steps : action.Else) ?? new())
                    await RunAsync(step, depth + 1);
                break;
            case "plugin":
                if (Plugins is null) throw new InvalidOperationException(T("Plugins aren't loaded."));
                await Plugins.RunActionAsync(action.Plugin?.Plugin, action.Plugin?.Item, action.Plugin?.Args);
                break;
            case "page" or "folder":

                GoTo?.Invoke(action.Page, false);
                break;
            case "back":
                GoTo?.Invoke(null, true);
                break;
            default:
                throw new InvalidDataException(T("Unknown action type: '{0}'", action.Type));
        }
    }

    static readonly Dictionary<string, ushort> Vk = BuildKeyMap();

    static Dictionary<string, ushort> BuildKeyMap()
    {
        var m = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
            ["enter"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20,
            ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D,
            ["home"] = 0x24, ["end"] = 0x23, ["pgup"] = 0x21, ["pgdn"] = 0x22,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["printscreen"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14,
            ["media_play_pause"] = 0xB3, ["media_next"] = 0xB0, ["media_prev"] = 0xB1, ["media_stop"] = 0xB2,
            ["volume_up"] = 0xAF, ["volume_down"] = 0xAE, ["volume_mute"] = 0xAD,
            ["="] = 0xBB, ["+"] = 0xBB, ["-"] = 0xBD, ["."] = 0xBE, [","] = 0xBC,
        };
        for (var c = 'a'; c <= 'z'; c++) m[c.ToString()] = (ushort)char.ToUpperInvariant(c);
        for (var d = 0; d <= 9; d++) m[d.ToString()] = (ushort)('0' + d);
        for (var f = 1; f <= 24; f++) m["f" + f] = (ushort)(0x70 + f - 1);
        return m;
    }

    static readonly HashSet<ushort> Extended = new() { 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x26, 0x28, 0x25, 0x27, 0x5B, 0x2C };

    public static void SendChord(string chord)
    {
        var parts = chord.Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries);

        if (chord.TrimEnd().EndsWith("++")) parts = parts.Append("+").ToArray();
        var codes = parts.Select(p => Vk.TryGetValue(p, out var v) ? v : throw new InvalidDataException(T("Unknown key: '{0}'", p))).ToList();

        var inputs = new List<INPUT>();
        foreach (var c in codes) inputs.Add(Key(c, up: false));
        for (var i = codes.Count - 1; i >= 0; i--) inputs.Add(Key(codes[i], up: true));
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) throw new InvalidOperationException(T("Couldn't send the keys. Windows blocks this while a window running as administrator is in front."));
    }

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,

                wScan = (ushort)MapVirtualKey(vk, 0),
                dwFlags = (up ? 0x0002u : 0) | (Extended.Contains(vk) ? 0x0001u : 0),
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
}
