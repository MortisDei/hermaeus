using System.Text.Json;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>What shapes of constraint a provider can actually enforce.</summary>
[Flags]
public enum LlmConstraintSupport
{
    None = 0,
    JsonSchema = 1,
    Grammar = 2
}

/// <summary>
/// Turns an <see cref="LlmOutputConstraint"/> into the field each provider
/// expects, and refuses in words when a provider cannot enforce one (r28 doc
/// 01 1.2/1.3).
/// </summary>
/// <remarks>
/// Verified against the installed llama-server (b10195) on
/// <c>/v1/chat/completions</c> before this was written, because a request
/// field a server does not recognise is dropped silently and unconstrained
/// output that happens to parse looks exactly like a working implementation:
/// a request carrying <c>response_format</c> with a one-integer-property
/// schema and the prompt "write a poem about the sea" returned
/// <c>{"waves": 100}</c>, and a request carrying a top-level <c>grammar</c> of
/// <c>root ::= "SEAGRAMMAR"</c> returned exactly that. Both reach the sampler.
/// </remarks>
public static class LlmOutputConstraintWire
{
    /// <summary>
    /// Name attached to the schema in an OpenAI-style <c>response_format</c>.
    /// The API requires one; nothing reads it back.
    /// </summary>
    private const string SchemaName = "hermaeus_output";

    /// <summary>
    /// The reason this request cannot be sent, or null when it can. A caller
    /// that set a constraint intends to parse the result without defending
    /// against prose, so a provider that cannot enforce one says so rather
    /// than quietly sending an unconstrained request.
    /// </summary>
    public static string? DescribeRefusal(LlmOutputConstraint? constraint, LlmConstraintSupport support, string providerName)
    {
        if (constraint is null)
            return null;

        if (!constraint.IsValid)
            return $"*{providerName} was given an output constraint that sets both a JSON schema and a grammar, or neither. Build it with LlmOutputConstraint.FromJsonSchema or FromGrammar.*";

        if (constraint.IsJsonSchema)
        {
            if (!support.HasFlag(LlmConstraintSupport.JsonSchema))
                return $"*{providerName} cannot enforce a JSON schema on this endpoint, and the request needed one ({Label(constraint)}). The request was not sent unconstrained.*";

            if (!TryParseSchema(constraint, out _))
                return $"*The output constraint ({Label(constraint)}) is not valid JSON, so {providerName} was not asked to enforce it.*";

            return null;
        }

        return support.HasFlag(LlmConstraintSupport.Grammar)
            ? null
            : $"*{providerName} cannot enforce a grammar, and the request needed one ({Label(constraint)}). The request was not sent unconstrained.*";
    }

    /// <summary>
    /// The OpenAI-style <c>response_format</c> value, or null when this
    /// constraint is not a JSON schema (or there is no constraint).
    /// </summary>
    public static object? ResponseFormat(LlmOutputConstraint? constraint)
    {
        if (constraint is null || !constraint.IsJsonSchema || !TryParseSchema(constraint, out var schema))
            return null;

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = SchemaName,
                strict = true,
                schema
            }
        };
    }

    /// <summary>
    /// Ollama's <c>format</c> value: the schema document itself, not wrapped.
    /// Null when this constraint is not a JSON schema.
    /// </summary>
    public static JsonElement? OllamaFormat(LlmOutputConstraint? constraint) =>
        constraint is not null && constraint.IsJsonSchema && TryParseSchema(constraint, out var schema)
            ? schema
            : null;

    /// <summary>llama.cpp's top-level <c>grammar</c> value, or null.</summary>
    public static string? Grammar(LlmOutputConstraint? constraint) =>
        constraint is not null && !constraint.IsJsonSchema && !string.IsNullOrWhiteSpace(constraint.Grammar)
            ? constraint.Grammar
            : null;

    private static bool TryParseSchema(LlmOutputConstraint constraint, out JsonElement schema)
    {
        try
        {
            schema = JsonDocument.Parse(constraint.JsonSchema!).RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            schema = default;
            return false;
        }
    }

    private static string Label(LlmOutputConstraint constraint) =>
        string.IsNullOrWhiteSpace(constraint.Description) ? "unnamed constraint" : constraint.Description;
}
