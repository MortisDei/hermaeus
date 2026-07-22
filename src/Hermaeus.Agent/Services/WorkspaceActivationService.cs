using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

public sealed class WorkspaceActivationService : IWorkspaceActivationService
{
    private readonly IWorkspaceManifestStore _manifests;
    private readonly IWorkspaceProfileStore _profiles;

    public WorkspaceActivationService(IWorkspaceManifestStore manifests, IWorkspaceProfileStore profiles)
    {
        _manifests = manifests;
        _profiles = profiles;
    }

    public async Task<WorkspaceActivation> ActivateAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var manifest = await _manifests.LoadAsync(workspaceRoot, ct);
        if (manifest is not null)
        {
            return new WorkspaceActivation(
                string.IsNullOrWhiteSpace(manifest.PreferredModelId) ? null : manifest.PreferredModelId,
                string.IsNullOrWhiteSpace(manifest.PreferredEmbeddingModelId) ? null : manifest.PreferredEmbeddingModelId,
                manifest.LinkedRagDatasetId,
                manifest.InstructionPaths,
                FromManifest: true,
                VoiceProfileName: string.IsNullOrWhiteSpace(manifest.VoiceProfileName) ? null : manifest.VoiceProfileName);
        }

        var profile = await _profiles.LoadAsync(workspaceRoot, ct);
        return new WorkspaceActivation(
            string.IsNullOrWhiteSpace(profile?.PreferredModelId) ? null : profile.PreferredModelId,
            string.IsNullOrWhiteSpace(profile?.PreferredEmbeddingModelId) ? null : profile.PreferredEmbeddingModelId,
            profile?.LinkedRagDatasetId,
            [],
            FromManifest: false);
    }
}
