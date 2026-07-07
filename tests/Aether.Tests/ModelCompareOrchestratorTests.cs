using Aether.Core.Models;
using Aether.Core.Services;
using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

public sealed class ModelCompareOrchestratorTests
{
    [Fact]
    public void ResolveTargets_uses_explicitly_selected_models()
    {
        var options = new List<CompareModelOptionViewModel>
        {
            new(new LlmModel { Id = "a", Name = "A" }) { IsSelected = true },
            new(new LlmModel { Id = "b", Name = "B" }) { IsSelected = false },
            new(new LlmModel { Id = "c", Name = "C" }) { IsSelected = true }
        };

        var targets = ModelCompareOrchestrator.ResolveTargets(options, fallbackModel: null);

        Assert.Equal(["a", "c"], targets.Select(t => t.ModelId));
    }

    [Fact]
    public void ResolveTargets_falls_back_to_the_active_model_when_none_selected()
    {
        var options = new List<CompareModelOptionViewModel>
        {
            new(new LlmModel { Id = "a", Name = "A" }) { IsSelected = false }
        };
        var fallback = new LlmModel { Id = "z", Name = "Z" };

        var targets = ModelCompareOrchestrator.ResolveTargets(options, fallback);

        Assert.Single(targets);
        Assert.Equal("z", targets[0].ModelId);
    }

    [Fact]
    public void ResolveTargets_returns_empty_when_nothing_selected_and_no_fallback()
    {
        var options = new List<CompareModelOptionViewModel>
        {
            new(new LlmModel { Id = "a", Name = "A" }) { IsSelected = false }
        };

        var targets = ModelCompareOrchestrator.ResolveTargets(options, fallbackModel: null);

        Assert.Empty(targets);
    }

    [Fact]
    public void ResolveTargets_caps_at_the_requested_maximum()
    {
        var options = Enumerable.Range(0, 6)
            .Select(i => new CompareModelOptionViewModel(new LlmModel { Id = $"m{i}", Name = $"M{i}" }) { IsSelected = true })
            .ToList();

        var targets = ModelCompareOrchestrator.ResolveTargets(options, fallbackModel: null, maxTargets: 4);

        Assert.Equal(4, targets.Count);
    }

    [Fact]
    public void ToResult_maps_usage_and_latency_from_the_run()
    {
        var run = new EvalRun(
            "run-1",
            EvalMode.QuickCompare,
            new EvalTarget("model-a", Label: "Model A"),
            [new CaseResult("case-1", "the answer", LatencyMs: 42, FirstTokenMs: 5, PromptTokens: 10, CompletionTokens: 3)],
            DateTime.UtcNow);

        var result = ModelCompareOrchestrator.ToResult(run);

        Assert.Equal("model-a", result.ModelId);
        Assert.Equal("Model A", result.DisplayName);
        Assert.Equal("the answer", result.Answer);
        Assert.Equal(42, result.TotalLatencyMs);
        Assert.Equal(5, result.FirstTokenMs);
        Assert.NotNull(result.Usage);
        Assert.Equal(13, result.Usage!.TotalTokens);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void ToResult_surfaces_the_error_and_leaves_usage_null_when_the_run_failed()
    {
        var run = new EvalRun(
            "run-2",
            EvalMode.QuickCompare,
            new EvalTarget("model-b"),
            [new CaseResult("case-1", string.Empty, LatencyMs: 7, Error: "timed out")],
            DateTime.UtcNow);

        var result = ModelCompareOrchestrator.ToResult(run);

        Assert.Equal("timed out", result.Error);
        Assert.Null(result.Usage);
        Assert.Equal("model-b", result.DisplayName);
    }
}
