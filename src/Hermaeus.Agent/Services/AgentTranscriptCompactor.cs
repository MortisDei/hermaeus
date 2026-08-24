using System.Buffers;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Produces a compact, model-facing view of a persisted transcript. The
/// transcript file remains an unmodified execution record.
/// </summary>
public static class AgentTranscriptCompactor
{
    private const int DiagnosticRepeatThreshold = 3;

    public static AgentTranscriptCompactionResult Compact(IReadOnlyList<AgentTranscriptEntry> entries)
    {
        var replay = new List<AgentTranscriptReplayEntry>(entries.Count);
        var diagnostics = new List<AgentTranscriptRepeatDiagnostic>();

        for (var start = 0; start < entries.Count;)
        {
            var first = entries[start];
            if (!CanCompact(first))
            {
                replay.Add(new AgentTranscriptReplayEntry(first, 1));
                start++;
                continue;
            }

            var end = start + 1;
            while (end < entries.Count && SameSuccessfulOutcome(first, entries[end]))
                end++;

            var count = end - start;
            if (count == 1)
            {
                replay.Add(new AgentTranscriptReplayEntry(first, 1));
                start = end;
                continue;
            }

            var last = entries[end - 1];
            replay.Add(new AgentTranscriptReplayEntry(
                first with
                {
                    Content = $"{first.Content}\n\n[Identical successful outcome repeated {count} times; steps {first.Step}-{last.Step}, transcript entries {start + 1}-{end}.]"
                },
                count));

            if (count >= DiagnosticRepeatThreshold)
                diagnostics.Add(new AgentTranscriptRepeatDiagnostic(first.ToolName!, count, first.Step, last.Step));

            start = end;
        }

        return new AgentTranscriptCompactionResult(replay, diagnostics);
    }

    /// <summary>
    /// Creates additive replay metadata for a freshly recorded tool result.
    /// Older transcript entries have neither field and are deliberately not
    /// compacted because their arguments and outcome cannot be proven equal.
    /// </summary>
    public static AgentTranscriptEntry FromToolResult(int step, AgentToolResult result, DateTime timestamp) =>
        new(step, "tool", result.Tool, result.ResultSummary, timestamp, CanonicalizeArguments(result.Arguments),
            IsReplaySafe(result), result.NormalizedOutcome);

    public static string CanonicalizeArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var element = JsonSerializer.SerializeToElement(arguments);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        WriteCanonical(element, writer);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool CanCompact(AgentTranscriptEntry entry) =>
        entry.Role == "tool"
        && entry.ReplaySafe == true
        && !string.IsNullOrWhiteSpace(entry.ToolName)
        && !string.IsNullOrWhiteSpace(entry.ArgumentsCanonical);

    private static bool SameSuccessfulOutcome(AgentTranscriptEntry first, AgentTranscriptEntry candidate) =>
        CanCompact(candidate)
        && string.Equals(first.ToolName, candidate.ToolName, StringComparison.Ordinal)
        && string.Equals(first.ArgumentsCanonical, candidate.ArgumentsCanonical, StringComparison.Ordinal)
        && string.Equals(first.Content, candidate.Content, StringComparison.Ordinal);

    private static bool IsReplaySafe(AgentToolResult result) =>
        result.Source is not null
        && result.NormalizedOutcome.Outcome == NormalizedOutcome.Succeeded
        && !result.TimedOut
        && (result.ExitCode is not { } exitCode || exitCode == 0)
        && !result.ResultSummary.Contains("[truncated:", StringComparison.Ordinal)
        && !result.ResultSummary.Contains("[listing truncated:", StringComparison.Ordinal)
        && !result.ResultSummary.Contains("\"truncated\":true", StringComparison.Ordinal);

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed record AgentTranscriptReplayEntry(AgentTranscriptEntry Entry, int RepeatCount);

public sealed record AgentTranscriptRepeatDiagnostic(string ToolName, int Count, int FirstStep, int LastStep)
{
    public string Describe() =>
        $"Diagnostic only: {ToolName} returned the same successful outcome {Count} consecutive times (steps {FirstStep}-{LastStep}). No action was blocked.";
}

public sealed record AgentTranscriptCompactionResult(
    IReadOnlyList<AgentTranscriptReplayEntry> Entries,
    IReadOnlyList<AgentTranscriptRepeatDiagnostic> Diagnostics);
