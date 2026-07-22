using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>r19 5.4: chat artifacts write into a fixed per-conversation sandbox folder under a sanitized, deduped filename.</summary>
public sealed class ChatArtifactServiceTests
{
    private static ChatArtifactService NewService(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ChatArtifactService(settings);
    }

    [Fact]
    public async Task SaveAsync_writes_under_the_conversation_specific_folder()
    {
        using var temp = new TempDir();
        var service = NewService(temp);

        var artifact = await service.SaveAsync("conv-1", "snippet.cs", "class Foo {}");

        Assert.True(File.Exists(artifact.FullPath));
        Assert.Equal("class Foo {}", await File.ReadAllTextAsync(artifact.FullPath));
        Assert.Contains(Path.Combine("chat-artifacts", "conv-1"), artifact.FullPath);
    }

    [Fact]
    public async Task SaveAsync_leaves_no_temp_file_behind()
    {
        using var temp = new TempDir();
        var service = NewService(temp);

        var artifact = await service.SaveAsync("conv-1", "notes.txt", "hello");

        var dir = Path.GetDirectoryName(artifact.FullPath)!;
        Assert.DoesNotContain(Directory.GetFiles(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_dedupes_a_repeated_filename_with_a_numbered_suffix()
    {
        using var temp = new TempDir();
        var service = NewService(temp);

        var first = await service.SaveAsync("conv-1", "report.md", "one");
        var second = await service.SaveAsync("conv-1", "report.md", "two");
        var third = await service.SaveAsync("conv-1", "report.md", "three");

        Assert.Equal("report.md", first.FileName);
        Assert.Equal("report (2).md", second.FileName);
        Assert.Equal("report (3).md", third.FileName);
        Assert.Equal("one", await File.ReadAllTextAsync(first.FullPath));
        Assert.Equal("two", await File.ReadAllTextAsync(second.FullPath));
    }

    [Fact]
    public async Task SaveAsync_rejects_a_traversal_attempt_by_collapsing_it_to_a_bare_filename()
    {
        using var temp = new TempDir();
        var service = NewService(temp);

        var artifact = await service.SaveAsync("conv-1", "../../evil.txt", "payload");

        var conversationDir = Path.GetFullPath(service.GetConversationDirectory("conv-1"));
        Assert.StartsWith(conversationDir, Path.GetFullPath(artifact.FullPath), StringComparison.Ordinal);
        Assert.Equal("evil.txt", artifact.FileName);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFileName_falls_back_to_a_safe_default_for_degenerate_input(string input)
    {
        Assert.Equal("artifact.txt", ChatArtifactService.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_replaces_invalid_filesystem_characters()
    {
        var sanitized = ChatArtifactService.SanitizeFileName("bad:name?.txt");
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => sanitized.Contains(c));
    }

    [Fact]
    public async Task ListAsync_returns_an_empty_list_before_anything_has_been_saved()
    {
        using var temp = new TempDir();
        var service = NewService(temp);

        var list = await service.ListAsync("never-saved");

        Assert.Empty(list);
    }

    [Fact]
    public async Task ListAsync_only_returns_artifacts_for_the_requested_conversation()
    {
        using var temp = new TempDir();
        var service = NewService(temp);
        await service.SaveAsync("conv-a", "a.txt", "a");
        await service.SaveAsync("conv-b", "b.txt", "b");

        var listA = await service.ListAsync("conv-a");

        var only = Assert.Single(listA);
        Assert.Equal("a.txt", only.FileName);
    }
}
