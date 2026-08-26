using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 04-models-arrive-complete.md. A GGUF model is frequently not one file:
/// it can be a set of shards, it can have a multimodal projector, and it can
/// have a Multi-Token Prediction head. The app treated a model as exactly one
/// file everywhere it downloaded or moved one, and the destination discarded
/// the repository entirely, which is why the owner has seven files named
/// mmproj-F16.gguf that cannot coexist in a flat folder.
/// </summary>
public sealed class ModelFileSetTests
{
    private static HfTreeEntry Entry(string path, long size = 1024) => new(path, size, $"sha-{path}");

    // ── 4.1 A model is a file set ───────────────────────────────────────────

    [Fact]
    public void A_sharded_model_resolves_all_shards_and_they_are_all_required()
    {
        var tree = new[]
        {
            Entry("Qwen3-30B-Q4_K_M-00001-of-00003.gguf"),
            Entry("Qwen3-30B-Q4_K_M-00002-of-00003.gguf"),
            Entry("Qwen3-30B-Q4_K_M-00003-of-00003.gguf"),
            Entry("Qwen3-30B-Q8_0-00001-of-00002.gguf")
        };

        var set = ModelFileSetResolver.Resolve("qwen/Qwen3", tree, "Qwen3-30B-Q4_K_M-00002-of-00003.gguf");

        Assert.True(set.IsSharded);
        Assert.Equal(3, set.Entries.Count);
        Assert.All(set.Entries, e => Assert.True(e.Required, "a partial shard set is a model that does not load"));
        Assert.DoesNotContain(set.Entries, e => e.FileName.Contains("Q8_0", StringComparison.Ordinal));
        Assert.Empty(set.Optional);
    }

    [Fact]
    public void An_explicit_source_mapping_offers_projector_and_mtp_head_on_by_default()
    {
        var tree = new[]
        {
            Entry("gemma-4-E4B-it-Q4_K_M.gguf", 4_200_000_000),
            Entry("mmproj-F16.gguf", 600_000_000),
            Entry("MTP/mtp-gemma-4-E4B-it.gguf", 59_000_000)
        };

        var mappings = new[]
        {
            new HfCompanionDeclaration("gemma-4-E4B-it-Q4_K_M.gguf", "mmproj-F16.gguf", ModelFileRole.Projector),
            new HfCompanionDeclaration("gemma-4-E4B-it-Q4_K_M.gguf", "MTP/mtp-gemma-4-E4B-it.gguf", ModelFileRole.DraftHead)
        };
        var set = ModelFileSetResolver.Resolve("unsloth/gemma-4-E4B-it-qat-GGUF", tree, "gemma-4-E4B-it-Q4_K_M.gguf", mappings);

        Assert.Equal(3, set.Entries.Count);
        var projector = Assert.Single(set.Entries, e => e.Role == ModelFileRole.Projector);
        var draft = Assert.Single(set.Entries, e => e.Role == ModelFileRole.DraftHead);
        Assert.False(projector.Required);
        Assert.False(draft.Required);
        Assert.True(projector.SelectedByDefault, "a multimodal model without its projector quietly cannot see");
        Assert.True(draft.SelectedByDefault, "the MTP head is what doc 03's speculative decoding needs");
        Assert.Equal(4_859_000_000, set.DefaultSelectionBytes);
    }

    [Fact]
    public void Filename_only_companions_are_not_inferred()
    {
        var flat = ModelFileSetResolver.Resolve("u/r",
            [Entry("model.gguf"), Entry("mtp-model.gguf")], "model.gguf");
        Assert.DoesNotContain(flat.Entries, e => e.Role == ModelFileRole.DraftHead);

        var nested = ModelFileSetResolver.Resolve("u/r",
            [Entry("model.gguf"), Entry("MTP/mtp-model.gguf")], "model.gguf");
        Assert.DoesNotContain(nested.Entries, e => e.Role == ModelFileRole.DraftHead);

        var mapped = ModelFileSetResolver.Resolve("u/r",
            [Entry("model.gguf"), Entry("other/mtp-model.gguf")], "model.gguf",
            [new HfCompanionDeclaration("model.gguf", "other/mtp-model.gguf", ModelFileRole.DraftHead)]);
        Assert.Contains(mapped.Entries, e => e.Role == ModelFileRole.DraftHead);
    }

    [Fact]
    public void A_repository_with_neither_offers_only_the_model()
    {
        var set = ModelFileSetResolver.Resolve("u/r", [Entry("model.gguf"), Entry("readme.md")], "model.gguf");

        var only = Assert.Single(set.Entries);
        Assert.Equal(ModelFileRole.Model, only.Role);
        Assert.True(only.Required);
        Assert.False(set.IsSharded);
    }

