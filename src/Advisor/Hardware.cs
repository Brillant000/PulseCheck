using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PulseCheck;

public enum Usage { Gaming, Creator, Work, Dev }

/// <summary>Everything the advisor needs to know about this PC.</summary>
public sealed class HardwareInfo
{
    public string CpuName = "", Socket = "", Board = "";
    public int Cores, Threads;
    public int? CpuTier;
    public double RamGB, RamMaxGB;
    public int RamModules, RamSlots, RamSpeed;
    public string RamType = "?";
    public bool RamSoldered;
    public string GpuName = "", GpuVendor = "?";
    public double VramGB;
    public int? GpuTier;
    public bool GpuIntegrated, GpuLaptopVariant;
    public DateTime? GpuDriverDate;
    public int ScreenW, ScreenH, ScreenHz, ScreenMaxHz;
    public bool IsLaptop, HasSsd, HasHdd;
    public double SysFreePct = 100, SysFreeGB;
    public string PowerPlan = "";
    public bool GameDvrOn, GameModeOff;
    public int StartupCount;
    public double UptimeDays;
}

/// <summary>Estimated performance classes loaded from the embedded Data/hardware.json.</summary>
public sealed class GpuDb
{
    public sealed record Gpu(Regex Re, int Tier, int Vram, bool Igpu);
    public sealed record GpuClass(int Tier, int Psu, int Cost, string[] Examples);
    public sealed record SocketInfo(int Tier, string[] Examples);

    public static GpuDb Instance { get; } = Load();

    public string AsOf = "";
    public List<Gpu> Gpus = new();
    public List<GpuClass> Classes = new();
    public Dictionary<string, SocketInfo> Sockets = new();
    public string[] PlatformExamples = Array.Empty<string>();

    static GpuDb Load()
    {
        var db = new GpuDb();
        using var s = typeof(GpuDb).Assembly.GetManifestResourceStream("PulseCheck.Data.hardware.json")!;
        using var doc = JsonDocument.Parse(s);
        var r = doc.RootElement;
        db.AsOf = r.GetProperty("dataAsOf").GetString() ?? "";
        foreach (var g in r.GetProperty("gpus").EnumerateArray())
            db.Gpus.Add(new Gpu(new Regex(g.GetProperty("re").GetString()!, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                g.GetProperty("tier").GetInt32(), g.GetProperty("vram").GetInt32(),
                g.TryGetProperty("igpu", out var ig) && ig.GetBoolean()));
        foreach (var c in r.GetProperty("classes").EnumerateArray())
            db.Classes.Add(new GpuClass(c.GetProperty("tier").GetInt32(), c.GetProperty("psu").GetInt32(), c.GetProperty("cost").GetInt32(),
                c.GetProperty("examples").EnumerateArray().Select(x => x.GetString()!).ToArray()));
        foreach (var p in r.GetProperty("sockets").EnumerateObject())
            db.Sockets[p.Name] = new SocketInfo(p.Value.GetProperty("tier").GetInt32(),
                p.Value.GetProperty("examples").EnumerateArray().Select(x => x.GetString()!).ToArray());
        db.PlatformExamples = r.GetProperty("platform").GetProperty("examples").EnumerateArray().Select(x => x.GetString()!).ToArray();
        return db;
    }

    /// <summary>Class (1-10) of a graphics card name, or null if unknown. Laptop variants rate one class lower.</summary>
    public (int tier, int vram, bool igpu)? FindGpu(string name)
    {
        foreach (var g in Gpus)
            if (g.Re.IsMatch(name))
            {
                int t = g.Tier;
                if (!g.Igpu && Regex.IsMatch(name, "Laptop|Max-Q|Mobile", RegexOptions.IgnoreCase)) t = Math.Max(1, t - 1);
                return (t, g.Vram, g.Igpu);
            }
        return null;
    }

    public GpuClass? ClassFor(int tier) => Classes.OrderBy(c => Math.Abs(c.Tier - tier)).ThenBy(c => c.Tier).FirstOrDefault();

    /// <summary>Gaming class (1-10) of a processor name, estimated from family, generation and suffix.</summary>
    public static int? CpuTier(string name)
    {
        var m = Regex.Match(name, @"Ryzen\s+(?:AI\s+)?(\d)\s+(?:PRO\s+)?(\d{4})(X3D|[A-Z]*)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            int cls = int.Parse(m.Groups[1].Value), gen = m.Groups[2].Value[0] - '0';
            int baseTier = cls switch { 3 => 1, 5 => 3, 7 => 4, _ => 4 };
            int genBonus = gen switch { 1 => 0, 2 => 1, 3 => 2, 4 => 2, 5 => 3, 6 => 3, 7 => 4, 8 => 4, _ => 5 };
            int t = baseTier + genBonus;
            var suf = m.Groups[3].Value.ToUpperInvariant();
            if (suf == "X3D") t += 1;
            if (suf.StartsWith("U")) t -= 1;
            return Math.Clamp(t, 1, 10);
        }
        m = Regex.Match(name, @"Core\W*(?:TM)?\W*Ultra\s+(\d)\s+(\d{3})", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value) switch { 5 => 7, 7 => 8, _ => 9 };
        m = Regex.Match(name, @"Core\W*(?:TM)?\W*i(\d)-(\d{4,5})([A-Z]*)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            int cls = int.Parse(m.Groups[1].Value);
            string num = m.Groups[2].Value;
            int gen = num.Length == 5 ? int.Parse(num[..2]) : num[0] - '0';
            int baseTier = cls switch { 3 => 1, 5 => 3, 7 => 4, _ => 5 };
            int genBonus = gen switch { <= 7 => 0, 8 or 9 or 10 => 1, 11 => 2, 12 => 3, _ => 4 };
            int t = baseTier + genBonus;
            if (m.Groups[3].Value.StartsWith("U", StringComparison.OrdinalIgnoreCase)) t -= 1;
            return Math.Clamp(t, 1, 10);
        }
        return null;
    }
}

public static class HardwareProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    static string S(ManagementBaseObject? o, string p) => (Wmi.G<string>(o, p) ?? "").Trim();

