using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyDeck.Server.Core;

public static class Audio
{
    static readonly object Gate = new();
    static IMMDeviceEnumerator? _enum;

    static IMMDeviceEnumerator Enumerator => _enum ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    static IMMDevice? Default(bool capture)
    {
        try
        {
            Marshal.ThrowExceptionForHR(Enumerator.GetDefaultAudioEndpoint(capture ? EDataFlow.Capture : EDataFlow.Render, ERole.Multimedia, out var dev));
            return dev;
        }
        catch { return null; }
    }

    static IAudioEndpointVolume? Endpoint(bool capture)
    {
        var dev = Default(capture);
        if (dev is null) return null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 , IntPtr.Zero, out var o));
        return (IAudioEndpointVolume)o;
    }

    public static bool? GetMute(bool capture)
    {
        lock (Gate)
        {
            try { var ep = Endpoint(capture); if (ep is null) return null; ep.GetMute(out var m); return m; }
            catch { return null; }
        }
    }

    public static void SetMute(bool capture, bool mute)
    {
        lock (Gate)
        {
            var ep = Endpoint(capture) ?? throw new InvalidOperationException(capture ? T("No microphone found.") : T("No audio output found."));
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(ep.SetMute(mute, ref ctx));
        }
    }

    public static float? GetVolume(string target)
    {
        lock (Gate)
        {
            try
            {
                if (target is "master" or "mic")
                {
                    var ep = Endpoint(target == "mic");
                    if (ep is null) return null;
                    ep.GetMasterVolumeLevelScalar(out var v);
                    return v;
                }
                if (target.StartsWith("app:"))
                {
                    foreach (var vol in AppVolumes(target[4..])) { vol.GetMasterVolume(out var v); return v; }
                }
            }
            catch { }
            return null;
        }
    }

    public static void SetVolume(string target, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        lock (Gate)
        {
            var ctx = Guid.Empty;
            if (target is "master" or "mic")
            {
                var ep = Endpoint(target == "mic") ?? throw new InvalidOperationException(T("No audio device found."));
                Marshal.ThrowExceptionForHR(ep.SetMasterVolumeLevelScalar(value, ref ctx));
                return;
            }
            if (target.StartsWith("app:"))
                foreach (var vol in AppVolumes(target[4..])) vol.SetMasterVolume(value, ref ctx);
        }
    }

    public static bool? GetMuteOf(string target)
    {
        if (target == "master") return GetMute(false);
        if (target == "mic") return GetMute(true);
        if (!target.StartsWith("app:")) return null;
        lock (Gate)
        {
            try { foreach (var vol in AppVolumes(target[4..])) { vol.GetMute(out var m); return m; } }
            catch { }
            return null;
        }
    }

    public static void SetMuteOf(string target, bool mute)
    {
        if (target == "master") { SetMute(false, mute); return; }
        if (target == "mic") { SetMute(true, mute); return; }
        if (!target.StartsWith("app:")) return;
        lock (Gate)
        {
            var ctx = Guid.Empty;
            foreach (var vol in AppVolumes(target[4..])) vol.SetMute(mute, ref ctx);
        }
    }

    public static List<string> ListApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            try
            {
                foreach (var (pid, _) in Sessions())
                {
                    if (pid == 0) continue;
                    try { names.Add(Process.GetProcessById((int)pid).ProcessName); } catch { }
                }
            }
            catch { }
        }
        return names.ToList();
    }

    static IEnumerable<ISimpleAudioVolume> AppVolumes(string processName)
    {
        foreach (var (pid, ctl) in Sessions())
        {
            if (pid == 0) continue;
            string name;
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { continue; }
            if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                yield return (ISimpleAudioVolume)ctl;
        }
    }

    static List<(uint Pid, IAudioSessionControl2 Ctl)> Sessions()
    {
        var list = new List<(uint, IAudioSessionControl2)>();
        var dev = Default(false);
        if (dev is null) return list;
        var iid = typeof(IAudioSessionManager2).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out var o));
        var mgr = (IAudioSessionManager2)o;
        Marshal.ThrowExceptionForHR(mgr.GetSessionEnumerator(out var en));
        en.GetCount(out var count);
        for (var i = 0; i < count; i++)
        {
            en.GetSession(i, out var s);
            if (s is IAudioSessionControl2 c2)
            {
                c2.GetProcessId(out var pid);
                list.Add((pid, c2));
            }
        }
        return list;
    }

    enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);

        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
