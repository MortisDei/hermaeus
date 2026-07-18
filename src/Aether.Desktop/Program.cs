using Avalonia;

namespace Aether.Desktop;

class Program
{
    private static string CrashLogPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, fileName);

    [STAThread]
    public static void Main(string[] args)
    {
        // A second instance would write to the same SQLite data root with no
        // cross-process coordination; refuse to start rather than risk it.
        if (!SingleInstanceGuard.TryAcquire())
            return;

        try
        {
            // Global unhandled exception handlers to capture unexpected crashes.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    var msg = ex?.ToString() ?? e.ExceptionObject?.ToString();
                    File.AppendAllText(CrashLogPath("aether_unhandled.log"), $"{DateTime.UtcNow}: UNHANDLED: {msg}\n");
                }
                catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    File.AppendAllText(CrashLogPath("aether_unobserved.log"), $"{DateTime.UtcNow}: UNOBSERVED: {e.Exception}\n");
                }
                catch { }
            };

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstanceGuard.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
