using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PulseCheck;

/// <summary>Catalogue of the fixes PulseCheck can apply, and which findings they belong to.</summary>
public static class Fixes
{
    public static Fix? Create(string id) => id switch
    {
        "game_capture" => new RegistryFix("game_capture", new Msg("fx.t.game_capture"), new[] { new Msg("fx.p.game_capture") },
            ("Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", 0),
            ("System\\GameConfigStore", "GameDVR_Enabled", 0)),
        "game_mode" => new RegistryFix("game_mode", new Msg("fx.t.game_mode"), new[] { new Msg("fx.p.game_mode") },
            ("Software\\Microsoft\\GameBar", "AutoGameModeEnabled", 1),
            ("Software\\Microsoft\\GameBar", "AllowAutoGameMode", 1)),
        "proxy" => new ProxyFix(),
        "power" => new PowerFix(),
        "refresh" => new RefreshFix(),
        "cleantemp" => new CleanTempFix(),
        "startup" => new StartupFix(),
        "restart" => new RestartFix(),
        "bloat" => new BloatFix(),
        _ => null,
    };

    /// <summary>The fix that corrects a warning, if there is a safe one.</summary>
    public static string? ForCheck(CheckResult r)
    {
        if (r.Status is not (Status.Warn or Status.Fail)) return null;
        var id = r.Name.Key switch
        {
            "n.startup" => "startup",
            "n.proxy" => "proxy",
            "n.diskspace" => "cleantemp",
            "n.reboot" or "n.uptime" => "restart",
            "n.bloat" => BloatFix.Candidates().Count > 0 ? "bloat" : null,
            _ => null,
        };
        return PackageInfo.Blocks(id) ? null : id;   // in the Store package, registry fixes become "Open settings"
    }

    /// <summary>For findings PulseCheck cannot safely fix itself: the Windows page where the user can.</summary>
    public static string? OpenTargetForCheck(CheckResult r)
    {
        if (r.Status is not (Status.Warn or Status.Fail)) return null;
        return r.Name.Key switch
        {
            "n.startup" => "ms-settings:startupapps",
            "n.proxy" => "ms-settings:network-proxy",
            "n.updates" => "ms-settings:windowsupdate",
            "n.bloat" => "ms-settings:appsfeatures",
            "n.activation" => "ms-settings:activation",
            "n.defender" or "n.firewall" or "n.sigs" or "n.antivirus" => "windowsdefender:",
            "n.devices" => "devmgmt.msc",
            "n.wifi" or "n.adapters" or "n.ip" or "n.gateway" => "ms-settings:network",
            _ => null,
        };
    }
}

/// <summary>Sets DWORD values under the current user's registry hive and remembers the previous ones.</summary>
public sealed class RegistryFix : Fix
{
    readonly (string path, string name, int value)[] sets;
    readonly Msg[] plan;
    public override string Id { get; }
    public override Msg Title { get; }

    public RegistryFix(string id, Msg title, Msg[] plan, params (string path, string name, int value)[] sets)
    { Id = id; Title = title; this.plan = plan; this.sets = sets; }

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => plan;

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        var before = new List<(string path, string name, object? value)>();
        try
        {
            foreach (var (path, name, value) in sets)
            {
                using var k = Reg.Create(path);
                before.Add((path, name, k.GetValue(name)));
                k.SetValue(name, value, RegistryValueKind.DWord);
            }
        }
        catch (Exception e) { Revert(before); return new FixOutcome(false, new Msg("fx.failed", e.Message)); }
        return new FixOutcome(true, null, () => Revert(before));
    }

    static void Revert(List<(string path, string name, object? value)> before)
    {
        foreach (var (path, name, value) in before)
        {
            using var k = Reg.Create(path);
            if (value == null) k.DeleteValue(name, false); else k.SetValue(name, value, RegistryValueKind.DWord);
        }
    }
}

