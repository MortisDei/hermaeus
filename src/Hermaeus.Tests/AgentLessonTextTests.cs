using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentLessonTextTests
{
    [Fact]
    public void Tokenize_lowercases_drops_short_tokens_and_stopwords()
    {
        var tokens = AgentLessonText.Tokenize("Fix the Build: dotnet test fails with CS0246!");

        Assert.Contains("fix", tokens);
        Assert.Contains("build", tokens);
        Assert.Contains("dotnet", tokens);
        Assert.Contains("test", tokens);
        Assert.Contains("fails", tokens);
        Assert.Contains("cs0246", tokens);
        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("with", tokens);
    }

    [Fact]
    public void Tokenize_returns_empty_for_blank_input() =>
        Assert.Empty(AgentLessonText.Tokenize("   "));

    [Fact]
    public void Fingerprint_is_stable_across_case_and_punctuation_variants()
    {
        var a = AgentLessonText.Fingerprint("Fix the dotnet build error");
        var b = AgentLessonText.Fingerprint("fix THE dotnet-build error!!!");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_differs_for_unrelated_goals()
    {
        var a = AgentLessonText.Fingerprint("Fix the dotnet build error");
        var b = AgentLessonText.Fingerprint("Summarize the quarterly report");

        Assert.NotEqual(a, b);
    }
}
