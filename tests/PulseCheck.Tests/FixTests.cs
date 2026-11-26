using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

/// <summary>
/// Fixes change the user's system, so they are tested for real: applied and undone against a throw-away
/// registry branch (Reg.Prefix) and a temporary folder. The user's real settings are never touched.
/// </summary>
[Collection("fixes")]
public sealed class FixTests : IDisposable
{
    readonly string prefix = $@"Software\PulseCheckTests\{Guid.NewGuid():N}\";
    readonly List<string> dirs = new();

    public FixTests() { Reg.Prefix = prefix; FixLog.Enabled = false; }

    public void Dispose()
    {
        Reg.Prefix = ""; FixLog.Enabled = true;
        Registry.CurrentUser.DeleteSubKeyTree(prefix.TrimEnd('\\'), false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\PulseCheckTests"))
            if (parent != null && parent.SubKeyCount == 0) { parent.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\PulseCheckTests", false); }
        foreach (var d in dirs) try { Directory.Delete(d, true); } catch { }
    }

    static object? Get(string path, string name) { using var k = Reg.Open(path); return k?.GetValue(name); }
    static void Set(string path, string name, object v) { using var k = Reg.Create(path); k.SetValue(name, v); }

    [Fact]
    public void Game_capture_is_switched_off_and_restored_exactly()
    {
        const string a = @"Software\Microsoft\Windows\CurrentVersion\GameDVR", b = @"System\GameConfigStore";
        Set(a, "AppCaptureEnabled", 1); Set(b, "GameDVR_Enabled", 1);
        var o = Fixes.Create("game_capture")!.Apply(null);
        Assert.True(o.Ok);
        Assert.Equal(0, Get(a, "AppCaptureEnabled")); Assert.Equal(0, Get(b, "GameDVR_Enabled"));
        o.Undo!();
        Assert.Equal(1, Get(a, "AppCaptureEnabled")); Assert.Equal(1, Get(b, "GameDVR_Enabled"));
    }

    [Fact]
    public void Game_mode_undo_removes_values_that_did_not_exist_before()
    {
        const string p = @"Software\Microsoft\GameBar";
        var o = Fixes.Create("game_mode")!.Apply(null);
        Assert.True(o.Ok);
        Assert.Equal(1, Get(p, "AutoGameModeEnabled"));
        o.Undo!();
        Assert.Null(Get(p, "AutoGameModeEnabled"));
        Assert.Null(Get(p, "AllowAutoGameMode"));
    }

    [Fact]
    public void Proxy_is_switched_off_without_forgetting_the_server_and_can_be_restored()
    {
        const string p = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        Set(p, "ProxyEnable", 1); Set(p, "ProxyServer", "corp-proxy:8080");
        var o = Fixes.Create("proxy")!.Apply(null);
        Assert.True(o.Ok);
        Assert.Equal(0, Get(p, "ProxyEnable"));
        Assert.Equal("corp-proxy:8080", Get(p, "ProxyServer"));
        o.Undo!();
        Assert.Equal(1, Get(p, "ProxyEnable"));
    }

    [Fact]
    public void Clean_temp_deletes_only_old_files_and_says_it_cannot_be_undone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pulsecheck-test-" + Guid.NewGuid().ToString("N")); dirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var old = Path.Combine(dir, "sub", "old.tmp"); var fresh = Path.Combine(dir, "fresh.tmp");
        File.WriteAllText(old, "x"); File.WriteAllText(fresh, "y");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-5)); File.SetLastAccessTime(old, DateTime.Now.AddDays(-5));

