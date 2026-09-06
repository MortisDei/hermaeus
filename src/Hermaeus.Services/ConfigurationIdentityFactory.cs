using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Creates the one configuration projection used by managed runtime,
/// benchmark, and Lab identity paths. Configuration identity describes what
/// was requested and what extra arguments were retained, not what a runtime
/// silently ignored or selected.
/// </summary>
public static class ConfigurationIdentityFactory
{
    private const int MaximumExtraIdentityEntries = 16;
    private static readonly HashSet<string> RecognizedNonCoreOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--alias", "--cache-reuse", "--cache-type-k", "--cache-type-v", "--flash-attn", "-fa",
        "--context-shift", "--no-context-shift", "--load-mode", "--mlock", "--mmap", "--no-mmap",
        "--cors-origins", "--cpu-moe", "-cmoe", "--n-cpu-moe", "-ncmoe", "--spec-type",
        "--spec-draft-model", "-md", "--model-draft", "-ngld", "--gpu-layers-draft", "--spec-draft-ngl",
        "--spec-draft-n-max", "--spec-draft-n-min", "--spec-draft-p-min", "--mmproj", "--embeddings",
        "--pooling", "-b", "--batch-size", "-ub", "--ubatch-size", "--threads-batch", "--fit-target",
        "--fit-ctx", "--device", "--split-mode", "--tensor-split", "--main-gpu", "--kv-offload",
        "--cache-ram", "--ctx-checkpoints", "--swa-checkpoints", "--checkpoint-min-step", "--kv-unified",
        "--kv-unified-per-slot", "--cache-idle-slots", "--slot-save-path"
    };

    public static ConfigurationIdentityV2 Create(ServerConfig config, string companionIdentity = "")
    {
        if (!config.TryGetGpuPlacement(out var placement, out _))
            placement = null;

        var parsed = ParseExtraArguments(config.ExtraArgs, out var complete);
        var speculative = config.Speculative ?? new SpeculativeDecodingConfig();
        var companion = string.IsNullOrWhiteSpace(companionIdentity)
            ? CompanionIdentity(speculative.DraftModelPath)
            : companionIdentity;

        return CreateCore(
            config.ContextSize,
            placement,
            config.GpuLayers,
            config.Threads,
            config.PromptThreads,
            config.Slots,
            config.KvCacheTypeK,
            config.KvCacheTypeV,
            config.FlashAttention,
            string.Join(",", speculative.Types),
            companion,
            SpeculativeParameters(speculative),
            config.CpuMoeLayers,
            parsed,
            complete);
    }

    public static ConfigurationIdentityV2 Create(LabConfiguration configuration)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        var complete = true;
        if (!string.IsNullOrWhiteSpace(configuration.ExtraArgumentsSha256))
        {
            parsed["extraArgumentsSha256"] = configuration.ExtraArgumentsSha256;
            complete = false;
        }

        if (!string.Equals(configuration.PromptCacheMode, "default", StringComparison.Ordinal))
            parsed["promptCacheMode"] = configuration.PromptCacheMode;

        var placement = configuration.GpuPlacement;
        if (placement is null && GpuPlacementIntent.TryFromLegacy(configuration.GpuLayers, out var legacy, out _))
            placement = legacy;

        return CreateCore(
            configuration.ContextSize,
            placement,
            configuration.GpuLayers,
            configuration.Threads,
            configuration.PromptThreads,
            configuration.Slots,
            configuration.KvCacheTypeK,
            configuration.KvCacheTypeV,
            configuration.FlashAttention,
            string.Join(",", configuration.SpeculativeTypes),
            configuration.SpeculativeCompanionIdentity,
            $"nmax={Format(configuration.SpeculativeNMax)};" +
            $"nmin={Format(configuration.SpeculativeNMin)};" +
            $"pmin={Format(configuration.SpeculativePMin)};" +
            $"ngld={Format(configuration.SpeculativeDraftGpuLayers)}",
            configuration.CpuMoeLayers,
            parsed,
            complete);
    }

    private static ConfigurationIdentityV2 CreateCore(
        int? contextSize,
        GpuPlacementIntent? placement,
        int? legacyGpuLayers,
        int? threads,
        int? promptThreads,
        int? slots,
        string kvCacheTypeK,
        string kvCacheTypeV,
        string flashAttention,
        string speculativeMechanism,
        string speculativeCompanionIdentity,
        string speculativeParameters,
        int? cpuMoeLayers,
        IReadOnlyDictionary<string, string> parsedExtraArguments,
        bool extrasComplete)
    {
        var placementValue = placement is null
            ? string.Empty
            : $"v{placement.SchemaVersion}:{placement.CanonicalValue}";
        var effectiveLegacy = placement?.LegacyGpuLayers ?? legacyGpuLayers;
        var complete = extrasComplete && placement is not null && placement.TryValidate(out _);
        return new ConfigurationIdentityV2(
            contextSize,
            effectiveLegacy,
            placementValue,
            threads,
            promptThreads,
            slots,
            null,
            null,
            kvCacheTypeK ?? string.Empty,
            kvCacheTypeV ?? string.Empty,
            flashAttention ?? string.Empty,
            speculativeMechanism,
            speculativeCompanionIdentity ?? string.Empty,
            speculativeParameters,
            cpuMoeLayers,
            parsedExtraArguments,
            complete ? IdentityCompleteness.Complete : IdentityCompleteness.Incomplete);
    }

    private static IReadOnlyDictionary<string, string> ParseExtraArguments(string? raw, out bool complete)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        complete = true;
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var tokens = ExtraArgsParser.Split(raw).ToArray();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var option = token.Split('=', 2)[0];
            if (IsTypedCoreOption(option))
            {
                if (token.IndexOf('=') < 0 && index + 1 < tokens.Length && !tokens[index + 1].StartsWith("-", StringComparison.Ordinal))
                    index++;
                continue;
            }

            var recognized = RecognizedNonCoreOptions.Contains(option);

            var value = token;
            if (token.IndexOf('=') < 0 && index + 1 < tokens.Length && !tokens[index + 1].StartsWith("-", StringComparison.Ordinal))
                value = $"{token}={tokens[++index]}";

            if (result.Count >= MaximumExtraIdentityEntries)
            {
                complete = false;
                continue;
            }

            var key = $"extra.{result.Count.ToString(CultureInfo.InvariantCulture)}";
            result[key] = Sha256(value);
            if (!recognized)
                complete = false;
        }

        return result;
    }

    private static bool IsTypedCoreOption(string option) => option.ToLowerInvariant() is
        "-c" or "--ctx-size" or "--context-size" or
        "--n-gpu-layers" or "--gpu-layers" or "-ngl" or "--fit" or
        "--threads" or "--parallel" or "--port" or "--host";

    private static string CompanionIdentity(string path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty
        : Sha256(Path.GetFullPath(path));

    private static string SpeculativeParameters(SpeculativeDecodingConfig config) =>
        $"nmax={Format(config.NMax)};nmin={Format(config.NMin)};pmin={Format(config.PMin)};ngld={Format(config.DraftGpuLayers)}";

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Format(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