public sealed class ProxyFix : Fix
{
    const string Path = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";
    public override string Id => "proxy";
    public override Msg Title => new("fx.t.proxy");

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw)
    {
        using var k = Reg.Open(Path);
        return new[] { new Msg("fx.p.proxy", k?.GetValue("ProxyServer") ?? "?") };
    }

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        try
        {
            using var k = Reg.Create(Path);
            var before = k.GetValue("ProxyEnable");
            k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            return new FixOutcome(true, null, () => { using var u = Reg.Create(Path); if (before == null) u.DeleteValue("ProxyEnable", false); else u.SetValue("ProxyEnable", before, RegistryValueKind.DWord); });
        }
        catch (Exception e) { return new FixOutcome(false, new Msg("fx.failed", e.Message)); }
    }
}

public sealed class PowerFix : Fix
{
    const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public override string Id => "power";
    public override Msg Title => new("fx.t.power");

    static (string guid, string name) Current()
    {
        var (_, o) = Proc.Run("powercfg", "/getactivescheme");
        var g = Regex.Match(o, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
        var n = Regex.Match(o, @"\(([^)]+)\)");
        return (g.Success ? g.Value : "", n.Success ? n.Groups[1].Value : "?");
    }

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => new[] { new Msg("fx.p.power", Current().name) };

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        var prev = Current().guid;
        var (code, _) = Proc.Run("powercfg", "/setactive " + Balanced);
        if (code != 0) return new FixOutcome(false, new Msg("fx.failed", "powercfg " + code));
        return new FixOutcome(true, null, string.IsNullOrEmpty(prev) ? null : () => Proc.Run("powercfg", "/setactive " + prev));
    }
}

public sealed class RefreshFix : Fix
{
    public override string Id => "refresh";
    public override Msg Title => new("fx.t.refresh");

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => new[]
    {
        new Msg("fx.p.refresh", hw?.ScreenHz ?? 0, hw?.ScreenMaxHz ?? 0, hw?.ScreenW ?? 0, hw?.ScreenH ?? 0),
        new Msg("fx.p.refresh2"),
    };

    static bool Set(int hz)
    {
        var m = Native.NewMode();
        if (!Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref m)) return false;
        m.dmDisplayFrequency = hz;
        m.dmFields = Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
        if (Native.ChangeDisplaySettings(ref m, Native.CDS_TEST) != Native.DISP_CHANGE_SUCCESSFUL) return false;   // never try an unsupported mode
        return Native.ChangeDisplaySettings(ref m, Native.CDS_UPDATEREGISTRY) == Native.DISP_CHANGE_SUCCESSFUL;
    }

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        if (hw == null || hw.ScreenMaxHz <= hw.ScreenHz) return new FixOutcome(false, new Msg("fx.failed", "no higher refresh rate"));
        int prev = hw.ScreenHz;
        if (!Set(hw.ScreenMaxHz)) return new FixOutcome(false, new Msg("fx.e.display"));
        return new FixOutcome(true, null, () => Set(prev));
    }
}

public sealed class CleanTempFix : Fix
{
    public string TempDir = DefaultTempDir();
    public TimeSpan MinAge = TimeSpan.FromDays(1);
    /// <summary>The preview stops counting after this many files, so the dialog never freezes on a huge folder.</summary>
    public int PreviewFileLimit = 20000;
    public override string Id => "cleantemp";
    public override Msg Title => new("fx.t.cleantemp");
    public override bool Reversible => false;
    public override bool Ready => IsSafeTempDir(TempDir);

