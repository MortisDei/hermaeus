using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RuntimeHealthPolicyTests
{
    [Fact]
    public void High_gpu_utilization_alone_does_not_raise_a_condition()
    {
        var conditions = RuntimeHealthPolicy.Evaluate(new RuntimeHealthInput(
            "identity", EvidenceTrust: RuntimeTelemetryTrustState.ProcessScoped));

        Assert.Empty(conditions);
    }

    [Fact]
    public void Unknown_evidence_does_not_raise_resource_conditions()
    {
        var conditions = RuntimeHealthPolicy.Evaluate(new RuntimeHealthInput(
            "identity", VramHeadroomBytes: 1, ObservedMemoryBytes: 100, PredictedMemoryBytes: 1,
            ContextUsed: 99, ContextLimit: 100, EvidenceTrust: RuntimeTelemetryTrustState.Unknown));

        Assert.Empty(conditions);
    }

    [Fact]
    public void Low_headroom_spill_memory_context_and_unhealthy_runtime_are_deterministic()
    {
        var conditions = RuntimeHealthPolicy.Evaluate(new RuntimeHealthInput(
            "identity",
            VramHeadroomBytes: RuntimeHealthPolicy.LowVramHeadroomBytes - 1,
            ExpectedGpuResident: true,
            SpillObserved: true,
            ObservedMemoryBytes: 126,
            PredictedMemoryBytes: 100,
            ContextUsed: 99,
            ContextLimit: 100,
            RuntimeHealthy: false,
            EvidenceTrust: RuntimeTelemetryTrustState.TrustedRuntime));

        Assert.Contains(conditions, item => item.Kind == RuntimeHealthConditionKind.LowVramHeadroom && item.Severity == RuntimeHealthSeverity.Critical);
        Assert.Contains(conditions, item => item.Kind == RuntimeHealthConditionKind.UnexpectedGpuSpill);
        Assert.Contains(conditions, item => item.Kind == RuntimeHealthConditionKind.MemoryAbovePrediction);
        Assert.Contains(conditions, item => item.Kind == RuntimeHealthConditionKind.ContextNearLimit);
        Assert.Contains(conditions, item => item.Kind == RuntimeHealthConditionKind.RuntimeUnavailable);
    }

    [Fact]
    public void Performance_collapse_requires_compatible_duration_and_samples()
    {
        var input = new RuntimeHealthInput("identity", CurrentDecodeTokensPerSecond: 4,
            CompatibleBaselineDecodeTokensPerSecond: 10,
            PerformanceCollapseDuration: RuntimeHealthPolicy.MinimumPerformanceCollapseDuration,
            PerformanceSampleCount: RuntimeHealthPolicy.MinimumPerformanceSamples,
            EvidenceTrust: RuntimeTelemetryTrustState.TrustedRuntime);

        Assert.Contains(RuntimeHealthPolicy.Evaluate(input), item => item.Kind == RuntimeHealthConditionKind.SustainedPerformanceCollapse);
        Assert.DoesNotContain(RuntimeHealthPolicy.Evaluate(input with { PerformanceSampleCount = 2 }),
            item => item.Kind == RuntimeHealthConditionKind.SustainedPerformanceCollapse);
    }

    [Fact]
    public void Notification_gate_deduplicates_recovers_and_allows_worse_severity()
    {
        var gate = new RuntimeHealthNotificationGate(TimeSpan.FromMinutes(5));
        var warning = new RuntimeHealthCondition(RuntimeHealthConditionKind.ContextNearLimit,
            RuntimeHealthSeverity.Warning, "fact", "trusted", "action");
        var critical = warning with { Severity = RuntimeHealthSeverity.Critical };
        var start = DateTime.UtcNow;

        Assert.Single(gate.Update("identity", [warning], start));
        Assert.Empty(gate.Update("identity", [warning], start.AddSeconds(1)));
        Assert.Single(gate.Update("identity", [critical], start.AddSeconds(2)));
        Assert.Empty(gate.Update("identity", [], start.AddSeconds(3)));
        Assert.Single(gate.Update("identity", [warning], start.AddMinutes(6)));
    }
}
