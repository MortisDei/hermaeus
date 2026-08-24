using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermaeus.Core.Models;

public static class EmpiricalExperienceDomains
{
    public const string AgentToolOutcome = "agent-tool-outcome";
    public const string GpuFitObservation = "gpu-fit-observation";
    public const string LabRun = "lab-run";

    public static readonly IReadOnlySet<string> Initial = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentToolOutcome, GpuFitObservation, LabRun
    };
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EmpiricalExperienceStatus { Current, Superseded }

public sealed record EmpiricalExperienceProvenance(string EvidenceId, SourceReference Source);

public sealed record EmpiricalExperience
{
    public string Id { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public string Domain { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? WorkspaceFingerprint { get; init; }
    public string ContextJson { get; init; } = "{}";
    public string ContextHash { get; init; } = string.Empty;
    public string ActionJson { get; init; } = "{}";
    public string ActionHash { get; init; } = string.Empty;
    public NormalizedToolOutcome Outcome { get; init; } = new();
    public IReadOnlyList<EmpiricalExperienceProvenance> Provenance { get; init; } = [];
    public string? RuntimeFingerprint { get; init; }
    public string? ModelFingerprint { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? CorrectsExperienceId { get; init; }
    public EmpiricalExperienceStatus Status { get; init; } = EmpiricalExperienceStatus.Current;
}

public sealed record EmpiricalExperienceDraft
{
    public int SchemaVersion { get; init; } = 1;
    public string Domain { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? WorkspaceFingerprint { get; init; }
    public string ContextJson { get; init; } = "{}";
    public string ActionJson { get; init; } = "{}";
    public NormalizedToolOutcome Outcome { get; init; } = new();
    public IReadOnlyList<EmpiricalExperienceProvenance> Provenance { get; init; } = [];
    public string? RuntimeFingerprint { get; init; }
    public string? ModelFingerprint { get; init; }
}

public sealed record EmpiricalExperienceQuery
{
    public string? Domain { get; init; }
    public string? ProjectId { get; init; }
    public string? WorkspaceFingerprint { get; init; }
    public string? RuntimeFingerprint { get; init; }
    public string? ModelFingerprint { get; init; }
    public string? ContextHash { get; init; }
    public string? ActionHash { get; init; }
    public NormalizedOutcome? Outcome { get; init; }
    public EvidenceOrigin? Origin { get; init; }
    public DateTime? CreatedFromUtc { get; init; }
    public DateTime? CreatedToUtc { get; init; }
    public EmpiricalExperienceStatus? Status { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record EmpiricalExperienceExport(int SchemaVersion, DateTime ExportedAtUtc, IReadOnlyList<EmpiricalExperience> Experiences);

public interface IEmpiricalExperienceCodec<TContext, TAction>
{
    string Domain { get; }
    string EncodeContext(TContext value);
    string EncodeAction(TAction value);
    TContext DecodeContext(string json);
    TAction DecodeAction(string json);
}

public abstract class EmpiricalExperienceCodec<TContext, TAction> : IEmpiricalExperienceCodec<TContext, TAction>
{
    public abstract string Domain { get; }
    public string EncodeContext(TContext value) => ExperienceJson.Canonicalize(value);
    public string EncodeAction(TAction value) => ExperienceJson.Canonicalize(value);
    public TContext DecodeContext(string json) => ExperienceJson.Decode<TContext>(json);
    public TAction DecodeAction(string json) => ExperienceJson.Decode<TAction>(json);
}

public sealed record AgentToolExperienceContext(string TaskId, int Step, int TranscriptEntry, string ToolName);
public sealed record AgentToolExperienceAction(string ArgumentsJson, int NormalizedDerivationVersion);
public sealed class AgentToolExperienceCodec : EmpiricalExperienceCodec<AgentToolExperienceContext, AgentToolExperienceAction>
{
    public override string Domain => EmpiricalExperienceDomains.AgentToolOutcome;
}

public sealed record GpuFitExperienceContext(string ConfigurationFingerprint, string AnalyticalInputJson);
public sealed record GpuFitExperienceAction(string ObservationSeriesId, string AnalyticalBreakdownJson);
public sealed class GpuFitExperienceCodec : EmpiricalExperienceCodec<GpuFitExperienceContext, GpuFitExperienceAction>
{
    public override string Domain => EmpiricalExperienceDomains.GpuFitObservation;
}

public sealed record LabRunExperienceContext(string DefinitionId, string RunId, string WorkloadFingerprint);
public sealed record LabRunExperienceAction(string ConfigurationJson, string ObservationIdsJson);
public sealed class LabRunExperienceCodec : EmpiricalExperienceCodec<LabRunExperienceContext, LabRunExperienceAction>
{
    public override string Domain => EmpiricalExperienceDomains.LabRun;
}

public static class ExperienceJson
{
    public const int MaxDocumentBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Canonicalize<T>(T value)
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(value, Options));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, parsed.RootElement);
        if (stream.Length > MaxDocumentBytes)
            throw new InvalidOperationException($"Experience document exceeds {MaxDocumentBytes} bytes.");
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string CanonicalizeJson(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
            throw new InvalidOperationException($"Experience document exceeds {MaxDocumentBytes} bytes.");
        using var parsed = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, parsed.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static T Decode<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Experience document was null.");

    public static string Hash(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
