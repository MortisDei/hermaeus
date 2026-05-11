using System.Text;
using System.Text.Json;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class SecretStore : ISecretStore
{
    private readonly ISettingsService _settings;
    private const string Prefix = "secret:";

    public SecretStore(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool IsReference(string value) => value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public async Task<string> StoreAsync(string name, string secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (IsReference(secret)) return secret;

        var all = await LoadAsync(ct);
        all[name] = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        var path = PathForStore();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), ct);
        TryRestrictPermissions(path);
        return $"{Prefix}{name}";
    }

    public async Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default)
    {
        if (!IsReference(valueOrReference)) return valueOrReference;
        var key = valueOrReference[Prefix.Length..];
        var all = await LoadAsync(ct);
        if (!all.TryGetValue(key, out var encoded)) return string.Empty;
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        var path = PathForStore();
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    private string PathForStore()
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(root, "secrets.local.json");
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}
