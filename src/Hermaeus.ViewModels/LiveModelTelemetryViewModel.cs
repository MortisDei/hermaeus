using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

/// <summary>
/// Compact Chat telemetry projection. It owns display state only; measurement
/// remains in <see cref="LiveModelTelemetrySampler"/> and the shared source.
/// </summary>
public sealed partial class LiveModelTelemetryViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly LiveModelTelemetrySampler _sampler;
    private RuntimeTelemetrySeries? _series;
    private long _generation;
    private readonly RuntimeHealthNotificationGate _healthGate = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _status = "Telemetry is closed.";
    [ObservableProperty] private string _runtimeIdentity = "Unknown";
    [ObservableProperty] private string _modelIdentity = "Unknown";
    [ObservableProperty] private string _runtimeIdentityStableId = string.Empty;
    [ObservableProperty] private string _modelIdentityStableId = string.Empty;
    [ObservableProperty] private string _decodeRate = "Unknown";
    [ObservableProperty] private string _processRam = "Unknown";
    [ObservableProperty] private string _processGpuMemory = "Unknown";
    [ObservableProperty] private string _processGpuMemoryDetail = "Per-process VRAM evidence is unavailable until a trustworthy process counter is supplied.";
    [ObservableProperty] private string _promptThroughput = "Unknown";
    [ObservableProperty] private string _timeToFirstToken = "Unknown";
    [ObservableProperty] private string _tokensServed = "Unknown";
    [ObservableProperty] private string _healthStatus = "No actionable health condition.";

    public UiBoundCollection<string> HealthConditions { get; } = [];
    public event Action<RuntimeHealthNotification>? NotificationRaised;

    public LiveModelTelemetryViewModel(LiveModelTelemetrySampler sampler)
    {
        _sampler = sampler;
        _sampler.SeriesChanged += OnSeriesChanged;
    }

    public IReadOnlyList<RuntimeTelemetrySample> Samples => _series?.Samples ?? [];
    public string UnknownNote => _series is null
        ? "Open telemetry during a local managed Chat request to sample the matching process."
        : "Missing counters remain Unknown and are not treated as zero.";
    public string RuntimeIdentityTooltip => FormatIdentityTooltip(RuntimeIdentity, RuntimeIdentityStableId);
    public string ModelIdentityTooltip => FormatIdentityTooltip(ModelIdentity, ModelIdentityStableId);

    /// <summary>
    /// Request-level metrics are direct provider evidence and remain available
    /// while the flyout is closed. They do not start an OS polling loop.
    /// </summary>
    public void RecordRequest(string modelId, string providerTag, ChatServerTimings? timing, ChatTokenUsage? usage, long firstTokenMs, long totalLatencyMs)
    {
        if (_series is null)
            RuntimeIdentity = $"{providerTag} (runtime identity not attached)";
        ModelIdentity = string.IsNullOrWhiteSpace(modelId) ? "Unknown" : modelId;
        TimeToFirstToken = firstTokenMs > 0 ? $"{firstTokenMs:N0} ms" : "Unknown";
        TokensServed = usage is null ? "Unknown" : $"{usage.CompletionTokens:N0}";
        PromptThroughput = timing?.PromptTokens is > 0 && timing.PromptMs is > 0
            ? $"{timing.PromptTokens.Value / timing.PromptMs.Value * 1000:N1} tokens/s"
            : "Unknown";
        DecodeRate = timing?.PredictedTokens is > 0 && timing.PredictedMs is > 0
            ? $"{timing.PredictedTokens.Value / timing.PredictedMs.Value * 1000:N1} tokens/s"
            : "Unknown";
        OnPropertyChanged(nameof(UnknownNote));
    }

    /// <summary>
    /// Applies a caller-supplied, identity-scoped health input. The VM does
    /// not infer GPU faults from timing or utilization and does not alert on
    /// Unknown evidence.
    /// </summary>
    public void ApplyHealth(RuntimeHealthInput input, DateTime? nowUtc = null)
    {
        var conditions = RuntimeHealthPolicy.Evaluate(input);
        var notifications = _healthGate.Update(input.RuntimeModelIdentity, conditions, nowUtc ?? DateTime.UtcNow);
        HealthConditions.Clear();
        foreach (var condition in conditions)
            HealthConditions.Add(condition.ObservedFact);
        HealthStatus = conditions.Count == 0 ? "No actionable health condition." : string.Join(" ", HealthConditions);
        foreach (var notification in notifications)
            NotificationRaised?.Invoke(notification);
    }

    public async Task OpenAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
    {
        var generation = ++_generation;
        IsOpen = true;
        Status = "Sampling the active runtime.";
        RuntimeIdentity = RuntimeTelemetryIdentityText.RuntimeLabel(request.RuntimeIdentity);
        ModelIdentity = RuntimeTelemetryIdentityText.ModelLabel(request.Fingerprint.Model);
        RuntimeIdentityStableId = request.RuntimeIdentity.StableId;
        ModelIdentityStableId = request.Fingerprint.Model.StableId;
        OnPropertyChanged(nameof(RuntimeIdentityTooltip));
        OnPropertyChanged(nameof(ModelIdentityTooltip));
        await _sampler.StartAsync(request, ct);
        await RunOnUiAsync(() =>
        {
            ApplySeries(_sampler.CurrentSeries, generation);
            return Task.CompletedTask;
        });
    }

    public async Task CloseAsync()
    {
        var generation = ++_generation;
        IsOpen = false;
        Status = "Telemetry is closed.";
        await _sampler.StopAsync();
        if (generation != _generation) return;
        _series = null;
        RuntimeIdentityStableId = string.Empty;
        ModelIdentityStableId = string.Empty;
        OnPropertyChanged(nameof(RuntimeIdentityTooltip));
        OnPropertyChanged(nameof(ModelIdentityTooltip));
        ProcessRam = "Unknown";
        ProcessGpuMemory = "Unknown";
        ProcessGpuMemoryDetail = "Per-process VRAM evidence is unavailable until a trustworthy process counter is supplied.";
        OnPropertyChanged(nameof(Samples));
        OnPropertyChanged(nameof(UnknownNote));
    }

    private void OnSeriesChanged(RuntimeTelemetrySeries? series)
    {
        var generation = _generation;
        RunOnUi(() => ApplySeries(series, generation));
    }

    private void ApplySeries(RuntimeTelemetrySeries? series, long generation)
    {
        if (series is null || !IsOpen || generation != _generation
            || !ReferenceEquals(series, _sampler.CurrentSeries)) return;
        _series = series;
        ProcessRam = FormatBytes(series.Current(RuntimeTelemetryMetric.ProcessWorkingSetBytes)?.ValueBytes);
        var gpuSample = series.Samples
            .Where(sample => sample.Metric == RuntimeTelemetryMetric.ProcessGpuMemoryBytes)
            .OrderByDescending(sample => sample.ObservedAtUtc)
            .FirstOrDefault();
        ProcessGpuMemory = FormatBytes(gpuSample?.ValueBytes);
        ProcessGpuMemoryDetail = FormatGpuDetail(gpuSample);
        Status = $"Captured {series.Samples.Count} bounded sample(s).";
        OnPropertyChanged(nameof(Samples));
        OnPropertyChanged(nameof(UnknownNote));
    }

    private static string FormatBytes(long? value) => value is null
        ? "Unknown"
        : $"{value.Value / (1024d * 1024d):N1} MB";

    private static string FormatGpuDetail(RuntimeTelemetrySample? sample) => sample is null
        ? "Per-process VRAM evidence is unavailable until a trustworthy process counter is supplied."
        : $"{sample.EvidenceCode}: {sample.Detail}";

    public async ValueTask DisposeAsync()
    {
        ++_generation;
        IsOpen = false;
        _sampler.SeriesChanged -= OnSeriesChanged;
        await _sampler.DisposeAsync();
    }

    private static string FormatIdentityTooltip(string label, string stableId) =>
        stableId.Length == 0 ? label : $"{label}\nStable identity: {stableId}";
}

internal static class RuntimeTelemetryIdentityText
{
    public static string RuntimeLabel(RuntimeIdentityV2 identity)
    {
        var label = string.IsNullOrWhiteSpace(identity.Kind) ? "Runtime" : identity.Kind;
        if (!string.IsNullOrWhiteSpace(identity.Version))
            label += $" {identity.Version}";
        if (!string.IsNullOrWhiteSpace(identity.Build))
            label += $" build {identity.Build}";
        if (!string.IsNullOrWhiteSpace(identity.Backend))
            label += $" ({identity.Backend})";
        return label;
    }

    public static string ModelLabel(ModelIdentityV2 identity)
    {
        var label = string.IsNullOrWhiteSpace(identity.ManifestIdentity)
            ? "Local model"
            : identity.ManifestIdentity;
        var details = string.Join(", ", new[] { identity.Architecture, identity.Quantization }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return details.Length == 0 ? label : $"{label} ({details})";
    }
}
