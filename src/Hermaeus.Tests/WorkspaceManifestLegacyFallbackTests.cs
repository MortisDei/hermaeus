using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r20 rename, doc 02.8: workspaces written before the product rename keep
/// their manifest at .aether/workspace.json. LoadAsync must still find it;
/// SaveAsync must always write to the new .hermaeus/workspace.json path.
/// </summary>
public sealed class WorkspaceManifestLegacyFallbackTests
{
    [Fact]
    public async Task LoadAsync_falls_back_to_legacy_aether_manifest_path()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        var legacyDir = Path.Combine(workspace, ".aether");
        Directory.CreateDirectory(legacyDir);
        var manifest = new WorkspaceManifest { PreferredModelId = "legacy-model" };
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "workspace.json"), JsonSerializer.Serialize(manifest, AgentJson.Options));

        var store = new WorkspaceManifestService();
        var loaded = await store.LoadAsync(workspace);

        Assert.NotNull(loaded);
        Assert.Equal("legacy-model", loaded!.PreferredModelId);
    }

    [Fact]
    public async Task SaveAsync_always_writes_the_new_hermaeus_manifest_path()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var store = new WorkspaceManifestService();
        await store.SaveAsync(workspace, new WorkspaceManifest { PreferredModelId = "current-model" });

        Assert.True(File.Exists(Path.Combine(workspace, ".hermaeus", "workspace.json")));
        Assert.False(File.Exists(Path.Combine(workspace, ".aether", "workspace.json")));
    }

    [Fact]
    public async Task LoadAsync_prefers_new_manifest_path_when_both_exist()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        var legacyDir = Path.Combine(workspace, ".aether");
        var newDir = Path.Combine(workspace, ".hermaeus");
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(newDir);
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "workspace.json"),
            JsonSerializer.Serialize(new WorkspaceManifest { PreferredModelId = "legacy-model" }, AgentJson.Options));
        await File.WriteAllTextAsync(Path.Combine(newDir, "workspace.json"),
            JsonSerializer.Serialize(new WorkspaceManifest { PreferredModelId = "current-model" }, AgentJson.Options));

        var store = new WorkspaceManifestService();
        var loaded = await store.LoadAsync(workspace);

        Assert.NotNull(loaded);
        Assert.Equal("current-model", loaded!.PreferredModelId);
    }
}
