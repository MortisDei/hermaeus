namespace Hermaeus.Core.Models;

public enum BenchmarkRunMode
{
    ColdWarm = 0,
    ColdOnly = 1,
    WarmOnly = 2
}

public enum BenchmarkPhase
{
    Cold = 0,
    Warm = 1
}

public sealed class BenchmarkRunMetadata
{
    public string AppVersion { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ModelHash { get; set; } = string.Empty;
    public string Quantization { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string RuntimeKind { get; set; } = string.Empty;
    public int? ContextSize { get; set; }
    public string PromptTemplate { get; set; } = string.Empty;
    public string SamplerSettings { get; set; } = string.Empty;
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public double? RepeatPenalty { get; set; }
    public int? Seed { get; set; }
    public int? GpuLayers { get; set; }
    public int? Threads { get; set; }
    public int? PromptThreads { get; set; }
    public int? BatchSize { get; set; }

    /// <summary>
    /// The managed llama-server <c>--cache-type-k</c> configuration that
    /// produced this run. Empty means the run did not resolve to a managed
    /// llama-server, or predates recording this field; it never implies f16.
    /// </summary>
    public string KvCacheTypeK { get; set; } = string.Empty;

    /// <summary>See <see cref="KvCacheTypeK"/> for the corresponding V cache.</summary>
    public string KvCacheTypeV { get; set; } = string.Empty;

    /// <summary>
    /// The managed llama-server Flash Attention choice (<c>auto</c>,
    /// <c>on</c>, or <c>off</c>) that produced this run. Empty means it was not
    /// recorded, not that the runtime chose an automatic setting.
    /// </summary>
    public string FlashAttention { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public bool? RerankerEnabled { get; set; }
    public string OS { get; set; } = string.Empty;
    public string CPU { get; set; } = string.Empty;
    public string RAM { get; set; } = string.Empty;
    public string GPU { get; set; } = string.Empty;

    // ── r27 03-drafting-and-proof.md 3.5: what produced the number ──────────
    // Without these, 3.6's comparison has nothing to key on: two runs of the
    // same suite against the same model would be indistinguishable even though
    // the whole point is that their speculative settings differed.

    /// <summary>The <c>--spec-type</c> list in effect, comma-separated. Empty when speculative decoding was off.</summary>
    public string SpeculativeTypes { get; set; } = string.Empty;

    /// <summary>File name of the draft model, when a <c>draft-*</c> type was in use.</summary>
    public string SpeculativeDraftModel { get; set; } = string.Empty;

    public int? SpeculativeNMax { get; set; }
    public int? SpeculativeNMin { get; set; }
    public double? SpeculativePMin { get; set; }
    public int? SpeculativeDraftGpuLayers { get; set; }

    /// <summary>
    /// Persistent identity of the model and inference configuration measured
    /// by this run. Historical records remain empty rather than being
    /// backfilled from presumed defaults.
    /// </summary>
    public EmpiricalProfileFingerprint? ProfileFingerprint { get; set; }

    /// <summary>
    /// Portable v2 identity for new observations. It composes runtime, model,
    /// hardware, and configuration identity without persisting local paths.
    /// </summary>
    public EmpiricalProfileFingerprintV2? ProfileFingerprintV2 { get; set; }

    /// <summary>
    /// Shared evidence pointer for this observation. Benchmark-generated data
    /// is direct local evidence, not a claimed universal model capability.
    /// </summary>
    public SourceReference? ObservationSource { get; set; }

    /// <summary>
    /// The one-line description of this run's speculative configuration, used
    /// as the difference a comparison reports between two runs.
    /// </summary>
    public string SpeculativeSummary =>
        string.IsNullOrWhiteSpace(SpeculativeTypes)
            ? "speculative decoding off"
            : string.IsNullOrWhiteSpace(SpeculativeDraftModel)
                ? SpeculativeTypes
                : $"{SpeculativeTypes} with {SpeculativeDraftModel}";

    /// <summary>
    /// The inference-engine configuration that can materially affect memory
    /// use and measured speed. Historical or non-managed runs retain the fact
    /// that this configuration was not captured.
    /// </summary>
    public string InferenceEngineSummary
    {
        get
        {
            var hasKv = !string.IsNullOrWhiteSpace(KvCacheTypeK)
                || !string.IsNullOrWhiteSpace(KvCacheTypeV);
            var hasFlashAttention = !string.IsNullOrWhiteSpace(FlashAttention);
            if (!hasKv && !hasFlashAttention)
                return "inference engine settings not recorded";

            var kv = hasKv
                ? $"KV cache K/V {ValueOrNotRecorded(KvCacheTypeK)}/{ValueOrNotRecorded(KvCacheTypeV)}"
                : "KV cache K/V not recorded";
            var flashAttention = hasFlashAttention
                ? $"Flash Attention {FlashAttention}"
                : "Flash Attention not recorded";
            return $"{kv}; {flashAttention}";
        }
    }

    /// <summary>The complete recorded engine configuration for run comparison.</summary>
    public string InferenceConfigurationSummary =>
        InferenceEngineSummary == "inference engine settings not recorded"
            ? SpeculativeSummary
            : $"{InferenceEngineSummary}; {SpeculativeSummary}";

    private static string ValueOrNotRecorded(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not recorded" : value;
}
