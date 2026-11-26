using System.Text.RegularExpressions;

namespace PulseCheck;

public enum SugKind { Quick, Settings, Upgrade }

/// <summary>One recommendation: why (A + B), what to do, and what to check before buying.</summary>
public sealed record Suggestion(
    SugKind Kind, int Priority, Msg Title, IReadOnlyList<Msg> Because, Msg Action,
    int Cost, string? Gain, IReadOnlyList<Msg> Notes, string? OpenUri = null, string? FixId = null);

/// <summary>
/// Turns the detected hardware and the chosen usage into concrete solutions.
/// Read-only advice: nothing is changed on the PC, no store links, no affiliate links.
/// </summary>
public static class Advisor
{
    static Msg M(string key, params object[] a) => new(key, a);

    static readonly string[] PresetActions = { "a.x.preset_high", "a.x.preset_ok", "a.x.preset_mid", "a.x.preset_low" };
    const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public static string ProfileKey(Usage u) => u switch
    {
        Usage.Gaming => "a.prof.gaming", Usage.Creator => "a.prof.creator", Usage.Work => "a.prof.work", _ => "a.prof.dev",
    };

    /// <summary>1 = 1080p, 2 = 1440p and ultrawide, 3 = 4K.</summary>
    public static int Resolution(int w, int h) { long px = (long)w * h; return px >= 7_000_000 ? 3 : px >= 3_000_000 ? 2 : 1; }

    /// <summary>Graphics class (1-10) needed to run recent games well on this display.</summary>
    public static int TargetGpuTier(int w, int h, int hz) => Resolution(w, h) switch
    {
        1 => hz >= 140 ? 6 : hz >= 76 ? 5 : 4,
        2 => hz >= 76 ? 7 : 6,
        _ => hz >= 76 ? 9 : 8,
    };

    public static int NeededVramGB(int w, int h) => Resolution(w, h) switch { 1 => 8, 2 => 12, _ => 16 };

    static Suggestion Make(SugKind kind, int prio, string title, Msg[] because, Msg action, int cost,
                           string? gain = null, string? uri = null, string? fixId = null, params Msg[] notes)
        => new(kind, prio, M(title), because, action, cost, gain, notes, uri, fixId);

