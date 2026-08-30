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
await app.Services.GetRequiredService<IMemoryStore>().InitializeAsync();
await app.Services.GetRequiredService<SqliteRagStore>().InitializeAsync();

var localApiSettings = settingsService.Settings.LocalApi;
if (!localApiSettings.Enabled)
{
    Console.Error.WriteLine("Hermaeus.LocalApi: LocalApi.Enabled is false in settings. Refusing to serve. Enable it in Settings > Local API first.");
    Environment.Exit(1);
    return;
}

var port = localApiSettings.Port is > 0 and <= 65535 ? localApiSettings.Port : 39300;
app.Urls.Clear();
app.Urls.Add($"http://127.0.0.1:{port}");

app.UseLocalApiTokenAuth();
app.MapLocalApiEndpoints();

await app.RunAsync();

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
