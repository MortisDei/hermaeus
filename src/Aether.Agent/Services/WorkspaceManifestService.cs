using System.Text.Json;
using Aether.Agent.Models;

namespace Aether.Agent.Services;

public sealed class WorkspaceManifestService : IWorkspaceManifestStore
{
    private const string ManifestRelativePath = ".aether/workspace.json";

    public async Task<WorkspaceManifest?> LoadAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(workspaceRoot);
        var path = AgentWorkspaceTools.ResolveSafePath(root, ManifestRelativePath);
        if (!File.Exists(path))
            return null;

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