    public static List<Suggestion> Build(HardwareInfo h, Usage u)
    {
        var db = GpuDb.Instance;
        var list = new List<Suggestion>();
        int hz = Math.Max(h.ScreenHz, h.ScreenMaxHz);
        int? gpu = h.GpuTier, cpu = h.CpuTier;
        bool gaming = u == Usage.Gaming, creator = u == Usage.Creator;

        // ------------------------------------------------------------ quick wins (free)
        if (h.ScreenHz > 0 && h.ScreenMaxHz > h.ScreenHz)
            list.Add(Make(SugKind.Quick, gaming ? 1 : 3, "a.t.refresh",
                new[] { M("a.b.refresh1", h.ScreenHz), M("a.b.refresh2", h.ScreenMaxHz, h.ScreenW, h.ScreenH) },
                M("a.x.refresh", h.ScreenMaxHz), 0, "a.gain.large", "ms-settings:display-advanced", "refresh"));

        bool lowMem = (h.RamType == "DDR5" && h.RamSpeed > 0 && h.RamSpeed <= 4800) || (h.RamType == "DDR4" && h.RamSpeed > 0 && h.RamSpeed <= 2666);
        if (lowMem && u != Usage.Work && !h.RamSoldered && !h.IsLaptop)
            list.Add(Make(SugKind.Quick, 2, "a.t.xmp",
                new[] { M("a.b.xmp1", h.RamType, h.RamSpeed), M("a.b.xmp2") },
                M("a.x.xmp"), 0, "a.gain.moderate", null, null, M("a.z.bios_safe")));

        if (h.GpuDriverDate is DateTime dd && (DateTime.Now - dd).TotalDays > 180)
        {
            string app = h.GpuVendor switch { "nvidia" => "NVIDIA App", "amd" => "AMD Software: Adrenalin Edition", "intel" => "Intel Driver & Support Assistant", _ => "Windows Update" };
            list.Add(Make(SugKind.Quick, gaming ? 2 : 4, "a.t.driver",
                new[] { M("a.b.driver1", h.GpuName, dd.ToString("d")), M("a.b.driver2") },
                M("a.x.driver", app), 0, "a.gain.small", "ms-settings:windowsupdate"));
        }

        if (gaming && h.GameDvrOn)
            list.Add(Make(SugKind.Quick, 3, "a.t.gamedvr", new[] { M("a.b.gamedvr1"), M("a.b.gamedvr2") },
                M("a.x.gamedvr"), 0, "a.gain.small", "ms-settings:gaming-gamedvr", "game_capture"));

        if (gaming && h.GameModeOff)
            list.Add(Make(SugKind.Quick, 3, "a.t.gamemode", new[] { M("a.b.gamemode1"), M("a.b.gamemode2") },
                M("a.x.gamemode"), 0, "a.gain.small", "ms-settings:gaming-gamemode", "game_mode"));

        if (h.StartupCount > 12)
            list.Add(Make(SugKind.Quick, 3, "a.t.startup", new[] { M("a.b.startup1", h.StartupCount), M("a.b.startup2") },
                M("a.x.startup"), 0, "a.gain.small", "ms-settings:startupapps", "startup"));

        if (h.PowerPlan == PowerSaver)
            list.Add(Make(SugKind.Quick, 1, "a.t.powerplan", new[] { M("a.b.powerplan1"), M("a.b.powerplan2") },
                M("a.x.powerplan"), 0, "a.gain.large", "ms-settings:powersleep", "power"));

        if (h.SysFreeGB > 0 && h.SysFreePct < 15)
            list.Add(Make(SugKind.Quick, 2, "a.t.storage", new[] { M("a.b.storage1", h.SysFreePct, h.SysFreeGB), M("a.b.storage2") },
                M("a.x.storage"), 0, "a.gain.moderate", "ms-settings:storagesense", "cleantemp"));

        if (h.UptimeDays > 14)
            list.Add(Make(SugKind.Quick, 4, "a.t.restart", new[] { M("a.b.restart1", h.UptimeDays), M("a.b.restart2") },
                M("a.x.restart"), 0, "a.gain.small", null, "restart"));

        // ------------------------------------------------------------ settings for this hardware
        if (gaming && gpu is int g && hz > 0 && h.ScreenW > 0)
        {
            int target = TargetGpuTier(h.ScreenW, h.ScreenH, hz), headroom = g - target;
            string upscaler = h.GpuVendor == "nvidia" && Regex.IsMatch(h.GpuName, "RTX", RegexOptions.IgnoreCase) ? "DLSS"
                            : h.GpuVendor == "intel" && g >= 3 ? "XeSS" : "FSR";
            int idx = headroom >= 1 ? 0 : headroom == 0 ? 1 : headroom == -1 ? 2 : 3;
            list.Add(new Suggestion(SugKind.Settings, 1, M("a.t.preset"),
                new[] { M("a.b.preset1", h.GpuName, g), M("a.b.preset2", target, h.ScreenW, h.ScreenH, hz) },
                M(PresetActions[idx], upscaler), 0, null, Array.Empty<Msg>()));
            list.Add(Make(SugKind.Settings, 2, "a.t.cap", new[] { M("a.b.cap1", hz), M("a.b.cap2") },
                M("a.x.cap", Math.Max(30, hz - 3)), 0));
        }

        if (creator)
            list.Add(Make(SugKind.Settings, 1, "a.t.proxy",
                new[] { M("a.b.proxy1", h.RamGB, h.VramGB), M("a.b.proxy2") }, M("a.x.proxy"), 0));

        // ------------------------------------------------------------ upgrades, best value first
        UpgradeGpu(list, h, u, hz, db);
        UpgradeCpu(list, h, u, hz, db);
        UpgradeMemory(list, h, u);
        UpgradeStorage(list, h, u);
        return list;
    }

