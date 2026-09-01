using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class SupportedTextFileTypesTests
{
    [Theory]
    [InlineData("runtime.log")]
    [InlineData("notes.rst")]
    [InlineData("table.csv")]
    [InlineData("Dockerfile")]
    [InlineData(".gitignore")]
    [InlineData("README")]
    public void Common_text_files_are_supported(string fileName)
    {
        Assert.True(SupportedTextFileTypes.IsSupported(fileName));
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("private.pem")]
    [InlineData("model.gguf")]
    [InlineData("library.dll")]
    public void Secrets_and_binary_model_files_are_not_supported(string fileName)
    {
        Assert.False(SupportedTextFileTypes.IsSupported(fileName));
    }

    [Fact]
    public void Agent_file_listing_includes_logs_without_including_binary_assets()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "runtime.log"), "log");
        File.WriteAllBytes(Path.Combine(root, "weights.gguf"), [0, 1, 2]);

        var listing = new AgentWorkspaceTools().ListFiles(new AgentWorkspaceOptions(root));

        Assert.Contains("runtime.log", listing);
        Assert.DoesNotContain("weights.gguf", listing);
    }

    [Fact]
    public async Task Chat_context_loads_a_log_as_bounded_text()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime.log");
        await File.WriteAllTextAsync(path, "log line available to the assistant");

        var attachment = Assert.Single(await ChatContextAttachment.LoadFilesAsync([path]));

        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.Contains("available to the assistant", attachment.Content);
    }
}
