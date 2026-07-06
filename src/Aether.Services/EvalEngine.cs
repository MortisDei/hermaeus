using System.Diagnostics;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class EvalEngine : IEvalEngine
{
    private readonly ILlmService _llm;

    public EvalEngine(ILlmService llm)
    {
        _llm = llm;
    }

    public async Task<IReadOnlyList<EvalRun>> RunQuickCompareAsync(
        string caseId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<EvalTarget> targets,
        LlmChatOptions? options = null,
        CancellationToken ct = default)
    {
        var runs = new List<EvalRun>(targets.Count);
        foreach (var target in targets)
        {
            var startedAt = DateTime.UtcNow;
            var clock = Stopwatch.StartNew();
            long? firstTokenMs = null;
            ChatTokenUsage? usage = null;
            var answer = new StringBuilder();
            var error = string.Empty;

            try
            {
                await foreach (var evt in _llm.StreamChatAsync(target.ModelId, messages, options, ct))
                {
                    if (evt.Usage is not null)
                        usage = evt.Usage;
                    if (!string.IsNullOrEmpty(evt.ContentDelta))
                    {
                        firstTokenMs ??= clock.ElapsedMilliseconds;
                        answer.Append(evt.ContentDelta);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                clock.Stop();
            }

            var result = new CaseResult(
                CaseId: caseId,
                Output: answer.ToString(),
                LatencyMs: clock.ElapsedMilliseconds,
                FirstTokenMs: firstTokenMs,
                PromptTokens: usage?.PromptTokens,
                CompletionTokens: usage?.CompletionTokens,
                Error: string.IsNullOrEmpty(error) ? null : error);

            runs.Add(new EvalRun(
                Id: Guid.NewGuid().ToString("n"),
                Mode: EvalMode.QuickCompare,
                Target: target,
                CaseResults: [result],
                StartedAt: startedAt,
                FinishedAt: DateTime.UtcNow));
        }

        return runs;
    }
}
