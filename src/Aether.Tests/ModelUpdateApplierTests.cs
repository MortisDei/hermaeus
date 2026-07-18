using Aether.Services;
using Xunit;

namespace Aether.Tests;

// r13 03-hugging-face.md 3.3: crash-safe atomic swap for applying an update.
public sealed class ModelUpdateApplierTests
{
    [Fact]
    public void Swap_happy_path_replaces_the_file_and_cleans_up_previous()
    {
        using var temp = new TempDir();
        var current = temp.PathFor("model.gguf");
        var replacement = temp.PathFor("model.gguf.update.tmp");
        File.WriteAllText(current, "old content");
        File.WriteAllText(replacement, "new content");

        var result = ModelUpdateApplier.Swap(current, replacement);

        Assert.True(result.Success);
        Assert.Equal("new content", File.ReadAllText(current));
        Assert.False(File.Exists(current + ".previous"));
        Assert.False(File.Exists(replacement));
    }

    [Fact]
    public void Swap_restores_the_original_when_the_second_move_fails()
    {
        using var temp = new TempDir();
        var current = temp.PathFor("model.gguf");
        var missingReplacement = temp.PathFor("does-not-exist.gguf.update.tmp");
        File.WriteAllText(current, "original content");

        var result = ModelUpdateApplier.Swap(current, missingReplacement);

        Assert.False(result.Success);
        Assert.True(File.Exists(current), "the original file must still exist after a failed swap");
        Assert.Equal("original content", File.ReadAllText(current));
        Assert.False(File.Exists(current + ".previous"), "no leftover .previous backup after a clean rollback");
    }

    [Fact]
    public void Swap_refuses_when_a_previous_backup_already_exists_rather_than_overwriting_it()
    {
        using var temp = new TempDir();
        var current = temp.PathFor("model.gguf");
        var replacement = temp.PathFor("model.gguf.update.tmp");
        File.WriteAllText(current, "current content");
        File.WriteAllText(replacement, "new content");
        File.WriteAllText(current + ".previous", "leftover from an earlier interrupted update");

        var result = ModelUpdateApplier.Swap(current, replacement);

        Assert.False(result.Success);
        Assert.Equal("current content", File.ReadAllText(current));
        Assert.Equal("leftover from an earlier interrupted update", File.ReadAllText(current + ".previous"));
        Assert.True(File.Exists(replacement), "the downloaded replacement is left alone, not deleted, on this refusal path");
    }
}
