using Aether.Rag.Storage;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

// r13 03-hugging-face.md 3.2: the privacy audit's "0 configured outbound destinations" claim
// must stay honest once a model is linked to a Hugging Face repo.
public sealed class PrivacyAuditHuggingFaceTests
{
    private static PrivacyAuditService NewAudit(SettingsService settings, ModelManifestStore? manifest) =>
        new(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings), modelManifest: manifest);

    [Fact]
    public async Task No_manifest_entries_means_no_hugging_face_item_and_no_extra_count()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var manifest = new ModelManifestStore(settings);
        var audit = NewAudit(settings, manifest);

        var items = await audit.ScanAsync();
        var count = await audit.CountOutboundDestinationsAsync();

        Assert.DoesNotContain(items, i => i.Name.Contains("Hugging Face", StringComparison.Ordinal));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_repo_linked_model_adds_the_disclosure_item_and_the_count()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Source = "hf-browser" });
        var audit = NewAudit(settings, manifest);

        var items = await audit.ScanAsync();
        var count = await audit.CountOutboundDestinationsAsync();

        Assert.Contains(items, i => i.Name.Contains("Hugging Face", StringComparison.Ordinal));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_manifest_entry_with_no_repo_id_does_not_count_as_linked()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "", Source = "manual" });
        var audit = NewAudit(settings, manifest);

        var items = await audit.ScanAsync();
        var count = await audit.CountOutboundDestinationsAsync();

        Assert.DoesNotContain(items, i => i.Name.Contains("Hugging Face", StringComparison.Ordinal));
        Assert.Equal(0, count);
    }
}
