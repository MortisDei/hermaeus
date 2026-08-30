using System.Text.Json;
using System.Text;
using Avalonia;

namespace Hermaeus.Desktop;

class Program
{
    internal static PackageIntegrationLaunch? PackageIntegrationLaunch { get; private set; }
    private const long MaxCrashLogBytes = 512 * 1024;
    private const int MaxCrashEntryCharacters = 128 * 1024;
    private static readonly object CrashLogLock = new();

    // r19 1.3: crash logs must land where the user's other logs and data
    // live, not next to the executable (unwritable in a packaged install,
    // and nobody thinks to look there). Program.Main runs before DI exists,
    // so the data root is resolved the same minimal way
    // AppLifecycleJournalService.JournalPath and
    // NativeKokoroVoiceProvider.ResolveAssetsDirectory already duplicate it:
    // read settings.json directly if present, else the LocalApplicationData
    // fallback.
    private static string CrashLogPath(string fileName)
    {
        var root = ResolveDataRootForCrashLog();
        var dir = Path.Combine(root, "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, fileName);
    }

    private static void AppendCrashLog(string fileName, string marker, string message)
        => AppendCrashLogAtPath(CrashLogPath(fileName), marker, message);

    internal static void AppendCrashLogAtPath(string path, string marker, string message)
    {
        try
        {
            var bounded = message.Length <= MaxCrashEntryCharacters
                ? message : message[..MaxCrashEntryCharacters] + "\n[crash detail truncated]";
            var bytes = Encoding.UTF8.GetBytes($"{DateTime.UtcNow}: {marker}: {bounded}\n");
            lock (CrashLogLock)
            {
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaxCrashLogBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
        catch { }
    }

    private static string ResolveDataRootForCrashLog()
    {
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus");
        try
        {
            var settingsPath = Path.Combine(defaultDir, "settings.json");
            if (!File.Exists(settingsPath))
                return defaultDir;

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.TryGetProperty("DataManagement", out var dm)
                && dm.TryGetProperty("DataRootDirectory", out var dr)
                && dr.ValueKind == JsonValueKind.String)
            {
                var configured = dr.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(configured))
                    return Path.GetFullPath(configured);
            }
        }
        catch
        {
            // Malformed/unreadable settings.json at this bootstrap point must
            // never block crash logging itself; fall back to the default root.
        }
        return defaultDir;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        PackageIntegrationLaunch = PackageIntegrationAction.Resolve(Environment.ProcessPath);

        // A second instance would write to the same SQLite data root with no
        // cross-process coordination; refuse to start rather than risk it.
        var ownsInstance = SingleInstanceGuard.TryAcquire();
        if (!ownsInstance && PackageIntegrationLaunch is null)
            return;

        if (PackageIntegrationLaunch is not null)
            PackageIntegrationLaunch = PackageIntegrationLaunch with { CanRun = ownsInstance };

        try
        {
            // Global unhandled exception handlers to capture unexpected crashes.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var msg = ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown unhandled exception";
                AppendCrashLog("hermaeus_unhandled.log", "UNHANDLED", msg);
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppendCrashLog("hermaeus_unobserved.log", "UNOBSERVED", e.Exception.ToString());
            };

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (ownsInstance)
                SingleInstanceGuard.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "hermaeus" })
            .WithInterFont()
            .LogToTrace();
}
