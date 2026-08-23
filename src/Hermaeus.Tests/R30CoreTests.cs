using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class R30CoreTests
{
    [Fact]
    public void Numeric_neutral_defaults_are_only_applied_on_first_spin()
    {
        Assert.Equal(1.1, NullableNumericDefaults.FirstSpin(null, "repeat_penalty", .5, 2, .1, 1));
        Assert.Equal(-.1, NullableNumericDefaults.FirstSpin(null, "frequency_penalty", -2, 2, .1, -1));
        Assert.Null(NullableNumericDefaults.FirstSpin(null, "top_k", 0, 500, 1, 1));
        Assert.Equal(.7, NullableNumericDefaults.FirstSpin(.7, "repeat_penalty", .5, 2, .1, 1));
    }

    [Fact]
    public void Reasoning_history_policy_requires_every_proof()
    {
        Assert.True(ReasoningHistoryPolicy.CanReplay("llama.cpp", true, true, true, true));
        Assert.False(ReasoningHistoryPolicy.CanReplay("openai", true, true, true, true));
        Assert.False(ReasoningHistoryPolicy.CanReplay("llama.cpp", true, false, true, true));
    }

    [Fact]
    public void Reasoning_wire_field_is_removed_when_policy_is_off()
    {
        var message = new ChatMessage("assistant", "answer", ReasoningContent: "private");
        Assert.Null(ReasoningHistoryPolicy.WithOptionalReasoning(message, false).ReasoningContent);
        Assert.Equal("private", ReasoningHistoryPolicy.WithOptionalReasoning(message, true).ReasoningContent);
    }

    [Fact]
    public void Reasoning_token_budget_counts_only_messages_that_carry_it()
    {
        var withReasoning = ChatContextUsageCalculator.TruncateHistoryToContextWindow(
            [new ChatMessage("assistant", new string('a', 1000), ReasoningContent: new string('b', 1000))], 512);
        var withoutReasoning = ChatContextUsageCalculator.TruncateHistoryToContextWindow(
            [new ChatMessage("assistant", new string('a', 1000))], 512);
        Assert.Single(withReasoning);
        Assert.Single(withoutReasoning);
    }

    [Fact]
    public void Benchmark_fixtures_accept_grouped_digits_and_alternatives()
    {
        var test = new BenchmarkCase
        {
            ExpectedKeywords = ["2621440"],
            ExpectedKeywordAlternatives = [["docs", "documentation"]]
        };
        var result = BenchmarkService.ScoreDeterministic(test, "2,621,440 documentation");
        Assert.True(result.Passed);
        Assert.False(BenchmarkService.ScoreDeterministic(test, "2,621,441 documentation").Passed);
        Assert.False(BenchmarkService.ScoreDeterministic(test, "2,621,440 notes").Passed);
    }

    [Fact]
    public void Benchmark_multiline_regex_and_refusal_contradiction_are_narrow()
    {
        var json = new BenchmarkCase { ExpectedRegexes = [@"\{.*name.*status.*\}"] };
        Assert.True(BenchmarkService.ScoreDeterministic(json, "{\n  \"name\": \"Hermaeus\",\n  \"status\": \"ready\"\n}").Passed);
        var refusal = new BenchmarkCase { ShouldRefuse = true };
        Assert.True(BenchmarkService.ScoreDeterministic(refusal, "There is no record, so I am unable to verify it.").Passed);
        Assert.False(BenchmarkService.ScoreDeterministic(refusal, "I cannot verify this, but it is 73%.").Passed);
    }

    [Fact]
    public void Reasoning_launch_flags_are_paired_and_unknown_is_quiet()
    {
        var config = new ServerConfig { PreserveReasoning = true };
        var enabled = ServerProcessManager.BuildLaunchArguments(config, true);
        Assert.Contains("--reasoning-preserve", enabled);
        Assert.DoesNotContain("--no-reasoning-preserve", enabled);
        config.PreserveReasoning = false;
        var disabled = ServerProcessManager.BuildLaunchArguments(config, true);
        Assert.Contains("--no-reasoning-preserve", disabled);
        Assert.DoesNotContain("--reasoning-preserve", disabled);
        Assert.DoesNotContain("--reasoning-preserve", ServerProcessManager.BuildLaunchArguments(config));
    }

    [Fact]
    public void Model_deletion_planner_rejects_outside_root_and_running_targets()
    {
        var root = Path.Combine(Path.GetTempPath(), "hermaeus-r30-path-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var model = Path.Combine(root, "model.gguf");
        File.WriteAllText(model, "model");
        Assert.True(ModelDeletionService.TryPlan(model, root, false, out var plan, out _));
        Assert.Single(plan!.Files);
        Assert.False(ModelDeletionService.TryPlan(Path.Combine(root, "..", "outside.gguf"), root, false, out _, out _));
        Assert.False(ModelDeletionService.TryPlan(model, root, true, out _, out _));
        File.Delete(model);
        Directory.Delete(root);
    }
}
