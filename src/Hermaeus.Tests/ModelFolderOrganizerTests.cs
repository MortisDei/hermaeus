using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

// r13 02-model-library.md 2.6: the model folder organizer. Plan is pure; execution touches
// disk and settings only after a preview+confirm gate the tests below do not exercise directly.
// r27 04-models-arrive-complete.md 4.3/4.4: the destination is now a per-model folder rather
// than one flat directory, because the projector sibling scan is load-bearing and a flat
// folder can hold exactly one file named mmproj-F16.gguf.
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
        Assert.Equal(Path.Combine(modelsDir, "llm", "unsloth__gemma-3-12b-it-GGUF", "gemma-3-12b-it-Q4_K_M.gguf"), Assert.Single(move.DestinationPaths));
        Assert.Equal("unsloth", move.HubRepoOrg);
        Assert.Equal("gemma-3-12b-it-GGUF", move.HubRepoName);
        Assert.Equal(1, plan.ProvenanceCount);
    }

    [Fact]
    public void Plan_leaves_a_model_already_in_its_own_folder_alone()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var modelDir = Path.Combine(modelsDir, "LLM", "already-organized");
        Directory.CreateDirectory(modelDir);
        var file = Path.Combine(modelDir, "already-organized.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Skips);
    }

    /// <summary>
    /// r27 04 4.4: an install that has already run Organize has a flat LLM folder
    /// where filenames are the only identity. Re-running must improve that
    /// without guessing wrong.
    /// </summary>
    [Fact]
    public void Plan_migrates_a_flat_file_into_a_folder_named_from_its_own_base_name()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        var file = Path.Combine(llmDir, "already-flat.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(Path.Combine(llmDir, "already-flat", "already-flat.gguf"), Assert.Single(move.DestinationPaths));
    }

    [Fact]
    public void Plan_migrates_a_flat_file_with_manifest_provenance_into_its_repository_folder()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        var file = Path.Combine(llmDir, "gemma-Q4.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file], repoIdsByPath: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(file)] = "unsloth/gemma-4-E4B-it-qat-GGUF"
        });

        var move = Assert.Single(plan.Moves);
        Assert.Equal(Path.Combine(llmDir, "unsloth__gemma-4-E4B-it-qat-GGUF", "gemma-Q4.gguf"), Assert.Single(move.DestinationPaths));
    }

    /// <summary>
    /// r27 04 4.3: this is the item that fixes the owner's seven-way
    /// mmproj-F16.gguf collision. In per-model folders they stop competing for
    /// one name, and each moves with the model it belongs to.
    /// </summary>
    [Fact]
    public void Plan_moves_a_companion_with_its_model_into_the_same_folder()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "models--unsloth--gemma-4-E4B-it-qat-GGUF", "snapshots", "abc");
        Directory.CreateDirectory(nested);
        var model = Path.Combine(nested, "gemma-Q4.gguf");
        var projector = Path.Combine(nested, "mmproj-F16.gguf");
        var draftHead = Path.Combine(nested, "mtp-gemma-4-E4B-it.gguf");
        File.WriteAllText(model, "model");
        File.WriteAllText(projector, "projector");
        File.WriteAllText(draftHead, "draft");

        // FindGgufModels deliberately excludes companions from the input; the
        // plan picks them up from the model's own directory.
        var plan = ModelFolderOrganizer.Plan(modelsDir, [model]);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(3, move.SourcePaths.Count);
        Assert.Contains(projector, move.SourcePaths);
        Assert.Contains(draftHead, move.SourcePaths);
        var folder = Path.Combine(modelsDir, "llm", "unsloth__gemma-4-E4B-it-qat-GGUF");
        Assert.All(move.DestinationPaths, d => Assert.Equal(folder, Path.GetDirectoryName(d)));
    }

    [Fact]
    public void Plan_is_unchanged_by_being_called_twice()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var nested = Path.Combine(modelsDir, "hub", "models--org--repo", "snapshots", "abc");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "model.gguf");
        File.WriteAllText(file, "fake");

        var first = ModelFolderOrganizer.Plan(modelsDir, [file]);
        var second = ModelFolderOrganizer.Plan(modelsDir, [file]);

        Assert.Equal(
            string.Join("|", first.Moves.SelectMany(m => m.DestinationPaths)),
            string.Join("|", second.Moves.SelectMany(m => m.DestinationPaths)));
        Assert.Equal(first.Skips.Count, second.Skips.Count);
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

        // The collision has to be inside the model's own folder to be one now.
        Directory.CreateDirectory(Path.Combine(llmDir, "model"));
        File.WriteAllText(Path.Combine(llmDir, "model", "model.gguf"), "already here");

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
        Directory.CreateDirectory(Path.Combine(llmDir, "big-model"));
        File.WriteAllText(Path.Combine(llmDir, "big-model", "big-model-00001-of-00002.gguf"), "existing");

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
        Assert.Equal(Path.Combine(modelsDir, "llm", "root-level", "root-level.gguf"), Assert.Single(move.DestinationPaths));
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
    public void Plan_reuses_a_pre_existing_LLM_directory_instead_of_creating_a_second_llm_one()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        Directory.CreateDirectory(Path.Combine(modelsDir, "LLM"));
        var nested = Path.Combine(modelsDir, "hub", "nested");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "model.gguf");
        File.WriteAllText(file, "fake");

        var plan = ModelFolderOrganizer.Plan(modelsDir, [file]);

        Assert.Equal(Path.Combine(modelsDir, "LLM"), plan.DestinationDirectory);
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
        Assert.True(File.Exists(Path.Combine(modelsDir, "llm", "model", "model.gguf")));
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

        var destination = Path.Combine(modelsDir, "llm", "unsloth__gemma-3-12b-it-GGUF", "gemma.gguf");
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
    public void RewriteReferences_follows_ServerConfig_projector_without_changing_use_preference()
    {
        var settings = new AppSettings();
        settings.ManagedServers.Clear();
        settings.ManagedServers.Add(new ServerConfig
        {
            ModelPath = @"C:\old\model.gguf",
            MmprojPath = @"C:\old\mmproj.gguf",
            UseProjector = false
        });

        ModelFolderOrganizer.RewriteReferences(settings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(@"C:\old\model.gguf")] = @"C:\new\LLM\model.gguf",
            [Path.GetFullPath(@"C:\old\mmproj.gguf")] = @"C:\new\vision\mmproj.gguf"
        });

        Assert.Equal(@"C:\new\LLM\model.gguf", settings.ManagedServers[0].ModelPath);
        Assert.Equal(@"C:\new\vision\mmproj.gguf", settings.ManagedServers[0].MmprojPath);
        Assert.False(settings.ManagedServers[0].UseProjector);
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
