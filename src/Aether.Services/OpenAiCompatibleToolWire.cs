using System.Text.Json;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Shared OpenAI-compatible wire format helpers for tool/function calling.
/// OpenAiService and LlamaCppService both speak this exact shape (llama.cpp's
/// server mirrors the OpenAI tool_calls format); Ollama reuses the tool
/// declaration shape but returns whole (non-fragmented) tool calls, so it
/// only uses <see cref="BuildTools"/> from here, not the accumulator.
/// </summary>
internal static class OpenAiCompatibleToolWire
{
    public static List<object> BuildMessages(IReadOnlyList<ChatMessage> messages, string? systemPrompt)
    {
        var msgs = messages.Select(m => (object)new OutgoingMessage(
            m.Role,
            BuildContent(m),
            m.ToolCallId,
            m.ToolCalls?.Select(tc => new OutgoingToolCall(tc.Id, "function", new OutgoingFunctionCall(tc.Name, tc.ArgumentsJson))).ToList())).ToList();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            msgs.Insert(0, new OutgoingMessage("system", systemPrompt, null, null));
        return msgs;
    }

    /// <summary>r19 5.3: a message with no images sends unchanged as a plain string (byte-identical
    /// to every non-vision path today); one WITH images becomes an OpenAI-style content-part array
    /// (llama-server's multimodal mode and the OpenAI provider both accept this same shape).</summary>
    private static object BuildContent(ChatMessage m)
    {
        if (m.Images is not { Count: > 0 })
            return m.Content;

        var parts = new List<object>();
        if (!string.IsNullOrEmpty(m.Content))
            parts.Add(new OutgoingTextPart("text", m.Content));
        foreach (var image in m.Images)
            parts.Add(new OutgoingImagePart("image_url", new OutgoingImageUrl(image.DataUri)));
        return parts;
    }

    public static List<object>? BuildTools(IReadOnlyList<LlmToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0) return null;
        return tools.Select(t => (object)new OutgoingTool("function", new OutgoingFunctionSpec(t.Name, t.Description, t.Parameters))).ToList();
    }

    /// <summary>Reads a streamed chunk's choices[0].delta.tool_calls (if any) into the accumulator.</summary>
    public static void AccumulateFromChunk(string json, ToolCallAccumulator accumulator)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array)
            {
                accumulator.Accumulate(toolCalls);
            }
        }
        catch (JsonException)
        {
            // A malformed chunk should not abort the rest of the stream; ParseStreamEvent handles reporting.
        }
    }

    /// <summary>
    /// OpenAI-compatible servers split a single tool call's id/name/arguments
    /// across several stream chunks, correlated by <c>index</c>; arguments
    /// arrive as successive string fragments that must be concatenated in
    /// order before the JSON they spell out can be parsed.
    /// </summary>
    public sealed class ToolCallAccumulator
    {
        private readonly SortedDictionary<int, Entry> _byIndex = new();

        public bool HasCalls => _byIndex.Count > 0;

        public void Accumulate(JsonElement deltaToolCalls)
        {
            foreach (var call in deltaToolCalls.EnumerateArray())
            {
                var index = call.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var idx) ? idx : 0;
                if (!_byIndex.TryGetValue(index, out var entry))
                {
                    entry = new Entry();
                    _byIndex[index] = entry;
                }

                if (call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(entry.Id))
                    entry.Id = idEl.GetString() ?? string.Empty;

                if (call.TryGetProperty("function", out var fn))
                {
                    if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(entry.Name))
                        entry.Name = nameEl.GetString() ?? string.Empty;
                    if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                        entry.Arguments.Append(argsEl.GetString());
                }
            }
        }

        public IReadOnlyList<LlmToolCallRequest> Complete() =>
            _byIndex.Values
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => new LlmToolCallRequest(
                    string.IsNullOrEmpty(e.Id) ? Guid.NewGuid().ToString("N") : e.Id,
                    e.Name,
                    e.Arguments.Length == 0 ? "{}" : e.Arguments.ToString()))
                .ToList();

        private sealed class Entry
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public readonly System.Text.StringBuilder Arguments = new();
        }
    }

    private sealed record OutgoingMessage(string role, object? content, string? tool_call_id, List<OutgoingToolCall>? tool_calls);
    private sealed record OutgoingToolCall(string id, string type, OutgoingFunctionCall function);
    private sealed record OutgoingFunctionCall(string name, string arguments);
    private sealed record OutgoingTool(string type, OutgoingFunctionSpec function);
    private sealed record OutgoingFunctionSpec(string name, string description, JsonElement parameters);
    private sealed record OutgoingTextPart(string type, string text);
    private sealed record OutgoingImagePart(string type, OutgoingImageUrl image_url);
    private sealed record OutgoingImageUrl(string url);
}
