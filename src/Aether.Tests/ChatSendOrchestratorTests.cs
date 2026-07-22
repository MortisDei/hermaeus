using System.Runtime.CompilerServices;
using Aether.Core.Models;
using Aether.Core.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ChatSendOrchestratorTests
{
    [Fact]
    public async Task StreamAsync_reports_tokens_and_usage_then_completes()
    {
        var llm = new ScriptedLlm(
            new LlmStreamEvent("hel"),
            new LlmStreamEvent("lo"),
            new LlmStreamEvent(Usage: new ChatTokenUsage(10, 2, 12)));

        var tokens = new List<string>();
        ChatTokenUsage? usage = null;

        var result = await ChatSendOrchestrator.StreamAsync(
            llm, "model-a", [new ChatMessage("user", "hi")], LlmChatOptions.Default,
            onToken: tokens.Add,
            onUsage: u => usage = u,
            CancellationToken.None);

        Assert.Equal(["hel", "lo"], tokens);
        Assert.NotNull(usage);
        Assert.Equal(12, usage!.TotalTokens);
        Assert.False(result.Cancelled);
        Assert.Null(result.Error);
        Assert.Equal(usage, result.Usage);
    }

    [Fact]
    public async Task StreamAsync_captures_length_finish_reason()
    {
        var llm = new ScriptedLlm(
            new LlmStreamEvent("hi"),
            new LlmStreamEvent(Usage: new ChatTokenUsage(10, 4096, 4106), IsFinal: true, FinishReason: "length"));

        var result = await ChatSendOrchestrator.StreamAsync(
            llm, "model-a", [new ChatMessage("user", "hi")], LlmChatOptions.Default,
            onToken: _ => { },
            onUsage: _ => { },
            CancellationToken.None);

        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_reports_cancellation_without_throwing()
    {
        var llm = new ScriptedLlm(throwCancelled: true);

        var result = await ChatSendOrchestrator.StreamAsync(
            llm, "model-a", [new ChatMessage("user", "hi")], LlmChatOptions.Default,
            onToken: _ => { },
            onUsage: _ => { },
            CancellationToken.None);

        Assert.True(result.Cancelled);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task StreamAsync_captures_exception_message_without_throwing()
    {
        var llm = new ScriptedLlm(throwMessage: "boom");

        var result = await ChatSendOrchestrator.StreamAsync(
            llm, "model-a", [new ChatMessage("user", "hi")], LlmChatOptions.Default,
            onToken: _ => { },
            onUsage: _ => { },
            CancellationToken.None);

        Assert.False(result.Cancelled);
        Assert.Equal("boom", result.Error);
    }

    private sealed class ScriptedLlm : ILlmService
    {
        private readonly LlmStreamEvent[] _events;
        private readonly bool _throwCancelled;
        private readonly string? _throwMessage;

        public ScriptedLlm(params LlmStreamEvent[] events)
        {
            _events = events;
        }

        public ScriptedLlm(bool throwCancelled = false, string? throwMessage = null)
        {
            _events = [];
            _throwCancelled = throwCancelled;
            _throwMessage = throwMessage;
        }

        public string ProviderName => "Scripted";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<LlmModel>());

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            if (_throwCancelled)
                throw new OperationCanceledException();
            if (_throwMessage is not null)
                throw new InvalidOperationException(_throwMessage);

            foreach (var evt in _events)
                yield return evt;
        }
    }
}
