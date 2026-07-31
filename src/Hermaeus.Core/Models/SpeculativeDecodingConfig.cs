namespace Hermaeus.Core.Models;

/// <summary>
/// r27 03-drafting-and-proof.md 3.1: speculative decoding as one composable
/// section rather than a bool per technique.
/// <c>--spec-type</c> accepts a comma-separated list and drafting and n-gram
/// speculation are not mutually exclusive, so a second independent bool beside
/// <c>NgramSpeculative</c> would have given two knobs that both own one flag and
/// can contradict each other.
/// Every field defaults to "emit nothing", so a config that never touches this
/// section produces a byte-identical launch command.
/// </summary>
public sealed class SpeculativeDecodingConfig
{
    /// <summary>
    /// Values for <c>--spec-type</c>, e.g. <c>["ngram-mod"]</c> or
    /// <c>["draft-mtp"]</c>. A list of strings rather than an enum, so adding a
    /// type llama.cpp already supports is data rather than code.
    /// </summary>
    public List<string> Types { get; set; } = [];

    /// <summary>Path to the draft model (<c>--spec-draft-model</c>). Only meaningful with a <c>draft-*</c> type.</summary>
    public string DraftModelPath { get; set; } = string.Empty;

    /// <summary>Draft-model GPU layers (<c>-ngld</c>). Null leaves the server's own default alone.</summary>
    public int? DraftGpuLayers { get; set; }

    /// <summary>Maximum drafted tokens (<c>--spec-draft-n-max</c>, server default 3).</summary>
    public int? NMax { get; set; }

    /// <summary>Minimum drafted tokens (<c>--spec-draft-n-min</c>, server default 0).</summary>
    public int? NMin { get; set; }

    /// <summary>Minimum draft probability (<c>--spec-draft-p-min</c>, server default 0.00).</summary>
    public double? PMin { get; set; }

    /// <summary>
    /// The full <c>--spec-type</c> list llama-server b10195 accepts. Offered in
    /// the UI so a value is chosen rather than typed; eagle3, dflash and dspark
    /// are deliberately absent (r27 doc 03 "what this doc does not do").
    /// </summary>
    public static IReadOnlyList<string> SupportedTypes { get; } =
    [
        "ngram-mod",
        "ngram-simple",
        "ngram-map-k",
        "ngram-map-k4v",
        "ngram-cache",
        "draft-mtp",
        "draft-simple"
    ];

    /// <summary>True when any selected type needs a second model file on disk.</summary>
    public bool RequiresDraftModel =>
        Types.Any(t => t.StartsWith("draft-", StringComparison.OrdinalIgnoreCase));

    public bool IsEnabled => Types.Count > 0;
}
