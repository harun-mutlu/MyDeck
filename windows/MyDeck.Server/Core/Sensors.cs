using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MyDeck.Server.Core;

public sealed record MetricInfo(string Key, string Name, string Short, string Unit, int Dec, double Max, string Group, double GaugeMin = 0, bool Loadable = false);

public sealed class Sensors : IDisposable
{
    public const int HistoryLength = 300;

    public static readonly MetricInfo[] Catalog =
    {
        new("cpu", T("CPU usage"), "CPU", "%", 0, 100, "cpu", Loadable: true),
        new("cpu.clock", T("CPU clock"), T("Clock"), "GHz", 2, 0, "cpu"),
        new("cpu.temp", T("CPU temperature (HWiNFO)"), T("Temperature"), "°C", 0, 100, "cpu", 30, Loadable: true),
        new("cpu.hotcore", T("Hottest core (HWiNFO)"), T("Hottest"), "°C", 0, 100, "cpu", 30, Loadable: true),
        new("cpu.power", T("CPU power (HWiNFO)"), T("Power"), "W", 0, 190, "cpu", Loadable: true),
        new("cpu.procs", T("Process count"), T("Processes"), "", 0, 0, "cpu"),
        new("uptime", T("Uptime"), T("Up"), "süre", 0, 0, "cpu"),
        new("gpu", T("GPU usage"), "GPU", "%", 0, 100, "gpu", Loadable: true),
        new("gpu.temp", T("GPU temperature"), T("Temperature"), "°C", 0, 100, "gpu", 30, Loadable: true),
        new("gpu.power", T("GPU power"), T("Power"), "W", 0, 350, "gpu", Loadable: true),
        new("gpu.clock", T("GPU clock"), T("Clock"), "MHz", 0, 0, "gpu"),
        new("gpu.vram", T("VRAM used"), "VRAM", "GB", 1, 12, "gpu", Loadable: true),
        new("gpu.vrampct", T("VRAM usage"), "VRAM", "%", 0, 100, "gpu", Loadable: true),
        new("gpu.fan", "GPU fan", "Fan", "%", 0, 100, "gpu"),
        new("ram", T("RAM usage"), "RAM", "%", 0, 100, "ram", Loadable: true),
        new("ram.used", T("RAM used"), T("Used"), "GB", 1, 32, "ram", Loadable: true),
        new("ram.free", T("Free RAM"), T("Free"), "GB", 1, 32, "ram"),
        new("disk", T("Disk activity"), "Disk", "%", 0, 100, "disk", Loadable: true),
        new("disk.read", T("Disk read"), T("Read"), "MB/s", 1, 0, "disk"),
        new("disk.write", T("Disk write"), T("Write"), "MB/s", 1, 0, "disk"),
        new("disk.total", T("Disk transfer"), T("Transfer"), "MB/s", 1, 0, "disk"),
        new("net.down", T("Download"), T("Download"), "Mbps", 1, 0, "net"),
        new("net.up", T("Upload"), T("Upload"), "Mbps", 1, 0, "net"),
    };

    static readonly Dictionary<string, MetricInfo> ByKey = Catalog.ToDictionary(m => m.Key);

    public MetricInfo? Info(string? key)
    {
        if (key is null || !ByKey.TryGetValue(key, out var m)) return null;
        return key switch
        {
            "ram.used" or "ram.free" when RamTotalGb > 0 => m with { Max = Math.Round(RamTotalGb) },
            "gpu.vram" when VramTotalGb > 0 => m with { Max = Math.Round(VramTotalGb) },
            "gpu.power" when PowerLimitW > 0 => m with { Max = Math.Round(PowerLimitW) },
            _ => m,
        };
    }

    public string CpuName { get; private set; } = "";
    public string GpuName { get; private set; } = "";
    public double RamTotalGb { get; private set; }
    public double VramTotalGb { get; private set; }
    public double PowerLimitW { get; private set; }
    public bool HasNvidia { get; private set; }