        var fix = new CleanTempFix { TempDir = dir };
        Assert.False(fix.Reversible);
        var o = fix.Apply(null);
        Assert.True(o.Ok);
        Assert.Null(o.Undo);                       // honest: no undo offered
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));           // recent files are never touched
        Assert.True(Directory.Exists(dir));        // the folder itself stays
    }

    [Fact]
    public void Startup_program_is_switched_off_like_task_manager_and_can_be_switched_back_on()
    {
        var e = new StartupFix.Entry { Name = "TestApp", Approved = "Run" };
        var o = StartupFix.DisableNow(new[] { e });
        Assert.True(o.Ok);
        var key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        Assert.Equal(3, ((byte[])Get(key, "TestApp")!)[0]);       // 3 = disabled
        o.Undo!();
        Assert.Equal(2, ((byte[])Get(key, "TestApp")!)[0]);       // 2 = enabled
    }

    [Fact]
    public void Startup_fix_is_not_ready_until_something_is_chosen()
    {
        var fix = new StartupFix { Entries = new() { new StartupFix.Entry { Name = "A" } } };
        Assert.False(fix.Ready);
        fix.Entries[0].Selected = true;
        Assert.True(fix.Ready);
    }

    [Fact]
    public void Only_warnings_get_a_fix_and_the_mapping_is_the_expected_one()
    {
        CheckResult R(string key, Status s) => new("cat.system", new Msg(key), s, new Msg("d.none"));
        Assert.Equal("startup", Fixes.ForCheck(R("n.startup", Status.Warn)));
        Assert.Equal("proxy", Fixes.ForCheck(R("n.proxy", Status.Warn)));
        Assert.Equal("cleantemp", Fixes.ForCheck(R("n.diskspace", Status.Fail)));
        Assert.Equal("restart", Fixes.ForCheck(R("n.reboot", Status.Warn)));
        Assert.Null(Fixes.ForCheck(R("n.startup", Status.Ok)));
        Assert.Null(Fixes.ForCheck(R("n.startup", Status.Info)));
        Assert.Null(Fixes.ForCheck(R("n.firewall", Status.Fail)));                 // security: never changed silently
        Assert.Equal("windowsdefender:", Fixes.OpenTargetForCheck(R("n.firewall", Status.Fail)));
        Assert.Equal("ms-settings:windowsupdate", Fixes.OpenTargetForCheck(R("n.updates", Status.Warn)));
        Assert.Null(Fixes.OpenTargetForCheck(R("n.updates", Status.Ok)));
    }

    [Theory]
    [InlineData("NortonLifeLock.NortonSecurity_8.0.0.0_x64__abc123", true)]
    [InlineData("king.com.CandyCrushSaga_1.2.3.4_neutral__kgqvnymyfvs32", true)]
    [InlineData("x'; Remove-Item C:\\ -Recurse; '", false)]
    [InlineData("name with space", false)]
    [InlineData("a$(calc)", false)]
    [InlineData("", false)]
    public void Only_plain_package_names_are_ever_passed_to_powershell(string name, bool safe)
        => Assert.Equal(safe, BloatFix.IsSafeName(name));

    [Fact]
    public void Removing_store_apps_is_announced_as_irreversible_and_never_preselected()
    {
        var fix = new BloatFix { Items = new() { new BloatFix.Item { Display = "X", FullName = "X_1" } } };
        Assert.False(fix.Reversible);
        Assert.False(fix.Ready);                       // nothing is ticked by default
        fix.Items[0].Selected = true;
        Assert.True(fix.Ready);
    }

    /// <summary>Runs on a WPF-capable STA thread and returns once the work is finished.</summary>
    static T OnSta<T>(Func<T> work)
    {
        T result = default!; Exception? error = null;
        var th = new System.Threading.Thread(() => { try { result = work(); } catch (Exception e) { error = e; } });
        th.SetApartmentState(System.Threading.ApartmentState.STA); th.Start(); th.Join();
        if (error != null) throw error;
        return result;
    }

    [Fact]
    public void Regression_fixes_with_a_checkbox_list_can_be_applied_from_a_background_thread()
    {
        // The dialog's CheckBoxes belong to the UI thread; Apply runs elsewhere. Reading a CheckBox
        // from the wrong thread threw "The calling thread cannot access this object".
        var startup = OnSta(() =>
        {
            var f = new StartupFix { Entries = new() { new StartupFix.Entry { Name = "ThreadApp", Approved = "Run" } } };
            f.Entries[0].Box = new System.Windows.Controls.CheckBox { IsChecked = true };
            f.CaptureSelection();                               // UI thread
            return f;
        });
        var o = System.Threading.Tasks.Task.Run(() => startup.Apply(null)).GetAwaiter().GetResult();   // background thread
        Assert.True(o.Ok);
        Assert.Equal(3, ((byte[])Get(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "ThreadApp")!)[0]);
        o.Undo!();

        var bloat = OnSta(() =>
        {
            var f = new BloatFix { Items = new() { new BloatFix.Item { Display = "X", FullName = "X_1" } } };
            f.Items[0].Box = new System.Windows.Controls.CheckBox { IsChecked = false };    // nothing ticked
            f.CaptureSelection();
            return f;
        });
        var ob = System.Threading.Tasks.Task.Run(() => bloat.Apply(null)).GetAwaiter().GetResult();     // must not throw
        Assert.False(ob.Ok);                                    // nothing selected: nothing removed
    }

    [Fact]
    public void Every_fix_announces_what_it_will_do_in_every_language()
    {
        foreach (var id in new[] { "game_capture", "game_mode", "proxy", "power", "refresh", "cleantemp", "startup", "restart", "bloat" })
        {
            var fix = Fixes.Create(id);
            Assert.NotNull(fix);
            var plan = fix!.Plan(new HardwareInfo { ScreenHz = 60, ScreenMaxHz = 165, ScreenW = 2560, ScreenH = 1440 });
            Assert.NotEmpty(plan);
            foreach (var code in Loc.Codes)
            {
                Loc.Set(code);
                Assert.False(string.IsNullOrWhiteSpace(fix.Title.Text));
                foreach (var m in plan) Assert.DoesNotMatch(@"\{\d+\}", m.Text);
            }
        }
        Loc.Set("en");
    }

    [Fact]
    public void Every_fix_the_advisor_offers_exists()
    {
        var h = new HardwareInfo
        {
            CpuName = "x", GpuName = "NVIDIA GeForce RTX 4060", GpuTier = 5, ScreenW = 1920, ScreenH = 1080, ScreenHz = 60, ScreenMaxHz = 144,
            RamGB = 16, RamModules = 2, RamSlots = 4, RamType = "DDR5", RamSpeed = 4800, HasSsd = true, SysFreePct = 5, SysFreeGB = 10,
            StartupCount = 30, GameDvrOn = true, GameModeOff = true, PowerPlan = "a1841308-3541-4fab-bc81-f71556f20b4a", UptimeDays = 40,
        };
        var ids = Enum.GetValues<Usage>().SelectMany(u => Advisor.Build(h, u)).Select(s => s.FixId).Where(i => i != null).Distinct().ToList();
        Assert.Contains("refresh", ids); Assert.Contains("game_capture", ids); Assert.Contains("startup", ids);
        Assert.Contains("power", ids); Assert.Contains("cleantemp", ids); Assert.Contains("restart", ids);
        foreach (var id in ids) Assert.NotNull(Fixes.Create(id!));
    }
}

[CollectionDefinition("fixes", DisableParallelization = true)]
public sealed class FixCollection { }
