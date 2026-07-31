namespace Hermaeus.Core.Models;

/// <summary>
/// A shape generation must take, enforced by the provider's sampler rather
/// than requested in prose (r28 doc 01). Exactly one of
/// <see cref="JsonSchema"/> and <see cref="Grammar"/> is set;
/// <see cref="FromJsonSchema"/> and <see cref="FromGrammar"/> are the only
/// construction paths that guarantee it.
/// </summary>
/// <remarks>
/// <see cref="Grammar"/> exists because llama.cpp's own surface has it and a
/// future caller may want a non-JSON shape. Nothing in the UI authors one.
/// </remarks>
public sealed record LlmOutputConstraint
{
    /// <summary>A JSON Schema document the response must validate against.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>A GBNF grammar the response must match.</summary>
    public string? Grammar { get; init; }

    /// <summary>
    /// Short human-readable label ("memory extraction v1") so a trace or the
    /// Context Inspector can say what was enforced without printing a schema.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>True when this constraint carries a JSON Schema rather than a grammar.</summary>
    public bool IsJsonSchema => !string.IsNullOrWhiteSpace(JsonSchema);

    public static LlmOutputConstraint FromJsonSchema(string jsonSchema, string description = "")
    {
        if (string.IsNullOrWhiteSpace(jsonSchema))
            throw new ArgumentException("A JSON schema constraint needs a schema.", nameof(jsonSchema));
        return new LlmOutputConstraint { JsonSchema = jsonSchema, Description = description ?? string.Empty };
    }

    public static LlmOutputConstraint FromGrammar(string grammar, string description = "")
    {
        if (string.IsNullOrWhiteSpace(grammar))
            throw new ArgumentException("A grammar constraint needs a grammar.", nameof(grammar));
        return new LlmOutputConstraint { Grammar = grammar, Description = description ?? string.Empty };
    }

    /// <summary>
    /// True when exactly one of the two shapes is set. A constraint that
    /// carries both or neither cannot be sent to any provider, and a caller
    /// that built one by hand (rather than through the factories) gets a
    /// refusal at the call site instead of an unconstrained request.
    /// </summary>
    public bool IsValid =>
        string.IsNullOrWhiteSpace(JsonSchema) != string.IsNullOrWhiteSpace(Grammar);
}
