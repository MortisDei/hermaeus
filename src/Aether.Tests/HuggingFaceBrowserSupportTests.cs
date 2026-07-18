using Aether.Services;
using Xunit;

namespace Aether.Tests;

// r13 03-hugging-face.md 3.4: destination-path + collision logic and multi-part detection.
public sealed class HuggingFaceBrowserSupportTests
{
    [Theory]
    [InlineData("model-00001-of-00003.gguf", true)]
    [InlineData("model-00003-of-00003.gguf", true)]
    [InlineData("model.gguf", false)]
    [InlineData("model-Q4_K_M.gguf", false)]
    public void IsMultiPartGguf_detects_the_dash_NNNNN_of_NNNNN_pattern(string fileName, bool expected)
    {
        Assert.Equal(expected, HuggingFaceBrowserSupport.IsMultiPartGguf(fileName));
    }

    [Fact]
    public void PlanDestination_uses_the_flat_LLM_folder_and_discards_repo_subfolders()
    {
        var (destination, collides) = HuggingFaceBrowserSupport.PlanDestination(@"C:\AI\Models", "subfolder/model.gguf");

        Assert.Equal(Path.Combine(@"C:\AI\Models", "LLM", "model.gguf"), destination);
        Assert.False(collides);
    }

    [Fact]
    public void PlanDestination_reports_a_collision_without_overwriting()
    {
        using var temp = new TempDir();
        var modelsDir = temp.PathFor("Models");
        var llmDir = Path.Combine(modelsDir, "LLM");
        Directory.CreateDirectory(llmDir);
        File.WriteAllText(Path.Combine(llmDir, "model.gguf"), "existing");

        var (destination, collides) = HuggingFaceBrowserSupport.PlanDestination(modelsDir, "model.gguf");

        Assert.True(collides);
        Assert.Equal("existing", File.ReadAllText(destination));
    }
}
