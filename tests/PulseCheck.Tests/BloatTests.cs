using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

/// <summary>
/// Regression for a real report: the app offered to remove "NortonSecurity" as a Store app, but it is a classic
/// program (registered from Program Files, or a leftover with no folder), so Remove-AppxPackage always failed.
/// </summary>
public class BloatTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe", true)]
    [InlineData(@"c:\program files\windowsapps\King.com.CandyCrushSaga_1.0.0.0_x64__kgqvnymyfvs32", true)]
    [InlineData(@"C:\Program Files\Norton Security\Engine\22.23.6.5", false)]   // the exact case seen on a real PC
    [InlineData(@"C:\Program Files\McAfee\WebAdvisor", false)]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Thing", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Only_real_Store_packages_are_offered_for_removal(string? root, bool expected)
        => Assert.Equal(expected, BloatFix.IsStoreRoot(root));

    [Fact]
    public void The_fix_candidates_and_the_scan_warning_come_from_the_same_list()
    {
        var names = BloatFix.StoreBloatPackages();
        Assert.Equal(names, BloatFix.Candidates().Select(c => c.FullName).ToList());
        Assert.All(names, n => Assert.True(BloatFix.IsSafeName(n)));
    }

    [Fact]
    public void Candidates_never_include_a_package_that_Windows_does_not_list_for_the_user_when_it_is_not_in_WindowsApps()
    {
        // Whatever this machine has, every candidate must come from a WindowsApps root (checked against the registry).
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        foreach (var n in BloatFix.StoreBloatPackages())
        {
            using var e = k!.OpenSubKey(n);
            Assert.True(BloatFix.IsStoreRoot(e?.GetValue("PackageRootFolder") as string), n);
        }
    }
}
