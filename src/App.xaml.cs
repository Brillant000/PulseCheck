using System.Windows;
using System.Windows.Threading;

namespace PulseCheck;

/// <summary>Writes unexpected errors to a local file. Nothing is ever sent anywhere.</summary>
public static class CrashLog
{
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseCheck", "error.log");

    public static void Write(Exception? e, string where)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var ver = typeof(CrashLog).Assembly.GetName().Version;
            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  PulseCheck {ver}  {Environment.OSVersion}  [{where}]{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* the error log must never cause another error */ }
    }
}

public partial class App : Application
{
    int shown;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => CrashLog.Write(ev.ExceptionObject as Exception, "domain");
        TaskScheduler.UnobservedTaskException += (_, ev) => { CrashLog.Write(ev.Exception, "task"); ev.SetObserved(); };
        base.OnStartup(e);
    }

    void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs ev)
    {
        CrashLog.Write(ev.Exception, "ui");
        ev.Handled = true;                       // keep the app alive: the user's work (a scan result) is not lost
        if (++shown > 3) { Shutdown(1); return; } // a loop of errors: stop instead of flooding the user
        try { MessageBox.Show(Loc.T("err.body", CrashLog.FilePath), Loc.T("err.title"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch { }
    }
}
