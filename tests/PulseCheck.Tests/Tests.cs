using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

public class LocalizationTests
{
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Lang"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }

    static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lang", code + ".json")))!;

    public static IEnumerable<object[]> Codes() => Loc.Codes.Select(c => new object[] { c });

    [Theory, MemberData(nameof(Codes))]
    public void Language_has_exactly_the_english_keys(string code)
    {
        var en = Load("en").Keys.ToHashSet();
        var other = Load(code).Keys.ToHashSet();
        Assert.Empty(en.Except(other));
        Assert.Empty(other.Except(en));
    }

    [Theory, MemberData(nameof(Codes))]
    public void Language_keeps_the_same_placeholders_as_english(string code)
    {
        var en = Load("en"); var tr = Load(code);
        foreach (var (key, text) in en)
        {
            static string[] Ph(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToArray();
            Assert.True(Ph(text).SequenceEqual(Ph(tr[key])), $"{code}:{key} placeholders differ");
        }
    }

    [Fact]
    public void Every_key_used_in_code_exists()
    {
        var en = Load("en");
        var src = string.Join("\n", Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\")).Select(File.ReadAllText));
        var used = Regex.Matches(src, "\"((?:cat|p|s|n|d|f|ui)\\.[a-z_0-9]+)\"").Select(m => m.Groups[1].Value).Distinct();
        Assert.Empty(used.Where(k => !en.ContainsKey(k)));
    }

    [Fact]
    public void Missing_key_returns_the_key_and_bad_format_does_not_throw()
    {
        Loc.Set("en");
        Assert.Equal("no.such.key", Loc.T("no.such.key"));
        Assert.Equal("{0} GB free ({1} %)", Loc.T("d.diskspace"));
    }

    [Fact]
    public void Formatting_fills_arguments()
    {
        Loc.Set("en");
        Assert.Equal("12.5 GB free (49 %)", Loc.T("d.diskspace", "12.5", 49));
    }
}

public class ScoreTests
{
    static CheckResult R(Status s) => new("cat.system", new Msg("n.cpuload"), s, new Msg("d.pct", 1));

    [Fact] public void Perfect_machine_scores_100() => Assert.Equal(100, Scanner.Score(new[] { R(Status.Ok), R(Status.Info) }));
    [Fact] public void A_warning_costs_5() => Assert.Equal(95, Scanner.Score(new[] { R(Status.Warn) }));
    [Fact] public void A_problem_costs_15() => Assert.Equal(85, Scanner.Score(new[] { R(Status.Fail) }));
    [Fact] public void Score_never_goes_below_zero() => Assert.Equal(0, Scanner.Score(Enumerable.Repeat(R(Status.Fail), 20)));
}

public class ReportTests
{
    [Fact]
    public void Report_is_html_escaped_and_localised()
    {
        Loc.Set("fr");
        var res = new[] { new CheckResult("cat.system", new Msg("n.cpuload"), Status.Warn, new Msg("d.check_error", "<script>x</script>")) };
        var html = Report.Build(res);
        Assert.Contains("lang=\"fr\"", html);
        Assert.DoesNotContain("<script>x</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Loc.Set("en");
    }
}
