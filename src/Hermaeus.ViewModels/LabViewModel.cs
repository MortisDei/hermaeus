using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using System.Text;
using System.Text.Json;

namespace Hermaeus.ViewModels;

public partial class ExperienceRowViewModel : ViewModelBase
{
    public ExperienceRowViewModel(EmpiricalExperience experience, string? labGroupLabel = null)
        : this([experience], labGroupLabel)
    {
    }

    public ExperienceRowViewModel(IReadOnlyList<EmpiricalExperience> experiences, string? labGroupLabel = null)
    {
        if (experiences.Count == 0)
            throw new ArgumentException("An evidence row needs at least one persisted record.", nameof(experiences));

        EvidenceRecords = experiences
            .OrderByDescending(IsCompletionSummary)
            .ThenByDescending(experience => experience.CreatedAtUtc)
            .ToArray();
        Experience = EvidenceRecords[0];
        LabRunId = EvidenceRecords.Select(ReadLabRunId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        LabGroupLabel = labGroupLabel ?? EvidenceRecords.Select(ReadLabGroupLabel)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var completion = Experience.Domain == EmpiricalExperienceDomains.LabRun
            ? TryReadSummary(Experience.ActionJson)
            : null;
        ResultDetails = completion is null
            ? null
            : new LabResultSummaryViewModel(Experience, completion, EvidenceRecords);
    }
    public EmpiricalExperience Experience { get; }
    public IReadOnlyList<EmpiricalExperience> EvidenceRecords { get; }
    public string Id => Experience.Id;
    public string Domain => Experience.Domain;
    public string DisplayDomain => string.IsNullOrWhiteSpace(LabGroupLabel) ? Domain : $"Lab: {LabGroupLabel}";
    public string RecordKindLabel => IsLabCompletionSummary ? "Experiment result" : IsLabEvidenceSlice ? "Configuration evidence" : Domain;
    public string EvidenceRecordLabel => EvidenceRecords.Count == 1
        ? "1 persisted evidence record"
        : $"{EvidenceRecords.Count} persisted records in this experiment execution";
    public string? LabRunId { get; }
    public string LabGroupLabel { get; }
    public string RunIdentityLabel => LabRunId is { Length: > 0 } runId ? $"Run {ShortId(runId)}" : string.Empty;
    public bool IsLabCompletionSummary => ResultDetails is not null;
    public bool IsLabEvidenceSlice => Experience.Domain == EmpiricalExperienceDomains.LabRun
        && !IsLabCompletionSummary && LabRunId is not null;
    public string OutcomeLabel => Experience.Outcome.Outcome.ToString();
    public string OriginLabel => Experience.Provenance.Count == 0
        ? "Unknown"
        : string.Join(", ", Experience.Provenance.Select(p => p.Source.EvidenceOrigin).Distinct());
    public string ScopeLabel => Experience.ProjectId ?? Experience.WorkspaceFingerprint ?? "Unscoped";
    public string CreatedLabel => Experience.CreatedAtUtc.ToLocalTime().ToString("g");
    public string StatusLabel => Experience.Status.ToString();
    public string ContextSummary => SummarizeJson(Experience.ContextJson);
    public string ActionSummary => SummarizeJson(Experience.ActionJson);
    public string ResultSummary => SummarizeLabCompletion(Experience.ActionJson);
    public LabResultSummaryViewModel? ResultDetails { get; }
    [ObservableProperty] private bool _isExportSelected;

    private static bool IsCompletionSummary(EmpiricalExperience experience) =>
        TryReadSummary(experience.ActionJson) is not null;

    private static string SummarizeJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return document.RootElement.ToString();

            var fields = document.RootElement.EnumerateObject()
                .Take(8)
                .Select(property => $"{property.Name}: {FormatValue(property.Value)}")
                .ToArray();
            return fields.Length == 0 ? "No structured fields." : string.Join("; ", fields);
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(json) ? "No detail." : json;
        }

