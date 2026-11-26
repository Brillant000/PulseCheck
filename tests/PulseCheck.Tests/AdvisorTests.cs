using System;
using System.Collections.Generic;
using System.Linq;
using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

public class HardwareDatabaseTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060 Ti", 6)]
    [InlineData("NVIDIA GeForce RTX 5090", 10)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 8)]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", 7)]
    [InlineData("NVIDIA GeForce RTX 4060 Ti", 6)]
    [InlineData("NVIDIA GeForce RTX 4060", 5)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", 4)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", 4)]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", 3)]
    [InlineData("AMD Radeon RX 7800 XT", 7)]
    [InlineData("AMD Radeon RX 9070 XT", 8)]
    [InlineData("AMD Radeon RX 6600", 5)]
    [InlineData("Intel(R) UHD Graphics 770", 1)]
    [InlineData("AMD Radeon(TM) Graphics", 1)]
    public void Gpu_class_is_detected(string name, int tier) => Assert.Equal(tier, GpuDb.Instance.FindGpu(name)?.tier);

    [Fact] public void Unknown_gpu_is_reported_as_unknown() => Assert.Null(GpuDb.Instance.FindGpu("Some Future Accelerator 9000"));

    [Theory]
    [InlineData("AMD Ryzen 5 7500F 6-Core Processor", 7)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 9)]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", 8)]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", 5)]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600K", 7)]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", 5)]
    [InlineData("Intel(R) Core(TM) i3-10100 CPU @ 3.60GHz", 2)]
    public void Cpu_class_is_estimated(string name, int tier) => Assert.Equal(tier, GpuDb.CpuTier(name));

    [Theory]
    [InlineData(1920, 1080, 60, 4)]
    [InlineData(1920, 1080, 144, 6)]
    [InlineData(2560, 1440, 60, 6)]
    [InlineData(2560, 1440, 144, 7)]
    [InlineData(3840, 2160, 60, 8)]
    [InlineData(3840, 2160, 144, 9)]
    public void Display_decides_the_needed_gpu_class(int w, int h, int hz, int tier) => Assert.Equal(tier, Advisor.TargetGpuTier(w, h, hz));

    [Fact] public void Every_gpu_class_used_for_upgrades_exists_and_has_examples()
    {
        foreach (var t in new[] { 4, 5, 6, 7, 8, 9, 10 })
        {
            var c = GpuDb.Instance.ClassFor(t);
            Assert.NotNull(c);
            Assert.Equal(t, c!.Tier);
            Assert.NotEmpty(c.Examples);
            Assert.True(c.Psu >= 400);
        }
    }
}

public class AdvisorTests
{
    [Theory]
    [InlineData("en")] [InlineData("fr")] [InlineData("es")] [InlineData("de")]
    public void Advisor_texts_are_embedded_and_load_in_every_language(string code)
    {
        Loc.Set(code);
        foreach (var key in new[] { "a.t.gpu", "a.x.ram_kit", "a.z.psu", "a.ui.tab_adv", "a.prof.creator", "a.cost.0" })
            Assert.NotEqual(key, Loc.T(key));            // would equal the key if the resource were missing
        Loc.Set("en");
    }

    static HardwareInfo WellMatched() => new()
    {
        CpuName = "AMD Ryzen 5 7500F", CpuTier = 7, Cores = 6, Socket = "AM5",
        GpuName = "NVIDIA GeForce RTX 5060 Ti", GpuTier = 6, GpuVendor = "nvidia", VramGB = 16,
        ScreenW = 1920, ScreenH = 1080, ScreenHz = 165, ScreenMaxHz = 165,
        RamGB = 32, RamModules = 2, RamSlots = 4, RamType = "DDR5", RamSpeed = 6000,
        HasSsd = true, SysFreePct = 50, SysFreeGB = 900, UptimeDays = 1,
    };

    static Suggestion? Find(List<Suggestion> l, string title) => l.FirstOrDefault(s => s.Title.Key == title);

    [Fact]
    public void A_well_matched_gaming_pc_gets_no_hardware_upgrade()
    {
        var s = Advisor.Build(WellMatched(), Usage.Gaming);
        Assert.DoesNotContain(s, x => x.Kind == SugKind.Upgrade);
    }

    [Fact]
    public void Weak_gpu_on_a_144_hz_1440p_screen_gets_a_class_7_card_with_A_plus_B_reasons()
    {
        var h = WellMatched();
        h.GpuName = "NVIDIA GeForce GTX 1660"; h.GpuTier = 4; h.VramGB = 6;
        h.ScreenW = 2560; h.ScreenH = 1440; h.ScreenHz = 144; h.ScreenMaxHz = 144;
        var gpu = Find(Advisor.Build(h, Usage.Gaming), "a.t.gpu");
        Assert.NotNull(gpu);
        Assert.Equal(7, (int)gpu!.Action.Args[0]);
        Assert.True(gpu.Because.Count >= 2);            // A: your card, B: your display
        Assert.Equal("a.gain.huge", gpu.Gain);          // 3 classes of difference
        Assert.Contains(gpu.Notes, n => n.Key == "a.z.psu");
    }