    /// <summary>
    /// Deleting files is the one thing PulseCheck does that cannot be undone, so the folder must be unmistakably a
    /// temp folder: not a drive root, and named Temp or Tmp. A misconfigured TEMP pointing at Documents, the user
    /// profile or a system folder is refused.
    /// </summary>
    public static bool IsSafeTempDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        string full;
        try { full = Path.GetFullPath(dir).TrimEnd('\\', '/'); } catch { return false; }
        if (full.Length < 4) return false;                                           // a drive root such as C:
        var name = Path.GetFileName(full);
        return name.Equals("Temp", StringComparison.OrdinalIgnoreCase) || name.Equals("Tmp", StringComparison.OrdinalIgnoreCase);
    }

    static string DefaultTempDir()
    {
        var t = Path.GetTempPath();
        if (IsSafeTempDir(t)) return t;
        // e.g. a per-session sub-folder such as ...\Temp\1: fall back to the standard user temp folder
        var std = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        return Directory.Exists(std) ? std : t;
    }

    static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }   // when in doubt, do not touch it
    }

    /// <summary>Old files only. Never enters a folder junction or symbolic link, so nothing outside the temp folder is reached.</summary>
    IEnumerable<FileInfo> Old()
    {
        var cutoff = DateTime.Now - MinAge;
        var stack = new Stack<string>(); stack.Push(TempDir);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files = Array.Empty<string>(), subs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); subs = Directory.GetDirectories(dir); } catch { }
            foreach (var s in subs) if (!IsReparsePoint(s)) stack.Push(s);
            foreach (var f in files)
            {
                if (IsReparsePoint(f)) continue;
                FileInfo? fi = null;
                try { fi = new FileInfo(f); } catch { }
                if (fi != null && fi.LastWriteTime < cutoff && fi.LastAccessTime < cutoff) yield return fi;
            }
        }
    }

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw)
    {
        if (!IsSafeTempDir(TempDir)) return new[] { new Msg("fx.e.tempunsafe", TempDir) };
        long bytes = 0; int n = 0; bool more = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var f in Old())
        {
            try { bytes += f.Length; } catch { }
            n++;
            if (n >= PreviewFileLimit || clock.ElapsedMilliseconds > 3000) { more = true; break; }
        }
        return new[] { new Msg(more ? "fx.p.cleantemp_more" : "fx.p.cleantemp", Math.Round(bytes / 1048576d), n), new Msg("fx.p.cleantemp2") };
    }

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        if (!IsSafeTempDir(TempDir)) return new FixOutcome(false, new Msg("fx.e.tempunsafe", TempDir));
        long freed = 0; int n = 0;
        foreach (var f in Old().ToList())
        {
            try { long len = f.Length; f.Delete(); freed += len; n++; } catch { /* in use: skip */ }
        }
        RemoveEmptyFolders(TempDir, true);
        return new FixOutcome(true, null, null, new Msg("fx.n.freed", Math.Round(freed / 1048576d), n));
    }

    /// <summary>Removes folders left empty, bottom-up, never the temp folder itself and never through a junction.</summary>
    static void RemoveEmptyFolders(string dir, bool isRoot)
    {
        string[] subs = Array.Empty<string>();
        try { subs = Directory.GetDirectories(dir); } catch { }
        foreach (var s in subs) if (!IsReparsePoint(s)) RemoveEmptyFolders(s, false);
        if (isRoot) return;
        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
    }
}
public sealed class RestartFix : Fix
{
    public override string Id => "restart";
    public override Msg Title => new("fx.t.restart");
    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => new[] { new Msg("fx.p.restart"), new Msg("fx.p.restart2") };

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        var (code, _) = Proc.Run("shutdown", "/r /t 60 /c \"PulseCheck: restart requested\"");
        if (code != 0) return new FixOutcome(false, new Msg("fx.failed", "shutdown " + code));
        return new FixOutcome(true, null, () => Proc.Run("shutdown", "/a"), new Msg("fx.n.restart"));
    }
}

/// <summary>Switches startup programs off the same way Task Manager does. Nothing is uninstalled.</summary>
public sealed class StartupFix : Fix
{
    public sealed class Entry
    {
        public string Name = "", Command = "", Approved = "Run";   // Approved = StartupApproved sub-key
        public bool Machine, Disabled, Keep, Selected;
        public CheckBox? Box;
        public bool Chosen => Selected || Box?.IsChecked == true;
    }

    const string ApprovedBase = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\";
    static readonly Regex KeepRx = new(@"SecurityHealth|Defender|Realtek|RtkAud|Synaptics|ELAN|Touchpad|Vanguard|Bluetooth", RegexOptions.IgnoreCase);

