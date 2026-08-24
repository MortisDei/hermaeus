using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Core.Models;

public enum IdentityCompleteness
{
    Complete,
    Incomplete
}

public enum ModelIdentityStrength
{
    VerifiedHash,
    Manifest,
    FileMetadataFallback,
    Unknown
}

public sealed record RuntimeIdentityV2(
    string Kind,
    string ExecutableSha256,
    long? ExecutableSizeBytes,
    DateTime? ExecutableModifiedUtc,
    string Version,
    string Build,
    string Compiler,
    string Backend,
    string ManagedAssetIdentity,
    IdentityCompleteness Completeness)
{
    public int VersionNumber => 2;
    public string StableId => IdentityHash.Compute(
        Kind, ExecutableSha256, Format(ExecutableSizeBytes), Format(ExecutableModifiedUtc),
        Version, Build, Compiler, Backend, ManagedAssetIdentity, Completeness.ToString());

    public bool IdentifiesSameRuntime(RuntimeIdentityV2? other)
    {
        if (other is null)
            return false;
        if (!string.IsNullOrWhiteSpace(ExecutableSha256) && !string.IsNullOrWhiteSpace(other.ExecutableSha256))
            return string.Equals(Kind, other.Kind, StringComparison.Ordinal)
                && string.Equals(ExecutableSha256, other.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
        return string.Equals(StableId, other.StableId, StringComparison.Ordinal);
    }

    private static string Format(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed record ModelIdentityV2(
    string ManifestIdentity,
    string Sha256,
    long? FileSizeBytes,
    DateTime? FileModifiedUtc,
    string Architecture,
    string Quantization,
    string CompanionIdentity,
    ModelIdentityStrength Strength,
    IdentityCompleteness Completeness)
{
    public int VersionNumber => 2;
    public string StableId => IdentityHash.Compute(
        ManifestIdentity, Sha256, Format(FileSizeBytes), Format(FileModifiedUtc),
        Architecture, Quantization, CompanionIdentity, Strength.ToString(), Completeness.ToString());

    private static string Format(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed record HardwareIdentityV2(
    string OperatingSystem,
    string Architecture,
    string GpuBackend,
    string GpuDevice,
    long? VramBytes,
    long? RamBytes,
    string DriverVersion,
    string DeviceLayout,
    IdentityCompleteness Completeness)
{
    public int VersionNumber => 2;
    public string StableId => IdentityHash.Compute(
        OperatingSystem, Architecture, GpuBackend, GpuDevice, Format(VramBytes),
        Format(RamBytes), DriverVersion, DeviceLayout, Completeness.ToString());

    private static string Format(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed record ConfigurationIdentityV2(
    int? ContextSize,
    int? GpuLayers,
    string GpuPlacement,
    int? Threads,
    int? PromptThreads,
    int? Slots,
    int? BatchSize,
    int? UBatchSize,
    string KvCacheTypeK,
    string KvCacheTypeV,
    string FlashAttention,
    string SpeculativeMechanism,
    string SpeculativeCompanionIdentity,
    string SpeculativeParameters,
    int? CpuMoeLayers,
    IReadOnlyDictionary<string, string> ParsedExtraArguments,
    IdentityCompleteness Completeness)
{
    public int VersionNumber => 2;
    public string StableId => IdentityHash.Compute(
        Format(ContextSize), Format(GpuLayers), GpuPlacement, Format(Threads),
        Format(PromptThreads), Format(Slots), Format(BatchSize), Format(UBatchSize),
        KvCacheTypeK, KvCacheTypeV, FlashAttention, SpeculativeMechanism,
        SpeculativeCompanionIdentity, SpeculativeParameters, Format(CpuMoeLayers),
        IdentityHash.CanonicalMap(ParsedExtraArguments), Completeness.ToString());

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed record EmpiricalProfileFingerprintV2(
    RuntimeIdentityV2 Runtime,
    ModelIdentityV2 Model,
    HardwareIdentityV2 Hardware,
    ConfigurationIdentityV2 Configuration)
{
    public int Version => 2;
    public IdentityCompleteness Completeness =>
        Runtime.Completeness == IdentityCompleteness.Complete
        && Model.Completeness == IdentityCompleteness.Complete
        && Hardware.Completeness == IdentityCompleteness.Complete
        && Configuration.Completeness == IdentityCompleteness.Complete
            ? IdentityCompleteness.Complete
            : IdentityCompleteness.Incomplete;

    public string StableId => IdentityHash.Compute(
        Runtime.StableId, Model.StableId, Hardware.StableId, Configuration.StableId);

    public bool IsExactlyCompatibleWith(EmpiricalProfileFingerprintV2? other) =>
        other is not null && string.Equals(StableId, other.StableId, StringComparison.Ordinal);
}

public sealed record RuntimeCapabilityObservation(
    string CapabilityId,
    CapabilityState State,
    string EvidenceCode,
    string Detail,
    RuntimeIdentityV2 RuntimeIdentity,
    ModelIdentityV2? ModelIdentity,
    IReadOnlyDictionary<string, string> Parameters,
    DateTime ObservedAtUtc)
{
    public const int MaximumParameters = 16;
    public const int MaximumCapabilityIdLength = 128;
    public const int MaximumParameterKeyLength = 64;
    public const int MaximumParameterValueLength = 256;

    public static RuntimeCapabilityObservation Create(
        string capabilityId,
        CapabilityState state,
        string evidenceCode,
        string detail,
        RuntimeIdentityV2 runtimeIdentity,
        ModelIdentityV2? modelIdentity,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        DateTime observedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("Capability id is required.", nameof(capabilityId));

        var bounded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in parameters ?? [])
        {
            if (bounded.Count >= MaximumParameters)
                break;
            var key = pair.Key?.Trim() ?? string.Empty;
            if (key.Length == 0)
                continue;
            key = key[..Math.Min(key.Length, MaximumParameterKeyLength)];
            var value = pair.Value ?? string.Empty;
            bounded[key] = value[..Math.Min(value.Length, MaximumParameterValueLength)];
        }

        var boundedId = capabilityId.Trim();
        boundedId = boundedId[..Math.Min(boundedId.Length, MaximumCapabilityIdLength)];
        return new RuntimeCapabilityObservation(
            boundedId, state, evidenceCode.Trim(), detail.Trim(), runtimeIdentity,
            modelIdentity, bounded, observedAtUtc.ToUniversalTime());
    }
}

public sealed class RuntimeCapabilityRegistry
{
    private readonly Dictionary<string, RuntimeCapabilityObservation> _observations = new(StringComparer.Ordinal);

    public IReadOnlyCollection<RuntimeCapabilityObservation> Observations => _observations.Values;

    public void Observe(RuntimeCapabilityObservation observation) =>
        _observations[observation.CapabilityId] = observation;

    public RuntimeCapabilityObservation? Find(string capabilityId) =>
        _observations.GetValueOrDefault(capabilityId);
}

internal static class IdentityHash
{
    public static string Compute(params string[] fields)
    {
        var canonical = string.Join("\u001f", fields.Select(field => field ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string CanonicalMap(IReadOnlyDictionary<string, string> values) =>
        string.Join("\u001e", values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}"));
}