        static string FormatValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => Truncate(value.GetString() ?? string.Empty),
            JsonValueKind.Array => $"[{value.GetArrayLength()} item(s)]",
            JsonValueKind.Object => "{...}",
            JsonValueKind.Null => "null",
            _ => value.ToString()
        };

        static string Truncate(string value) => value.Length <= 120 ? value : value[..117] + "...";
    }

    private static string SummarizeLabCompletion(string json)
    {
        try
        {
            var summary = TryReadSummary(json);
            if (summary is null)
                return string.Empty;

            var configurations = (summary.Configurations ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
            var comparisons = summary.Comparisons ?? [];
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(summary.ExperimentName))
                lines.Add($"Experiment: {summary.ExperimentName}.");
            lines.Add($"Result: {FormatStatus(summary.Status)}.");
            lines.Add($"Tested: {FormatConfigurations(configurations, comparisons, summary.Configurations)}.");
            lines.Add($"Started: {summary.StartedAtUtc.ToLocalTime():g}." +
                (summary.CompletedAtUtc is { } completed ? $" Completed: {completed.ToLocalTime():g}." : string.Empty));

            if (summary.DetailedComparisons is { Count: > 0 } detailedComparisons)
            {
                foreach (var comparison in detailedComparisons)
                    lines.Add(FormatComparison(comparison, configurations));
            }
            else
            {
                foreach (var comparison in comparisons)
                    lines.Add(FormatComparison(comparison, configurations));
            }

            var hasEligibleComparison = summary.DetailedComparisons?.Any(comparison => comparison.CanShowHeadlineDelta)
                ?? comparisons.Any(comparison => comparison.CanShowHeadlineDelta);
            var eligible = comparisons
                .Where(comparison => comparison.CanShowHeadlineDelta)
                .Select(comparison => configurations.TryGetValue(comparison.CandidateConfigurationId, out var configuration)
                    ? configuration.Label : comparison.CandidateConfigurationId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            lines.Add(hasEligibleComparison
                ? eligible.Length == 1
                    ? $"Recommendation: {eligible[0]} is the only correctness-eligible candidate; review it before applying."
                    : "Recommendation: multiple candidates are eligible; no automatic winner was selected."
                : "Recommendation: none; no controlled candidate conclusion was established and Apply remains unavailable.");

            var failures = summary.Failures ?? [];
            if (failures.Count > 0)
                lines.Add($"Failures: {string.Join(" ", failures)}");
            lines.Add($"Evidence: {(summary.EvidenceSliceIds ?? []).Count} immutable configuration slice(s).");
            return string.Join(Environment.NewLine, lines);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string? ReadLabRunId(EmpiricalExperience experience)
    {
        foreach (var json in new[] { experience.ActionJson, experience.ContextJson })
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("runId", out var runId)
                    && runId.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(runId.GetString()))
                    return runId.GetString();

                // Start/failure snapshots use the snapshot's durable `id`, while
                // completion summaries and evidence slices use `runId`. The
                // definition marker keeps a context definition's own id out of
                // execution grouping.
                if (root.TryGetProperty("definition", out var definition)
                    && definition.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("id", out var snapshotId)
                    && snapshotId.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(snapshotId.GetString()))
                    return snapshotId.GetString();
            }
            catch (JsonException) { }
        }
        return null;
    }

    public static string? GetLabRunId(EmpiricalExperience experience) => ReadLabRunId(experience);

    private static LabRunCompletionSummary? TryReadSummary(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("runId", out var runId)
                || runId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(runId.GetString())
                || !root.TryGetProperty("comparisons", out var comparisons)
                || comparisons.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("evidenceSliceIds", out var evidenceSliceIds)
                || evidenceSliceIds.ValueKind != JsonValueKind.Array)
                return null;

            var summary = JsonSerializer.Deserialize<LabRunCompletionSummary>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return summary is { RunId.Length: > 0 } ? summary : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadLabGroupLabel(EmpiricalExperience experience)
    {
        if (!string.Equals(experience.Domain, EmpiricalExperienceDomains.LabRun, StringComparison.Ordinal))
            return null;
        var summary = TryReadSummary(experience.ActionJson);
        if (!string.IsNullOrWhiteSpace(summary?.ExperimentName))
            return summary.ExperimentName;
        return "Lab experiment";
    }

    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8];

    private static string FormatStatus(LabRunStatus status) => status switch
    {
        LabRunStatus.PartiallySucceeded => "Partially succeeded",
        _ => status.ToString()
    };

    private static string FormatConfigurations(IReadOnlyDictionary<string, LabConfiguration> configurations,
        IReadOnlyList<LabComparisonDecision> comparisons, IReadOnlyList<LabConfiguration>? configured)
    {
        var ids = comparisons.SelectMany(item => new[] { item.BaselineConfigurationId, item.CandidateConfigurationId })
            .Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            ids = (configured ?? []).Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? "No configuration comparisons were recorded" : string.Join(", ", ids.Select(Label));

        string Label(string id) => configurations.TryGetValue(id, out var configuration)
            ? $"{configuration.Label} ({id})" : id;
    }

    private static string FormatComparison(LabComparisonDecision comparison,
        IReadOnlyDictionary<string, LabConfiguration> configurations)
    {
        var baseline = configurations.TryGetValue(comparison.BaselineConfigurationId, out var baselineConfiguration)
            ? baselineConfiguration.Label : comparison.BaselineConfigurationId;
        var candidate = configurations.TryGetValue(comparison.CandidateConfigurationId, out var candidateConfiguration)
            ? candidateConfiguration.Label : comparison.CandidateConfigurationId;
        return $"{baseline} vs {candidate}: " + (comparison.CanShowHeadlineDelta
            ? $"correctness passed; {comparison.Equivalence.Detail}"
            : comparison.RefusalReason);
    }

    private static string FormatComparison(LabComparison comparison,
        IReadOnlyDictionary<string, LabConfiguration> configurations)
    {
        if (!comparison.CanShowHeadlineDelta)
            return $"{Label(comparison.BaselineConfigurationId)} vs {Label(comparison.CandidateConfigurationId)}: {comparison.RefusalReason}";

        var deltas = comparison.BaselineMetrics.Join(comparison.CandidateMetrics,
                baseline => baseline.MetricId, candidate => candidate.MetricId,
                (baseline, candidate) => FormatDelta(baseline, candidate))
            .Where(delta => delta.Length > 0).ToArray();
        return $"{Label(comparison.BaselineConfigurationId)} vs {Label(comparison.CandidateConfigurationId)}: correctness passed; {comparison.Equivalence.Detail}" +
            (deltas.Length == 0 ? string.Empty : $" Measurements: {string.Join("; ", deltas)}.");

        string Label(string id) => configurations.TryGetValue(id, out var configuration)
            ? configuration.Label : id;
    }

    private static string FormatDelta(LabMetricSummary baseline, LabMetricSummary candidate)
    {
        if (baseline.Median is not { } before || candidate.Median is not { } after)
            return string.Empty;
        var delta = after - before;
        return $"{candidate.MetricId}: {before:0.##} to {after:0.##} {candidate.Unit} ({delta:+0.##;-0.##;0})";
    }
}

