using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

/// <summary>
/// What kind of thing a <see cref="SourceReference"/> points back to. The
/// long-term goal (docs/review/archived/r1/07-roadmap.md, "provenance everywhere") is for
/// any answer to be traceable to memories, chunks, files, and tool output
/// through this one shared shape rather than each surface inventing its own.
/// </summary>
public enum ProvenanceKind
{
    Rag,
    Memory,
    Workspace,
    AgentTool,

    /// <summary>A locally recorded benchmark or Speed Check observation.</summary>
    Benchmark,

    /// <summary>r24 doc 02 2.6: a Recall hit injected into chat context. Untrusted text
    /// the model reads, never instruction the app acts on; cannot carry a memory id a
    /// [MEMORY_UPDATE]/[MEMORY_FORGET] marker could target.</summary>
    Recall,

    /// <summary>A structured empirical experience record.</summary>
    Experience,

    /// <summary>An immutable controlled Lab experiment or observation.</summary>
    Lab,

    /// <summary>A direct runtime or operating-system measurement.</summary>
    RuntimeObservation
}

/// <summary>
/// How strongly Hermaeus can stand behind an item of evidence. This is kept on
/// the shared source reference so memories, lessons, benchmark observations,
/// and future consumers do not grow competing provenance models.
/// </summary>
[JsonConverter(typeof(EvidenceOriginJsonConverter))]
public enum EvidenceOrigin
{
    /// <summary>A runtime, operating system, or visible operation reported it.</summary>
    DirectObservation = 0,

    /// <summary>A person supplied it; it is not independently established.</summary>
    UserProvided = 1,

    /// <summary>A model inferred, summarized, proposed, or predicted it.</summary>
    ModelInference = 2,

    /// <summary>Named inputs and versioned deterministic code produced it.</summary>
    DeterministicCalculation = 3,

    /// <summary>A deterministic parser copied or transformed it from a source.</summary>
    Extracted = 4
}

/// <summary>
/// Preserves the two legacy EvidenceOrigin representations while making every
/// new write explicit. Numeric value 2 and the legacy string "Inferred" both
/// mean ModelInference. Unknown values are refused rather than promoted into
/// a fact.
/// </summary>
public sealed class EvidenceOriginJsonConverter : JsonConverter<EvidenceOrigin>
{
    public override EvidenceOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                0 => EvidenceOrigin.DirectObservation,
                1 => EvidenceOrigin.UserProvided,
                2 => EvidenceOrigin.ModelInference,
                3 => EvidenceOrigin.DeterministicCalculation,
                4 => EvidenceOrigin.Extracted,
                _ => throw new JsonException($"Unknown evidence origin value {numeric}.")
            };
        }

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Evidence origin must be a string or compatible legacy number.");

        var value = reader.GetString()?.Replace("-", "_", StringComparison.Ordinal).Trim();
        return value?.ToLowerInvariant() switch
        {
            "directobservation" or "direct_observation" => EvidenceOrigin.DirectObservation,
            "deterministiccalculation" or "deterministic_calculation" => EvidenceOrigin.DeterministicCalculation,
            "userprovided" or "user_provided" => EvidenceOrigin.UserProvided,
            "extracted" => EvidenceOrigin.Extracted,
            "modelinference" or "model_inference" or "inferred" => EvidenceOrigin.ModelInference,
            _ => throw new JsonException($"Unknown evidence origin '{value}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, EvidenceOrigin value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            EvidenceOrigin.DirectObservation => "direct_observation",
            EvidenceOrigin.DeterministicCalculation => "deterministic_calculation",
            EvidenceOrigin.UserProvided => "user_provided",
            EvidenceOrigin.Extracted => "extracted",
            EvidenceOrigin.ModelInference => "model_inference",
            _ => throw new JsonException($"Unknown evidence origin value {(int)value}.")
        });
    }
}

/// <summary>
/// A pointer back to where a piece of content actually came from: a RAG
/// chunk, a memory, a workspace file, or an agent tool result. Deliberately
/// small and serializable so it can ride on traces, tool results, and UI
/// view models without pulling in project-specific types.
/// </summary>
public sealed record SourceReference(
    ProvenanceKind Kind,
    string Title,
    string? Locator = null,
    string? Snippet = null,
    double? Score = null,
    DateTime? Timestamp = null,
    EvidenceOrigin EvidenceOrigin = EvidenceOrigin.DirectObservation);
