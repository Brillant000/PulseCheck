using System.Globalization;
using System.Management;
using System.Text.Json;

namespace PulseCheck;

public enum Status { Ok, Warn, Fail, Info }

/// <summary>A translatable message: a resource key plus format arguments.</summary>
public sealed record Msg(string Key, params object[] Args)
{
    public string Text => Loc.T(Key, Args);
    public override string ToString() => Text;
}

public sealed record CheckResult(string Category, Msg Name, Status Status, Msg Detail, Msg? Fix = null);

/// <summary>Tiny localisation layer: flat JSON dictionaries embedded in the assembly.</summary>
public static class Loc
{
    public static readonly string[] Codes = { "en", "fr", "es", "de" };
    public static readonly Dictionary<string, string> Names = new()
    {
        ["en"] = "English", ["fr"] = "Français", ["es"] = "Español", ["de"] = "Deutsch",
    };

    static readonly Dictionary<string, string> en = Load("en");
    static Dictionary<string, string> cur = en;
    public static string Current { get; private set; } = "en";

    static Dictionary<string, string> Load(string code)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream($"PulseCheck.Lang.{code}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s)!;
    }

    public static void Set(string code)
    {
        if (!Codes.Contains(code)) code = "en";
        Current = code;
        cur = code == "en" ? en : Load(code);
    }

    public static string Detect()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Codes.Contains(two) ? two : "en";
    }

    public static string T(string key, params object[] args)
    {
        if (!cur.TryGetValue(key, out var s) && !en.TryGetValue(key, out s)) return key;
        if (args.Length == 0) return s;
        try { return string.Format(s, args); } catch (FormatException) { return s; }
    }
}

/// <summary>WMI helpers. Every query is failure-tolerant: errors yield an empty list.</summary>
public static class Wmi
{
    public static List<ManagementBaseObject> Q(string query, string scope = @"root\cimv2")
    {
        var list = new List<ManagementBaseObject>();
        try
        {
            using var s = new ManagementObjectSearcher(scope, query);
            foreach (var o in s.Get()) list.Add(o);
        }
        catch { }
        return list;
    }

    public static T G<T>(ManagementBaseObject? o, string prop)
    {
        try
        {
            var v = o?[prop];
            if (v == null) return default!;
            return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture);
        }
        catch { return default!; }
    }
}
