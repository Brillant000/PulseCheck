using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

/// <summary>
/// In the Microsoft Store package, registry changes to existing values stay in a private copy and never reach
/// Windows (verified on real builds). Those fixes must then become "Open settings", never a button that says
/// "done" while changing nothing.
/// </summary>
public class PackageModeTests
{
    static void InMode(bool packaged, Action body)
    {
        PackageInfo.Override = packaged;
        try { body(); } finally { PackageInfo.Override = null; }
    }

    [Theory]
    [InlineData("game_capture", true)] [InlineData("game_mode", true)] [InlineData("proxy", true)] [InlineData("startup", true)]
    [InlineData("power", false)] [InlineData("refresh", false)] [InlineData("restart", false)]
    [InlineData("cleantemp", false)] [InlineData("bloat", false)]
    public void Only_registry_fixes_are_blocked_in_the_package(string id, bool blocked)
    {
        InMode(true, () => Assert.Equal(blocked, PackageInfo.Blocks(id)));
        InMode(false, () => Assert.False(PackageInfo.Blocks(id)));   // the normal .exe keeps every fix
    }

    [Fact]
    public void Every_blocked_fix_id_is_a_real_fix()
    {
        foreach (var id in PackageInfo.RegistryFixes) Assert.NotNull(Fixes.Create(id));
    }

    [Theory]
    [InlineData("n.startup", "ms-settings:startupapps")]
    [InlineData("n.proxy", "ms-settings:network-proxy")]
    public void A_blocked_finding_has_no_Fix_button_but_does_open_the_right_page(string key, string uri)
    {
        var r = new CheckResult("cat.system", new Msg(key), Status.Warn, new Msg("d.check_error", "x"));
        InMode(true, () => { Assert.Null(Fixes.ForCheck(r)); Assert.Equal(uri, Fixes.OpenTargetForCheck(r)); });
        InMode(false, () => Assert.NotNull(Fixes.ForCheck(r)));
    }

    [Fact]
    public void Every_advisor_suggestion_with_a_blocked_fix_still_offers_an_Open_action()
    {
        var pcs = new[]
        {
            new HardwareInfo { GameDvrOn = true, GameModeOff = true, StartupCount = 25, IsLaptop = false, HasSsd = true, RamGB = 16 },
            new HardwareInfo { GameDvrOn = true, StartupCount = 18, IsLaptop = true, RamGB = 8 },
            new HardwareInfo { GameModeOff = true, StartupCount = 30, RamGB = 32, GpuName = "NVIDIA GeForce RTX 4070", ScreenW = 2560, ScreenH = 1440, ScreenHz = 144, ScreenMaxHz = 144 },
        };
        int seen = 0;
        foreach (var h in pcs)
            foreach (var u in Enum.GetValues<Usage>())
                foreach (var s in Advisor.Build(h, u).Where(s => PackageInfo.RegistryFixes.Contains(s.FixId ?? "")))
                {
                    seen++;
                    Assert.False(string.IsNullOrEmpty(s.OpenUri), $"{s.Title.Key} has a registry fix but no page to open");
                }
        Assert.True(seen > 0, "the test did not exercise any registry-based suggestion");
    }

    [Fact]
    public void Outside_a_package_log_paths_are_left_untouched()
    {
        InMode(false, () => Assert.Equal(FixLog.FilePath, PackageInfo.VisiblePath(FixLog.FilePath)));
    }

    [Fact]
    public void Inside_a_package_the_log_path_points_to_the_package_cache_or_stays_valid()
    {
        InMode(true, () =>
        {
            var p = PackageInfo.VisiblePath(FixLog.FilePath);
            Assert.EndsWith("changes.log", p);          // when the process has no package identity (tests) it falls back to the nominal path
        });
    }
}
