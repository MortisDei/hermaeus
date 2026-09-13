using System.Globalization;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// Reconciles the five runtime evidence boundaries shared by Lab and
/// Benchmarks. This is intentionally a validator, not a source of inferred
/// configuration: any missing boundary leaves the envelope ineligible.
/// </summary>
public static class RuntimeEvidenceEvaluator
{
    public static RuntimeEvidenceEnvelope Evaluate(
        string workflow,
        string runId,
        string candidateId,
        RuntimeIdentityV2? expectedRuntime,
        ModelIdentityV2? modelIdentity,
        ConfigurationIdentityV2? requestedConfiguration,
        ConfigurationIdentityV2? resolvedConfiguration,
        ConfigurationIdentityV2? launchedConfiguration,
        RuntimeLaunchProcessEvidence? process,
        EffectiveLaunchObservation? effectiveLaunch,
        IEnumerable<string>? requiredEffectiveFields,
        IEnumerable<string>? telemetryProcessInstanceIds,
        RuntimeEvidenceStatus unavailableStatus,
        IReadOnlyDictionary<string, string>? expectedEffectiveValues = null)
    {
        var reasons = new List<string>();
        var required = (requiredEffectiveFields ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var telemetry = (telemetryProcessInstanceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var expectedValues = expectedEffectiveValues ?? new Dictionary<string, string>(StringComparer.Ordinal);

        if (expectedRuntime is null || string.IsNullOrWhiteSpace(expectedRuntime.StableId))
            reasons.Add("runtime-identity-missing");
        if (modelIdentity is null || string.IsNullOrWhiteSpace(modelIdentity.StableId))
            reasons.Add("model-identity-missing");

        var requestedId = requestedConfiguration?.StableId ?? string.Empty;
        var resolvedId = resolvedConfiguration?.StableId ?? string.Empty;
        var launchedId = launchedConfiguration?.StableId ?? string.Empty;
        if (requestedConfiguration is null || string.IsNullOrWhiteSpace(requestedId))
            reasons.Add("configuration-requested-missing");
        if (resolvedConfiguration is null || string.IsNullOrWhiteSpace(resolvedId))
            reasons.Add("configuration-resolved-missing");
        if (!string.Equals(requestedId, resolvedId, StringComparison.Ordinal))
            reasons.Add("configuration-requested-resolved-mismatch");
        if (launchedConfiguration is null || string.IsNullOrWhiteSpace(launchedId))
            reasons.Add("configuration-launched-missing");
        else if (!string.Equals(launchedId, resolvedId, StringComparison.Ordinal))
            reasons.Add("configuration-resolved-launched-mismatch");

        if (!HasProcessEvidence(process))
            reasons.Add("process-missing");

        if (effectiveLaunch is null)
        {
            reasons.Add("effective-runtime-missing");
        }
        else
        {
            if (!effectiveLaunch.PropsProbeSucceeded || !effectiveLaunch.IsAuditable)
                reasons.Add("effective-runtime-not-auditable");

            if (expectedRuntime is not null && !expectedRuntime.IdentifiesSameRuntime(effectiveLaunch.RuntimeIdentity))
                reasons.Add("effective-runtime-identity-mismatch");

            var effectiveProcess = effectiveLaunch.Process;
            if (!HasProcessEvidence(effectiveProcess))
                reasons.Add("effective-process-missing");
            else if (process is not null && effectiveProcess is not null
                && !SameProcess(process, effectiveProcess))
                reasons.Add("effective-process-mismatch");

            foreach (var field in required)
            {
                var observation = effectiveLaunch.Fields.FirstOrDefault(value =>
                    string.Equals(value.Field, field, StringComparison.Ordinal));
                if (observation is null
                    || observation.EvidenceState != AdaptiveEvidenceState.Proven
                    || string.IsNullOrWhiteSpace(observation.EffectiveValue))
                    reasons.Add($"effective-field:{field}");
                else if (expectedValues.TryGetValue(field, out var expected)
                    && !Equivalent(field, observation.EffectiveValue, expected))
                    reasons.Add($"effective-field-mismatch:{field}");
            }
        }

        if (process is { ExecutableSha256.Length: > 0 }
            && expectedRuntime is { ExecutableSha256.Length: > 0 }
            && !string.Equals(process.ExecutableSha256, expectedRuntime.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            reasons.Add("process-executable-mismatch");

        if (required.Length > 0)
        {
            if (!HasProcessEvidence(process))
                reasons.Add("telemetry-process-missing");
            else
            {
                var expectedInstance = RuntimeTelemetrySeries.ProcessInstance(process!.ProcessId, process.StartedAtUtc);
                if (telemetry.Length == 0)
                    reasons.Add("telemetry-missing");
                if (telemetry.Any(value => !string.Equals(value, expectedInstance, StringComparison.Ordinal)))
                    reasons.Add("telemetry-process-mismatch");
                if (telemetry.Length > 0 && !telemetry.Contains(expectedInstance, StringComparer.Ordinal))
                    reasons.Add("telemetry-process-unbound");
            }
        }

        var distinctReasons = reasons.Distinct(StringComparer.Ordinal).ToArray();
        var status = distinctReasons.Length == 0
            ? RuntimeEvidenceStatus.Verified
            : distinctReasons.Any(value => value.Contains("mismatch", StringComparison.Ordinal)
                || value.Contains("unbound", StringComparison.Ordinal))
                ? RuntimeEvidenceStatus.Mismatch
                : unavailableStatus;

        return new RuntimeEvidenceEnvelope
        {
            Workflow = workflow,
            RunId = runId,
            CandidateId = candidateId,
            RequestedConfigurationStableId = requestedId,
            ResolvedConfigurationStableId = resolvedId,
            LaunchedConfigurationStableId = launchedId,
            RuntimeIdentity = expectedRuntime,
            ModelIdentity = modelIdentity,
            RequestedConfiguration = requestedConfiguration,
            ResolvedConfiguration = resolvedConfiguration,
            Process = process,
            EffectiveLaunch = effectiveLaunch,
            EffectiveConfigurationStableId = effectiveLaunch is not null
                && !distinctReasons.Any(reason => reason.StartsWith("effective-", StringComparison.Ordinal))
                ? launchedId
                : string.Empty,
            EffectiveFields = effectiveLaunch?.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Field)
                    && !string.IsNullOrWhiteSpace(field.EffectiveValue))
                .GroupBy(field => field.Field, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().EffectiveValue!, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal),
            TelemetryProcessInstanceIds = telemetry,
            Status = status,
            Reasons = distinctReasons
        };
    }

    public static IReadOnlyDictionary<string, string> ExpectedEffectiveValues(
        ConfigurationIdentityV2? configuration,
        IEnumerable<string>? fields)
    {
        if (configuration is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in (fields ?? []).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var value = field switch
            {
                "context" => configuration.ContextSize?.ToString(CultureInfo.InvariantCulture),
                "slots" => configuration.Slots.HasValue
                    ? Math.Max(1, configuration.Slots.Value).ToString(CultureInfo.InvariantCulture)
                    : null,
                "gpu_layers" => ExpectedGpuLayers(configuration),
                "fit" => IsAuto(configuration) ? "on" : "off",
                "threads" => configuration.Threads?.ToString(CultureInfo.InvariantCulture),
                "prompt_threads" => configuration.PromptThreads?.ToString(CultureInfo.InvariantCulture),
                "batch_size" => configuration.BatchSize?.ToString(CultureInfo.InvariantCulture),
                "ubatch_size" => configuration.UBatchSize?.ToString(CultureInfo.InvariantCulture),
                "kv_cache_type_k" => configuration.KvCacheTypeK,
                "kv_cache_type_v" => configuration.KvCacheTypeV,
                // llama.cpp resolves an "auto" request to a runtime-specific
                // on/off decision. Require the field to be observed, but do
                // not compare that decision to the literal request value.
                "flash_attention" => configuration.FlashAttention.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : configuration.FlashAttention,
                "cpu_moe_layers" => configuration.CpuMoeLayers?.ToString(CultureInfo.InvariantCulture),
                "speculative_mechanism" => configuration.SpeculativeMechanism,
                "speculative_nmax" => SpeculativeParameter(configuration.SpeculativeParameters, "nmax"),
                "speculative_nmin" => SpeculativeParameter(configuration.SpeculativeParameters, "nmin"),
                "speculative_pmin" => SpeculativeParameter(configuration.SpeculativeParameters, "pmin"),
                "speculative_draft_gpu_layers" => SpeculativeParameter(configuration.SpeculativeParameters, "ngld"),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(value))
                result[field] = value;
        }

        return result;
    }

    private static bool HasProcessEvidence(RuntimeLaunchProcessEvidence? value) =>
        value is { ProcessId: > 0 }
        && value.StartedAtUtc != default
        && !string.IsNullOrWhiteSpace(value.ExecutablePath)
        && value.Arguments is { Count: > 0 };

    private static bool SameProcess(RuntimeLaunchProcessEvidence left, RuntimeLaunchProcessEvidence right) =>
        left.ProcessId == right.ProcessId
        && left.StartedAtUtc.ToUniversalTime() == right.StartedAtUtc.ToUniversalTime()
        && string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);

    private static bool Equivalent(string field, string actual, string expected)
    {
        actual = actual.Trim();
        expected = expected.Trim();
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            return true;
        if (field == "gpu_layers"
            && ((expected.Equals("all", StringComparison.OrdinalIgnoreCase) && actual == "-1")
                || (actual.Equals("all", StringComparison.OrdinalIgnoreCase) && expected == "-1")))
            return true;
        if (field is "fit" or "flash_attention"
            && TryBoolean(actual, out var actualBoolean)
            && TryBoolean(expected, out var expectedBoolean))
            return actualBoolean == expectedBoolean;
        return IsNumericField(field)
            && decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber)
            && decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
            && actualNumber == expectedNumber;
    }

    private static bool IsNumericField(string field) => field is
        "context" or "slots" or "gpu_layers" or "threads" or "prompt_threads" or
        "batch_size" or "ubatch_size" or "cpu_moe_layers" or "speculative_nmax" or
        "speculative_nmin" or "speculative_pmin" or "speculative_draft_gpu_layers";

    private static bool TryBoolean(string value, out bool result)
    {
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value == "1")
        {
            result = true;
            return true;
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value == "0")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static string? ExpectedGpuLayers(ConfigurationIdentityV2 configuration)
    {
        if (IsAuto(configuration) || !configuration.GpuLayers.HasValue)
            return null;
        return configuration.GpuLayers.Value == -1
            ? "all"
            : configuration.GpuLayers.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAuto(ConfigurationIdentityV2 configuration) =>
        configuration.GpuPlacement.EndsWith(":auto", StringComparison.OrdinalIgnoreCase);

    private static string? SpeculativeParameter(string value, string key)
    {
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator > 0 && string.Equals(part[..separator], key, StringComparison.OrdinalIgnoreCase))
                return part[(separator + 1)..];
        }

        return null;
    }
}
