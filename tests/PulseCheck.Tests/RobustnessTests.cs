using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

/// <summary>
/// Bug hunting: the advisor is fed thousands of random (and nonsensical) PCs and must always return
/// sensible, fully translated advice without throwing, whatever the hardware looks like.
/// </summary>
public class RobustnessTests
{
    static readonly string[] Gpus =
    {
        "NVIDIA GeForce RTX 5090", "NVIDIA GeForce RTX 5060 Ti", "NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA GeForce GTX 1050 Ti",
        "NVIDIA GeForce GT 710", "AMD Radeon RX 9070 XT", "AMD Radeon RX 580", "AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics 770",
        "Intel(R) Arc(TM) B580 Graphics", "Some Future Accelerator 9000", "", "Microsoft Basic Display Adapter",
    };
    static readonly string[] Cpus =
    {
        "AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Ryzen 5 3600 6-Core Processor", "13th Gen Intel(R) Core(TM) i5-13600K",
        "Intel(R) Core(TM) i3-6100 CPU @ 3.70GHz", "Intel(R) Core(TM) Ultra 7 265K", "AMD Ryzen 5 7640U w/ Radeon 760M", "Unknown CPU", "",
    };
    static readonly string[] Sockets = { "AM4", "AM5", "LGA1700", "LGA1851", "LGA1151", "LGA1200", "", "U3E1" };
    static readonly string[] RamTypes = { "?", "DDR3", "DDR4", "DDR5", "LPDDR" };
    static readonly (int w, int h)[] Screens = { (0, 0), (1366, 768), (1920, 1080), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160), (7680, 4320) };

    static HardwareInfo Random(Random r)
    {
        var gpuName = Gpus[r.Next(Gpus.Length)];
        var db = GpuDb.Instance;
        var g = db.FindGpu(gpuName);
        var (w, h) = Screens[r.Next(Screens.Length)];
        int[] hzs = { 0, 30, 60, 75, 120, 144, 165, 240 };
        int hz = hzs[r.Next(hzs.Length)];
        var cpuName = Cpus[r.Next(Cpus.Length)];
        return new HardwareInfo
        {
            CpuName = cpuName, CpuTier = GpuDb.CpuTier(cpuName), Cores = r.Next(0, 33), Threads = r.Next(0, 65),
            Socket = Sockets[r.Next(Sockets.Length)], Board = r.Next(2) == 0 ? "" : "Some Board",
            GpuName = gpuName, GpuTier = g?.tier, GpuIntegrated = g?.igpu ?? false, VramGB = r.Next(0, 33), GpuVendor = new[] { "nvidia", "amd", "intel", "?" }[r.Next(4)],
            GpuDriverDate = r.Next(3) == 0 ? null : DateTime.Now.AddDays(-r.Next(0, 1200)),
            ScreenW = w, ScreenH = h, ScreenHz = hz, ScreenMaxHz = hz + (r.Next(2) == 0 ? 0 : r.Next(0, 200)),
            RamGB = new[] { 0, 2, 4, 8, 12, 16, 24, 32, 64, 128 }[r.Next(10)], RamModules = r.Next(0, 5), RamSlots = r.Next(0, 9), RamMaxGB = r.Next(0, 3) == 0 ? 0 : 64,
            RamType = RamTypes[r.Next(RamTypes.Length)], RamSpeed = new[] { 0, 2133, 2666, 3200, 4800, 5600, 6000 }[r.Next(7)], RamSoldered = r.Next(4) == 0,
            IsLaptop = r.Next(3) == 0, HasSsd = r.Next(2) == 0, HasHdd = r.Next(2) == 0,
            SysFreePct = r.Next(0, 101), SysFreeGB = r.Next(0, 2000), PowerPlan = r.Next(4) == 0 ? "a1841308-3541-4fab-bc81-f71556f20b4a" : "",
            GameDvrOn = r.Next(2) == 0, GameModeOff = r.Next(2) == 0, StartupCount = r.Next(0, 40), UptimeDays = r.Next(0, 60),
        };
    }

    [Fact]
    public void Advisor_never_throws_and_always_gives_sound_translated_advice_on_5000_random_pcs()
    {
        var r = new Random(20261127);
        var allowedGain = new HashSet<string?> { null, "a.gain.small", "a.gain.moderate", "a.gain.large", "a.gain.huge" };
        var problems = new List<string>();

        for (int n = 0; n < 5000; n++)
        {
            var h = Random(r);
            foreach (var u in Enum.GetValues<Usage>())
            {
                List<Suggestion> list;
                try { list = Advisor.Build(h, u); }
                catch (Exception e) { problems.Add($"#{n} {u}: threw {e.GetType().Name}: {e.Message}"); continue; }

                foreach (var s in list)
                {
                    string who = $"#{n} {u} {s.Title.Key}";
                    if (s.Because.Count < 1) problems.Add($"{who}: no reasons");
                    if (s.Priority < 1) problems.Add($"{who}: priority {s.Priority}");
                    if (s.Cost < 0 || s.Cost > 4) problems.Add($"{who}: cost {s.Cost}");
                    if (!allowedGain.Contains(s.Gain)) problems.Add($"{who}: gain {s.Gain}");
                    if (s.FixId != null && Fixes.Create(s.FixId) == null) problems.Add($"{who}: unknown fix {s.FixId}");

                    // laptops and soldered parts must never be told to swap what cannot be swapped
                    if (h.IsLaptop && s.Title.Key is "a.t.gpu" or "a.t.cpu" or "a.t.platform" && h.IsLaptop) problems.Add($"{who}: hardware swap advised for a laptop");
                    if (h.RamSoldered && s.Action.Key is "a.x.ram_add" or "a.x.ram_kit" or "a.x.channel") problems.Add($"{who}: RAM swap advised for soldered memory");
                    if (s.Kind == SugKind.Upgrade && s.Cost == 0 && s.Priority != 5) problems.Add($"{who}: a free upgrade?");

                    if (s.Title.Key == "a.t.gpu" && s.Action.Args.Length > 0 && s.Action.Args[0] is int t)
                    {
                        if (t < 4 || t > 10) problems.Add($"{who}: GPU class {t}");
                        if (h.GpuTier is int cur && t <= cur && u == Usage.Gaming) problems.Add($"{who}: recommends class {t} but the card is already class {cur}");
                    }
                }

                // every message must be translated and have all its placeholders filled, in every language
                foreach (var code in Loc.Codes)
                {
                    Loc.Set(code);
                    foreach (var m in list.SelectMany(s => s.Because.Append(s.Action).Append(s.Title).Concat(s.Notes)))
                    {
                        var text = m.Text;
                        if (text == m.Key) problems.Add($"#{n} {u} {code}: missing text for {m.Key}");
                        if (Regex.IsMatch(text, @"\{\d+\}")) problems.Add($"#{n} {u} {code}: unfilled placeholder in {m.Key}");
                    }
                }
                Loc.Set("en");

                // deterministic
                var again = Advisor.Build(h, u);
                if (!again.Select(x => x.Title.Key).SequenceEqual(list.Select(x => x.Title.Key))) problems.Add($"#{n} {u}: not deterministic");
            }
            if (problems.Count > 15) break;
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(15)));
    }

    [Fact]
    public void A_host_that_never_answers_is_declared_unreachable_in_seconds_not_minutes()
    {
        // 192.0.2.0/24 is reserved for documentation (RFC 5737): nothing ever answers there.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (loss, avg, jitter) = Scanner.PingStats("192.0.2.1", 10, 300);
        clock.Stop();
        Assert.Equal(100, loss);
        Assert.Null(avg); Assert.Null(jitter);
        Assert.True(clock.ElapsedMilliseconds < 4000, $"took {clock.ElapsedMilliseconds} ms: it kept waiting for all 10 attempts");
    }

    [Fact]
    public void Pinging_the_local_machine_still_reports_a_working_link()
    {
        var (loss, avg, _) = Scanner.PingStats("127.0.0.1", 4, 1000);
        Assert.Equal(0, loss);
        Assert.NotNull(avg);
    }

    [Fact]
    public void A_completely_empty_hardware_profile_does_not_crash_any_usage()
    {
        foreach (var u in Enum.GetValues<Usage>()) Assert.NotNull(Advisor.Sorted(Advisor.Build(new HardwareInfo(), u)));
    }

    [Fact]
    public void Report_builds_for_any_hardware_and_language_without_leaking_markup()
    {
        var r = new Random(7);
        foreach (var code in Loc.Codes)
        {
            Loc.Set(code);
            for (int i = 0; i < 50; i++)
            {
                var h = Random(r); h.GpuName = "<img src=x onerror=alert(1)>";
                var res = new List<CheckResult> { new("cat.system", new Msg("n.cpuload"), Status.Warn, new Msg("d.check_error", "<script>bad()</script>")) };
                var html = Report.Build(res, h, Usage.Gaming);
                Assert.DoesNotContain("<script>bad()", html);
                Assert.DoesNotContain("<img src=x", html);
            }
        }
        Loc.Set("en");
    }

    [Theory]
    [InlineData("xx")] [InlineData("")] [InlineData("EN")] [InlineData("fr-FR")]
    public void Unknown_languages_fall_back_to_english_instead_of_crashing(string code)
    {
        Loc.Set(code);
        Assert.NotEqual("a.t.gpu", Loc.T("a.t.gpu"));
        Loc.Set("en");
    }

    [Theory]
    [InlineData("")] [InlineData("garbage")] [InlineData("AMD Ryzen")] [InlineData("Intel(R) Core(TM)")]
    public void Unparseable_cpu_names_are_reported_as_unknown_not_guessed(string name) => Assert.Null(GpuDb.CpuTier(name));
}
