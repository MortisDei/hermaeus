using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class EvalEngineTests
{
    private sealed class PerModelLlm : ILlmService
    {
        public string ProviderName => "PerModel";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<LlmModel>());

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (modelId == "broken")
                throw new InvalidOperationException("model unavailable");

            await Task.Delay(1, ct);
            yield return new LlmStreamEvent($"answer from {modelId}");
            yield return new LlmStreamEvent(Usage: new ChatTokenUsage(5, 3, 8), IsFinal: true);
        }
    }

    [Fact]
    public async Task RunQuickCompareAsync_returns_one_run_per_target_in_order()
    {
        var engine = new EvalEngine(new PerModelLlm());
        var messages = new List<ChatMessage> { new("user", "hello") };
        var targets = new List<EvalTarget> { new("model-a", Label: "A"), new("model-b", Label: "B") };

        var runs = await engine.RunQuickCompareAsync("case-1", messages, targets);

        Assert.Equal(2, runs.Count);
        Assert.Equal("model-a", runs[0].Target.ModelId);
        Assert.Equal("model-b", runs[1].Target.ModelId);
        Assert.All(runs, r => Assert.Equal(EvalMode.QuickCompare, r.Mode));
        Assert.Equal("answer from model-a", runs[0].CaseResults[0].Output);
        Assert.Equal(5, runs[0].CaseResults[0].PromptTokens);
    }

    [Fact]
    public async Task RunQuickCompareAsync_isolates_a_failing_target()
    {
        var engine = new EvalEngine(new PerModelLlm());
        var messages = new List<ChatMessage> { new("user", "hello") };
        var targets = new List<EvalTarget> { new("broken"), new("model-b") };

        var runs = await engine.RunQuickCompareAsync("case-1", messages, targets);

        Assert.Equal(2, runs.Count);
        Assert.Equal("model unavailable", runs[0].CaseResults[0].Error);
        Assert.Equal(string.Empty, runs[0].CaseResults[0].Output);
        Assert.Null(runs[1].CaseResults[0].Error);
        Assert.Equal("answer from model-b", runs[1].CaseResults[0].Output);
    }
}