    public volatile bool NeedProcesses;

    public event Action? Sampled;

    readonly object _lock = new();
    readonly Dictionary<string, double[]> _hist = new();
    int _head;
    long _samples;
    Dictionary<string, double> _latest = new();
    List<(string Name, double Cpu)> _procs = new();
    readonly CancellationTokenSource _cts = new();
    readonly Thread _thread;

    public Sensors()
    {
        foreach (var m in Catalog) _hist[m.Key] = new double[HistoryLength];
        _thread = new Thread(Loop) { IsBackground = true, Name = "MyDeck sensors", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public Dictionary<string, double> Latest { get { lock (_lock) return new(_latest); } }
    public List<(string Name, double Cpu)> TopProcesses { get { lock (_lock) return new(_procs); } }

    public double[] History(string key, int n = HistoryLength)
    {
        n = Math.Clamp(n, 1, HistoryLength);
        var r = new double[n];
        lock (_lock)
        {
            if (!_hist.TryGetValue(key, out var buf)) return r;
            var have = (int)Math.Min(_samples, HistoryLength);
            for (var i = 0; i < n; i++)
            {
                var back = n - i;
                if (back > have) { r[i] = have > 0 ? buf[(_head - have + HistoryLength) % HistoryLength] : 0; continue; }
                r[i] = buf[(_head - back + HistoryLength) % HistoryLength];
            }
        }
        return r;
    }

    void Loop()
    {
        try { Init(); } catch { }
        var sw = Stopwatch.StartNew();
        var next = 1000L;
        while (!_cts.IsCancellationRequested)
        {
            var wait = next - sw.ElapsedMilliseconds;
            if (wait > 0 && _cts.Token.WaitHandle.WaitOne((int)wait)) break;
            next += 1000;
            if (sw.ElapsedMilliseconds > next) next = sw.ElapsedMilliseconds + 1000;
            try { Sample(); } catch { }
            try { Sampled?.Invoke(); } catch { }
        }
    }

    IntPtr _query;
    IntPtr _cCpu, _cPerf, _cProcs, _cDiskIdle, _cDiskRead, _cDiskWrite, _cNetIn, _cNetOut, _cGpu3d;
    double _baseMhz;
    IntPtr _nv;

    void Init()
    {
        using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            CpuName = CleanCpuName(k?.GetValue("ProcessorNameString") as string ?? "");
            _baseMhz = Convert.ToDouble(k?.GetValue("~MHz") ?? 0);
        }

        if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0)
        {
            _cCpu = Add(@"\Processor Information(_Total)\% Processor Utility", @"\Processor(_Total)\% Processor Time");
            _cPerf = Add(@"\Processor Information(_Total)\% Processor Performance");
            _cProcs = Add(@"\System\Processes");
            _cDiskIdle = Add(@"\PhysicalDisk(_Total)\% Idle Time");
            _cDiskRead = Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _cDiskWrite = Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            _cNetIn = Add(@"\Network Interface(*)\Bytes Received/sec");
            _cNetOut = Add(@"\Network Interface(*)\Bytes Sent/sec");
            _cGpu3d = Add(@"\GPU Engine(*engtype_3D)\Utilization Percentage");
            PdhCollectQueryData(_query);
        }

        try
        {
            if (nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out _nv) == 0)
            {
                HasNvidia = true;
                var sb = new StringBuilder(96);
                if (nvmlDeviceGetName(_nv, sb, 96) == 0) GpuName = CleanGpuName(sb.ToString());
                if (nvmlDeviceGetMemoryInfo(_nv, out var mem) == 0) VramTotalGb = mem.Total / 1073741824.0;
                if (nvmlDeviceGetEnforcedPowerLimit(_nv, out var lim) == 0) PowerLimitW = lim / 1000.0;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }

        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref ms)) RamTotalGb = ms.ullTotalPhys / 1073741824.0;
    }

    IntPtr Add(string path, string? fallback = null)
    {
        if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var c) == 0) return c;
        if (fallback is not null && PdhAddEnglishCounter(_query, fallback, IntPtr.Zero, out c) == 0) return c;
        return IntPtr.Zero;
    }

    void Sample()
    {
        var v = new Dictionary<string, double>();
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            v["cpu"] = Math.Clamp(Value(_cCpu), 0, 100);
            var perf = Value(_cPerf);
            if (_baseMhz > 0 && perf > 0) v["cpu.clock"] = _baseMhz * perf / 100 / 1000;
            v["cpu.procs"] = Value(_cProcs);
            v["disk"] = Math.Clamp(100 - Value(_cDiskIdle), 0, 100);
            var rd = Value(_cDiskRead) / 1048576; var wr = Value(_cDiskWrite) / 1048576;
            v["disk.read"] = rd; v["disk.write"] = wr; v["disk.total"] = rd + wr;

            v["net.down"] = MaxOf(_cNetIn) * 8 / 1_000_000;
            v["net.up"] = MaxOf(_cNetOut) * 8 / 1_000_000;
            if (!HasNvidia) v["gpu"] = Math.Clamp(SumOf(_cGpu3d), 0, 100);
        }
        v["uptime"] = Environment.TickCount64 / 1000.0;

        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref ms))
        {
            var total = ms.ullTotalPhys / 1073741824.0;
            var free = ms.ullAvailPhys / 1073741824.0;
            v["ram"] = (total - free) / total * 100;
            v["ram.used"] = total - free;
            v["ram.free"] = free;
        }

        if (HasNvidia)
        {
            if (nvmlDeviceGetUtilizationRates(_nv, out var u) == 0) v["gpu"] = u.Gpu;
            if (nvmlDeviceGetTemperature(_nv, 0, out var t) == 0) v["gpu.temp"] = t;
            if (nvmlDeviceGetPowerUsage(_nv, out var mw) == 0) v["gpu.power"] = mw / 1000.0;
            if (nvmlDeviceGetClockInfo(_nv, 0, out var clk) == 0) v["gpu.clock"] = clk;
            if (nvmlDeviceGetMemoryInfo(_nv, out var mem) == 0 && mem.Total > 0)
            {
                v["gpu.vram"] = mem.Used / 1073741824.0;
                v["gpu.vrampct"] = (double)mem.Used / mem.Total * 100;
            }
            if (nvmlDeviceGetFanSpeed(_nv, out var fan) == 0) v["gpu.fan"] = fan;
        }

        SampleHwinfo(v);

        var procs = NeedProcesses ? SampleProcesses() : null;

        lock (_lock)
        {
            foreach (var m in Catalog)
            {
                var val = v.TryGetValue(m.Key, out var x) && double.IsFinite(x) ? x : _latest.GetValueOrDefault(m.Key);
                _hist[m.Key][_head] = val;
                v[m.Key] = val;
            }
            _head = (_head + 1) % HistoryLength;
            _samples++;
            _latest = v;
            if (procs is not null) _procs = procs;
        }
    }

    public string HwinfoState { get; private set; } = "";

    System.IO.MemoryMappedFiles.MemoryMappedFile? _hwFile;
    System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? _hw;
    DateTime _hwRetryAt;
    uint _hwCount;
    long _hwTemp = -1, _hwHot = -1, _hwPower = -1;

    static readonly string[] TempLabels = { "CPU Package", "CPU (Tctl/Tdie)", "CPU Die (average)", "CPU" };
    static readonly string[] HotLabels = { "Core Max", "CPU (Tctl/Tdie)", "Core Temperatures (avg)" };
    static readonly string[] PowerLabels = { "CPU Package Power", "CPU PPT", "CPU Power" };

    public string HwinfoLabels { get; private set; } = "";

    void SampleHwinfo(Dictionary<string, double> v)
    {
        if (SampleGadget(v)) return;
        SampleShm(v);
    }

    bool SampleGadget(Dictionary<string, double> v)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\HWiNFO64\VSB");
            if (key is null) return false;
            var labels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 200; i++)
            {
                if (key.GetValue("Label" + i) is not string label) break;
                var raw = (key.GetValue("ValueRaw" + i) as string ?? key.GetValue("Value" + i) as string ?? "").Trim();
                var num = new string(raw.Replace(',', '.').TakeWhile(ch => char.IsDigit(ch) || ch is '.' or '-').ToArray());
                if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    labels.TryAdd(label.Trim(), d);
            }
            if (labels.Count == 0) return false;
            double? Pick(string[] names) { foreach (var n in names) if (labels.TryGetValue(n, out var d)) return d; return null; }
            var temp = Pick(TempLabels); var hot = Pick(HotLabels); var power = Pick(PowerLabels);

            temp ??= labels.FirstOrDefault(k => k.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase) && k.Key.Contains("Package", StringComparison.OrdinalIgnoreCase) && !k.Key.Contains("Power", StringComparison.OrdinalIgnoreCase)).Value is var tp and > 0 ? tp : null;
            power ??= labels.FirstOrDefault(k => k.Key.Contains("Power", StringComparison.OrdinalIgnoreCase) && k.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase)).Value is var pw and > 0 ? pw : null;
            if (temp is { } a) v["cpu.temp"] = a;
            if (hot is { } b) v["cpu.hotcore"] = b;
            else if (temp is { } a2) v["cpu.hotcore"] = a2;
            if (power is { } c) v["cpu.power"] = c;
            HwinfoLabels = string.Join(", ", labels.Keys);
            if (temp is null && power is null) { HwinfoState = "etiket"; return true; }
            HwinfoState = "gadget";
            return true;
        }
        catch { return false; }
    }

    void SampleShm(Dictionary<string, double> v)
    {
        if (_hw is null)
        {
            if (DateTime.UtcNow < _hwRetryAt) return;
            _hwRetryAt = DateTime.UtcNow.AddSeconds(10);
            foreach (var name in new[] { @"Global\HWiNFO_SENS_SM2", "HWiNFO_SENS_SM2" })
            {
                try
                {
                    _hwFile = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(name, System.IO.MemoryMappedFiles.MemoryMappedFileRights.Read);
                    _hw = _hwFile.CreateViewAccessor(0, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
                    break;
                }
                catch { _hwFile?.Dispose(); _hwFile = null; }
            }
            if (_hw is null) { HwinfoState = "yok"; return; }
            _hwCount = 0;
        }
        try
        {
            if (_hw.ReadUInt32(0) != 0x53695748) throw new InvalidDataException();
            var readOff = _hw.ReadUInt32(32);
            var readSize = _hw.ReadUInt32(36);
            var count = _hw.ReadUInt32(40);
            if (count != _hwCount)
            {
                _hwCount = count;
                _hwTemp = Find(readOff, readSize, count, 1, TempLabels);
                _hwHot = Find(readOff, readSize, count, 1, HotLabels);
                _hwPower = Find(readOff, readSize, count, 5, PowerLabels);
            }
            if (_hwTemp >= 0) v["cpu.temp"] = _hw.ReadDouble(_hwTemp + 284);
            if (_hwHot >= 0) v["cpu.hotcore"] = _hw.ReadDouble(_hwHot + 284);
            if (_hwPower >= 0) v["cpu.power"] = _hw.ReadDouble(_hwPower + 284);
            HwinfoState = "bağlı";
        }
        catch
        {
            _hw?.Dispose(); _hwFile?.Dispose(); _hw = null; _hwFile = null;
            HwinfoState = "yok";
        }
    }

    long Find(uint readOff, uint size, uint count, uint type, string[] labels)
    {
        var buf = new byte[128];
        var found = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < count; i++)
        {
            long off = readOff + (long)i * size;
            if (_hw!.ReadUInt32(off) != type) continue;
            _hw.ReadArray(off + 12, buf, 0, 128);
            var n = Array.IndexOf(buf, (byte)0);
            var label = Encoding.Latin1.GetString(buf, 0, n < 0 ? 128 : n).Trim();
            if (!found.ContainsKey(label)) found[label] = off;
        }
        foreach (var l in labels) if (found.TryGetValue(l, out var o)) return o;
        return -1;
    }

    readonly Dictionary<int, (string Name, TimeSpan Cpu)> _lastCpu = new();
    DateTime _lastProcAt;

    List<(string, double)> SampleProcesses()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastProcAt).TotalMilliseconds;
        _lastProcAt = now;
        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id is 0 or 4) continue;
                try
                {
                    var t = p.TotalProcessorTime;
                    seen.Add(p.Id);
                    if (_lastCpu.TryGetValue(p.Id, out var old) && elapsed is > 200 and < 5000)
                    {
                        var pct = (t - old.Cpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100;
                        if (pct > 0) byName[old.Name] = byName.GetValueOrDefault(old.Name) + pct;
                    }
                    _lastCpu[p.Id] = (p.ProcessName, t);
                }
                catch { }
            }
        }
        foreach (var id in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) _lastCpu.Remove(id);
        return byName.OrderByDescending(kv => kv.Value).Take(5).Select(kv => (kv.Key, Math.Round(kv.Value, 1))).ToList();
    }

    static string CleanCpuName(string s)
    {
        s = s.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^\d+(th|st|nd|rd) Gen\s+", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*@.*$", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+\d+-Core Processor$", "");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }

    static string CleanGpuName(string s) =>
        s.Replace("NVIDIA ", "").Replace("GeForce ", "").Trim();

    static double Value(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return 0;
        return PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) == 0 && v.CStatus is 0 or 1
            ? v.DoubleValue : 0;
    }

    static IEnumerable<(string Name, double Value)> Items(IntPtr counter)
    {
        if (counter == IntPtr.Zero) yield break;
        uint size = 0;
        if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0) yield break;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buf) != 0) yield break;
            var stride = IntPtr.Size + 16;
            var list = new List<(string, double)>();
            for (var i = 0; i < count; i++)
            {
                var item = buf + i * stride;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                var status = Marshal.ReadInt32(item + IntPtr.Size);
                var val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                if (status is 0 or 1) list.Add((name, val));
            }
            foreach (var x in list) yield return x;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static double MaxOf(IntPtr counter)
    {
        var m = 0.0;
        foreach (var (name, val) in Items(counter))
            if (!name.Contains("isatap", StringComparison.OrdinalIgnoreCase) && !name.Contains("Teredo", StringComparison.OrdinalIgnoreCase))
                m = Math.Max(m, val);
        return m;
    }

    static double SumOf(IntPtr counter) => Items(counter).Sum(x => x.Value);

    public void Dispose()
    {
        _cts.Cancel();
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _hw?.Dispose(); _hwFile?.Dispose();
        try { if (HasNvidia) nvmlShutdown(); } catch { }
    }

    const uint PDH_FMT_DOUBLE = 0x00000200;
    const uint PDH_FMT_NOCAP100 = 0x00008000;
    const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Explicit)]
    struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong Total, Free, Used; }

    const string NV = "nvml.dll";
    [DllImport(NV)] static extern int nvmlInit_v2();
    [DllImport(NV)] static extern int nvmlShutdown();
    [DllImport(NV)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(NV, CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport(NV)] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvUtil util);
    [DllImport(NV)] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(NV)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(NV)] static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);
    [DllImport(NV)] static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(NV)] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvMem memory);
    [DllImport(NV)] static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint percent);
}
