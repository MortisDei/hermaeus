using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public static class RuntimeHealthPolicy
{
    public const double LowVramHeadroomBytes = 256 * 1024 * 1024;
    public const double ContextWarningRatio = 0.90;
    public const double ContextCriticalRatio = 0.98;
    public const double MemoryPredictionWarningRatio = 1.25;
    public const double PerformanceCollapseRatio = 0.50;
    public const int MinimumPerformanceSamples = 3;
    public static readonly TimeSpan MinimumPerformanceCollapseDuration = TimeSpan.FromSeconds(30);

    public static IReadOnlyList<RuntimeHealthCondition> Evaluate(RuntimeHealthInput input)
    {
        var conditions = new List<RuntimeHealthCondition>();
        var trusted = input.EvidenceTrust != RuntimeTelemetryTrustState.Unknown;

        if (trusted && input.VramHeadroomBytes is < LowVramHeadroomBytes)
        {
            conditions.Add(new RuntimeHealthCondition(
                RuntimeHealthConditionKind.LowVramHeadroom,
                RuntimeHealthSeverity.Critical,
                "Observed GPU headroom is critically low.",
                "Trusted runtime or process-scoped measurement.",
                "Reduce context or GPU layers, or inspect the Lab fit evidence."));
        }

        if (trusted && input.ExpectedGpuResident == true && input.SpillObserved == true)
        {
            conditions.Add(new RuntimeHealthCondition(
                RuntimeHealthConditionKind.UnexpectedGpuSpill,
                RuntimeHealthSeverity.Warning,
                "Observed runtime memory placement includes spill or offload.",
                "Trusted runtime observation.",
                "Inspect the active configuration and compare it in Lab."));
        }

        if (trusted && input.ObservedMemoryBytes is > 0 && input.PredictedMemoryBytes is > 0
            && input.ObservedMemoryBytes.Value > input.PredictedMemoryBytes.Value * MemoryPredictionWarningRatio)
        {
            conditions.Add(new RuntimeHealthCondition(
                RuntimeHealthConditionKind.MemoryAbovePrediction,
                RuntimeHealthSeverity.Warning,
                "Observed comparable memory is materially above the GPU Fit prediction.",
                "Fingerprint-compatible observation and prediction.",
                "Review the matching GPU Fit and Lab evidence before changing settings."));
        }

        if (trusted && input.ContextUsed is >= 0 && input.ContextLimit is > 0)
        {
            var ratio = input.ContextUsed.Value / input.ContextLimit.Value;
            if (ratio >= ContextCriticalRatio || ratio >= ContextWarningRatio)
            {
                conditions.Add(new RuntimeHealthCondition(
                    RuntimeHealthConditionKind.ContextNearLimit,
                    ratio >= ContextCriticalRatio ? RuntimeHealthSeverity.Critical : RuntimeHealthSeverity.Warning,
                    $"Context usage is {ratio:P0} of the effective limit.",
                    "Provider-reported context usage.",
                    "Shorten the conversation or increase the effective context only after reviewing fit."));
            }
        }

        if (trusted && input.CurrentDecodeTokensPerSecond is > 0
            && input.CompatibleBaselineDecodeTokensPerSecond is > 0
            && input.CurrentDecodeTokensPerSecond.Value < input.CompatibleBaselineDecodeTokensPerSecond.Value * PerformanceCollapseRatio
            && input.PerformanceCollapseDuration >= MinimumPerformanceCollapseDuration
            && input.PerformanceSampleCount >= MinimumPerformanceSamples)
        {
            conditions.Add(new RuntimeHealthCondition(
                RuntimeHealthConditionKind.SustainedPerformanceCollapse,
                RuntimeHealthSeverity.Warning,
                "Decode speed is sustained materially below the compatible observed baseline.",
                "Compatible baseline with minimum duration and sample count.",
                "Inspect runtime health and compare the configuration in Lab."));
        }

        if (input.RuntimeStatusKnown && !input.RuntimeHealthy)
        {
            conditions.Add(new RuntimeHealthCondition(
                RuntimeHealthConditionKind.RuntimeUnavailable,
                RuntimeHealthSeverity.Critical,
                "The runtime expected by Chat is unavailable or unhealthy.",
                "Direct runtime health state.",
                "Open Services and restart or repair the active runtime."));
        }

        return conditions;
    }
}

public sealed record RuntimeHealthNotification(
    string Identity,
    RuntimeHealthCondition Condition,
    DateTime RaisedAtUtc);

/// <summary>Transition and cooldown gate for restrained health notifications.</summary>
public sealed class RuntimeHealthNotificationGate
{
    private readonly TimeSpan _cooldown;
    private readonly Dictionary<string, RuntimeHealthCondition> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastRaised = new(StringComparer.Ordinal);

    public RuntimeHealthNotificationGate(TimeSpan? cooldown = null) =>
        _cooldown = cooldown ?? TimeSpan.FromMinutes(5);

    public IReadOnlyList<RuntimeHealthNotification> Update(
        string identity,
        IReadOnlyList<RuntimeHealthCondition> conditions,
        DateTime nowUtc)
    {
        var now = nowUtc.ToUniversalTime();
        var raised = new List<RuntimeHealthNotification>();
        var current = conditions.Where(condition => condition.IsActive)
            .ToDictionary(condition => Key(identity, condition.Kind), StringComparer.Ordinal);

        foreach (var previous in _active.Keys.Except(current.Keys).ToArray())
            _active.Remove(previous);

        foreach (var pair in current)
        {
            var condition = pair.Value;
            var becameWorse = _active.TryGetValue(pair.Key, out var previous)
                && condition.Severity > previous.Severity;
            if (!_active.ContainsKey(pair.Key) || becameWorse)
            {
                raised.Add(new RuntimeHealthNotification(identity, condition, now));
                _lastRaised[pair.Key] = now;
            }
            _active[pair.Key] = condition;
        }

        return raised;
    }

    private static string Key(string identity, RuntimeHealthConditionKind kind) => $"{identity}:{kind}";
}
