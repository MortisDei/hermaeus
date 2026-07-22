using System.Text.Json;
using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

public sealed class WorkspaceManifestService : IWorkspaceManifestStore
{
    private const string ManifestRelativePath = ".hermaeus/workspace.json";

    // r20 rename: workspaces written before the product rename keep their
    // manifest at the old path. Read it as a fallback; always write to the
    // new path so the workspace migrates the next time it's saved.
    private const string LegacyManifestRelativePath = ".aether/workspace.json";

    public async Task<WorkspaceManifest?> LoadAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(workspaceRoot);
        var path = AgentWorkspaceTools.ResolveSafePath(root, ManifestRelativePath);
        if (!File.Exists(path))
        {
            var legacyPath = AgentWorkspaceTools.ResolveSafePath(root, LegacyManifestRelativePath);
            if (!File.Exists(legacyPath))
                return null;
            path = legacyPath;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<WorkspaceManifest>(json, AgentJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string workspaceRoot, WorkspaceManifest manifest, CancellationToken ct = default)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(workspaceRoot);
        var path = AgentWorkspaceTools.ResolveSafePath(root, ManifestRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicFileWriter.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, AgentJson.Options), ct);
    }
}
