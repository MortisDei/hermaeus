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
    public const string ParserVersion = "llama-props-scalar-v1";

    public static EffectiveLaunchObservation Parse(
        ServerConfig config,
        RuntimeIdentityV2 runtimeIdentity,
        string? propsJson)
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
                    Add(root, effective, "slots", "parallel", "slots");
                    Add(root, effective, "split_mode", "split_mode");
                    Add(root, effective, "tensor_split", "tensor_split");
                    Add(root, effective, "main_gpu", "main_gpu");
                    Add(root, effective, "kv_cache_type_k", "cache_type_k", "kv_cache_type_k");
                    Add(root, effective, "kv_cache_type_v", "cache_type_v", "kv_cache_type_v");
                    Add(root, effective, "cpu_moe", "cpu_moe", "n_cpu_moe");
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
                effective.GetValueOrDefault("gpu_layers"), "props.gpu_layers"),
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

        var contextKnown = effective.ContainsKey("context");
        var placementKnown = effective.ContainsKey("gpu_layers");
        var auditable = propsSucceeded
            && contextKnown
            && placementKnown
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
            auditable);

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
    }
}
