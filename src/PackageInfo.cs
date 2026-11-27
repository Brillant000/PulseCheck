using System.Runtime.InteropServices;
using System.Text;

namespace PulseCheck;

/// <summary>
/// Knows whether PulseCheck runs from its Microsoft Store (MSIX) package.
/// In a package, Windows keeps registry changes to existing HKCU values in a private copy of the registry that the
/// rest of Windows never sees (verified on Windows 11 builds 26100 and 26300, even with write virtualization turned
/// off in the manifest). A registry-based Fix would then report "done" without changing anything, so in a package
/// those fixes are replaced by the button that opens the right Windows settings page.
/// </summary>
internal static class PackageInfo
{
    const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFamilyName(ref int length, StringBuilder? name);

    static bool? detected;
    /// <summary>Tests set this to simulate either mode.</summary>
    public static bool? Override;

    public static bool IsPackaged
    {
        get
        {
            if (Override is bool o) return o;
            if (detected is bool d) return d;
            try { int len = 0; detected = GetCurrentPackageFullName(ref len, null) != AppModelErrorNoPackage; }
            catch { detected = false; }
            return detected.Value;
        }
    }

    /// <summary>Fixes that write existing registry values: they cannot take effect from inside a package.</summary>
    public static readonly IReadOnlySet<string> RegistryFixes =
        new HashSet<string> { "game_capture", "game_mode", "proxy", "startup" };

    /// <summary>True when this fix must be offered as "Open settings" instead of "Fix" in the current mode.</summary>
    public static bool Blocks(string? fixId) => fixId != null && IsPackaged && RegistryFixes.Contains(fixId);

    /// <summary>The real location of a file under %LOCALAPPDATA% (a packaged app's writes land in its private cache).</summary>
    public static string VisiblePath(string nominalPath)
    {
        if (!IsPackaged) return nominalPath;
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!nominalPath.StartsWith(local, StringComparison.OrdinalIgnoreCase)) return nominalPath;
            int len = 0; GetCurrentPackageFamilyName(ref len, null);
            var sb = new StringBuilder(len);
            if (len == 0 || GetCurrentPackageFamilyName(ref len, sb) != 0) return nominalPath;
            return Path.Combine(local, "Packages", sb.ToString(), "LocalCache", "Local", nominalPath[local.Length..].TrimStart('\\'));
        }
        catch { return nominalPath; }
    }
}
