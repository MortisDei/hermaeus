using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Parses bounded scalar fields from a managed llama.cpp launch receipt. It is
/// intentionally scoped to the selected runtime identity and never treats
/// health or a rendered argument as effective placement proof.
/// </summary>
public static class EffectiveLaunchObservationParser
{
    // This identifies the parser and receipt schema, not a product release.
    // Keep it stable across rounds so receipts remain reusable by later
    // adaptive, diagnostics, and benchmark workflows.
    public const string ParserVersion = "llama-effective-runtime-v2";

    private static readonly Regex ContextLogRegex =
        new(@"\bn_ctx\s*=\s*(?<value>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContextSlotLogRegex =
        new(@"\bn_ctx_slot\s*=\s*(?<value>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ThreadsLogRegex =
        new(@"\bn_threads\s*=\s*(?<value>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SlotsLogRegex =
        new(@"\bn_slots\s*=\s*(?<value>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KvCacheLogRegex =
        new(@"\b(?<kind>K|V)\s*\((?<type>[^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FlashAttentionLogRegex =
        new(@"\bFlash\s+Attention\s+(?<state>enabled|disabled)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static EffectiveLaunchObservation Parse(
        ServerConfig config,
        RuntimeIdentityV2 runtimeIdentity,
        string? propsJson,
        RuntimeLaunchProcessEvidence? process = null,
        string? runtimeLog = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(runtimeIdentity);

        var effective = new Dictionary<string, string?>(StringComparer.Ordinal);
        var propsSucceeded = false;
        if (!string.IsNullOrWhiteSpace(propsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(propsJson);
                propsSucceeded = true;
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    Add(root, effective, "context", "ctx_size", "n_ctx", "context_size");
                    Add(root, effective, "gpu_layers", "n_gpu_layers", "gpu_layers");
                    Add(root, effective, "fit", "fit");
                    Add(root, effective, "fit_target", "fit_target");
                    Add(root, effective, "fit_minimum_context", "fit_ctx", "fit_minimum_context");
                    Add(root, effective, "slots", "parallel", "n_parallel", "slots");
                    Add(root, effective, "split_mode", "split_mode");
                    Add(root, effective, "tensor_split", "tensor_split");
                    Add(root, effective, "main_gpu", "main_gpu");
                    Add(root, effective, "kv_cache_type_k", "cache_type_k", "kv_cache_type_k");
                    Add(root, effective, "kv_cache_type_v", "cache_type_v", "kv_cache_type_v");
                    Add(root, effective, "cpu_moe", "cpu_moe", "n_cpu_moe");
                    Add(root, effective, "flash_attention", "flash_attn", "flash_attention");
                    Add(root, effective, "threads", "threads", "n_threads");
                    Add(root, effective, "prompt_threads", "threads_batch", "prompt_threads");
                    Add(root, effective, "batch_size", "n_batch", "batch_size");
                    Add(root, effective, "ubatch_size", "n_ubatch", "ubatch_size");
                    Add(root, effective, "speculative_mechanism", "spec_type", "speculative_type");
                    Add(root, effective, "speculative_nmax", "spec_n_max", "n_max");
                    Add(root, effective, "speculative_nmin", "spec_n_min", "n_min");
                    Add(root, effective, "speculative_pmin", "spec_p_min", "p_min");
                    Add(root, effective, "speculative_draft_gpu_layers", "spec_draft_ngl", "draft_gpu_layers");

                    if (root.TryGetProperty("default_generation_settings", out var generation)
                        && generation.ValueKind == JsonValueKind.Object
                        && generation.TryGetProperty("params", out var parameters)
                        && parameters.ValueKind == JsonValueKind.Object)
                    {
                        Add(parameters, effective, "context", "ctx_size", "n_ctx", "context_size");
                    }

                    Add(root, effective, "slots", "total_slots");
                }
                else
                {
                    propsSucceeded = false;
                }
            }
            catch (JsonException)
            {
                propsSucceeded = false;
            }
        }

        var evidenceIds = effective.Keys.ToDictionary(
            field => field,
            field => $"props.{field}",
            StringComparer.Ordinal);
        foreach (var (field, value) in ParseRuntimeLog(runtimeLog))
        {
            if (effective.ContainsKey(field))
                continue;

            effective[field] = value;
            evidenceIds[field] = $"runtime.log.{field}";
        }

        var placement = config.TryGetGpuPlacement(out var intent, out _)
            ? intent
            : null;

        var runtimeGpuLayers = ServerProcessManager.ParseGpuLayerLog(runtimeLog ?? string.Empty);
        var gpuEvidenceId = evidenceIds.GetValueOrDefault("gpu_layers", "props.gpu_layers");
        if (runtimeGpuLayers.Used is int usedLayers)
        {
            effective["gpu_layers"] = placementValue(usedLayers, runtimeGpuLayers.Total);
            gpuEvidenceId = "runtime.log.gpu_layers";
        }

        var renderedLayers = placement?.Kind switch
        {
            GpuPlacementKind.Cpu => "0",
            GpuPlacementKind.All => "all",
            GpuPlacementKind.Exact => placement.ExactLayerCount?.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        var fields = new List<AdaptiveFieldObservation>
        {
            Field("context", config.ContextSize.ToString(CultureInfo.InvariantCulture),
                config.ContextSize.ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("context"), EvidenceId("context")),
            Field("gpu_layers", placement?.CanonicalValue, renderedLayers,
                effective.GetValueOrDefault("gpu_layers"), gpuEvidenceId),
            Field("fit", placement?.Kind == GpuPlacementKind.Auto ? "on" : "off",
                placement?.Kind == GpuPlacementKind.Auto ? "on" : "off",
                effective.GetValueOrDefault("fit"), EvidenceId("fit")),
            Field("fit_target", null, config.RuntimeFitTargetBytes?.ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("fit_target"), EvidenceId("fit_target")),
            Field("fit_minimum_context", null, config.RuntimeFitMinimumContext?.ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("fit_minimum_context"), EvidenceId("fit_minimum_context")),
            Field("slots", config.Slots.ToString(CultureInfo.InvariantCulture),
                Math.Max(1, config.Slots).ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("slots"), EvidenceId("slots"))
        };

        AddOptionalField("kv_cache_type_k", config.KvCacheTypeK);
        AddOptionalField("kv_cache_type_v", config.KvCacheTypeV);
        AddOptionalField("flash_attention", config.FlashAttention);
        AddOptionalField("cpu_moe_layers", config.CpuMoeLayers.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("threads", config.Threads.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("prompt_threads", config.PromptThreads.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("batch_size", null);
        AddOptionalField("ubatch_size", null);
        AddOptionalField("speculative_mechanism", string.Empty);
        AddOptionalField("speculative_nmax", config.Speculative?.NMax?.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("speculative_nmin", config.Speculative?.NMin?.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("speculative_pmin", config.Speculative?.PMin?.ToString(CultureInfo.InvariantCulture));
        AddOptionalField("speculative_draft_gpu_layers", config.Speculative?.DraftGpuLayers?.ToString(CultureInfo.InvariantCulture));

        var contextKnown = effective.ContainsKey("context");
        var placementKnown = effective.ContainsKey("gpu_layers");
        var slotsKnown = effective.ContainsKey("slots");
        var auditable = propsSucceeded
            && contextKnown
            && placementKnown
            && slotsKnown
            && (!config.EnableRuntimePropertiesEndpoint || HasProcessEvidence(process))
            && fields.All(field => field.Field is not ("fit_target" or "fit_minimum_context")
                || field.EffectiveValue is not null || field.PlannedValue is null);

        return new(
            runtimeIdentity,
            ParserVersion,
            propsSucceeded,
            placement?.Kind == GpuPlacementKind.Auto,
            config.RuntimeFitTargetBytes,
            config.RuntimeFitMinimumContext,
            fields,
            fields.Select(field => field.EvidenceId).ToArray(),
            auditable)
        {
            Process = process
        };

        AdaptiveFieldObservation Field(
            string name,
            string? configured,
            string? rendered,
            string? observed,
            string evidenceId) => new(
                name,
                configured,
                rendered,
                rendered,
                observed,
                observed is null ? AdaptiveEvidenceState.Unknown : AdaptiveEvidenceState.Proven,
                evidenceId);

        static void Add(JsonElement root, IDictionary<string, string?> values, string field, params string[] names)
        {
            foreach (var name in names)
            {
                if (!root.TryGetProperty(name, out var value)
                    || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    values[field] = value.ToString();
                    return;
                }
            }
        }

        static bool HasProcessEvidence(RuntimeLaunchProcessEvidence? value) =>
            value is { ProcessId: > 0 }
            && !string.IsNullOrWhiteSpace(value.ExecutablePath)
            && value.Arguments is { Count: > 0 };

        string EvidenceId(string field) => evidenceIds.GetValueOrDefault(field, $"props.{field}");

        void AddOptionalField(string name, string? configured)
        {
            if (!effective.ContainsKey(name))
                return;
            fields.Add(Field(name, configured, null, effective.GetValueOrDefault(name), EvidenceId(name)));
        }

        string? placementValue(int usedLayers, int? totalLayers) =>
            placement?.Kind == GpuPlacementKind.All
                && totalLayers is int total
                && usedLayers == total
                ? "all"
                : usedLayers.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> ParseRuntimeLog(string? runtimeLog)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(runtimeLog))
            return values;

        foreach (var line in runtimeLog.Split('\n'))
        {
            var context = ContextLogRegex.Match(line);
            if (context.Success)
                values["context"] = context.Groups["value"].Value;
            else
            {
                var contextSlot = ContextSlotLogRegex.Match(line);
                if (contextSlot.Success)
                    values["context"] = contextSlot.Groups["value"].Value;
            }

            var threads = ThreadsLogRegex.Match(line);
            if (threads.Success)
                values["threads"] = threads.Groups["value"].Value;

            var slots = SlotsLogRegex.Match(line);
            if (slots.Success)
                values["slots"] = slots.Groups["value"].Value;

            if (line.Contains("llama_kv_cache", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in KvCacheLogRegex.Matches(line))
                {
                    var kind = match.Groups["kind"].Value;
                    var field = kind.Equals("K", StringComparison.OrdinalIgnoreCase)
                        ? "kv_cache_type_k"
                        : "kv_cache_type_v";
                    values[field] = match.Groups["type"].Value.Trim();
                }
            }

            var flash = FlashAttentionLogRegex.Match(line);
            if (flash.Success)
                values["flash_attention"] = flash.Groups["state"].Value.Equals("enabled", StringComparison.OrdinalIgnoreCase)
                    ? "on"
                    : "off";
        }

        return values;
    }
}
