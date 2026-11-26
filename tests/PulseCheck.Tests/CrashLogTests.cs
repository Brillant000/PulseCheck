using System;
using System.IO;
using PulseCheck;
using Xunit;

namespace PulseCheck.Tests;

public class CrashLogTests
{
    [Fact]
    public void An_error_is_written_locally_with_its_details_and_never_throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pulsecheck-test-" + Guid.NewGuid().ToString("N"));
        var old = CrashLog.FilePath;
        try
        {
            CrashLog.FilePath = Path.Combine(dir, "sub", "error.log");
            CrashLog.Write(new InvalidOperationException("boom"), "unit");
            var text = File.ReadAllText(CrashLog.FilePath);
            Assert.Contains("boom", text); Assert.Contains("[unit]", text); Assert.Contains("PulseCheck", text);

            CrashLog.Write(null, "null");                         // a null error must not throw either
            CrashLog.FilePath = "Z:\\does\\not\\exist\\<>|\\error.log";
            CrashLog.Write(new Exception("x"), "bad path");       // an unwritable path must not throw
        }
        finally { CrashLog.FilePath = old; try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("en")] [InlineData("fr")] [InlineData("es")] [InlineData("de")]
    public void The_error_message_is_translated_and_shows_where_details_were_saved(string code)
    {
        Loc.Set(code);
        Assert.NotEqual("err.body", Loc.T("err.body", @"C:\x\error.log"));
        Assert.Contains(@"C:\x\error.log", Loc.T("err.body", @"C:\x\error.log"));
        Assert.NotEqual("err.title", Loc.T("err.title"));
        Loc.Set("en");
    }
}
