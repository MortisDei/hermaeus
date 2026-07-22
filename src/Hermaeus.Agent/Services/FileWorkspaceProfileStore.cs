using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

public sealed class FileWorkspaceProfileStore : IWorkspaceProfileStore
{
    private readonly ISettingsService _settings;

    public FileWorkspaceProfileStore(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<WorkspaceProfile?> LoadAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var path = GetProfilePath(workspaceRoot);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<WorkspaceProfile>(json, AgentJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public async Task<WorkspaceProfile> SaveAsync(WorkspaceProfile profile, CancellationToken ct = default)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        profile.WorkspaceRoot = NormalizeWorkspaceRoot(profile.WorkspaceRoot);
        profile.UpdatedAt = DateTime.UtcNow;

        var path = GetProfilePath(profile.WorkspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicFileWriter.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, AgentJson.Options), ct);
        return profile;
    }

    private string GetProfilePath(string workspaceRoot)
    {
        var root = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
        var dataRoot = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(root);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeWorkspaceRoot(workspaceRoot)))).ToLowerInvariant();
        return Path.Combine(dataRoot, "agent", "workspaces", hash, "profile.json");
    }

    private static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        var trimmed = workspaceRoot.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Workspace root is required.");

        return Path.GetFullPath(trimmed);
    }
}