    public static HardwareInfo Read()
    {
        var h = new HardwareInfo();
        var db = GpuDb.Instance;

        try
        {
            var cpu = Wmi.Q("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,SocketDesignation FROM Win32_Processor").FirstOrDefault();
            h.CpuName = S(cpu, "Name"); h.Cores = Wmi.G<int>(cpu, "NumberOfCores"); h.Threads = Wmi.G<int>(cpu, "NumberOfLogicalProcessors");
            var sock = S(cpu, "SocketDesignation");
            var sm = Regex.Match(sock, @"AM[45]|LGA\s?\d{4}", RegexOptions.IgnoreCase);
            h.Socket = sm.Success ? sm.Value.Replace(" ", "").ToUpperInvariant() : sock;
            h.CpuTier = GpuDb.CpuTier(h.CpuName);
            var bb = Wmi.Q("SELECT Manufacturer,Product FROM Win32_BaseBoard").FirstOrDefault();
            h.Board = $"{S(bb, "Manufacturer")} {S(bb, "Product")}".Trim();
        }
        catch { }

        try
        {
            var chassis = Wmi.Q("SELECT ChassisTypes FROM Win32_SystemEnclosure").FirstOrDefault()?["ChassisTypes"] as ushort[];
            var laptopTypes = new ushort[] { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
            h.IsLaptop = (chassis?.Any(c => laptopTypes.Contains(c)) ?? false) || Wmi.Q("SELECT BatteryStatus FROM Win32_Battery").Count > 0;
        }
        catch { }

        try
        {
            var mem = Wmi.Q("SELECT Capacity,Speed,ConfiguredClockSpeed,SMBIOSMemoryType FROM Win32_PhysicalMemory");
            h.RamModules = mem.Count;
            h.RamGB = Math.Round(mem.Sum(m => (double)Wmi.G<ulong>(m, "Capacity")) / 1073741824d);
            h.RamSpeed = (int)mem.Select(m => Wmi.G<uint>(m, "ConfiguredClockSpeed")).DefaultIfEmpty(0u).Max();
            if (h.RamSpeed == 0) h.RamSpeed = (int)mem.Select(m => Wmi.G<uint>(m, "Speed")).DefaultIfEmpty(0u).Max();
            int t = mem.Select(m => Wmi.G<int>(m, "SMBIOSMemoryType")).DefaultIfEmpty(0).Max();
            h.RamType = t switch { 34 => "DDR5", 26 => "DDR4", 24 => "DDR3", 27 or 28 or 29 or 30 or 35 => "LPDDR", _ => "?" };
            h.RamSoldered = h.RamType == "LPDDR";
            var arr = Wmi.Q("SELECT MemoryDevices,MaxCapacity,MaxCapacityEx FROM Win32_PhysicalMemoryArray").FirstOrDefault();
            h.RamSlots = Wmi.G<int>(arr, "MemoryDevices");
            double kb = Wmi.G<ulong>(arr, "MaxCapacityEx"); if (kb <= 0) kb = Wmi.G<ulong>(arr, "MaxCapacity");
            h.RamMaxGB = Math.Round(kb / 1048576d);
        }
        catch { }

        try
        {
            string best = ""; int bestTier = -1;
            var vcs = Wmi.Q("SELECT Name,DriverDate,AdapterRAM FROM Win32_VideoController");
            foreach (var v in vcs)
            {
                var name = S(v, "Name");
                if (Regex.IsMatch(name, "Basic|Remote|Mirage|Meta|Parsec|Virtual|Hyper-V", RegexOptions.IgnoreCase)) continue;
                var g = db.FindGpu(name);
                int t = g?.tier ?? 0;
                if (t > bestTier || best == "")
                {
                    bestTier = t; best = name; h.GpuTier = g?.tier; h.GpuIntegrated = g?.igpu ?? false;
                    try { var dd = Wmi.G<string>(v, "DriverDate"); if (!string.IsNullOrEmpty(dd)) h.GpuDriverDate = ManagementDateTimeConverter.ToDateTime(dd); } catch { }
                    h.VramGB = g?.vram ?? 0;
                }
            }
            h.GpuName = best;
            h.GpuLaptopVariant = Regex.IsMatch(best, "Laptop|Max-Q|Mobile", RegexOptions.IgnoreCase);
            h.GpuVendor = Regex.IsMatch(best, "NVIDIA|GeForce", RegexOptions.IgnoreCase) ? "nvidia"
                        : Regex.IsMatch(best, "AMD|Radeon", RegexOptions.IgnoreCase) ? "amd"
                        : Regex.IsMatch(best, "Intel", RegexOptions.IgnoreCase) ? "intel" : "?";
            double real = RegistryVramGB(best);
            if (real > 0) h.VramGB = real;
        }
        catch { }

        try
        {
            var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, -1, ref cur))
            {
                h.ScreenW = cur.dmPelsWidth; h.ScreenH = cur.dmPelsHeight; h.ScreenHz = cur.dmDisplayFrequency; h.ScreenMaxHz = cur.dmDisplayFrequency;
                for (int i = 0; ; i++)
                {
                    var m = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (!EnumDisplaySettings(null, i, ref m)) break;
                    if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmDisplayFrequency > h.ScreenMaxHz && m.dmDisplayFrequency < 1000)
                        h.ScreenMaxHz = m.dmDisplayFrequency;
                }
            }
        }
        catch { }

        try
        {
            var media = Wmi.Q("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").Select(d => Wmi.G<int>(d, "MediaType")).ToList();
            h.HasSsd = media.Contains(4); h.HasHdd = media.Contains(3);
            var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var ld = Wmi.Q($"SELECT FreeSpace,Size FROM Win32_LogicalDisk WHERE DeviceID='{drive}'").FirstOrDefault();
            double size = Wmi.G<double>(ld, "Size"), free = Wmi.G<double>(ld, "FreeSpace");
            if (size > 0) { h.SysFreePct = Math.Round(100 * free / size); h.SysFreeGB = Math.Round(free / 1073741824d); }
        }
        catch { }

        try
        {
            var o = Run("powercfg", "/getactivescheme");
            var g = Regex.Match(o, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
            h.PowerPlan = g.Success ? g.Value.ToLowerInvariant() : "";
            using var dvr = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
            h.GameDvrOn = Convert.ToInt32(dvr?.GetValue("AppCaptureEnabled") ?? 0) == 1;
            using var gb = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            h.GameModeOff = gb?.GetValue("AutoGameModeEnabled") is int i && i == 0;
            h.StartupCount = Scanner.StartupCount();
            h.UptimeDays = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalDays, 1);
        }
        catch { }
        return h;
    }

    static double RegistryVramGB(string gpuName)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var sub in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var k = cls!.OpenSubKey(sub);
                if (k == null || !string.Equals(k.GetValue("DriverDesc") as string, gpuName, StringComparison.OrdinalIgnoreCase)) continue;
                var v = k.GetValue("HardwareInformation.qwMemorySize");
                long bytes = v switch { long l => l, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), int i => i, _ => 0 };
                if (bytes > 0) return Math.Round(bytes / 1073741824d);
            }
        }
        catch { }
        return 0;
    }

    static string Run(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(4000); return o;
        }
        catch { return ""; }
    }

    /// <summary>Best guess of how this PC is used, from the software installed on it.</summary>
    public static Usage GuessUsage(HardwareInfo h)
    {
        var names = new List<string>();
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var k = Registry.LocalMachine.OpenSubKey(path);
            foreach (var sub in k?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var s = k!.OpenSubKey(sub);
                if (s?.GetValue("DisplayName") is string n) names.Add(n);
            }
        }
        int Count(string rx) => names.Count(n => Regex.IsMatch(n, rx, RegexOptions.IgnoreCase));
        int gaming = Count(@"Steam|Epic Games|Battle\.net|Riot|EA app|EA Desktop|Ubisoft|GOG GALAXY");
        int creator = Count(@"Premiere|DaVinci|Blender|After Effects|Photoshop|Lightroom|Maya|3ds Max|Cinema 4D|OBS Studio");
        int dev = Count(@"Visual Studio|JetBrains|Docker|VMware|VirtualBox|Android Studio|Node\.js");
        if (creator >= 2 && creator >= gaming) return Usage.Creator;
        if (dev >= 3 && dev > gaming) return Usage.Dev;
        if (gaming > 0) return Usage.Gaming;
        if (creator > 0) return Usage.Creator;
        return h.GpuTier >= 4 && !h.GpuIntegrated ? Usage.Gaming : Usage.Work;
    }
}