    static void UpgradeGpu(List<Suggestion> list, HardwareInfo h, Usage u, int hz, GpuDb db)
    {
        if (h.GpuTier is not int g || h.ScreenW <= 0) return;
        int target = TargetGpuTier(h.ScreenW, h.ScreenH, hz);

        if (u == Usage.Gaming && target - g >= 1)
        {
            int gap = target - g;
            if (h.IsLaptop)
            {
                list.Add(Make(SugKind.Upgrade, 5, "a.t.laptop",
                    new[] { M("a.b.laptop1"), M("a.b.laptop2", target, g) }, M("a.x.laptop"), 0));
                return;
            }
            var cls = db.ClassFor(Math.Clamp(target, 4, 10))!;
            int need = NeededVramGB(h.ScreenW, h.ScreenH);
            var because = new List<Msg> { M("a.b.gpu1", h.GpuName, g), M("a.b.gpu2", h.ScreenW, h.ScreenH, hz, target) };
            if (h.CpuTier is int ct && ct <= target - 3) because.Add(M("a.b.gpu3", ct));
            list.Add(new Suggestion(SugKind.Upgrade, gap >= 2 ? 2 : 4, M("a.t.gpu"), because,
                M("a.x.gpu_gaming", cls.Tier, string.Join(", ", cls.Examples), need), cls.Cost,
                gap >= 3 ? "a.gain.huge" : gap == 2 ? "a.gain.large" : "a.gain.moderate",
                new[] { M("a.z.psu", cls.Psu), M("a.z.fit"), M("a.z.examples") }));
        }
        else if (u == Usage.Creator && !h.IsLaptop && (g < 6 || (h.VramGB > 0 && h.VramGB < 8)))
        {
            var cls = db.ClassFor(g < 6 ? 6 : 7)!;
            list.Add(new Suggestion(SugKind.Upgrade, 2, M("a.t.gpu"),
                new[] { M("a.b.gpu1", h.GpuName, g), M("a.b.gpu_creator2", 12) },
                M("a.x.gpu_creator", cls.Tier, string.Join(", ", cls.Examples)), cls.Cost, "a.gain.large",
                new[] { M("a.z.psu", cls.Psu), M("a.z.fit"), M("a.z.examples") }));
        }
    }

    static void UpgradeCpu(List<Suggestion> list, HardwareInfo h, Usage u, int hz, GpuDb db)
    {
        if (h.IsLaptop || h.CpuTier is not int ct) return;
        var because = new List<Msg>();
        int prio = 3;

        if (u == Usage.Gaming)
        {
            int gpuAfter = Math.Max(h.GpuTier ?? 0, h.ScreenW > 0 ? TargetGpuTier(h.ScreenW, h.ScreenH, hz) : 0);
            if (!(ct <= gpuAfter - 3 || ct <= 3)) return;
            because.Add(M("a.b.cpu1", h.CpuName, ct));
            because.Add(M("a.b.cpu2", gpuAfter));
            prio = ct <= gpuAfter - 4 ? 2 : 3;
        }
        else if ((u == Usage.Creator || u == Usage.Dev) && h.Cores > 0 && h.Cores < 8)
        {
            because.Add(M("a.b.cores1", h.Cores));
            because.Add(M("a.b.cores2"));
        }
        else return;

        db.Sockets.TryGetValue(h.Socket, out var path);
        bool inSocket = path != null && path.Tier >= ct + 2;
        if (inSocket)
            list.Add(new Suggestion(SugKind.Upgrade, prio, M("a.t.cpu"), because,
                M("a.x.cpu_socket", h.Socket, string.Join(", ", path!.Examples)), 3, "a.gain.large",
                new[] { M("a.z.bios", h.Board), M("a.z.cooler") }));
        else
            list.Add(new Suggestion(SugKind.Upgrade, prio, M("a.t.platform"),
                because.Append(M("a.b.platform", string.IsNullOrEmpty(h.Socket) ? "?" : h.Socket)).ToList(),
                M("a.x.platform", string.Join("; ", db.PlatformExamples)), 4, "a.gain.huge",
                new[] { M("a.z.windows"), M("a.z.keep") }));
    }