public sealed class LabResultSummaryViewModel
{
    public LabResultSummaryViewModel(EmpiricalExperience experience, LabRunCompletionSummary summary,
        IReadOnlyList<EmpiricalExperience> evidenceRecords)
    {
        RunId = summary.RunId;
        ExperimentLabel = string.IsNullOrWhiteSpace(summary.ExperimentName) ? "Lab experiment" : summary.ExperimentName;
        ModelLabel = !string.IsNullOrWhiteSpace(summary.ModelIdentityLabel)
            ? summary.ModelIdentityLabel
            : evidenceRecords.Select(ReadModelIdentityLabel).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? (!string.IsNullOrWhiteSpace(experience.ModelFingerprint)
                    ? $"Recorded identity {ShortId(experience.ModelFingerprint)}"
                    : "Not recorded");
        ResultStatus = FormatStatus(summary.Status);
        StartedLabel = summary.StartedAtUtc == default ? "Not recorded" : summary.StartedAtUtc.ToLocalTime().ToString("g");
        CompletedLabel = summary.CompletedAtUtc is { } completed ? completed.ToLocalTime().ToString("g") : "Not recorded";
        EvidenceLabel = summary.EvidenceSliceIds.Count == 1
            ? "1 immutable configuration slice"
            : $"{summary.EvidenceSliceIds.Count} immutable configuration slices";
        var failures = summary.Failures ?? [];
        FailuresLabel = failures.Count == 0 ? string.Empty : string.Join(" ", failures);

        var configurations = (summary.Configurations ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var decisions = summary.Comparisons ?? [];
        var comparisons = summary.DetailedComparisons is { Count: > 0 } detailed
            ? detailed.Select(comparison => new LabResultComparisonViewModel(comparison, configurations)).ToArray()
            : decisions
                .Select(decision => new LabResultComparisonViewModel(ToComparison(decision), configurations))
                .ToArray();
        Comparisons = comparisons;
        TestedConfigurations = FormatConfigurations(configurations, decisions, summary.Configurations);

        var eligible = comparisons
            .Where(comparison => comparison.IsEligible)
            .Select(comparison => comparison.CandidateLabel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        RecommendationLabel = eligible.Length switch
        {
            0 => "No recommendation: no correctness-eligible candidate conclusion was established.",
            1 => $"Recommended for review: {eligible[0]} is the only correctness-eligible candidate.",
            _ => "No automatic winner: multiple correctness-eligible candidates remain for review."
        };
    }

    public string RunId { get; }
    public string RunIdentityLabel => $"Run {RunId}";
    public string ExperimentLabel { get; }
    public string ModelLabel { get; }
    public string ResultStatus { get; }
    public string StartedLabel { get; }
    public string CompletedLabel { get; }
    public string TestedConfigurations { get; }
    public string RecommendationLabel { get; }
    public string FailuresLabel { get; }
    public bool HasFailures => FailuresLabel.Length > 0;
    public string EvidenceLabel { get; }
    public IReadOnlyList<LabResultComparisonViewModel> Comparisons { get; }

    private static LabComparison ToComparison(LabComparisonDecision decision) => new()
    {
        BaselineConfigurationId = decision.BaselineConfigurationId,
        CandidateConfigurationId = decision.CandidateConfigurationId,
        IsControlled = decision.IsControlled,
        FingerprintDifferences = decision.FingerprintDifferences,
        Equivalence = decision.Equivalence,
        CorrectnessPassed = decision.CorrectnessPassed,
        CanShowHeadlineDelta = decision.CanShowHeadlineDelta,
        RefusalReason = decision.RefusalReason
    };

    private static string? ReadModelIdentityLabel(EmpiricalExperience experience)
    {
        if (!string.Equals(experience.Domain, EmpiricalExperienceDomains.LabRun, StringComparison.Ordinal))
            return null;

        try
        {
            using var document = JsonDocument.Parse(experience.ActionJson);
            if (!document.RootElement.TryGetProperty("definition", out _))
                return null;

            var run = JsonSerializer.Deserialize<LabRunSnapshot>(experience.ActionJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var model = run?.Definition?.ProfileFingerprint?.Model;
            return model is null ? null : DescribeModelIdentity(model);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DescribeModelIdentity(ModelIdentityV2 model)
    {
        if (!string.IsNullOrWhiteSpace(model.ManifestIdentity))
            return Truncate(model.ManifestIdentity);

        var descriptor = string.Join(" · ", new[] { model.Architecture, model.Quantization }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(descriptor) ? null : Truncate(descriptor);
    }

    private static string FormatConfigurations(IReadOnlyDictionary<string, LabConfiguration> configurations,
        IReadOnlyList<LabComparisonDecision> comparisons, IReadOnlyList<LabConfiguration>? configured)
    {
        var ids = comparisons.SelectMany(item => new[] { item.BaselineConfigurationId, item.CandidateConfigurationId })
            .Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            ids = (configured ?? []).Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? "No configuration comparisons were recorded" : string.Join(", ", ids.Select(Label));

        string Label(string id) => configurations.TryGetValue(id, out var configuration)
            ? $"{configuration.Label} ({id})" : id;
    }

    private static string FormatStatus(LabRunStatus status) => status switch
    {
        LabRunStatus.PartiallySucceeded => "Partially succeeded",
        _ => status.ToString()
    };

    private static string ShortId(string value) => value.Length <= 12 ? value : value[..12];
    private static string Truncate(string value) => value.Length <= 128 ? value : value[..125] + "...";
}

public sealed class LabResultComparisonViewModel
{
    public LabResultComparisonViewModel(LabComparison comparison,
        IReadOnlyDictionary<string, LabConfiguration> configurations)
    {
        BaselineLabel = configurations.TryGetValue(comparison.BaselineConfigurationId, out var baseline)
            ? baseline.Label : comparison.BaselineConfigurationId;
        CandidateLabel = configurations.TryGetValue(comparison.CandidateConfigurationId, out var candidate)
            ? candidate.Label : comparison.CandidateConfigurationId;
        ConfigurationLabel = $"{BaselineLabel} vs {CandidateLabel}";
        ThroughputLabel = FormatMetricPair(
            comparison.BaselineMetrics.FirstOrDefault(metric => metric.MetricId == "decode.tokens_per_second"),
            comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "decode.tokens_per_second"));
        RamLabel = FormatMemoryPair(comparison.BaselineMetrics, comparison.CandidateMetrics,
            "memory.ram.observed", "memory.ram.predicted");
        VramLabel = FormatMemoryPair(comparison.BaselineMetrics, comparison.CandidateMetrics,
            "memory.gpu.observed", "memory.gpu.predicted");
        IsEligible = comparison.CanShowHeadlineDelta;
        CorrectnessLabel = comparison.CorrectnessPassed
            ? "Passed"
            : comparison.Equivalence.State == LabEquivalenceState.Different ? "Failed" : "Not established";
        CorrectnessDetail = comparison.CanShowHeadlineDelta
            ? comparison.Equivalence.Detail
            : comparison.RefusalReason;
    }

    public string BaselineLabel { get; }
    public string CandidateLabel { get; }
    public string ConfigurationLabel { get; }
    public string ThroughputLabel { get; }
    public string RamLabel { get; }
    public string VramLabel { get; }
    public bool IsEligible { get; }
    public string CorrectnessLabel { get; }
    public string CorrectnessDetail { get; }

    private static string FormatMetricPair(LabMetricSummary? baseline, LabMetricSummary? candidate)
    {
        var before = baseline?.Median;
        var after = candidate?.Median;
        if (before is null && after is null)
            return "Unknown";

        var unit = candidate?.Unit ?? baseline?.Unit ?? string.Empty;
        var result = $"{FormatNumber(before)} -> {FormatNumber(after)} {unit}".TrimEnd();
        if (before is { } beforeValue && after is { } afterValue)
        {
            var delta = afterValue - beforeValue;
            var percentage = beforeValue > 0 ? $", {delta / beforeValue:+0.0%;-0.0%;0.0%}" : string.Empty;
            result += $" ({delta:+0.##;-0.##;0} {unit}{percentage})".TrimEnd();
        }
        return result;
    }

    private static string FormatMemoryPair(IReadOnlyList<LabMetricSummary> baselineMetrics,
        IReadOnlyList<LabMetricSummary> candidateMetrics, string observedId, string predictedId)
    {
        var before = SelectMemory(baselineMetrics, observedId, predictedId);
        var after = SelectMemory(candidateMetrics, observedId, predictedId);
        if (before.Value is null && after.Value is null)
            return "Unknown";

        var result = $"{FormatNumber(before.Value)} -> {FormatNumber(after.Value)} GiB";
        if (before.Value is { } beforeValue && after.Value is { } afterValue)
        {
            var delta = afterValue - beforeValue;
            var percentage = beforeValue > 0 ? $", {delta / beforeValue:+0.0%;-0.0%;0.0%}" : string.Empty;
            result += $" ({delta:+0.##;-0.##;0} GiB{percentage})";
        }

        var source = before.Source == after.Source
            ? before.Source
            : $"{before.Source} -> {after.Source}";
        return string.IsNullOrWhiteSpace(source) ? result : $"{result}; {source}";
    }

    private static (double? Value, string Source) SelectMemory(IReadOnlyList<LabMetricSummary> metrics,
        string observedId, string predictedId)
    {
        var observed = metrics.FirstOrDefault(metric => metric.MetricId == observedId);
        if (observed is not null && (observed.Maximum ?? observed.Median) is { } observedValue)
            return (observedValue / 1024 / 1024 / 1024, "observed peak");

        var predicted = metrics.FirstOrDefault(metric => metric.MetricId == predictedId);
        return predicted?.Median is { } predictedValue
            ? (predictedValue / 1024 / 1024 / 1024, "predicted")
            : (null, string.Empty);
    }

    private static string FormatNumber(double? value) => value?.ToString("0.##") ?? "Unknown";
}

public sealed class LabRecipeRowViewModel
{
    public LabRecipeRowViewModel(LabRecipePlan plan) => Plan = plan;
    public LabRecipePlan Plan { get; }
    public string Label => Plan.Label;
    public string AvailabilityLabel => Plan.Availability.ToString();
    public string Detail => Plan.AvailabilityDetail;
    public string CandidateLabel => $"Baseline + {Plan.Candidates.Count} candidate(s), max {Plan.MaximumRunCount} runs";
    public bool CanRun => Plan.Availability == CapabilityState.Available;
}

public partial class LabViewModel : ViewModelBase
{
    private readonly IEmpiricalExperienceStore _store;
    private readonly IToastService _toasts;
    private readonly ILabExperimentService? _experiments;
    private readonly ILabRecipeService? _recipes;
    private readonly ISettingsService? _settings;
    private readonly ServicesViewModel? _services;
    private readonly RecommendationDerivationService? _recommendationDerivation;
    private readonly RecommendationApplicationService? _recommendationApplication;
    private string? _reviewRecommendationId;

    public LabViewModel(IEmpiricalExperienceStore store, IToastService toasts)
        : this(store, toasts, null, null, null)
    {
    }

    public LabViewModel(IEmpiricalExperienceStore store, IToastService toasts,
        ILabExperimentService? experiments, ISettingsService? settings, ILabRecipeService? recipes,
        ServicesViewModel? services = null,
        RecommendationDerivationService? recommendationDerivation = null,
        RecommendationApplicationService? recommendationApplication = null)
    {
        _store = store;
        _toasts = toasts;
        _experiments = experiments;
        _settings = settings;
        _recipes = recipes;
        _services = services;
        _recommendationDerivation = recommendationDerivation;
        _recommendationApplication = recommendationApplication;
        if (_services is not null)
            _services.ServerAvailabilityChanged += OnServicesAvailabilityChanged;

        RefreshConfiguredServers();
        if (SelectedServer is not null)
            CandidateContextSize = SelectedServer.ContextSize;
    }

    public UiBoundCollection<ExperienceRowViewModel> Experiences { get; } = [];
    public IReadOnlyList<string> DomainOptions { get; } = ["All", .. EmpiricalExperienceDomains.Initial.OrderBy(x => x)];
    public IReadOnlyList<string> OutcomeOptions { get; } = ["All", .. Enum.GetNames<NormalizedOutcome>()];
    public IReadOnlyList<string> OriginOptions { get; } = ["All", .. Enum.GetNames<EvidenceOrigin>()];
    public IReadOnlyList<string> StatusOptions { get; } = ["All", .. Enum.GetNames<EmpiricalExperienceStatus>()];

    [ObservableProperty] private string _domainFilter = "All";
    [ObservableProperty] private string _outcomeFilter = "All";
    [ObservableProperty] private string _originFilter = "All";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _projectFilter = string.Empty;
    [ObservableProperty] private string _workspaceFilter = string.Empty;
    [ObservableProperty] private string _modelFilter = string.Empty;
    [ObservableProperty] private string _runtimeFilter = string.Empty;
    [ObservableProperty] private DateTimeOffset? _createdFrom;
    [ObservableProperty] private DateTimeOffset? _createdTo;
    [ObservableProperty] private ExperienceRowViewModel? _selectedExperience;
    [ObservableProperty] private string _correctionOutcome = nameof(NormalizedOutcome.Unknown);
    [ObservableProperty] private string _correctionDetail = string.Empty;
    [ObservableProperty] private string _exportJson = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private ServerConfig? _selectedServer;
    [ObservableProperty] private string _experimentName = "Isolated baseline";
    [ObservableProperty] private int _candidateContextSize = 4096;
    [ObservableProperty] private string _definitionPreview = string.Empty;
    [ObservableProperty] private string _runStatus = "Not started";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestoreFailure))]
    private string _restoreStatus = "Not required";
    [ObservableProperty] private string _runtimeIsolation = "No Lab runtime is active.";
    [ObservableProperty] private string _comparisonSummary = string.Empty;
    [ObservableProperty] private string _applyReviewSummary = string.Empty;
    [ObservableProperty] private bool _isRunActive;
    [ObservableProperty] private LabRecipeRowViewModel? _selectedRecipe;
    [ObservableProperty] private string _recipePrompt = "Reply with exactly: Hermaeus Lab.";
    [ObservableProperty] private bool _isRecipeRunning;
    [ObservableProperty] private string _tradeoffSummary = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoAppliedRecommendation))]
    private string _appliedRecommendationId = string.Empty;

    private LabRunSnapshot? _currentRun;
    private LabApplyReview? _applyReview;
    private CancellationTokenSource? _recipeCts;
    private TaskCompletionSource<bool>? _recipeCompletion;
    private IReadOnlyList<string> _suspendedSourceServers = [];
    private readonly Dictionary<string, string> _suspendedSourceConfigurationFingerprints = new(StringComparer.Ordinal);

    public bool HasSelection => SelectedExperience is not null;
    public bool HasRestoreFailure => RestoreStatus.StartsWith("Failed:", StringComparison.Ordinal)
        || RestoreStatus.StartsWith("Blocked:", StringComparison.Ordinal);
    [ObservableProperty] private bool _hasAnyEvidence;
    public string EvidenceEmptyState => HasAnyEvidence
        ? "No evidence matches these filters."
        : "No evidence has been captured yet.";
    public string EvidenceEmptyHint => HasAnyEvidence
        ? "Clear or broaden the filters to inspect the evidence already captured."
        : "Run an isolated experiment or guided recipe to capture the first evidence record.";
    private readonly UiBoundCollection<ServerConfig> _configuredServers = [];
    public IReadOnlyList<ServerConfig> ConfiguredServers => _configuredServers;
    public string ConfiguredServerHint => ConfiguredServers.Count switch
    {
        0 => "No configured Chat server is available. Save a non-embedding managed server on Services first.",
        1 => "Using the only configured Chat server.",
        _ => "Choose the configured Chat server used as the Lab source."
    };
    public bool HasMultipleConfiguredServers => ConfiguredServers.Count > 1;
    public UiBoundCollection<LabRecipeRowViewModel> RecipeOptions { get; } = [];
    public Func<EmpiricalExperience, Task<bool>>? ConfirmRemoval { get; set; }
    public Func<LabApplyReview, Task<bool>>? ConfirmApply { get; set; }
    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }
    public bool CanStartRun => !IsRunActive && !IsRecipeRunning && !IsBusy;
    public bool CanRunRecipe => !IsRunActive && !IsRecipeRunning && !IsBusy;
    public bool CanReviewCurrentRun => GetReviewRun() is
        { Status: LabRunStatus.Succeeded or LabRunStatus.PartiallySucceeded } run
        && run.Comparisons.Any(comparison => comparison.CanShowHeadlineDelta);
    public bool CanConfirmApply => _applyReview?.CanApply == true && ConfirmApply is not null;
    public bool CanUndoAppliedRecommendation => _recommendationApplication is not null
        && !string.IsNullOrWhiteSpace(AppliedRecommendationId);

    partial void OnSelectedServerChanged(ServerConfig? value)
    {
        if (value is not null) CandidateContextSize = value.ContextSize;
        OnPropertyChanged(nameof(HasMultipleConfiguredServers));
    }

    partial void OnIsRunActiveChanged(bool value) => NotifyRunCommands();
    partial void OnIsRecipeRunningChanged(bool value) => NotifyRunCommands();
    partial void OnIsBusyChanged(bool value) => NotifyRunCommands();

    private void NotifyRunCommands()
    {
        FreezeAndStartCommand.NotifyCanExecuteChanged();
        RunSelectedRecipeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(CanRunRecipe));
    }

    private void OnServicesAvailabilityChanged(object? sender, EventArgs e) => RunOnUi(RefreshConfiguredServers);

    private void RefreshConfiguredServers()
    {
        var selectedId = SelectedServer?.Id;
        var servers = _services is null
            ? _settings?.Settings.ManagedServers.Where(server => !server.EmbeddingsMode)
            : _services.Servers.Where(server => !server.EmbeddingsMode).Select(server => server.BuildConfig());

        _configuredServers.Clear();
        if (servers is not null)
        {
            foreach (var server in servers)
                _configuredServers.Add(server);
        }

        SelectedServer = _configuredServers.FirstOrDefault(server => string.Equals(server.Id, selectedId, StringComparison.Ordinal))
            ?? _configuredServers.FirstOrDefault();
        OnPropertyChanged(nameof(ConfiguredServerHint));
        OnPropertyChanged(nameof(HasMultipleConfiguredServers));
    }

    partial void OnSelectedExperienceChanged(ExperienceRowViewModel? value)
    {
        _applyReview = null;
        ApplyReviewSummary = string.Empty;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanReviewCurrentRun));
        OnPropertyChanged(nameof(CanConfirmApply));
        if (value is null) return;
        CorrectionOutcome = value.Experience.Outcome.Outcome.ToString();
        CorrectionDetail = value.Experience.Outcome.Detail;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            StatusMessage = "Lab is busy; evidence will refresh when the current operation finishes.";
            return;
        }
        IsBusy = true;
        try
        {
            await RefreshEvidenceCoreAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not load evidence", ex.Message, ToastKind.Error, 5000); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CorrectSelectedAsync()
    {
        if (SelectedExperience?.Experience is not { } prior || !Enum.TryParse<NormalizedOutcome>(CorrectionOutcome, out var outcome)) return;
        try
        {
            var provenance = prior.Provenance.Take(15).Concat([
                new EmpiricalExperienceProvenance($"correction:{prior.Id}", new SourceReference(
                    ProvenanceKind.Experience, "User correction", prior.Id, EvidenceOrigin: EvidenceOrigin.UserProvided))
            ]).ToArray();
            await _store.CorrectAsync(prior.Id, new EmpiricalExperienceDraft
            {
                SchemaVersion = prior.SchemaVersion, Domain = prior.Domain, ProjectId = prior.ProjectId,
                WorkspaceFingerprint = prior.WorkspaceFingerprint, ContextJson = prior.ContextJson, ActionJson = prior.ActionJson,
                RuntimeFingerprint = prior.RuntimeFingerprint, ModelFingerprint = prior.ModelFingerprint, Provenance = provenance,
                Outcome = NormalizedToolOutcome.Create(outcome, "user-correction", CorrectionDetail)
            });
            await RefreshAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not correct evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedExperience?.Experience is not { } selected) return;
        if (ConfirmRemoval is null || !await ConfirmRemoval(selected)) return;
        try { await _store.RemoveAsync(selected.Id); await RefreshAsync(); }
        catch (Exception ex) { _toasts.Show("Could not remove evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var ids = Experiences.Where(x => x.IsExportSelected)
            .SelectMany(x => x.EvidenceRecords)
            .Select(x => x.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0 && SelectedExperience is not null)
            ids = SelectedExperience.EvidenceRecords.Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        try { ExportJson = await _store.ExportAsync(ids); StatusMessage = $"Prepared {ids.Length} record(s) for copy or save."; }
        catch (Exception ex) { _toasts.Show("Could not export evidence", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task CopyEvidenceDetailAsync()
    {
        if (SelectedExperience is null)
            return;
        if (RequestCopyToClipboard is null)
        {
            StatusMessage = "Clipboard access is unavailable in this session.";
            return;
        }

        var copied = await RequestCopyToClipboard(BuildEvidenceDetail(SelectedExperience));
        StatusMessage = copied
            ? "Copied Lab evidence detail."
            : "Could not copy Lab evidence detail.";
    }

    internal static string BuildEvidenceDetail(ExperienceRowViewModel row)
    {
        var text = new StringBuilder();
        text.AppendLine("Lab evidence detail");
        text.AppendLine($"Record: {row.Id}");
        text.AppendLine($"Domain: {row.Domain}");
        text.AppendLine($"Outcome: {row.OutcomeLabel}");
        text.AppendLine($"Status: {row.StatusLabel}");
        text.AppendLine();
        foreach (var evidence in row.EvidenceRecords)
        {
            text.AppendLine($"Evidence {evidence.Id}");
            text.AppendLine($"Created: {evidence.CreatedAtUtc:O}");
            text.AppendLine($"Domain: {evidence.Domain}");
            text.AppendLine($"Outcome: {evidence.Outcome.Outcome} - {evidence.Outcome.Detail}");
            text.AppendLine($"Context: {evidence.ContextJson}");
            text.AppendLine($"Action: {evidence.ActionJson}");
            foreach (var provenance in evidence.Provenance)
                text.AppendLine($"Source: {provenance.Source.Title} ({provenance.Source.EvidenceOrigin})");
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task RefreshRecipesAsync()
    {
        if (_recipes is null)
        {
            StatusMessage = "Lab recipes are unavailable in this session.";
            return;
        }
        if (SelectedServer is null)
        {
            StatusMessage = "Select a configured Chat server before inspecting recipes.";
            return;
        }
        if (IsRecipeRunning || IsRunActive)
        {
            StatusMessage = "Another Lab run is already active.";
            return;
        }
        try
        {
            var plans = await _recipes.InspectAsync(SelectedServer);
            RecipeOptions.Clear();
            foreach (var plan in plans) RecipeOptions.Add(new LabRecipeRowViewModel(plan));
            SelectedRecipe = RecipeOptions.FirstOrDefault(row => row.CanRun) ?? RecipeOptions.FirstOrDefault();
            StatusMessage = RecipeOptions.Count == 0 ? "No recipes are available for this runtime." : $"{RecipeOptions.Count} recipe(s) inspected.";
        }
        catch (Exception ex) { _toasts.Show("Could not inspect Lab recipes", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand(CanExecute = nameof(CanRunRecipe))]
    private async Task RunSelectedRecipeAsync()
    {
        if (_recipes is null)
        {
            StatusMessage = "Lab recipes are unavailable in this session.";
            return;
        }
        if (SelectedServer is null)
        {
            StatusMessage = "Select a configured Chat server before running a recipe.";
            return;
        }
        if (SelectedRecipe?.CanRun != true)
        {
            StatusMessage = "The selected recipe is unavailable for this exact runtime.";
            return;
        }
        if (IsRecipeRunning || IsRunActive)
        {
            StatusMessage = "Another Lab run is already active.";
            return;
        }
        _recipeCts = new CancellationTokenSource();
        _recipeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRecipeRunning = true;
        IsBusy = true;
        try
        {
            if (_suspendedSourceServers.Count == 0)
                RestoreStatus = "Not required";
            await SuspendSelectedSourceAsync();
            _currentRun = await _recipes.RunAsync(SelectedRecipe.Plan, SelectedServer, RecipePrompt, _recipeCts.Token);
            ShowCompletedRun(_currentRun);
            TradeoffSummary = BuildTradeoffSummary(_currentRun);
            var failureMessage = _currentRun.Status == LabRunStatus.Failed
                ? $"Lab recipe failed: {_currentRun.Failures.FirstOrDefault() ?? "The Lab run failed without a detail."}"
                : null;
            await RefreshEvidenceCoreAsync(failureMessage);
        }
        catch (OperationCanceledException) when (_recipeCts?.IsCancellationRequested == true)
        {
            RunStatus = "Cancelled";
            StatusMessage = "Lab recipe cancelled; any captured evidence was retained.";
            await RefreshEvidenceCoreAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RunStatus = "Failed";
            var failureMessage = $"Lab recipe failed: {ex.Message}";
            StatusMessage = failureMessage;
            _toasts.Show("Lab recipe failed", ex.Message, ToastKind.Error, 5000);
            try { await RefreshEvidenceCoreAsync(failureMessage); }
            catch (Exception refreshEx)
            {
                _toasts.Show("Could not refresh Lab evidence", refreshEx.Message, ToastKind.Warning, 5000);
                StatusMessage = failureMessage;
            }
        }
        finally
        {
            await RestoreSuspendedSourceAsync();
            IsRecipeRunning = false;
            IsBusy = false;
            _recipeCts.Dispose();
            _recipeCts = null;
            _recipeCompletion?.TrySetResult(true);
            _recipeCompletion = null;
        }
    }

    public async Task ShutdownAsync()
    {
        _recipeCts?.Cancel();
        if (_recipeCompletion?.Task is { } recipeCompletion)
            await Task.WhenAny(recipeCompletion, Task.Delay(TimeSpan.FromSeconds(10)));

        if (_experiments is not null && _currentRun?.Status == LabRunStatus.Running)
        {
            try
            {
                _currentRun = await _experiments.CancelAsync(_currentRun.Id);
                ShowCompletedRun(_currentRun);
            }
            catch (Exception ex)
            {
                _toasts.Show("Could not stop Lab", ex.Message, ToastKind.Warning, 7000);
            }
        }

        await RestoreSuspendedSourceAsync();
    }

    [RelayCommand]
    private void CancelRecipe()
    {
        if (_recipeCts is null)
        {
            StatusMessage = "No Lab recipe is running.";
            return;
        }
        StatusMessage = "Cancelling Lab recipe...";
        _recipeCts.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private async Task FreezeAndStartAsync()
    {
        if (_experiments is null)
        {
            StatusMessage = "Lab experiments are unavailable in this session.";
            return;
        }
        if (SelectedServer is null)
        {
            StatusMessage = "Select a configured Chat server before starting a Lab run.";
            return;
        }
        if (IsRunActive || IsRecipeRunning)
        {
            StatusMessage = "Another Lab run is already active.";
            return;
        }
        IsBusy = true;
        try
        {
            if (_suspendedSourceServers.Count == 0)
                RestoreStatus = "Not required";
            await SuspendSelectedSourceAsync();
            var baseline = ConfigurationFrom(SelectedServer, "baseline", "Baseline");
            var candidate = baseline with { Id = "candidate-1", Label = "Candidate", ContextSize = CandidateContextSize };
            var definition = await _experiments.CreateDefinitionAsync(
                ExperimentName, "isolated-runtime-v1", SelectedServer, baseline, [candidate], 1,
                LabCorrectnessRequirement.ExactEquivalence);
            DefinitionPreview = definition.CanonicalJson();
            _currentRun = await _experiments.StartAsync(definition, SelectedServer);
            RunStatus = _currentRun.Status.ToString();
            IsRunActive = _currentRun.Status == LabRunStatus.Running;
            RuntimeIsolation = _currentRun.TemporaryPort is int port
                ? $"Dedicated loopback runtime on 127.0.0.1:{port}. Saved Services settings are unchanged."
                : _currentRun.Failures.FirstOrDefault() ?? "The isolated runtime did not start.";
            var failureMessage = _currentRun.Status == LabRunStatus.Failed
                ? $"Lab run failed: {_currentRun.Failures.FirstOrDefault() ?? "The isolated runtime did not start."}"
                : null;
            await RefreshEvidenceCoreAsync(failureMessage);
            if (!IsRunActive)
                await RestoreSuspendedSourceAsync();
        }
        catch (Exception ex)
        {
            RunStatus = "Failed";
            StatusMessage = $"Lab run failed: {ex.Message}";
            _toasts.Show("Could not start Lab run", ex.Message, ToastKind.Error, 5000);
            await CancelActiveRunAfterFailureAsync();
            await RestoreSuspendedSourceAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task CancelActiveRunAfterFailureAsync()
    {
        if (_experiments is null || !IsRunActive || _currentRun?.Status != LabRunStatus.Running)
            return;

        try
        {
            _currentRun = await _experiments.CancelAsync(_currentRun.Id);
            ShowCompletedRun(_currentRun);
        }
        catch (Exception ex)
        {
            _toasts.Show("Could not clean up failed Lab run", ex.Message, ToastKind.Warning, 7000);
        }
    }

    private async Task RefreshEvidenceCoreAsync(string? statusMessage = null)
    {
        var query = new EmpiricalExperienceQuery
        {
            Domain = Choice(DomainFilter), ProjectId = Text(ProjectFilter), WorkspaceFingerprint = Text(WorkspaceFilter),
            ModelFingerprint = Text(ModelFilter), RuntimeFingerprint = Text(RuntimeFilter),
            Outcome = ParseChoice<NormalizedOutcome>(OutcomeFilter), Origin = ParseChoice<EvidenceOrigin>(OriginFilter),
            Status = ParseChoice<EmpiricalExperienceStatus>(StatusFilter),
            CreatedFromUtc = CreatedFrom?.UtcDateTime, CreatedToUtc = CreatedTo?.UtcDateTime, Limit = 500
        };
        var rows = await _store.QueryAsync(query);
        HasAnyEvidence = rows.Count > 0 || (await _store.QueryAsync(new EmpiricalExperienceQuery { Limit = 1 })).Count > 0;
        // Rebuilding the ItemsSource does not reliably clear a two-way ListBox
        // selection. Clear it explicitly so the first newly-created row always
        // hydrates every selected-result binding, including nested result data.
        SelectedExperience = null;
        Experiences.Clear();
        foreach (var group in rows.GroupBy(row => EvidenceExecutionKey(row), StringComparer.Ordinal))
        {
            Experiences.Add(new ExperienceRowViewModel(group.ToArray()));
        }
        SelectedExperience = Experiences.FirstOrDefault();
        OnPropertyChanged(nameof(EvidenceEmptyState));
        OnPropertyChanged(nameof(EvidenceEmptyHint));
        StatusMessage = statusMessage ?? (rows.Count == 0
            ? "No evidence matches these filters."
            : $"{Experiences.Count} execution/result entry(ies) from {rows.Count} persisted evidence record(s).");

        static string EvidenceExecutionKey(EmpiricalExperience row) =>
            row.Domain == EmpiricalExperienceDomains.LabRun
            && ExperienceRowViewModel.GetLabRunId(row) is { } runId
                ? $"lab:{runId}"
                : $"experience:{row.Id}";
    }

    [RelayCommand]
    private async Task CompleteRunAsync()
    {
        if (_experiments is null || _currentRun?.Status != LabRunStatus.Running) return;
        try
        {
            var fingerprint = _currentRun.Definition.ProfileFingerprint;
            var observation = new LabObservation
            {
                RunId = _currentRun.Id, ConfigurationId = _currentRun.Definition.Baseline.Id,
                CaseId = "runtime-start", Repetition = 0, MetricId = "runtime.ready", Value = 1,
                Unit = "boolean", Source = "isolated-runtime-health", Trust = "TrustedRuntime",
                RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
                HardwareFingerprint = fingerprint.Hardware.StableId,
                ConfigurationFingerprint = _currentRun.Definition.ConfigurationFingerprints[_currentRun.Definition.Baseline.Id]
            };
            _currentRun = await _experiments.CompleteAsync(_currentRun.Id, [observation], []);
            ShowCompletedRun(_currentRun);
            await RefreshAsync();
            await RestoreSuspendedSourceAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not complete Lab run", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task CancelRunAsync()
    {
        if (_experiments is null || _currentRun is null || !IsRunActive) return;
        try
        {
            _currentRun = await _experiments.CancelAsync(_currentRun.Id);
            ShowCompletedRun(_currentRun);
            await RefreshAsync();
            await RestoreSuspendedSourceAsync();
        }
        catch (Exception ex) { _toasts.Show("Could not cancel Lab run", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task ReviewApplyAsync()
    {
        var run = GetReviewRun();
        if (_experiments is null || run is null)
        {
            ApplyReviewSummary = "This historical evidence is read-only. Review the current in-memory run before applying settings.";
            OnPropertyChanged(nameof(CanConfirmApply));
            return;
        }
        try
        {
            var candidateId = run.Comparisons
                .FirstOrDefault(comparison => comparison.CanShowHeadlineDelta)?.CandidateConfigurationId
                ?? run.Definition.Candidates.FirstOrDefault()?.Id;
            if (candidateId is null)
            {
                ApplyReviewSummary = "The completed Lab run has no candidate to review.";
                OnPropertyChanged(nameof(CanConfirmApply));
                return;
            }

            _applyReview = _experiments.CreateApplyReview(run.Id, candidateId);
            _reviewRecommendationId = null;
            try
            {
                if (_recommendationDerivation is not null && _recommendationApplication is not null
                    && _settings is not null && _settings.Settings.ManagedServers.FirstOrDefault(server => server.Id == run.Definition.TargetServerId) is { } current
                    && run.Definition.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) is { } candidate
                    && run.Comparisons.FirstOrDefault(comparison => comparison.CandidateConfigurationId == candidateId) is { } comparison
                    && comparison.CanShowHeadlineDelta)
                {
                    var proposed = _settings.Settings.Clone().ManagedServers.First(server => server.Id == current.Id);
                    LabConfigurationMapper.ApplyTo(proposed, candidate);
                    var currentIdentity = ConfigurationIdentityFactory.Create(current);
                    var evaluatedAt = run.CompletedAtUtc ?? DateTime.UtcNow;
                    var evidenceId = string.IsNullOrWhiteSpace(run.CompletionEvidenceId)
                        ? $"lab-completion-{run.Id}"
                        : run.CompletionEvidenceId;
                    var recommendation = await _recommendationDerivation.DeriveAsync(new RecommendationProposal(
                        RecommendationKind.RuntimeConfiguration,
                        current.Id,
                        currentIdentity.StableId,
                        ManagedServerRecommendationPatch.Create(current.Id, current, proposed),
                        [new RecommendationEvidenceReference(
                            evidenceId,
                            "lab-correctness-gated-comparison",
                            Required: true,
                            run.Definition.ProfileFingerprint.Completeness == IdentityCompleteness.Complete
                                ? CapabilityState.Available : CapabilityState.Unknown,
                            evaluatedAt,
                            TimeSpan.FromDays(30))],
                        [new RecommendationCondition("candidate", candidate.Id),
                            new RecommendationCondition("correctness", "passed")],
                        [new RecommendationTradeoff("restart", "requires-explicit-restart")],
                        "review-lab-winner",
                        1,
                        "lab-correctness-gated-winner",
                        evaluatedAt,
                        currentIdentity.Completeness == IdentityCompleteness.Complete,
                        TargetExists: true,
                        RequiredEvidenceRevoked: false,
                        Contradicted: false,
                        RequiredEvidenceExpired: false,
                        MinimumFactsComplete: run.Definition.ProfileFingerprint.Completeness == IdentityCompleteness.Complete,
                        Actionable: true,
                        ExpiresAtUtc: evaluatedAt.AddDays(30)));
                    if (recommendation.Eligibility == RecommendationEligibility.Actionable)
                        _reviewRecommendationId = recommendation.Id;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            {
                _reviewRecommendationId = null;
            }
            ApplyReviewSummary = _applyReview.CanApply
                ? "Review ready. No settings have been saved." + Environment.NewLine
                    + string.Join(Environment.NewLine, _applyReview.Changes.Select(change => $"{change.Field}: {change.CurrentValue} -> {change.ProposedValue}"))
                    + (_reviewRecommendationId is null ? string.Empty : Environment.NewLine + $"Recommendation {_reviewRecommendationId} is ready for explicit Apply.")
                : _applyReview.RefusalReason;
            OnPropertyChanged(nameof(CanConfirmApply));
        }
        catch (Exception ex)
        {
            _applyReview = null;
            OnPropertyChanged(nameof(CanConfirmApply));
            _toasts.Show("Could not review Lab result", ex.Message, ToastKind.Error, 5000);
        }
    }

    [RelayCommand]
    private async Task ConfirmApplyAsync()
    {
        var review = _applyReview;
        var selectedRunId = SelectedExperience?.LabRunId;
        if (_experiments is null || review?.CanApply != true || ConfirmApply is null
            || !await ConfirmApply(review)) return;

        if (!ReferenceEquals(_applyReview, review)
            || !string.Equals(SelectedExperience?.LabRunId, selectedRunId, StringComparison.Ordinal))
        {
            ApplyReviewSummary = "The selected evidence changed while Apply was being confirmed. Review the current selection again.";
            OnPropertyChanged(nameof(CanConfirmApply));
            return;
        }

        try
        {
            if (_reviewRecommendationId is not null && _recommendationApplication is not null)
            {
                var result = await _recommendationApplication.ApplyAsync(_reviewRecommendationId);
                if (!result.Succeeded)
                    throw new InvalidOperationException(result.Message);
                AppliedRecommendationId = _reviewRecommendationId;
            }
            else
            {
                await _experiments.ApplyAsync(review);
            }
            ApplyReviewSummary = "Reviewed fields were saved through the normal Settings flow.";
            _applyReview = null;
            _reviewRecommendationId = null;
            OnPropertyChanged(nameof(CanConfirmApply));
            OnPropertyChanged(nameof(CanUndoAppliedRecommendation));
        }
        catch (Exception ex) { _toasts.Show("Could not apply Lab result", ex.Message, ToastKind.Error, 5000); }
    }

    [RelayCommand]
    private async Task UndoAppliedRecommendationAsync()
    {
        if (_recommendationApplication is null || string.IsNullOrWhiteSpace(AppliedRecommendationId))
            return;
        try
        {
            var result = await _recommendationApplication.UndoAsync(AppliedRecommendationId);
            if (!result.Succeeded)
                throw new InvalidOperationException(result.Message);
            AppliedRecommendationId = string.Empty;
            ApplyReviewSummary = "The reviewed settings were restored. Any running server remains unchanged.";
            OnPropertyChanged(nameof(CanUndoAppliedRecommendation));
        }
        catch (Exception ex) { _toasts.Show("Could not undo Lab result", ex.Message, ToastKind.Error, 5000); }
    }

    private LabRunSnapshot? GetReviewRun()
    {
        if (_experiments is null)
            return null;

        var selectedRunId = SelectedExperience?.LabRunId;
        return selectedRunId is not null
            ? _experiments.GetRun(selectedRunId)
            : _currentRun;
    }

    private void ShowCompletedRun(LabRunSnapshot run)
    {
        RunStatus = run.Status.ToString();
        IsRunActive = false;
        OnPropertyChanged(nameof(CanReviewCurrentRun));
        RuntimeIsolation = "The dedicated Lab runtime is stopped and its ownership record is cleaned up.";
        ComparisonSummary = run.Comparisons.Count == 0
            ? "No controlled comparison was produced."
            : string.Join(Environment.NewLine, run.Comparisons.Select(comparison => comparison.CanShowHeadlineDelta
                ? $"{comparison.CandidateConfigurationId}: controlled and correctness-gated"
                : $"{comparison.CandidateConfigurationId}: {comparison.RefusalReason}"));
    }

    private static string BuildTradeoffSummary(LabRunSnapshot run)
    {
        if (run.Comparisons.Count == 0) return "No candidate comparison completed.";
        var summaries = run.Comparisons.Select(comparison =>
        {
            var baseline = comparison.BaselineMetrics.FirstOrDefault(metric => metric.MetricId == "decode.tokens_per_second");
            var candidate = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "decode.tokens_per_second");
            var speed = baseline?.Median is double before && candidate?.Median is double after
                ? $"decode {before:0.0} -> {after:0.0} tokens/s ({after - before:+0.0;-0.0;0.0}, {Percent(before, after)})"
                : "decode Unknown (a performance delta cannot be calculated)";
            var predictedRam = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "memory.ram.predicted")?.Median;
            var observedRam = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "memory.ram.observed")?.Maximum;
            var predictedGpu = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "memory.gpu.predicted")?.Median;
            var observedGpu = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "memory.gpu.observed")?.Maximum;
            var candidateLabel = run.Definition.Candidates.FirstOrDefault(candidate => candidate.Id == comparison.CandidateConfigurationId)?.Label
                ?? comparison.CandidateConfigurationId;
            var prefix = run.Definition.ProtocolId == "prompt-prefix-reuse-v1" ? PrefixSummary(comparison) : string.Empty;
            var eligibility = comparison.CanShowHeadlineDelta
                ? comparison.Equivalence.State == LabEquivalenceState.Equivalent
                    ? "eligible: controlled and equivalent"
                    : "eligible: controlled"
                : $"excluded: {comparison.RefusalReason}";
            return $"{candidateLabel}: {speed}; {prefix}RAM {Bytes(predictedRam)} predicted, {Bytes(observedRam)} observed peak; "
                + $"GPU {Bytes(predictedGpu)} predicted, {Bytes(observedGpu)} observed peak; correctness {comparison.Equivalence.State}; {eligibility}";
        }).ToList();

        var measured = run.Comparisons
            .Select(comparison => (Comparison: comparison, Value: comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "decode.tokens_per_second")?.Median))
            .Where(value => value.Value.HasValue && value.Comparison.CanShowHeadlineDelta)
            .ToArray();
        if (measured.Length > 1 && measured.Select(value => value.Value!.Value).Max() - measured.Select(value => value.Value!.Value).Min() < 0.05)
            summaries.Add("Measured decode performance is effectively tied across the eligible candidates; no best result is established.");

        var eligible = run.Comparisons
            .Where(comparison => comparison.CanShowHeadlineDelta)
            .Select(comparison => run.Definition.Candidates.FirstOrDefault(candidate => candidate.Id == comparison.CandidateConfigurationId)?.Label
                ?? comparison.CandidateConfigurationId)
            .ToArray();
        summaries.Insert(0, eligible.Length switch
        {
            0 => "Recommendation: none. No correctness-eligible candidate is available.",
            1 => $"Recommendation: {eligible[0]} is the only correctness-eligible candidate. Review before applying.",
            _ => "Recommendation: multiple candidates are eligible; no automatic winner was selected."
        });
        return string.Join(Environment.NewLine, summaries);

        static string Bytes(double? value) => value.HasValue ? $"{value.Value / 1024 / 1024 / 1024:0.00} GiB" : "Unknown";
        static string Percent(double before, double after) => before > 0 ? $"{(after - before) / before:+0.0%;-0.0%;0.0%}" : "percentage Unknown";
        static string PrefixSummary(LabComparison comparison)
        {
            var before = comparison.BaselineMetrics.FirstOrDefault(metric => metric.MetricId == "prompt.milliseconds")?.Median;
            var after = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "prompt.milliseconds")?.Median;
            var reused = comparison.CandidateMetrics.FirstOrDefault(metric => metric.MetricId == "prompt.reused.tokens")?.Median;
            var timing = before.HasValue && after.HasValue ? $"prompt {before:0.0} -> {after:0.0} ms" : "prompt timing Unknown";
            var evidence = reused.HasValue ? $"direct reused {reused:0} tokens" : "controlled timing effect; reused tokens Unknown";
            return $"{timing}; {evidence}; ";
        }
    }

    private static LabConfiguration ConfigurationFrom(ServerConfig source, string id, string label)
        => LabConfigurationMapper.FromServer(source, id, label);

    private async Task SuspendSelectedSourceAsync()
    {
        if (_services is null || SelectedServer is null || _suspendedSourceServers.Count > 0)
            return;

        var source = _services.Servers.FirstOrDefault(server => server.Id == SelectedServer.Id);
        if (source is null)
            return;

        _suspendedSourceConfigurationFingerprints[source.Id] = JsonSerializer.Serialize(source.BuildConfig());
        _suspendedSourceServers = await _services.SuspendRunningServersAsync([SelectedServer.Id]);
        if (_suspendedSourceServers.Count > 0)
        {
            RestoreStatus = "Pending";
            RuntimeIsolation = "The source Chat runtime is stopped and fully unloaded while Lab uses the GPU. It will be restored when the run ends.";
        }
        else
        {
            RestoreStatus = "Not required";
            _suspendedSourceConfigurationFingerprints.Clear();
        }
    }

    private async Task RestoreSuspendedSourceAsync()
    {
        if (_services is null || _suspendedSourceServers.Count == 0)
            return;
        var suspended = _suspendedSourceServers;
        var changed = suspended.Where(id =>
        {
            var server = _services.Servers.FirstOrDefault(item => item.Id == id);
            return server is null
                || !_suspendedSourceConfigurationFingerprints.TryGetValue(id, out var before)
                || !string.Equals(before, JsonSerializer.Serialize(server.BuildConfig()), StringComparison.Ordinal);
        }).ToArray();
        if (changed.Length > 0)
        {
            _toasts.Show("Lab source was not restarted", "The source configuration changed during the run. Review it and start the original runtime manually instead of silently launching a different configuration.", ToastKind.Warning, 7000);
            RestoreStatus = "Blocked: source configuration changed during Lab.";
            RuntimeIsolation = "The source Chat runtime stayed stopped because its configuration changed during Lab. Review Services before restarting it.";
            _suspendedSourceServers = [];
            _suspendedSourceConfigurationFingerprints.Clear();
            return;
        }

        try
        {
            await _services.RestartServersAsync(suspended);
            var failed = suspended.Where(id => _services.Servers.FirstOrDefault(server => server.Id == id)?.IsRunning != true).ToArray();
            if (failed.Length > 0)
                throw new InvalidOperationException($"The source runtime did not return to Running state: {string.Join(", ", failed)}.");
            _suspendedSourceServers = [];
            _suspendedSourceConfigurationFingerprints.Clear();
            RestoreStatus = "Restored";
            RuntimeIsolation = "The isolated Lab runtime was torn down and the original source Chat runtime was restored.";
        }
        catch (Exception ex)
        {
            _toasts.Show("Could not restore Lab source", ex.Message, ToastKind.Warning, 7000);
            RestoreStatus = $"Failed: {ex.Message}";
            RuntimeIsolation = $"The source Chat runtime remains stopped after Lab. Restore failed: {ex.Message}";
        }
    }

    private static string? Choice(string value) => string.Equals(value, "All", StringComparison.Ordinal) ? null : value;
    private static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static T? ParseChoice<T>(string value) where T : struct, Enum =>
        string.Equals(value, "All", StringComparison.Ordinal) ? null : Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
