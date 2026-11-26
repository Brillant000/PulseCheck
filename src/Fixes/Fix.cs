using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace PulseCheck;

/// <summary>Result of applying a fix. <see cref="Undo"/> is null when the change cannot be reverted.</summary>
public sealed record FixOutcome(bool Ok, Msg? Error = null, Action? Undo = null, Msg? Note = null);

/// <summary>
/// A change PulseCheck can make for the user. Rules: it is only ever applied after the user has seen
/// <see cref="Plan"/> and confirmed, it touches nothing beyond what the plan says, and it is reversible
/// whenever that is technically possible.
/// </summary>
public abstract class Fix
{
    public abstract string Id { get; }
    public abstract Msg Title { get; }
    public virtual bool Reversible => true;
    public abstract IReadOnlyList<Msg> Plan(HardwareInfo? hw);
    public virtual UIElement? ExtraUi(HardwareInfo? hw) => null;
    public virtual bool Ready => true;
    public abstract FixOutcome Apply(HardwareInfo? hw);
}

/// <summary>Registry access for fixes. Tests redirect it under a scratch key through <see cref="Prefix"/>.</summary>
public static class Reg
{
    public static string Prefix = "";

    public static RegistryKey? Open(string path, bool write = false) => Registry.CurrentUser.OpenSubKey(Prefix + path, write);
    public static RegistryKey Create(string path) => Registry.CurrentUser.CreateSubKey(Prefix + path);
}

/// <summary>Local record of every change PulseCheck makes, so nothing happens silently.</summary>
public static class FixLog
{
    public static bool Enabled = true;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseCheck", "changes.log");

    public static void Write(string action, string id, string detail = "")
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {action,-6} {id}  {detail}{Environment.NewLine}");
        }
        catch { /* logging must never break a fix */ }
    }
}

internal static class Proc
{
    public static (int code, string output) Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return (p.HasExited ? p.ExitCode : -1, o);
        }
        catch { return (-1, ""); }
    }
}
