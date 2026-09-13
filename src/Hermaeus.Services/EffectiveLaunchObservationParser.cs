using System.Globalization;
using System.Text.Json;
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

        var placement = config.TryGetGpuPlacement(out var intent, out _)
            ? intent
            : null;

        var runtimeGpuLayers = ServerProcessManager.ParseGpuLayerLog(runtimeLog ?? string.Empty);
        var gpuEvidenceId = "props.gpu_layers";
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
                effective.GetValueOrDefault("context"), "props.context"),
            Field("gpu_layers", placement?.CanonicalValue, renderedLayers,
                effective.GetValueOrDefault("gpu_layers"), gpuEvidenceId),
            Field("fit", placement?.Kind == GpuPlacementKind.Auto ? "on" : "off",
                placement?.Kind == GpuPlacementKind.Auto ? "on" : "off",
                effective.GetValueOrDefault("fit"), "props.fit"),
            Field("fit_target", null, config.RuntimeFitTargetBytes?.ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("fit_target"), "props.fit_target"),
            Field("fit_minimum_context", null, config.RuntimeFitMinimumContext?.ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("fit_minimum_context"), "props.fit_minimum_context"),
            Field("slots", config.Slots.ToString(CultureInfo.InvariantCulture),
                Math.Max(1, config.Slots).ToString(CultureInfo.InvariantCulture),
                effective.GetValueOrDefault("slots"), "props.slots")
        };

        AddOptionalField("kv_cache_type_k", config.KvCacheTypeK, "props.kv_cache_type_k");
        AddOptionalField("kv_cache_type_v", config.KvCacheTypeV, "props.kv_cache_type_v");
        AddOptionalField("flash_attention", config.FlashAttention, "props.flash_attention");
        AddOptionalField("cpu_moe_layers", config.CpuMoeLayers.ToString(CultureInfo.InvariantCulture), "props.cpu_moe");
        AddOptionalField("threads", config.Threads.ToString(CultureInfo.InvariantCulture), "props.threads");
        AddOptionalField("prompt_threads", config.PromptThreads.ToString(CultureInfo.InvariantCulture), "props.prompt_threads");
        AddOptionalField("batch_size", null, "props.batch_size");
        AddOptionalField("ubatch_size", null, "props.ubatch_size");
        AddOptionalField("speculative_mechanism", string.Empty, "props.speculative_mechanism");
        AddOptionalField("speculative_nmax", config.Speculative?.NMax?.ToString(CultureInfo.InvariantCulture), "props.speculative_nmax");
        AddOptionalField("speculative_nmin", config.Speculative?.NMin?.ToString(CultureInfo.InvariantCulture), "props.speculative_nmin");
        AddOptionalField("speculative_pmin", config.Speculative?.PMin?.ToString(CultureInfo.InvariantCulture), "props.speculative_pmin");
        AddOptionalField("speculative_draft_gpu_layers", config.Speculative?.DraftGpuLayers?.ToString(CultureInfo.InvariantCulture), "props.speculative_draft_gpu_layers");

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

        void AddOptionalField(string name, string? configured, string evidenceId)
        {
            if (!effective.ContainsKey(name))
                return;
            fields.Add(Field(name, configured, null, effective.GetValueOrDefault(name), evidenceId));
        }

        string? placementValue(int usedLayers, int? totalLayers) =>
            placement?.Kind == GpuPlacementKind.All
                && totalLayers is int total
                && usedLayers == total
                ? "all"
                : usedLayers.ToString(CultureInfo.InvariantCulture);
    }
}
