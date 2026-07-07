using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.ViewModels;

/// <summary>
/// Pure helpers for ChatViewModel.CompareSelectedModelsAsync: resolving which
/// models to compare, and mapping an EvalRun back into a display row.
/// </summary>
public static class ModelCompareOrchestrator
{
    public static List<EvalTarget> ResolveTargets(
        IEnumerable<CompareModelOptionViewModel> compareModels,
        LlmModel? fallbackModel,
        int maxTargets = 4)
    {
        var selected = compareModels.Where(m => m.IsSelected).Take(maxTargets).ToList();
        if (selected.Count == 0 && fallbackModel is not null)
            return [new EvalTarget(fallbackModel.Id, Label: fallbackModel.DisplayName)];

        return selected.Select(o => new EvalTarget(o.Model.Id, Label: o.Model.DisplayName)).ToList();
    }

    public static ModelCompareResultViewModel ToResult(EvalRun run)
    {
        var result = run.CaseResults[0];
        return new ModelCompareResultViewModel
        {
            ModelId = run.Target.ModelId,
            DisplayName = run.Target.Label ?? run.Target.ModelId,
            Answer = result.Output,
            FirstTokenMs = result.FirstTokenMs ?? 0,
            TotalLatencyMs = result.LatencyMs,
            Usage = result.PromptTokens is { } pt
                ? new ChatTokenUsage(pt, result.CompletionTokens ?? 0, pt + (result.CompletionTokens ?? 0))
                : null,
            Error = result.Error ?? string.Empty
        };
    }
}
