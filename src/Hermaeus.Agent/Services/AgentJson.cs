using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermaeus.Agent.Services;

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>
    /// Non-indented variant for JSONL files (agent.trace.jsonl, transcript.jsonl),
    /// where each physical line must be exactly one JSON value. Using the
    /// indented <see cref="Options"/> there would spread one entry's pretty
    /// printed JSON across many lines, corrupting line-oriented replay.
    /// </summary>
    public static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}
