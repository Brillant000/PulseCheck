using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PulseCheck;

/// <summary>Runs every check. Everything is local and read-only, except small network tests.</summary>
public sealed class Scanner
{
    const string Specs_ = "cat.specs", NewPc_ = "cat.newpc", Sys_ = "cat.system",
                 Sto_ = "cat.storage", Sec_ = "cat.security", Net_ = "cat.network";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    readonly List<CheckResult> R = new();
    bool? gwOk, ipOk, dnsOk;

    public bool Speed { get; init; } = true;

    /// <summary>Detailed hardware profile, read during the scan and used by the Solutions tab.</summary>
    public HardwareInfo Hardware { get; private set; } = new();

    public static bool IsAdmin =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static int Score(IEnumerable<CheckResult> r) =>
        Math.Max(0, 100 - 15 * r.Count(x => x.Status == Status.Fail) - 5 * r.Count(x => x.Status == Status.Warn));

    void A(string cat, string n, Status s, string d, object[]? da = null, string? f = null,
           object[]? fa = null, object[]? na = null) =>
        R.Add(new CheckResult(cat, new Msg(n, na ?? Array.Empty<object>()), s,
            new Msg(d, da ?? Array.Empty<object>()),
            f == null ? null : new Msg(f, fa ?? Array.Empty<object>())));

    static string T(string? s) => string.IsNullOrWhiteSpace(s) ? "?" : s.Trim();

    public Task<List<CheckResult>> RunAsync(IProgress<(int i, int n, string label)> progress)
    {
        var steps = new List<(string label, Action act)>
        {
            ("p.specs", Specs), ("p.cpuram", CpuRam), ("p.storage", Storage), ("p.system", SystemHealth),
            ("p.security", Security), ("p.newpc", NewPc), ("p.network", Network),
        };
        if (Speed) steps.Add(("p.speed", SpeedTest));
        steps.Add(("p.verdict", Verdict));
        steps.Add(("a.p.advisor", () => Hardware = HardwareProbe.Read()));

        return Task.Run(() =>
        {
            for (int i = 0; i < steps.Count; i++)
            {
                progress.Report((i, steps.Count, steps[i].label));
                try { steps[i].act(); }
                catch (Exception e)
                {
                    A(Sys_, "n.check_error", Status.Info, "d.check_error", new object[] { $"{steps[i].label}: {e.Message}" });
                }
            }
            progress.Report((steps.Count, steps.Count, "p.verdict"));
            return R;
        });
    }

