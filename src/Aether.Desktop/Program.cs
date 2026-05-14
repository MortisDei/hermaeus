using Avalonia;

namespace Aether.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Global unhandled exception handlers to capture unexpected crashes.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                var msg = ex?.ToString() ?? e.ExceptionObject?.ToString();
                System.IO.File.AppendAllText("aether_unhandled.log", $"{DateTime.UtcNow}: UNHANDLED: {msg}\n");
            }
            catch { }
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                System.IO.File.AppendAllText("aether_unobserved.log", $"{DateTime.UtcNow}: UNOBSERVED: {e.Exception}\n");
            }
            catch { }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
