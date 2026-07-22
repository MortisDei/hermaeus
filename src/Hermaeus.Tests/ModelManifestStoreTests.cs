using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

// r13 03-hugging-face.md 3.1: model provenance manifest round-trip.
public sealed class ModelManifestStoreTests
{
    [Fact]
    public async Task Upsert_then_load_round_trips_an_entry_keyed_by_file_path()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var store = new ModelManifestStore(settings);

        await store.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "unsloth/gemma-3-12b-it-GGUF",
            RepoFile = "gemma-3-12b-it-Q4_K_M.gguf",
            Sha256 = "abc123",
            SizeBytes = 4,
            Source = "hf-browser"
        });

        var found = await store.FindAsync(modelPath);
        Assert.NotNull(found);
        Assert.Equal("unsloth/gemma-3-12b-it-GGUF", found!.RepoId);
        Assert.Equal("abc123", found.Sha256);
    }

    [Fact]
    public async Task Upsert_is_case_insensitive_on_the_file_path_key_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("Model.gguf");
        File.WriteAllText(modelPath, "fake");
        var store = new ModelManifestStore(settings);

        await store.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "a/b", Source = "manual" });
        var found = await store.FindAsync(modelPath.ToUpperInvariant());

        Assert.NotNull(found);
    }

    [Fact]
    public async Task Upsert_replaces_the_existing_entry_for_the_same_path_instead_of_duplicating()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var store = new ModelManifestStore(settings);

        await store.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "a/b", Sha256 = "old", Source = "manual" });
        await store.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "a/b", Sha256 = "new", Source = "manual" });

        var all = await store.LoadAsync();
        Assert.Single(all);
        Assert.Equal("new", all[0].Sha256);
    }

    [Fact]
    public async Task Loading_prunes_an_entry_whose_file_was_deleted_without_erroring()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var store = new ModelManifestStore(settings);
        await store.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "a/b", Source = "manual" });

        File.Delete(modelPath);
        var all = await store.LoadAsync();

        Assert.Empty(all);
    }

    [Fact]
    public async Task Manifest_file_lives_under_the_data_root_and_is_swept_by_DataRootManifest()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var store = new ModelManifestStore(settings);
        await store.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "a/b", Source = "manual" });

        var dataRoot = SettingsService.ResolveDataRoot(settings.Settings);
        var swept = DataRootManifest.EnumerateAll(dataRoot);

        Assert.Contains(swept, entry => Path.GetFileName(entry.SourcePath) == "model-manifest.json");
    }
}
