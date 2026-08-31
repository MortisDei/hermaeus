using System.Text.Json;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// Converts the small, persisted managed-server recommendation patch into
/// typed settings edits. The patch contains only server identity and changed
/// scalar settings. It never contains executable, model, projector, draft
/// model, or other path-bearing values.
/// </summary>
public static class ManagedServerRecommendationPatch
{
    public const string TargetDomain = "managed-server";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SupportedFields =
    [
        "contextSize", "gpuPlacement", "threads", "promptThreads", "slots",
        "kvCacheTypeK", "kvCacheTypeV", "flashAttention", "cpuMoeLayers",
        "speculativeTypes", "draftGpuLayers", "speculativeNMax", "speculativeNMin",
        "speculativePMin"
    ];

    public static RecommendationPatch Create(string serverId, ServerConfig before, ServerConfig after)
    {
        ValidateServerId(serverId);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

        AddIfDifferent(changes, "contextSize", before.ContextSize, after.ContextSize);
        AddIfDifferent(changes, "threads", before.Threads, after.Threads);
        AddIfDifferent(changes, "promptThreads", before.PromptThreads, after.PromptThreads);
        AddIfDifferent(changes, "slots", before.Slots, after.Slots);
        AddIfDifferent(changes, "kvCacheTypeK", before.KvCacheTypeK, after.KvCacheTypeK);
        AddIfDifferent(changes, "kvCacheTypeV", before.KvCacheTypeV, after.KvCacheTypeV);
        AddIfDifferent(changes, "flashAttention", before.FlashAttention, after.FlashAttention);
        AddIfDifferent(changes, "cpuMoeLayers", before.CpuMoeLayers, after.CpuMoeLayers);

        var beforePlacement = PlacementValue(before);
        var afterPlacement = PlacementValue(after);
        if (!string.Equals(beforePlacement, afterPlacement, StringComparison.Ordinal))
            changes["gpuPlacement"] = after.TryGetGpuPlacement(out var placement, out var error)
                ? placement
                : throw new InvalidOperationException(error ?? "The proposed GPU placement is invalid.");

        var beforeSpeculative = before.Speculative ?? new SpeculativeDecodingConfig();
        var afterSpeculative = after.Speculative ?? new SpeculativeDecodingConfig();
        AddIfDifferent(changes, "speculativeTypes", beforeSpeculative.Types, afterSpeculative.Types);
        AddIfDifferent(changes, "draftGpuLayers", beforeSpeculative.DraftGpuLayers, afterSpeculative.DraftGpuLayers);
        AddIfDifferent(changes, "speculativeNMax", beforeSpeculative.NMax, afterSpeculative.NMax);
        AddIfDifferent(changes, "speculativeNMin", beforeSpeculative.NMin, afterSpeculative.NMin);
        AddIfDifferent(changes, "speculativePMin", beforeSpeculative.PMin, afterSpeculative.PMin);

        if (changes.Count == 0)
            throw new InvalidOperationException("The proposed managed-server patch does not change any supported setting.");
        return CreateFromChanges(serverId, changes);
    }