    [Fact]
    public void A_laptop_is_never_told_to_swap_its_gpu_or_cpu()
    {
        var h = WellMatched();
        h.IsLaptop = true; h.GpuTier = 3; h.CpuTier = 2; h.ScreenW = 2560; h.ScreenH = 1440; h.ScreenHz = 144; h.ScreenMaxHz = 144;
        var s = Advisor.Build(h, Usage.Gaming);
        Assert.Null(Find(s, "a.t.gpu"));
        Assert.Null(Find(s, "a.t.cpu"));
        Assert.Null(Find(s, "a.t.platform"));
        Assert.NotNull(Find(s, "a.t.laptop"));
    }

    [Fact]
    public void Screen_running_below_its_maximum_refresh_rate_is_a_free_quick_win()
    {
        var h = WellMatched(); h.ScreenHz = 60;
        var s = Find(Advisor.Build(h, Usage.Gaming), "a.t.refresh");
        Assert.NotNull(s);
        Assert.Equal(0, s!.Cost);
        Assert.Equal(SugKind.Quick, s.Kind);
        Assert.Equal("ms-settings:display-advanced", s.OpenUri);
    }

    [Fact]
    public void Eight_gb_of_ram_for_gaming_recommends_more_memory_that_matches_the_board()
    {
        var h = WellMatched(); h.RamGB = 8; h.RamModules = 1; h.RamSlots = 2; h.RamType = "DDR4"; h.RamSpeed = 3200;
        var ram = Find(Advisor.Build(h, Usage.Gaming), "a.t.ram");
        Assert.NotNull(ram);
        Assert.Equal(1, ram!.Priority);
        Assert.Contains(ram.Notes, n => n.Key == "a.z.ddr" && (string)n.Args[0] == "DDR4");
    }

    [Fact]
    public void Soldered_memory_is_never_offered_as_an_upgrade_path()
    {
        var h = WellMatched(); h.RamGB = 8; h.RamModules = 2; h.RamSlots = 2; h.RamSoldered = true; h.IsLaptop = true;
        var ram = Find(Advisor.Build(h, Usage.Gaming), "a.t.ram");
        Assert.NotNull(ram);
        Assert.Equal("a.x.ram_soldered", ram!.Action.Key);
    }

    [Fact]
    public void A_pc_with_only_a_hard_drive_is_told_to_get_an_ssd_first()
    {
        var h = WellMatched(); h.HasSsd = false; h.HasHdd = true;
        var s = Advisor.Sorted(Advisor.Build(h, Usage.Work));
        var ssd = Find(s, "a.t.ssd");
        Assert.NotNull(ssd);
        Assert.Equal(1, ssd!.Priority);
        Assert.Equal("a.gain.huge", ssd.Gain);
    }

    [Fact]
    public void Creators_need_more_memory_and_a_capable_gpu_than_office_users()
    {
        var h = WellMatched(); h.RamGB = 16; h.RamModules = 2; h.GpuTier = 3; h.GpuName = "NVIDIA GeForce GTX 1060";
        Assert.NotNull(Find(Advisor.Build(h, Usage.Creator), "a.t.ram"));
        Assert.NotNull(Find(Advisor.Build(h, Usage.Creator), "a.t.gpu"));
        Assert.Null(Find(Advisor.Build(h, Usage.Work), "a.t.ram"));
        Assert.Null(Find(Advisor.Build(h, Usage.Work), "a.t.gpu"));
    }

    [Fact]
    public void An_old_cpu_with_no_upgrade_path_gets_a_platform_plan_not_a_cpu_swap()
    {
        var h = WellMatched(); h.CpuName = "Intel(R) Core(TM) i3-6100"; h.CpuTier = 1; h.Socket = "LGA1151";
        var s = Advisor.Build(h, Usage.Gaming);
        Assert.NotNull(Find(s, "a.t.platform"));
        Assert.Null(Find(s, "a.t.cpu"));
    }

    [Fact]
    public void Every_suggestion_explains_why_and_what_to_do_and_every_upgrade_has_a_cost()
    {
        var h = WellMatched();
        h.GpuTier = 3; h.GpuName = "GTX 1060"; h.CpuTier = 3; h.RamGB = 8; h.RamModules = 1; h.RamSlots = 2; h.RamType = "DDR4"; h.RamSpeed = 2400;
        h.HasSsd = false; h.HasHdd = true; h.ScreenHz = 60; h.ScreenMaxHz = 144; h.StartupCount = 25; h.GameDvrOn = true; h.GameModeOff = true;
        h.GpuDriverDate = DateTime.Now.AddDays(-400); h.UptimeDays = 30; h.SysFreePct = 8; h.SysFreeGB = 20;
        foreach (var u in Enum.GetValues<Usage>())
            foreach (var s in Advisor.Build(h, u))
            {
                Assert.True(s.Because.Count >= 1, s.Title.Key);
                Assert.False(string.IsNullOrWhiteSpace(s.Action.Key));
                if (s.Kind == SugKind.Upgrade) Assert.True(s.Cost >= 0);
                // every message renders without leaving placeholders in any language
                foreach (var code in Loc.Codes)
                {
                    Loc.Set(code);
                    foreach (var m in s.Because.Append(s.Action).Append(s.Title).Concat(s.Notes))
                        Assert.DoesNotMatch(@"\{\d+\}", m.Text);
                }
            }
        Loc.Set("en");
    }
}