    // ------------------------------------------------------------------ SPECS
    void Specs()
    {
        var cs = Wmi.Q("SELECT Manufacturer,Model FROM Win32_ComputerSystem").FirstOrDefault();
        if (cs != null) A(Specs_, "n.machine", Status.Info, "d.machine", new object[] { T(Wmi.G<string>(cs, "Manufacturer")), T(Wmi.G<string>(cs, "Model")) });

        var os = Wmi.Q("SELECT Caption,BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (os != null) A(Specs_, "n.os", Status.Info, "d.os", new object[] { T(Wmi.G<string>(os, "Caption")), T(Wmi.G<string>(os, "BuildNumber")) });

        var bios = Wmi.Q("SELECT SMBIOSBIOSVersion,SerialNumber FROM Win32_BIOS").FirstOrDefault();
        if (bios != null) A(Specs_, "n.bios", Status.Info, "d.bios", new object[] { T(Wmi.G<string>(bios, "SMBIOSBIOSVersion")), T(Wmi.G<string>(bios, "SerialNumber")) });

        var cpu = Wmi.Q("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor").FirstOrDefault();
        if (cpu != null) A(Specs_, "n.cpu", Status.Info, "d.cpu", new object[] { T(Wmi.G<string>(cpu, "Name")), Wmi.G<int>(cpu, "NumberOfCores"), Wmi.G<int>(cpu, "NumberOfLogicalProcessors") });

        var mem = Wmi.Q("SELECT Capacity,Speed,ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        if (mem.Count > 0)
        {
            double gb = mem.Sum(m => (double)Wmi.G<ulong>(m, "Capacity")) / 1073741824d;
            uint sp = mem.Max(m => Wmi.G<uint>(m, "ConfiguredClockSpeed"));
            if (sp == 0) sp = mem.Max(m => Wmi.G<uint>(m, "Speed"));
            A(Specs_, "n.ram", Status.Info, "d.ram", new object[] { Math.Round(gb), mem.Count, sp });
        }

        var gpus = Wmi.Q("SELECT Name,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController")
            .Where(g => !Regex.IsMatch(Wmi.G<string>(g, "Name") ?? "", "Basic|Remote|Mirage|Meta|Parsec|Virtual", RegexOptions.IgnoreCase)).ToList();
        if (gpus.Count > 0)
        {
            A(Specs_, "n.gpu", Status.Info, "d.gpu", new object[] { string.Join(", ", gpus.Select(g => T(Wmi.G<string>(g, "Name")))) });
            var scr = gpus.FirstOrDefault(g => Wmi.G<int>(g, "CurrentHorizontalResolution") > 0);
            if (scr != null) A(Specs_, "n.screen", Status.Info, "d.screen", new object[] { Wmi.G<int>(scr, "CurrentHorizontalResolution"), Wmi.G<int>(scr, "CurrentVerticalResolution"), Wmi.G<int>(scr, "CurrentRefreshRate") });
        }

        var disks = Wmi.Q("SELECT Model,Size FROM Win32_DiskDrive");
        if (disks.Count > 0)
            A(Specs_, "n.storage", Status.Info, "d.storage", new object[] {
                string.Join("; ", disks.Select(d => $"{T(Wmi.G<string>(d, "Model"))} ({Math.Round(Wmi.G<ulong>(d, "Size") / 1e9)} GB)")) });
    }

    // ------------------------------------------------------------------ CPU / RAM
    void CpuRam()
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = Wmi.Q("SELECT LoadPercentage FROM Win32_Processor");
            if (p.Count > 0) { sum += p.Average(x => Wmi.G<double>(x, "LoadPercentage")); n++; }
            Thread.Sleep(400);
        }
        if (n > 0)
        {
            int avg = (int)Math.Round(sum / n);
            var s = avg >= 90 ? Status.Fail : avg >= 70 ? Status.Warn : Status.Ok;
            A(Sys_, "n.cpuload", s, "d.pct", new object[] { avg }, s == Status.Ok ? null : "f.cpu");
        }

        var os = Wmi.Q("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        if (os != null)
        {
            double total = Wmi.G<double>(os, "TotalVisibleMemorySize"), free = Wmi.G<double>(os, "FreePhysicalMemory");
            if (total > 0)
            {
                int used = (int)Math.Round(100 * (total - free) / total);
                double gb = Math.Round(total / 1048576d, 1);
                var s = used >= 90 ? Status.Fail : used >= 80 ? Status.Warn : Status.Ok;
                A(Sys_, "n.ramuse", s, "d.ramuse", new object[] { used, gb }, s == Status.Ok ? null : "f.ram");
                if (gb < 7.5) A(Sys_, "n.ramsize", Status.Warn, "d.ramsize", new object[] { gb }, "f.ramsize");
            }
        }
    }

    // ------------------------------------------------------------------ STORAGE
    void Storage()
    {
        foreach (var d in Wmi.Q("SELECT DeviceID,FreeSpace,Size FROM Win32_LogicalDisk WHERE DriveType=3"))
        {
            double size = Wmi.G<double>(d, "Size"), free = Wmi.G<double>(d, "FreeSpace");
            if (size <= 0) continue;
            int pct = (int)Math.Round(100 * free / size);
            var s = pct < 10 ? Status.Fail : pct < 20 ? Status.Warn : Status.Ok;
            A(Sto_, "n.diskspace", s, "d.diskspace", new object[] { Math.Round(free / 1073741824d, 1), pct },
              s == Status.Ok ? null : "f.diskspace", na: new object[] { Wmi.G<string>(d, "DeviceID") });
        }

        var pd = Wmi.Q("SELECT FriendlyName,HealthStatus,MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
        if (pd.Count == 0) { A(Sto_, "n.diskhealth_all", Status.Info, "d.nodiskhealth"); return; }
        foreach (var d in pd)
        {
            int h = Wmi.G<int>(d, "HealthStatus"), mt = Wmi.G<int>(d, "MediaType");
            string media = mt == 3 ? "HDD" : mt == 4 ? "SSD" : "?";
            string hs = h == 0 ? "s.healthy" : h == 1 ? "s.warning_h" : "s.unhealthy";
            A(Sto_, "n.diskhealth", h == 0 ? Status.Ok : Status.Fail, "d.diskhealth",
              new object[] { media, Loc.T(hs) }, h == 0 ? null : "f.diskhealth", na: new object[] { T(Wmi.G<string>(d, "FriendlyName")) });
        }
    }

    // ------------------------------------------------------------------ SYSTEM
    void SystemHealth()
    {
        double days = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalDays, 1);
        A(Sys_, "n.uptime", days > 14 ? Status.Warn : Status.Ok, "d.uptime", new object[] { days }, days > 14 ? "f.uptime" : null);

        bool pending = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null
                    || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;
        if (pending) A(Sys_, "n.reboot", Status.Warn, "d.reboot", f: "f.reboot"); else A(Sys_, "n.reboot", Status.Ok, "d.none");

        int startup = StartupCount();
        A(Sys_, "n.startup", startup > 15 ? Status.Warn : Status.Ok, "d.startup", new object[] { startup }, startup > 15 ? "f.startup" : null);

        var critical = new[] { "EventLog", "Dhcp", "Dnscache", "mpssvc", "Winmgmt", "nlasvc", "LanmanWorkstation", "Audiosrv" };
        var stopped = Wmi.Q("SELECT Name,State,DisplayName FROM Win32_Service")
            .Where(s => critical.Contains(Wmi.G<string>(s, "Name")) && Wmi.G<string>(s, "State") != "Running")
            .Select(s => Wmi.G<string>(s, "DisplayName")).ToList();
        if (stopped.Count > 0) A(Sys_, "n.services", Status.Warn, "d.services", new object[] { string.Join(", ", stopped) }, "f.services");
        else A(Sys_, "n.services", Status.Ok, "d.allrunning");

        var bad = Wmi.Q("SELECT Name,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(d => Wmi.G<int>(d, "ConfigManagerErrorCode") is not (22 or 45)).Select(d => T(Wmi.G<string>(d, "Name"))).Distinct().ToList();
        if (bad.Count > 0) A(Sys_, "n.devices", Status.Warn, "d.devices", new object[] { bad.Count, string.Join(", ", bad.Take(4)) }, "f.devices");
        else A(Sys_, "n.devices", Status.Ok, "d.nodev");

        EventErrors();
    }

    internal static int StartupCount()
    {
        var items = Wmi.Q("SELECT Name FROM Win32_StartupCommand").Select(o => Wmi.G<string>(o, "Name") ?? "").ToList();
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spots = new (RegistryKey hive, string path)[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
        };
        foreach (var (hive, path) in spots)
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) continue;
            foreach (var name in k.GetValueNames())
                if (k.GetValue(name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1)
                {
                    disabled.Add(name);
                    disabled.Add(Regex.Replace(name, @"\.lnk$", "", RegexOptions.IgnoreCase));
                }
        }
        return items.Count(n => !disabled.Contains(n));
    }

    void EventErrors()
    {
        var events = new List<(string provider, int id)>();
        foreach (var log in new[] { "System", "Application" })
        {
            try
            {
                var q = new EventLogQuery(log, PathType.LogName, "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]");
                using var rd = new EventLogReader(q);
                for (EventRecord? e = rd.ReadEvent(); e != null && events.Count < 2000; e = rd.ReadEvent())
                {
                    using (e) events.Add((e.ProviderName ?? "?", e.Id));
                }
            }
            catch { }
        }
        if (events.Count == 0) { A(Sys_, "n.events", Status.Ok, "d.noevents"); return; }

        var top = string.Join(", ", events.GroupBy(e => e.provider).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} ×{g.Count()}"));
        A(Sys_, "n.events", events.Count > 200 ? Status.Warn : Status.Info, "d.events", new object[] { events.Count, top }, events.Count > 200 ? "f.events" : null);

        int crashes = events.Count(e => (e.provider == "Microsoft-Windows-Kernel-Power" && e.id == 41) || (e.provider.Contains("BugCheck") && e.id == 1001));
        if (crashes > 0) A(Sys_, "n.crashes", Status.Fail, "d.crashes", new object[] { crashes }, "f.crashes");
        int disk = events.Count(e => e.provider is "disk" or "Ntfs" or "volmgr");
        if (disk > 0) A(Sys_, "n.diskerrors", Status.Fail, "d.diskerrors", new object[] { disk }, "f.diskerrors");
    }

    // ------------------------------------------------------------------ SECURITY
    void Security()
    {
        var mp = Wmi.Q("SELECT RealTimeProtectionEnabled,AntivirusSignatureAge FROM MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender").FirstOrDefault();
        if (mp == null) A(Sec_, "n.antivirus", Status.Info, "d.noav");
        else
        {
            bool rt = Wmi.G<bool>(mp, "RealTimeProtectionEnabled");
            A(Sec_, "n.defender", rt ? Status.Ok : Status.Fail, rt ? "d.on" : "d.off", f: rt ? null : "f.defender");
            int age = Wmi.G<int>(mp, "AntivirusSignatureAge");
            A(Sec_, "n.sigs", age > 7 ? Status.Warn : Status.Ok, "d.sigs", new object[] { age }, age > 7 ? "f.sigs" : null);
        }

        var profiles = Wmi.Q("SELECT Name,Enabled FROM MSFT_NetFirewallProfile", @"root\StandardCimv2");
        if (profiles.Count > 0)
        {
            var off = profiles.Where(p => Wmi.G<int>(p, "Enabled") == 0).Select(p => Wmi.G<string>(p, "Name")).ToList();
            if (off.Count > 0) A(Sec_, "n.firewall", Status.Fail, "d.fwoff", new object[] { string.Join(", ", off) }, "f.firewall");
            else A(Sec_, "n.firewall", Status.Ok, "d.fwon");
        }

        DateTime? last = null;
        foreach (var q in Wmi.Q("SELECT InstalledOn FROM Win32_QuickFixEngineering"))
        {
            var s = Wmi.G<string>(q, "InstalledOn");
            // WMI returns US-formatted dates (M/d/yyyy); try that first, then the local format.
            if (!string.IsNullOrEmpty(s) &&
                (DateTime.TryParse(s, new System.Globalization.CultureInfo("en-US"), System.Globalization.DateTimeStyles.None, out var d) || DateTime.TryParse(s, out d)) &&
                d <= DateTime.Now.AddDays(1))
                if (last == null || d > last) last = d;
        }
        if (last == null) A(Sec_, "n.updates", Status.Info, "d.noupdates");
        else
        {
            int days = (int)(DateTime.Now - last.Value).TotalDays;
            A(Sec_, "n.updates", days > 60 ? Status.Warn : Status.Ok, "d.updates", new object[] { days }, days > 60 ? "f.updates" : null);
        }

        using (var sb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
        {
            var v = sb?.GetValue("UEFISecureBootEnabled");
            if (v == null) A(Sec_, "n.secureboot", Status.Info, "d.unknown");
            else if (Convert.ToInt32(v) == 1) A(Sec_, "n.secureboot", Status.Ok, "d.sbon");
            else A(Sec_, "n.secureboot", Status.Warn, "d.sboff", f: "f.secureboot");
        }

        var tpm = Wmi.Q("SELECT SpecVersion FROM Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm").FirstOrDefault();
        if (tpm != null) A(Sec_, "n.tpm", Status.Ok, "d.tpm", new object[] { T(Wmi.G<string>(tpm, "SpecVersion")).Split(',')[0] });
        else if (!IsAdmin) A(Sec_, "n.tpm", Status.Info, "d.tpm_unknown");
        else A(Sec_, "n.tpm", Status.Warn, "d.tpm_none", f: "f.tpm");
    }

    // ------------------------------------------------------------------ NEW PC
    static readonly string[] Bloat =
        { "McAfee", "Norton", "Avast", "WildTangent", "CandyCrush", "Candy Crush", "Booking", "ExpressVPN", "TikTok", "Disney", "Hulu" };

    void NewPc()
    {
        var act = Wmi.Q("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'").FirstOrDefault();
        if (act != null)
        {
            bool ok = Wmi.G<int>(act, "LicenseStatus") == 1;
            A(NewPc_, "n.activation", ok ? Status.Ok : Status.Warn, ok ? "d.activated" : "d.notactivated", f: ok ? null : "f.activation");
        }

        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var k = Registry.LocalMachine.OpenSubKey(path);
            if (k == null) continue;
            foreach (var sub in k.GetSubKeyNames())
            {
                using var s = k.OpenSubKey(sub);
                var name = s?.GetValue("DisplayName") as string;
                if (name != null && Bloat.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase))) found.Add(name);
            }
        }
        using (var pk = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
            foreach (var n in pk?.GetSubKeyNames() ?? Array.Empty<string>())
                if (Bloat.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase))) found.Add(n.Split('_')[0]);
        if (found.Count > 0) A(NewPc_, "n.bloat", Status.Warn, "d.bloat", new object[] { string.Join(", ", found.Take(6)) }, "f.bloat");
        else A(NewPc_, "n.bloat", Status.Ok, "d.nobloat");

        var mem = Wmi.Q("SELECT Speed,ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        if (mem.Count > 0)
        {
            int slots = Wmi.G<int>(Wmi.Q("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").FirstOrDefault(), "MemoryDevices");
            if (mem.Count == 1 && slots >= 2) A(NewPc_, "n.channel", Status.Warn, "d.single", f: "f.single");
            else if (mem.Count >= 2) A(NewPc_, "n.channel", Status.Ok, "d.dual");

            uint cfg = mem.Min(m => Wmi.G<uint>(m, "ConfiguredClockSpeed")), spd = mem.Max(m => Wmi.G<uint>(m, "Speed"));
            if (cfg > 0 && spd > 0)
            {
                if (cfg < spd) A(NewPc_, "n.xmp", Status.Warn, "d.xmp", new object[] { cfg, spd }, "f.xmp");
                else A(NewPc_, "n.xmp", Status.Ok, "d.xmpok", new object[] { cfg });
            }
        }

        if (Wmi.Q("SELECT BatteryStatus FROM Win32_Battery").Count > 0)
        {
            double full = Wmi.G<double>(Wmi.Q("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\WMI").FirstOrDefault(), "FullChargedCapacity");
            double design = Wmi.G<double>(Wmi.Q("SELECT DesignedCapacity FROM BatteryStaticData", @"root\WMI").FirstOrDefault(), "DesignedCapacity");
            if (design > 0 && full > 0)
            {
                int wear = (int)Math.Max(0, Math.Round(100 - 100 * full / design));
                if (wear >= 25) A(NewPc_, "n.battery", Status.Fail, "d.batwear", new object[] { wear, full, design }, "f.batwear");
                else if (wear >= 10) A(NewPc_, "n.battery", Status.Warn, "d.batwear", new object[] { wear, full, design }, "f.batwear");
                else A(NewPc_, "n.battery", Status.Ok, "d.batok", new object[] { wear });
            }
        }

        var media = Wmi.Q("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").Select(d => Wmi.G<int>(d, "MediaType")).ToList();
        if (media.Count > 0)
        {
            if (media.Contains(4)) A(NewPc_, "n.hdd", Status.Ok, "d.ssdok");
            else if (media.Contains(3)) A(NewPc_, "n.hdd", Status.Warn, "d.hddonly", f: "f.hdd");
        }
    }

    // ------------------------------------------------------------------ NETWORK
    static (int loss, long? avg, long? jitter) PingStats(string host, int count)
    {
        using var p = new Ping();
        var t = new List<long>(); int lost = 0;
        for (int i = 0; i < count; i++)
        {
            try { var r = p.Send(host, 2000); if (r.Status == IPStatus.Success) t.Add(r.RoundtripTime); else lost++; }
            catch { lost++; }
            Thread.Sleep(100);
        }
        long? avg = t.Count > 0 ? (long)Math.Round(t.Average()) : null;
        long? jit = t.Count > 0 ? t.Max() - t.Min() : null;
        return (100 * lost / count, avg, jit);
    }

    static string Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return o;
        }
        catch { return ""; }
    }

    static bool Tcp(string host, int port, int ms = 3000)
    {
        try { using var c = new TcpClient(); return c.ConnectAsync(host, port).Wait(ms) && c.Connected; }
        catch { return false; }
    }

    void Network()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToList();
        var phys = all.Where(n => !Regex.IsMatch(n.Description, "VPN|TAP|TUN|WireGuard|Virtual|Hyper-V|VMware|vEthernet|Bluetooth", RegexOptions.IgnoreCase)).ToList();

        if (phys.Count == 0)
        {
            A(Net_, "n.adapters", Status.Fail, "d.noadapter", f: "f.noadapter");
            gwOk = false;
            return;
        }
        A(Net_, "n.adapters", Status.Ok, "d.adapters", new object[] { string.Join(", ", phys.Select(n => $"{n.Name} ({n.Speed / 1_000_000} Mb/s)")) });

        foreach (var n in phys)
        {
            var ip = n.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            if (ip == null) continue;
            if (ip.StartsWith("169.254.")) A(Net_, "n.ip", Status.Fail, "d.apipa", new object[] { ip }, "f.apipa", na: new object[] { n.Name });
            else A(Net_, "n.ip", Status.Ok, "d.ip", new object[] { ip }, na: new object[] { n.Name });
        }

        var gw = phys.SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))?.Address.ToString();
        if (gw == null) { gwOk = false; A(Net_, "n.gateway", Status.Fail, "d.nogw", f: "f.gwdown"); }
        else
        {
            var (loss, avg, _) = PingStats(gw, 8);
            gwOk = loss < 100;
            if (loss == 100) A(Net_, "n.gateway", Status.Fail, "d.gwdown", new object[] { gw }, "f.gwdown");
            else if (loss > 0 || avg > 20) A(Net_, "n.gateway", Status.Warn, "d.gw", new object[] { gw, loss, avg?.ToString() ?? "-" }, "f.gwweak");
            else A(Net_, "n.gateway", Status.Ok, "d.gw", new object[] { gw, loss, avg?.ToString() ?? "-" });
        }

        var wifi = Run("netsh", "wlan show interfaces");
        var m = Regex.Match(wifi, @"(?m)^\s*[^:\r\n]+:\s*(\d{1,3})%\s*$");
        if (m.Success)
        {
            int sig = int.Parse(m.Groups[1].Value);
            var ssid = Regex.Match(wifi, @"(?m)^\s*SSID\s*:\s*(.+?)\s*$");
            A(Net_, "n.wifi", sig < 40 ? Status.Warn : Status.Ok, "d.wifi", new object[] { sig, ssid.Success ? ssid.Groups[1].Value : "?" }, sig < 40 ? "f.wifi" : null);
        }

        foreach (var host in new[] { "1.1.1.1", "8.8.8.8" })
        {
            var (loss, avg, jit) = PingStats(host, 10);
            if (loss < 100) ipOk = true; else ipOk ??= false;
            var args = new object[] { host, loss, avg?.ToString() ?? "-", jit?.ToString() ?? "-" };
            var na = new object[] { host };
            if (loss == 100) A(Net_, "n.ping", Status.Fail, "d.unreach", f: "f.noint", na: na);
            else if (loss >= 10) A(Net_, "n.ping", Status.Fail, "d.ping", args, "f.lossy", na: na);
            else if (loss > 0 || avg > 100 || jit > 80) A(Net_, "n.ping", Status.Warn, "d.ping", args, "f.meh", na: na);
            else A(Net_, "n.ping", Status.Ok, "d.ping", args, na: na);
        }

        var dns = phys.SelectMany(n => n.GetIPProperties().DnsAddresses).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Distinct().ToList();
        if (dns.Count > 0) A(Net_, "n.dnsservers", Status.Info, "d.list", new object[] { string.Join(", ", dns) });
        var sw = Stopwatch.StartNew();
        try
        {
            Dns.GetHostAddresses("www.microsoft.com"); sw.Stop();
            dnsOk = true;
            A(Net_, "n.dns", sw.ElapsedMilliseconds > 300 ? Status.Warn : Status.Ok, "d.dns", new object[] { sw.ElapsedMilliseconds }, sw.ElapsedMilliseconds > 300 ? "f.dnsslow" : null);
        }
        catch { dnsOk = false; A(Net_, "n.dns", Status.Fail, "d.dnsfail", f: "f.dnsfail"); }

        sw.Restart();
        try
        {
            var body = Http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt").GetAwaiter().GetResult(); sw.Stop();
            if (!body.Contains("Microsoft Connect Test")) A(Net_, "n.web", Status.Warn, "d.captive", f: "f.captive");
            else A(Net_, "n.web", Status.Ok, "d.web", new object[] { sw.ElapsedMilliseconds });
        }
        catch { A(Net_, "n.web", Status.Fail, "d.noweb", f: "f.noweb"); }

        if (Tcp("www.microsoft.com", 443)) A(Net_, "n.https", Status.Ok, "d.reachable");
        else A(Net_, "n.https", Status.Fail, "d.blocked", f: "f.https");

        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
        {
            if (Convert.ToInt32(k?.GetValue("ProxyEnable") ?? 0) == 1) A(Net_, "n.proxy", Status.Warn, "d.proxy", new object[] { k?.GetValue("ProxyServer") ?? "?" }, "f.proxy");
            else A(Net_, "n.proxy", Status.Ok, "d.noproxy");
        }
        var vpn = all.Where(n => Regex.IsMatch(n.Description, "VPN|TAP|WireGuard|TUN", RegexOptions.IgnoreCase)).Select(n => n.Name).ToList();
        if (vpn.Count > 0) A(Net_, "n.vpn", Status.Info, "d.vpn", new object[] { string.Join(", ", vpn) });
    }

    void SpeedTest()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var data = Http.GetByteArrayAsync("https://speed.cloudflare.com/__down?bytes=10000000").GetAwaiter().GetResult();
            sw.Stop();
            double mbps = Math.Round(data.Length * 8 / 1e6 / sw.Elapsed.TotalSeconds, 1);
            A(Net_, "n.speed", mbps < 5 ? Status.Warn : Status.Ok, "d.speed", new object[] { mbps }, mbps < 5 ? "f.speed" : null);
        }
        catch { A(Net_, "n.speed", Status.Info, "d.noweb"); }
    }

    void Verdict()
    {
        if (gwOk == false) A(Net_, "n.verdict", Status.Fail, "d.v_local", f: "f.v_local");
        else if (ipOk == false) A(Net_, "n.verdict", Status.Fail, "d.v_isp", f: "f.v_isp");
        else if (dnsOk == false) A(Net_, "n.verdict", Status.Fail, "d.v_dns", f: "f.v_dns");
        else if (ipOk == true && dnsOk == true) A(Net_, "n.verdict", Status.Ok, "d.v_ok");
    }
}
