using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public sealed class FileAgentWorkspaceMemoryStore : IAgentWorkspaceMemoryStore
{
    private readonly ISettingsService _settings;

    public FileAgentWorkspaceMemoryStore(ISettingsService settings)
    {
        _settings = settings;
    }

    private string AgentRoot
    {
        get
        {
            var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
                : Path.GetFullPath(configured);
            return Path.Combine(root, "agent");
        }
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.Combine(AgentRoot, "workspaces"));
        return Task.CompletedTask;
    }

    public string GetWorkspaceDirectory(string workspaceRoot)
    {
        var safeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeWorkspaceRoot(workspaceRoot)))).ToLowerInvariant();
        return Path.Combine(AgentRoot, "workspaces", safeHash);
    }

    public async Task<IReadOnlyList<AgentWorkspaceMemoryEntry>> ListAsync(string workspaceRoot, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var path = Path.Combine(GetWorkspaceDirectory(workspaceRoot), "memory.json");
        if (!File.Exists(path))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<AgentWorkspaceMemoryEntry>>(json, AgentJson.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<AgentWorkspaceMemoryEntry> UpsertAsync(AgentWorkspaceMemoryEntry entry, CancellationToken ct = default)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        entry.WorkspaceRoot = NormalizeWorkspaceRoot(entry.WorkspaceRoot);
        entry.Title = entry.Title.Trim();
        entry.Body = entry.Body.Trim();
        entry.UpdatedAt = DateTime.UtcNow;
        if (entry.CreatedAt == default)
            entry.CreatedAt = entry.UpdatedAt;

        var workspaceDir = GetWorkspaceDirectory(entry.WorkspaceRoot);
        Directory.CreateDirectory(workspaceDir);

        var all = (await ListAsync(entry.WorkspaceRoot, ct)).ToList();
        var index = all.FindIndex(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            all[index] = entry;
        else
            all.Add(entry);

        var path = Path.Combine(workspaceDir, "memory.json");
        await AtomicFileWriter.WriteAllTextAsync(path, JsonSerializer.Serialize(all.OrderByDescending(x => x.UpdatedAt).ToList(), AgentJson.Options), ct);
        return entry;
    }

    public async Task DeleteAsync(string workspaceRoot, string id, CancellationToken ct = default)
    {
        var normalized = NormalizeWorkspaceRoot(workspaceRoot);
        var workspaceDir = GetWorkspaceDirectory(normalized);
        var path = Path.Combine(workspaceDir, "memory.json");
        if (!File.Exists(path))
            return;

        var items = (await ListAsync(normalized, ct)).Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0)
        {
            try { File.Delete(path); }
            catch { }
            return;
        }

        await AtomicFileWriter.WriteAllTextAsync(path, JsonSerializer.Serialize(items.OrderByDescending(x => x.UpdatedAt).ToList(), AgentJson.Options), ct);
    }

    private static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        var trimmed = workspaceRoot.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Workspace root is required for workspace memory.");

        return Path.GetFullPath(trimmed);
    }
}
