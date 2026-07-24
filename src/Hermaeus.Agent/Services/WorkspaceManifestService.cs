using System.Text.Json;
using System.Text.Json.Nodes;
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
            return ParseManifest(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the manifest's <c>policy</c> object separately from the rest
    /// of the manifest (r23 3.1): a malformed policy - bad shape, a negative
    /// cap - is rejected as a whole, with a warning, but must never take the
    /// entire manifest down with it. Half-parsed security config is worse
    /// than none, because the user would trust a boundary that is not
    /// actually there.
    /// </summary>
    private static WorkspaceManifest? ParseManifest(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject;
        if (node is null)
            return JsonSerializer.Deserialize<WorkspaceManifest>(json, AgentJson.Options);

        WorkspacePolicy? policy = null;
        string? policyWarning = null;
        if (node.TryGetPropertyValue("policy", out var policyNode) && policyNode is not null)
        {
            node.Remove("policy");
            try
            {
                var candidate = policyNode.Deserialize<WorkspacePolicy>(AgentJson.Options);
                var validationError = candidate is null ? "policy could not be parsed" : ValidatePolicy(candidate);
                if (validationError is null)
                    policy = candidate;
                else
                    policyWarning = $"Workspace policy is malformed and was ignored ({validationError}); the workspace is unrestricted until it is fixed.";
            }
            catch (JsonException ex)
            {
                policyWarning = $"Workspace policy is malformed and was ignored ({ex.Message}); the workspace is unrestricted until it is fixed.";
            }
        }

        var manifest = node.Deserialize<WorkspaceManifest>(AgentJson.Options) ?? new WorkspaceManifest();
        manifest.Policy = policy;
        manifest.PolicyRejectionWarning = policyWarning;
        return manifest;
    }

    private static string? ValidatePolicy(WorkspacePolicy policy)
    {
        if (policy.MaxFileReadsPerTask < 0)
            return "maxFileReadsPerTask cannot be negative";

        foreach (var pattern in policy.ReadAllow.Concat(policy.WriteAllow).Concat(policy.Never))
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "a policy glob pattern cannot be blank";
            try { AgentWorkspaceTools.GlobToRegex(pattern); }
            catch { return $"'{pattern}' is not a valid glob pattern"; }
        }

        return null;
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
