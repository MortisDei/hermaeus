using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Workspace memory backed by the shared IMemoryStore using MemoryScope.Workspace,
/// replacing the per-workspace memory.json files. On first initialize it imports
/// any legacy memory.json files and renames them to memory.json.migrated.
/// </summary>
public sealed class WorkspaceMemoryStore : IAgentWorkspaceMemoryStore
{
    private const string Category = "workspace";

    private readonly IMemoryStore _memories;
    private readonly IKnowledgeRevisionStore _knowledge;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public WorkspaceMemoryStore(
        IMemoryStore memories,
        ISettingsService settings,
        IKnowledgeRevisionStore? knowledge = null)
    {
        _memories = memories;
        _settings = settings;
        _knowledge = knowledge ?? memories as IKnowledgeRevisionStore
            ?? throw new ArgumentException("The memory store must expose knowledge revision writes.", nameof(memories));
    }

    private string AgentRoot
    {
        get
        {
            var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
                : Path.GetFullPath(configured);
            return Path.Combine(root, "agent");
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await _memories.InitializeAsync(ct);
            await ImportLegacyFilesAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    // Kept for scratch/artifact paths that live alongside the old memory files.
    public string GetWorkspaceDirectory(string workspaceRoot)
    {
        var safeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeWorkspaceRoot(workspaceRoot)))).ToLowerInvariant();
        return Path.Combine(AgentRoot, "workspaces", safeHash);
    }

    public async Task<IReadOnlyList<AgentWorkspaceMemoryEntry>> ListAsync(string workspaceRoot, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var normalized = NormalizeWorkspaceRoot(workspaceRoot);
        var rows = await _memories.GetByScopeAsync(MemoryScope.Workspace, normalized, includeArchived: false, ct);
        return rows.Select(ToEntry).OrderByDescending(e => e.UpdatedAt).ToList();
    }

    public async Task<AgentWorkspaceMemoryEntry> UpsertAsync(AgentWorkspaceMemoryEntry entry, CancellationToken ct = default)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        await InitializeAsync(ct);

        entry.WorkspaceRoot = NormalizeWorkspaceRoot(entry.WorkspaceRoot);
        entry.Title = entry.Title.Trim();
        entry.Body = entry.Body.Trim();
        entry.UpdatedAt = DateTime.UtcNow;
        if (entry.CreatedAt == default)
            entry.CreatedAt = entry.UpdatedAt;

        var current = await _memories.GetByIdAsync(entry.Id, ct);
        if (current is null && IsAutomaticWorkspaceProfile(entry))
        {
            var existingProfiles = (await _memories.GetByScopeAsync(
                    MemoryScope.Workspace, entry.WorkspaceRoot, includeArchived: false, ct))
                .Where(IsAutomaticWorkspaceProfile)
                .OrderByDescending(memory => memory.UpdatedAt)
                .ToList();
            var canonical = existingProfiles.FirstOrDefault();
            if (canonical is not null)
            {
                entry.Id = canonical.Id;
                entry.CreatedAt = canonical.CreatedAt;
                current = canonical;
                foreach (var duplicate in existingProfiles.Skip(1))
                {
                    var revision = await _knowledge.GetCurrentRevisionAsync(duplicate.Id, ct);
                    if (revision is not null)
                        await _knowledge.HardDeleteAsync(duplicate.Id, revision.RevisionId, ct);
                }
            }
        }

        var memory = ToMemory(entry);
        if (current is null)
        {
            await _knowledge.CreateAssertionAsync(new KnowledgeRevisionDraft(
                memory,
                TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
                SourceReferences: memory.Source is null ? [] : [memory.Source]), ct);
        }
        else
        {
            var revision = await _knowledge.GetCurrentRevisionAsync(entry.Id, ct)
                ?? throw new InvalidOperationException($"Workspace memory '{entry.Id}' has no current revision.");
            await _knowledge.ReviseAssertionAsync(entry.Id, revision.RevisionId, new KnowledgeRevisionDraft(
                memory,
                TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
                SourceReferences: memory.Source is null ? null : [memory.Source]), ct);
        }
        return entry;
    }

    private static bool IsAutomaticWorkspaceProfile(Memory memory) =>
        string.Equals(memory.Title, "Workspace profile", StringComparison.Ordinal)
        && memory.Tags.Contains("auto", StringComparer.OrdinalIgnoreCase)
        && memory.Tags.Contains("profile", StringComparer.OrdinalIgnoreCase);

    private static bool IsAutomaticWorkspaceProfile(AgentWorkspaceMemoryEntry entry) =>
        string.Equals(entry.Title, "Workspace profile", StringComparison.Ordinal)
        && entry.Tags.Contains("auto", StringComparer.OrdinalIgnoreCase)
        && entry.Tags.Contains("profile", StringComparer.OrdinalIgnoreCase);

    public async Task DeleteAsync(string workspaceRoot, string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var normalized = NormalizeWorkspaceRoot(workspaceRoot);
        var existing = await _memories.GetByIdAsync(id, ct);
        if (existing is { Scope: MemoryScope.Workspace } && string.Equals(existing.ScopeId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            var revision = await _knowledge.GetCurrentRevisionAsync(id, ct);
            if (revision is not null)
                await _knowledge.HardDeleteAsync(id, revision.RevisionId, ct);
        }
    }

    private async Task ImportLegacyFilesAsync(CancellationToken ct)
    {
        var workspacesDir = Path.Combine(AgentRoot, "workspaces");
        if (!Directory.Exists(workspacesDir))
            return;

        foreach (var file in Directory.EnumerateFiles(workspacesDir, "memory.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var entries = JsonSerializer.Deserialize<List<AgentWorkspaceMemoryEntry>>(json, AgentJson.Options) ?? [];
                foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.WorkspaceRoot)))
                {
                    if (await _memories.GetByIdAsync(entry.Id, ct) is not null)
                        continue;
                    entry.WorkspaceRoot = NormalizeWorkspaceRoot(entry.WorkspaceRoot);
                    var memory = ToMemory(entry);
                    await _knowledge.CreateAssertionAsync(new KnowledgeRevisionDraft(
                        memory,
                        TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
                        SourceReferences: memory.Source is null ? [] : [memory.Source]), ct);
                }

                File.Move(file, file + ".migrated", overwrite: true);
            }
            catch
            {
                // Leave unreadable files in place; the agent still works with an empty scope.
            }
        }
    }

    private static Memory ToMemory(AgentWorkspaceMemoryEntry entry) => new()
    {
        Id = entry.Id,
        Scope = MemoryScope.Workspace,
        ScopeId = entry.WorkspaceRoot,
        Title = entry.Title,
        Content = entry.Body,
        Category = Category,
        Tags = entry.Tags.ToList(),
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt,
        Source = new SourceReference(
            ProvenanceKind.Workspace,
            string.IsNullOrWhiteSpace(entry.Title) ? "Workspace memory" : entry.Title,
            Locator: entry.WorkspaceRoot,
            Snippet: entry.Body,
            Timestamp: entry.UpdatedAt,
            EvidenceOrigin: EvidenceOrigin.UserProvided)
    };

    private static AgentWorkspaceMemoryEntry ToEntry(Memory memory) => new()
    {
        Id = memory.Id,
        WorkspaceRoot = memory.ScopeId,
        Title = memory.Title,
        Body = memory.Content,
        Tags = memory.Tags.ToList(),
        CreatedAt = memory.CreatedAt,
        UpdatedAt = memory.UpdatedAt
    };

    private static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        var trimmed = workspaceRoot.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Workspace root is required for workspace memory.");

        return Path.GetFullPath(trimmed);
    }
}
