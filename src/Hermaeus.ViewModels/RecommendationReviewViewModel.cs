using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

/// <summary>
/// Shared review-card projection. It deliberately renders stable codes and
/// typed patch values, while Apply, Dismiss, and Undo remain service-owned
/// decisions rather than UI mutations.
/// </summary>
public sealed partial class RecommendationReviewViewModel : ObservableObject
{
    private readonly RecommendationApplicationService _application;
    private readonly Func<Task> _refresh;
    private readonly Action<string>? _navigate;

    public string Id { get; }
    public RecommendationKind Kind { get; }
    public RecommendationEligibility Eligibility { get; }
    public RecommendationStatus Status { get; private set; }
    public string TargetIdentity { get; }
    public string RuleId { get; }
    public string WhyNow { get; }
    public string CurrentSummary { get; }
    public string ProposedSummary { get; }
    public string EvidenceSummary { get; }
    public string TradeoffSummary { get; }
    public string FreshnessSummary { get; }
    public string TargetArea => Kind switch
    {
        RecommendationKind.DefaultModel => "models",
        RecommendationKind.Retest => "lab",
        RecommendationKind.ResourceConflict => "doctor",
        _ => "services"
    };

    public bool CanApply => Status == RecommendationStatus.Current
        && Eligibility == RecommendationEligibility.Actionable
        && string.Equals(Kind == RecommendationKind.RuntimeConfiguration ? "managed-server" : string.Empty,
            ManagedServerRecommendationPatch.TargetDomain, StringComparison.Ordinal);
    public bool CanDismiss => Status == RecommendationStatus.Current;
    public bool CanUndo => Status == RecommendationStatus.Accepted;

    public RecommendationReviewViewModel(
        ConfigurationRecommendation recommendation,
        RecommendationApplicationService application,
        ServerConfig? currentServer,
        Func<Task> refresh,
        Action<string>? navigate = null)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _navigate = navigate;
        Id = recommendation.Id;
        Kind = recommendation.Kind;
        Eligibility = recommendation.Eligibility;
        Status = recommendation.Status;
        TargetIdentity = recommendation.TargetIdentity;
        RuleId = recommendation.RuleId;
        WhyNow = recommendation.ReasonCode;
        CurrentSummary = FormatChanges(recommendation.ProposedPatch.CanonicalJson, currentServer, useProposed: false);
        ProposedSummary = FormatChanges(recommendation.ProposedPatch.CanonicalJson, currentServer, useProposed: true);
        EvidenceSummary = recommendation.Evidence.Count == 0
            ? "No evidence recorded."
            : string.Join("; ", recommendation.Evidence.Select(value =>
                $"{value.EvidenceKind}: {value.State}{(value.Required ? " (required)" : string.Empty)}"));
        TradeoffSummary = recommendation.Tradeoffs.Count == 0
            ? "No trade-offs recorded."
            : string.Join("; ", recommendation.Tradeoffs.Select(value => $"{value.Code}: {value.Value}"));
        FreshnessSummary = recommendation.ExpiresAtUtc is { } expires
            ? $"Evidence evaluated {recommendation.EvaluatedAtUtc.ToLocalTime():g}; expires {expires.ToLocalTime():g}."
            : $"Evidence evaluated {recommendation.EvaluatedAtUtc.ToLocalTime():g}; no expiry was supplied.";
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        await _application.ApplyAsync(Id);
        await _refresh();
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        await _application.DismissAsync(Id);
        await _refresh();
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        await _application.UndoAsync(Id);
        await _refresh();
    }

    [RelayCommand]
    private void InspectTarget() => _navigate?.Invoke(TargetArea);

    private static string FormatChanges(string json, ServerConfig? currentServer, bool useProposed)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var changes = root.TryGetProperty("changes", out var managedChanges)
                && managedChanges.ValueKind == JsonValueKind.Object
                ? managedChanges.EnumerateObject().Select(property => (Key: property.Name, Value: property.Value)).ToArray()
                : root.EnumerateObject()
                    .Where(property => !string.Equals(property.Name, "serverId", StringComparison.Ordinal))
                    .Select(property => (Key: property.Name, Value: property.Value)).ToArray();
            var values = changes.Select(pair =>
            {
                if (!useProposed && currentServer is null)
                    return $"{pair.Key}: unknown";
                var value = useProposed
                    ? pair.Value
                    : CurrentValue(currentServer!, pair.Key);
                return $"{pair.Key}: {FormatValue(value)}";
            });
            var prefix = useProposed ? "Proposed" : "Current";
            return $"{prefix}: {string.Join(", ", values)}";
        }
        catch (InvalidOperationException)
        {
            return useProposed ? "Proposed: unavailable for this target." : "Current: unavailable for this target.";
        }
    }

    private static JsonElement CurrentValue(ServerConfig server, string field)
    {
        object? value = field switch
        {
            "contextSize" => server.ContextSize,
            "gpuPlacement" => server.TryGetGpuPlacement(out var placement, out _) ? placement : null,
            "threads" => server.Threads,
            "promptThreads" => server.PromptThreads,
            "slots" => server.Slots,
            "kvCacheTypeK" => server.KvCacheTypeK,
            "kvCacheTypeV" => server.KvCacheTypeV,
            "flashAttention" => server.FlashAttention,
            "cpuMoeLayers" => server.CpuMoeLayers,
            "speculativeTypes" => (server.Speculative ?? new SpeculativeDecodingConfig()).Types,
            "draftGpuLayers" => (server.Speculative ?? new SpeculativeDecodingConfig()).DraftGpuLayers,
            "speculativeNMax" => (server.Speculative ?? new SpeculativeDecodingConfig()).NMax,
            "speculativeNMin" => (server.Speculative ?? new SpeculativeDecodingConfig()).NMin,
            "speculativePMin" => (server.Speculative ?? new SpeculativeDecodingConfig()).PMin,
            _ => null
        };
        return JsonSerializer.SerializeToElement(value);
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "default",
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join(",", value.EnumerateArray().Select(FormatValue)),
        JsonValueKind.Object when value.TryGetProperty("kind", out var kind) => kind.GetString() ?? value.GetRawText(),
        _ => value.ToString()
    };
}
