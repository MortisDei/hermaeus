using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public interface IAgentScenarioStore
{
    /// <summary>
    /// Built-in scenarios (shipped next to the binaries) plus user scenarios
    /// from {DataRoot}/agent-scenarios. A user scenario whose id matches a
    /// built-in replaces it. Never throws for a malformed scenario; it is
    /// skipped and reported via <paramref name="warnings"/>.
    /// </summary>
    Task<IReadOnlyList<AgentScenario>> LoadAllAsync(ICollection<string>? warnings = null, CancellationToken ct = default);
}

public sealed class AgentScenarioStore : IAgentScenarioStore
{
    private readonly ISettingsService _settings;
    private readonly string _builtInRoot;

    public AgentScenarioStore(ISettingsService settings)
        : this(settings, Path.Combine(AppContext.BaseDirectory, "agent-scenarios"))
    {
    }

    /// <summary>Test seam: only a public constructor is visible to DI's constructor resolution, so this never affects production wiring.</summary>
    internal AgentScenarioStore(ISettingsService settings, string builtInRoot)
    {
        _settings = settings;
        _builtInRoot = builtInRoot;
    }

    public async Task<IReadOnlyList<AgentScenario>> LoadAllAsync(ICollection<string>? warnings = null, CancellationToken ct = default)
    {
        var sink = warnings ?? new List<string>();
        var byId = new Dictionary<string, AgentScenario>(StringComparer.OrdinalIgnoreCase);

        await LoadRootAsync(_builtInRoot, isBuiltIn: true, byId, sink, ct);
        await LoadRootAsync(UserRoot(), isBuiltIn: false, byId, sink, ct);

        return byId.Values.OrderBy(s => s.Manifest.Id, StringComparer.Ordinal).ToList();
    }

    private string UserRoot()
    {
        var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "agent-scenarios");
    }

    private static async Task LoadRootAsync(
        string root,
        bool isBuiltIn,
        Dictionary<string, AgentScenario> byId,
        ICollection<string> warnings,
        CancellationToken ct)
    {
        if (!Directory.Exists(root))
            return;

        var folders = Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal);
        var seenInThisRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(folder, "scenario.json");
            if (!File.Exists(manifestPath))
                continue;

            AgentScenarioManifest manifest;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, ct);
                manifest = JsonSerializer.Deserialize<AgentScenarioManifest>(json, AgentJson.Options)
                    ?? throw new JsonException("Empty scenario manifest.");
            }
            catch (JsonException ex)
            {
                warnings.Add($"{folder}: failed to load scenario.json ({ex.Message}).");
                continue;
            }

            if (manifest.SchemaVersion > 1)
            {
                warnings.Add($"{folder}: schema_version {manifest.SchemaVersion} is not supported by this build.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(manifest.Id))
                manifest.Id = Path.GetFileName(folder).ToLowerInvariant();
            manifest.MaxSteps = Math.Clamp(manifest.MaxSteps, 1, 15);

            if (!seenInThisRoot.Add(manifest.Id))
            {
                warnings.Add($"{folder}: duplicate scenario id '{manifest.Id}' within the same root; skipped.");
                continue;
            }

            foreach (var warning in AgentScenarioManifestValidator.Validate(manifest, manifest.Id))
                warnings.Add(warning);

            var workspaceDir = Path.Combine(folder, "workspace");
            byId[manifest.Id] = new AgentScenario(manifest, folder, workspaceDir, isBuiltIn);
        }
    }
}
