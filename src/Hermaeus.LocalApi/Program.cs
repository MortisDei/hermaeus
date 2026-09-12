using Hermaeus.Composition;
using Hermaeus.Core.Services;
using Hermaeus.LocalApi;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHermaeusCoreServices();
var settingsPath = ReadSettingsPath(args);
if (settingsPath is not null)
    builder.Services.AddSingleton<ISettingsService>(new SettingsService(settingsPath));

var app = builder.Build();

var settingsService = app.Services.GetRequiredService<ISettingsService>();
await settingsService.LoadAsync();
var lifecycle = app.Services.GetRequiredService<IApplicationLifecycleCoordinator>();
var startup = await lifecycle.StartAsync();
if (!startup.Ready)
{
    Console.Error.WriteLine("Hermaeus.LocalApi: shared application startup was incomplete.");
    foreach (var phase in startup.Phases.Where(phase => !phase.Succeeded))
        Console.Error.WriteLine($"  {phase.Name}: {phase.Error}");
    await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
    Environment.Exit(1);
    return;
}

var localApiSettings = settingsService.Settings.LocalApi;
if (!localApiSettings.Enabled)
{
    Console.Error.WriteLine("Hermaeus.LocalApi: LocalApi.Enabled is false in settings. Refusing to serve. Enable it in Settings > Local API first.");
    await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
    Environment.Exit(1);
    return;
}

var port = localApiSettings.Port is > 0 and <= 65535 ? localApiSettings.Port : 39300;
app.Urls.Clear();
app.Urls.Add($"http://127.0.0.1:{port}");

app.UseLocalApiTokenAuth();
app.MapLocalApiEndpoints();

try
{
    await app.RunAsync();
}
finally
{
    var shutdown = await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
    if (!shutdown.Clean)
        Console.Error.WriteLine("Hermaeus.LocalApi: shared application shutdown was incomplete; lifecycle evidence was retained.");
}

static string? ReadSettingsPath(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--settings-path", StringComparison.Ordinal))
            return Path.GetFullPath(args[i + 1]);
    }

    return null;
}

// Exposed for WebApplicationFactory<Program>-based integration tests.
public partial class Program;