    [Fact]
    public void A_projector_in_a_different_repository_directory_is_not_offered()
    {
        var set = ModelFileSetResolver.Resolve("u/r",
            [Entry("quants/model.gguf"), Entry("mmproj-F16.gguf")], "quants/model.gguf");

        Assert.DoesNotContain(set.Entries, e => e.Role == ModelFileRole.Projector);
    }

    // ── 4.2 Per-model destination folders ───────────────────────────────────

    [Fact]
    public void Destination_is_under_a_repo_folder_and_is_stable_across_calls()
    {
        using var temp = new TempDir();
        var models = temp.PathFor("Models");
        Directory.CreateDirectory(models);

        var first = HuggingFaceBrowserSupport.PlanDestination(models, "MTP/mtp-gemma.gguf", "unsloth/gemma-4-E4B-it-qat-GGUF");
        var second = HuggingFaceBrowserSupport.PlanDestination(models, "gemma-Q4.gguf", "unsloth/gemma-4-E4B-it-qat-GGUF");

        Assert.Equal(Path.GetDirectoryName(first.DestinationPath), Path.GetDirectoryName(second.DestinationPath));
        Assert.Contains("unsloth__gemma-4-E4B-it-qat-GGUF", first.DestinationPath);
        // The repo's internal folder structure is flattened into the model
        // folder, so the sibling scan that finds projectors finds this too.
        Assert.EndsWith("mtp-gemma.gguf", first.DestinationPath);
        Assert.DoesNotContain($"MTP{Path.DirectorySeparatorChar}", first.DestinationPath);
    }

    [Fact]
    public void A_repository_id_cannot_escape_the_destination_root()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llm");
        Directory.CreateDirectory(root);

        foreach (var hostile in new[] { "../../etc", "..", "/absolute/path", "C:\\Windows\\System32", "a/../../b", "" })
        {
            var resolved = ModelRepoFolder.TryResolvePath(root, hostile, out var folder, out var error);
            if (!resolved)
            {
                Assert.NotEqual(string.Empty, error);
                continue;
            }

            // Whatever it resolves to is one literal folder segment inside the
            // root. A ".." that survives as part of a name is a directory called
            // "..__..__etc", not a traversal.
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, folder, StringComparison.OrdinalIgnoreCase);
            var relative = Path.GetRelativePath(Path.GetFullPath(root), folder);
            Assert.Single(relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Assert.DoesNotContain(relative.Split(Path.DirectorySeparatorChar), segment => segment is ".." or ".");
        }
    }

    [Fact]
    public void Two_repositories_that_sanitise_to_the_same_name_get_distinct_folders()
    {
        // Both contain characters that cannot survive as a folder name, so both
        // sanitise toward "org__model-x". They must never silently merge.
        var first = ModelRepoFolder.Resolve("org/model:x");
        var second = ModelRepoFolder.Resolve("org/model*x");

        Assert.NotEqual(first, second);
        Assert.Equal(first, ModelRepoFolder.Resolve("org/model:x"));
    }

    [Fact]
    public void An_ordinary_repository_id_reads_as_itself()
    {
        Assert.Equal("unsloth__gemma-4-E4B-it-qat-GGUF", ModelRepoFolder.Resolve("unsloth/gemma-4-E4B-it-qat-GGUF"));
        Assert.Equal("bartowski__Qwen3-30B-GGUF", ModelRepoFolder.Resolve("bartowski/Qwen3-30B-GGUF"));
        Assert.Equal("unknown-model", ModelRepoFolder.Resolve("   "));
    }

    [Fact]
    public void Same_named_companions_from_different_repositories_coexist()
    {
        using var temp = new TempDir();
        var models = temp.PathFor("Models");
        Directory.CreateDirectory(models);

        var a = HuggingFaceBrowserSupport.PlanDestination(models, "mmproj-F16.gguf", "unsloth/gemma-4-E4B-it-qat-GGUF");
        var b = HuggingFaceBrowserSupport.PlanDestination(models, "mmproj-F16.gguf", "unsloth/gemma-4-12B-it-qat-GGUF");

        // The exact collision the owner has seven copies of today.
        Assert.NotEqual(a.DestinationPath, b.DestinationPath);
        Assert.False(a.Collides);
        Assert.False(b.Collides);
    }

    [Fact]
    public void An_unknown_repository_falls_back_to_the_flat_destination_rather_than_somewhere_unexpected()
    {
        using var temp = new TempDir();
        var models = temp.PathFor("Models");
        Directory.CreateDirectory(models);

        var planned = HuggingFaceBrowserSupport.PlanDestination(models, "model.gguf");

        Assert.Equal(Path.Combine(models, LlmFolderName.Resolve(models), "model.gguf"), planned.DestinationPath);
    }
}
