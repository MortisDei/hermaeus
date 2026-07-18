using Aether.Core.Models;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

// r13 02-model-library.md 2.6: flat-folder organizer. Plan is pure; execution touches disk
// and settings only after a preview+confirm gate the tests below don't exercise directly.
public sealed class ModelFolderOrganizerTests
{
    [Fact]
    public void Plan_moves_a_nested_hub_cache_file_and_captures_provenance()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "models--unsloth--gemma-3-12b-it-GGUF", "snapshots", "abc123");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "gemma-3-12b-it-Q4_K_M.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(file, Assert.Single(move.SourcePaths));
        Assert.Equal(Path.Combine(modelsDir, "LLM", "gemma-3-12b-it-Q4_K_M.gguf"), Assert.Single(move.DestinationPaths));
        Assert.Equal("unsloth", move.HubRepoOrg);
        Assert.Equal("gemma-3-12b-it-GGUF", move.HubRepoName);
        Assert.Equal(1, plan.ProvenanceCount);
    }

    [Fact]
    public void Plan_leaves_a_file_already_directly_under_LLM_alone()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        var file = Path.Combine(llmDir, "already-flat.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Skips);
    }

    [Fact]
    public void Plan_skips_a_name_collision_at_the_destination_without_overwriting()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        var existing = Path.Combine(llmDir, "model.gguf");
        File.WriteAllText(existing, "already here");

        var nested = Path.Combine(modelsDir, "hub", "nested");
        Directory.CreateDirectory(nested);
        var incoming = Path.Combine(nested, "model.gguf");
        File.WriteAllText(incoming, "incoming");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [incoming]);

        Assert.Empty(plan.Moves);
        var skip = Assert.Single(plan.Skips);
        Assert.Equal(incoming, skip.SourcePath);
    }

    [Fact]
    public void Plan_moves_multipart_sets_together_or_not_at_all()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "big-model");
        Directory.CreateDirectory(nested);
        var part1 = Path.Combine(nested, "big-model-00001-of-00002.gguf");
        var part2 = Path.Combine(nested, "big-model-00002-of-00002.gguf");
        File.WriteAllText(part1, "part1");
        File.WriteAllText(part2, "part2");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [part1, part2]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(2, move.SourcePaths.Count);
        Assert.Contains(part1, move.SourcePaths);
        Assert.Contains(part2, move.SourcePaths);
    }

    [Fact]
    public void Plan_moves_multipart_set_collision_atomically_skips_both_parts()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        File.WriteAllText(Path.Combine(llmDir, "big-model-00001-of-00002.gguf"), "existing");

        var nested = Path.Combine(modelsDir, "hub", "big-model");
        Directory.CreateDirectory(nested);
        var part1 = Path.Combine(nested, "big-model-00001-of-00002.gguf");
        var part2 = Path.Combine(nested, "big-model-00002-of-00002.gguf");
        File.WriteAllText(part1, "part1");
        File.WriteAllText(part2, "part2");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [part1, part2]);

        Assert.Empty(plan.Moves);
        Assert.Equal(2, plan.Skips.Count);
    }

    [Fact]
    public void Plan_moves_root_level_files_into_LLM_by_default()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        Directory.CreateDirectory(modelsDir);
        var file = Path.Combine(modelsDir, "root-level.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(Path.Combine(modelsDir, "LLM", "root-level.gguf"), Assert.Single(move.DestinationPaths));
    }

    [Fact]
    public void Plan_can_opt_out_of_moving_root_level_files()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        Directory.CreateDirectory(modelsDir);
        var file = Path.Combine(modelsDir, "root-level.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file], moveRootLevelFiles: false);

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Skips);
    }

    [Fact]
    public void Plan_never_renames_files()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "nested");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "weird name (v2).gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal("weird name (v2).gguf", Path.GetFileName(move.DestinationPaths[0]));
    }

    [Fact]
    public async Task ExecuteAsync_moves_the_file_and_settings_object_is_saved()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "nested");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "model.gguf");
        File.WriteAllText(source, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [source]);
        var result = await ModelFolderOrganizer.ExecuteAsync(plan, settings);

        Assert.Single(result.Moved);
        Assert.Empty(result.Failed);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(modelsDir, "LLM", "model.gguf")));
    }

    [Fact]
    public async Task ExecuteAsync_writes_a_migration_manifest_entry_for_a_hub_cache_move()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "models--unsloth--gemma-3-12b-it-GGUF", "snapshots", "abc123");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "gemma.gguf");
        File.WriteAllText(source, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [source]);
        var manifestStore = new ModelManifestStore(settings);
        await ModelFolderOrganizer.ExecuteAsync(plan, settings, manifestStore);

        var destination = Path.Combine(modelsDir, "LLM", "gemma.gguf");
        var entry = await manifestStore.FindAsync(destination);
        Assert.NotNull(entry);
        Assert.Equal("unsloth/gemma-3-12b-it-GGUF", entry!.RepoId);
        Assert.Equal("migration", entry.Source);
        Assert.Equal(string.Empty, entry.Sha256);
    }

    // ── Reference rewrite (pure, on a settings object) ────────────────────────────────────
    [Fact]
    public void RewriteReferences_follows_ServerConfig_ModelPath()
    {
        var settings = new AppSettings();
        settings.ManagedServers.Clear();
        settings.ManagedServers.Add(new ServerConfig { ModelPath = @"C:\old\model.gguf" });

        ModelFolderOrganizer.RewriteReferences(settings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(@"C:\old\model.gguf")] = @"C:\new\LLM\model.gguf"
        });

        Assert.Equal(@"C:\new\LLM\model.gguf", settings.ManagedServers[0].ModelPath);
    }

    [Fact]
    public void RewriteReferences_follows_LlamaTuneProfile_ModelPath()
    {
        var settings = new AppSettings();
        settings.LlamaTuneProfiles.Add(new LlamaTuneProfile { ModelPath = @"C:\old\model.gguf" });

        ModelFolderOrganizer.RewriteReferences(settings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(@"C:\old\model.gguf")] = @"C:\new\LLM\model.gguf"
        });

        Assert.Equal(@"C:\new\LLM\model.gguf", settings.LlamaTuneProfiles[0].ModelPath);
    }

    [Fact]
    public void RewriteReferences_follows_ModelProfile_key()
    {
        var settings = new AppSettings();
        settings.ModelProfiles.Add(new ModelProfile { ModelId = @"C:\old\model.gguf", DisplayName = "My model" });

        ModelFolderOrganizer.RewriteReferences(settings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(@"C:\old\model.gguf")] = @"C:\new\LLM\model.gguf"
        });

        Assert.Equal(@"C:\new\LLM\model.gguf", settings.ModelProfiles[0].ModelId);
        Assert.Equal("My model", settings.ModelProfiles[0].DisplayName);
    }

    [Fact]
    public void RewriteReferences_leaves_unrelated_paths_untouched()
    {
        var settings = new AppSettings();
        settings.ManagedServers.Clear();
        settings.ManagedServers.Add(new ServerConfig { ModelPath = @"C:\unrelated\other.gguf" });

        ModelFolderOrganizer.RewriteReferences(settings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(@"C:\old\model.gguf")] = @"C:\new\LLM\model.gguf"
        });

        Assert.Equal(@"C:\unrelated\other.gguf", settings.ManagedServers[0].ModelPath);
    }

    // ── Empty-directory cleanup ────────────────────────────────────────────────────────────
    [Fact]
    public void FindEmptyDirectories_finds_only_directories_with_no_entries()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("hub");
        var empty = Path.Combine(root, "empty-dir");
        var nonEmpty = Path.Combine(root, "has-file");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "file.txt"), "x");

        var found = ModelFolderOrganizer.FindEmptyDirectories(root);

        Assert.Contains(empty, found);
        Assert.DoesNotContain(nonEmpty, found);
    }

    [Fact]
    public void RemoveEmptyDirectories_never_removes_a_directory_that_has_files()
    {
        using var temp = new TempDir();
        var nonEmpty = temp.PathFor("has-file");
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "file.txt"), "x");

        ModelFolderOrganizer.RemoveEmptyDirectories([nonEmpty]);

        Assert.True(Directory.Exists(nonEmpty));
    }
}