    public List<Entry> Entries = new();
    public override string Id => "startup";
    public override Msg Title => new("fx.t.startup");
    public override bool Ready => Entries.Any(e => e.Chosen);

    public static List<Entry> Enumerate()
    {
        var list = new List<Entry>();
        void FromRun(RegistryKey? hive, bool machine)
        {
            using var k = hive?.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
            foreach (var n in k?.GetValueNames() ?? Array.Empty<string>())
                list.Add(new Entry { Name = n, Command = (k!.GetValue(n) as string) ?? "", Machine = machine, Approved = "Run" });
        }
        FromRun(Registry.CurrentUser, false);
        FromRun(Registry.LocalMachine, true);
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            foreach (var f in Directory.GetFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                list.Add(new Entry { Name = Path.GetFileName(f), Command = f, Approved = "StartupFolder" });
        }
        catch { }
        foreach (var e in list)
        {
            e.Keep = KeepRx.IsMatch(e.Name) || KeepRx.IsMatch(e.Command);
            using var a = (e.Machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(ApprovedBase + e.Approved);
            e.Disabled = a?.GetValue(e.Name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1;
        }
        return list.Where(e => !e.Disabled).ToList();
    }

    public override void CaptureSelection() { foreach (var e in Entries) e.Selected = e.Chosen; }

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => new[] { new Msg("fx.p.startup"), new Msg("fx.p.startup2") };

    public override UIElement? ExtraUi(HardwareInfo? hw)
    {
        Entries = Enumerate();
        bool admin = Scanner.IsAdmin;
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var e in Entries)
        {
            bool locked = e.Machine && !admin;
            var cb = new CheckBox { IsEnabled = !locked, Margin = new Thickness(0, 5, 0, 5), Foreground = Theme.Br("Ink") };
            var sp = new StackPanel();
            var line = new TextBlock { Text = e.Name, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br("Ink"), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(line);
            var tag = locked ? Loc.T("fx.needadmin") : e.Keep ? Loc.T("fx.keep") : null;
            var cmd = new TextBlock { Text = (tag != null ? tag + "  ·  " : "") + e.Command, FontSize = 11.5, Foreground = Theme.Br(e.Keep || locked ? "Warn" : "Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 };
            sp.Children.Add(cmd);
            cb.Content = sp; e.Box = cb;
            panel.Children.Add(cb);
        }
        if (Entries.Count == 0) panel.Children.Add(new TextBlock { Text = Loc.T("fx.nothing"), Foreground = Theme.Br("Muted") });
        return new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    static byte[] Disabled()
    {
        var b = new byte[12]; b[0] = 3;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(b, 4);
        return b;
    }

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        var chosen = Entries.Where(e => e.Selected).ToList();   // plain data: safe off the UI thread
        var done = new List<(Entry e, object? before)>();
        try
        {
            foreach (var e in chosen)
            {
                using var k = e.Machine ? Registry.LocalMachine.CreateSubKey(ApprovedBase + e.Approved) : Reg.Create(ApprovedBase + e.Approved);
                done.Add((e, k.GetValue(e.Name)));
                k.SetValue(e.Name, Disabled(), RegistryValueKind.Binary);
            }
        }
        catch (Exception ex) { Revert(done); return new FixOutcome(false, new Msg("fx.failed", ex.Message)); }
        return new FixOutcome(true, null, () => Revert(done), new Msg("fx.n.startup", done.Count));
    }

    // Used by tests and by the dialog-free path: disable named entries directly.
    public static FixOutcome DisableNow(IEnumerable<Entry> chosen)
    {
        var fix = new StartupFix { Entries = chosen.ToList() };
        foreach (var e in fix.Entries) e.Selected = true;
        return fix.Apply(null);
    }

    static void Revert(List<(Entry e, object? before)> done)
    {
        foreach (var (e, before) in done)
        {
            using var k = e.Machine ? Registry.LocalMachine.CreateSubKey(ApprovedBase + e.Approved) : Reg.Create(ApprovedBase + e.Approved);
            if (before is byte[] b) k.SetValue(e.Name, b, RegistryValueKind.Binary);
            else { var on = new byte[12]; on[0] = 2; k.SetValue(e.Name, on, RegistryValueKind.Binary); }
        }
    }
}

/// <summary>Removes preinstalled Microsoft Store apps (trials, promos) for the current user. Win32 programs are left to Settings.</summary>
public sealed class BloatFix : Fix
{
    public sealed class Item { public string Display = "", FullName = ""; public bool Selected; public CheckBox? Box; public bool Chosen => Selected || Box?.IsChecked == true; }

    const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    public List<Item> Items = new();
    public override string Id => "bloat";
    public override Msg Title => new("fx.t.bloat");
    public override bool Reversible => false;
    public override bool Ready => Items.Any(i => i.Chosen);

    /// <summary>Registry data is treated as untrusted: only plain package names are ever passed to PowerShell.</summary>
    public static bool IsSafeName(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9._~-]+$") && s.Length < 200;

    /// <summary>
    /// A real Microsoft Store (MSIX) package lives under ...\WindowsApps. The registry also keeps entries that are NOT
    /// removable Store apps: a classic program that registered an identity from its own folder (Norton does this, in
    /// Program Files), or a leftover whose folder no longer exists. Remove-AppxPackage cannot remove those.
    /// </summary>
    public static bool IsStoreRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root) && root.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Full names of preinstalled-looking Store packages that can really be removed.</summary>
    public static List<string> StoreBloatPackages()
    {
        var list = new List<string>();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Packages);
            foreach (var n in k?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!IsSafeName(n) || !Scanner.Bloat.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase))) continue;
                using var entry = k!.OpenSubKey(n);
                if (IsStoreRoot(entry?.GetValue("PackageRootFolder") as string)) list.Add(n);
            }
        }
        catch { }
        return list;
    }

    public static List<Item> Candidates() =>
        StoreBloatPackages().Select(n => new Item { FullName = n, Display = n.Split('_')[0] }).ToList();

    public override void CaptureSelection() { foreach (var i in Items) i.Selected = i.Chosen; }

    public override IReadOnlyList<Msg> Plan(HardwareInfo? hw) => new[] { new Msg("fx.p.bloat"), new Msg("fx.p.bloat2") };

    public override UIElement? ExtraUi(HardwareInfo? hw)
    {
        Items = Candidates();
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var i in Items)
        {
            i.Box = new CheckBox { Margin = new Thickness(0, 5, 0, 5), Foreground = Theme.Br("Ink"), Content = new TextBlock { Text = i.Display, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br("Ink") } };
            panel.Children.Add(i.Box);
        }
        if (Items.Count == 0) panel.Children.Add(new TextBlock { Text = Loc.T("fx.nobloat"), Foreground = Theme.Br("Muted"), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    public override FixOutcome Apply(HardwareInfo? hw)
    {
        int removed = 0; var failed = new List<string>();
        foreach (var i in Items.Where(x => x.Selected && IsSafeName(x.FullName)))   // plain data: safe off the UI thread
        {
            // the error text goes to stdout so the user sees why, not just the package name
            var (code, output) = Proc.Run("powershell", $"-NoProfile -NonInteractive -Command \"try {{ Remove-AppxPackage -Package '{i.FullName}' -ErrorAction Stop }} catch {{ Write-Output $_.Exception.Message; exit 1 }}\"");
            if (code == 0) removed++;
            else
            {
                var why = (output ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                failed.Add(why.Length == 0 ? i.Display : $"{i.Display} ({(why.Length > 140 ? why[..140] + "…" : why)})");
            }
        }
        if (removed == 0) return new FixOutcome(false, new Msg("fx.failed", string.Join(", ", failed)));
        return new FixOutcome(true, null, null, new Msg("fx.n.bloat", removed));
    }
}