    static void UpgradeMemory(List<Suggestion> list, HardwareInfo h, Usage u)
    {
        if (h.RamGB <= 0) return;
        int rec = u switch { Usage.Gaming => 16, Usage.Creator => 32, Usage.Work => 16, _ => 32 };
        int trigger = u == Usage.Work ? 8 : rec;
        string usage = Loc.T(ProfileKey(u));
        int freeSlots = Math.Max(0, h.RamSlots - h.RamModules);

        if (h.RamGB < trigger)
        {
            int prio = h.RamGB < 12 || (u != Usage.Gaming && u != Usage.Work && h.RamGB < 16) ? 1 : 2;
            var because = new[] { M("a.b.ram1", h.RamGB), M("a.b.ram2", rec, usage) };
            if (h.RamSoldered)
            {
                list.Add(Make(SugKind.Upgrade, 5, "a.t.ram", because, M("a.x.ram_soldered"), 0));
                return;
            }
            int each = h.RamModules > 0 ? (int)(h.RamGB / h.RamModules) : 8;
            Msg action; int cost = rec >= 32 ? 2 : 1;
            if (freeSlots >= 1 && h.RamGB + each >= rec - 0.5)
                action = M("a.x.ram_add", h.RamType, each, (int)h.RamGB + each);
            else
                action = M("a.x.ram_kit", h.RamType, rec, rec / (h.RamSlots >= 2 ? 2 : 1));
            var notes = new List<Msg> { M("a.z.ddr", h.RamType), M("a.z.match") };
            if (h.RamMaxGB > 0) notes.Add(M("a.z.max", h.RamMaxGB));
            if (h.IsLaptop) notes.Add(M("a.z.laptop_ram"));
            list.Add(new Suggestion(SugKind.Upgrade, prio, M("a.t.ram"), because, action, cost,
                h.RamGB < 12 ? "a.gain.huge" : "a.gain.large", notes));
        }
        else if (h.RamModules == 1 && h.RamSlots >= 2 && !h.RamSoldered && (u == Usage.Gaming || u == Usage.Creator))
        {
            list.Add(new Suggestion(SugKind.Upgrade, 3, M("a.t.channel"),
                new[] { M("a.b.channel1"), M("a.b.channel2") }, M("a.x.channel", h.RamType, (int)h.RamGB), 1, "a.gain.moderate",
                new[] { M("a.z.ddr", h.RamType), M("a.z.match") }));
        }
    }

    static void UpgradeStorage(List<Suggestion> list, HardwareInfo h, Usage u)
    {
        if (!h.HasSsd && h.HasHdd)
            list.Add(Make(SugKind.Upgrade, 1, "a.t.ssd", new[] { M("a.b.ssd1"), M("a.b.ssd2") }, M("a.x.ssd"), 1, "a.gain.huge"));
        else if (u == Usage.Creator && h.SysFreeGB > 0 && (h.SysFreeGB < 150 || h.SysFreePct < 25))
            list.Add(Make(SugKind.Upgrade, 3, "a.t.scratch", new[] { M("a.b.scratch1", h.SysFreeGB), M("a.b.scratch2") }, M("a.x.scratch"), 2, "a.gain.moderate"));
    }

    /// <summary>Quick wins first, then settings, then upgrades; inside a group the most valuable first.</summary>
    public static List<Suggestion> Sorted(List<Suggestion> list) =>
        list.OrderBy(s => s.Kind).ThenBy(s => s.Priority).ThenBy(s => s.Cost).ToList();
}