    public static RecommendationPatch CreateFromChanges(string serverId, IReadOnlyDictionary<string, object?> changes)
    {
        ValidateServerId(serverId);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0 || changes.Keys.Any(key => !SupportedFields.Contains(key, StringComparer.Ordinal)))
            throw new ArgumentException("The managed-server patch contains an unsupported or empty change set.", nameof(changes));
        var json = JsonSerializer.Serialize(new { serverId, changes }, JsonOptions);
        return RecommendationPatch.Create(TargetDomain, json);
    }

    public static ParsedManagedServerPatch Parse(string canonicalJson)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("serverId", out var serverIdElement)
            || serverIdElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("changes", out var changesElement)
            || changesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A managed-server patch must contain serverId and changes.");

        var serverId = serverIdElement.GetString() ?? string.Empty;
        ValidateServerId(serverId);
        var changes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in changesElement.EnumerateObject())
        {
            if (!SupportedFields.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidOperationException($"Managed-server patch field '{property.Name}' is not supported.");
            changes[property.Name] = property.Value.Clone();
        }
        if (changes.Count == 0)
            throw new InvalidOperationException("A managed-server patch must contain at least one change.");
        return new ParsedManagedServerPatch(serverId, changes);
    }

    public static void Apply(ServerConfig target, ParsedManagedServerPatch patch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(patch);
        if (!string.Equals(target.Id, patch.ServerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The managed-server patch target does not match the selected server.");

        foreach (var (field, value) in patch.Changes)
        {
            switch (field)
            {
                case "contextSize": target.ContextSize = ReadInt(value, field, 128, 2_097_152); break;
                case "threads": target.Threads = ReadInt(value, field, 0, 1024); break;
                case "promptThreads": target.PromptThreads = ReadInt(value, field, 0, 1024); break;
                case "slots": target.Slots = ReadInt(value, field, 1, 64); break;
                case "kvCacheTypeK": target.KvCacheTypeK = ReadText(value, field, 32); break;
                case "kvCacheTypeV": target.KvCacheTypeV = ReadText(value, field, 32); break;
                case "flashAttention": target.FlashAttention = ReadText(value, field, 8); break;
                case "cpuMoeLayers": target.CpuMoeLayers = ReadInt(value, field, -1, 4096); break;
                case "gpuPlacement": target.GpuPlacement = ReadPlacement(value); break;
                case "speculativeTypes":
                    target.Speculative ??= new SpeculativeDecodingConfig();
                    target.Speculative.Types = ReadTypes(value);
                    break;
                case "draftGpuLayers": target.Speculative ??= new SpeculativeDecodingConfig(); target.Speculative.DraftGpuLayers = ReadNullableInt(value, field, 0, 4096); break;
                case "speculativeNMax": target.Speculative ??= new SpeculativeDecodingConfig(); target.Speculative.NMax = ReadNullableInt(value, field, 0, 128); break;
                case "speculativeNMin": target.Speculative ??= new SpeculativeDecodingConfig(); target.Speculative.NMin = ReadNullableInt(value, field, 0, 128); break;
                case "speculativePMin": target.Speculative ??= new SpeculativeDecodingConfig(); target.Speculative.PMin = ReadNullableDouble(value, field, 0, 1); break;
                default: throw new InvalidOperationException($"Managed-server patch field '{field}' is not supported.");
            }
        }
        if (!target.TryGetGpuPlacement(out _, out var placementError))
            throw new InvalidOperationException(placementError ?? "The resulting GPU placement is invalid.");
    }

    private static GpuPlacementIntent? Placement(ServerConfig config) =>
        config.TryGetGpuPlacement(out var placement, out _) ? placement : null;

    private static string PlacementValue(ServerConfig config) => Placement(config)?.CanonicalValue ?? string.Empty;

    private static GpuPlacementIntent ReadPlacement(JsonElement value)
    {
        try
        {
            var placement = value.Deserialize<GpuPlacementIntent>(JsonOptions)
                ?? throw new InvalidOperationException("GPU placement is null.");
            if (!placement.TryValidate(out var error))
                throw new InvalidOperationException(error ?? "GPU placement is invalid.");
            return placement;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("GPU placement is not valid JSON for the managed-server patch.", ex);
        }
    }

    private static List<string> ReadTypes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("speculativeTypes must be an array.");
        var result = value.EnumerateArray().Select(item => ReadText(item, "speculativeTypes", 32)).ToList();
        if (result.Count > 4)
            throw new InvalidOperationException("speculativeTypes contains too many entries.");
        return result;
    }

    private static int ReadInt(JsonElement value, string field, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)
            || result < minimum || result > maximum)
            throw new InvalidOperationException($"{field} is outside its supported range.");
        return result;
    }

    private static int? ReadNullableInt(JsonElement value, string field, int minimum, int maximum) =>
        value.ValueKind == JsonValueKind.Null ? null : ReadInt(value, field, minimum, maximum);

    private static double? ReadNullableDouble(JsonElement value, string field, double minimum, double maximum)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result)
            || double.IsNaN(result) || double.IsInfinity(result) || result < minimum || result > maximum)
            throw new InvalidOperationException($"{field} is outside its supported range.");
        return result;
    }

    private static string ReadText(JsonElement value, string field, int maximum)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Contains('/') || text.Contains('\\'))
            throw new InvalidOperationException($"{field} is not a valid bounded setting value.");
        return text;
    }

    private static void AddIfDifferent<T>(IDictionary<string, object?> changes, string key, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
            changes[key] = after;
    }

    private static void ValidateServerId(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId) || serverId.Length > 128
            || serverId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("A managed-server patch requires a safe server id.", nameof(serverId));
    }
}

public sealed record ParsedManagedServerPatch(
    string ServerId,
    IReadOnlyDictionary<string, JsonElement> Changes);
