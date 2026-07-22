namespace Hermaeus.Services;

public sealed record EngineOptionPreset(int ContextSize, string KvCacheType);

/// <summary>
/// Hardware-tier engine-option recommendation (r18 04-llama-server-engine-options.md 4.3),
/// distilled from the owner-supplied llama-server tuning guide's per-tier cheat sheet (6 GB:
/// 8k ctx + q4_0 KV; 8 GB: 16k + q8_0; 16 GB: 32k+ + q8_0). Pure and hardware-only: the caller
/// (<c>ServicesViewModel.SuggestEngineSettings</c>) fills these into the editable server-editor
/// form and never saves or applies them without an explicit user click - the same contract as
/// Auto Tune's result. Never forces a value; always a recommendation the user can decline.
/// </summary>
public static class EngineOptionPresets
{
    /// <summary>Returns the recommended <see cref="EngineOptionPreset"/> for the given VRAM
    /// budget, capped to the model's training context length when known.</summary>
    public static EngineOptionPreset Recommend(long vramBytes, int? trainingContextLength)
    {
        var (contextSize, kvCacheType) = vramBytes switch
        {
            >= 16_000_000_000 => (32768, "q8_0"),
            >= 8_000_000_000 => (16384, "q8_0"),
            >= 6_000_000_000 => (8192, "q4_0"),
            > 0 => (4096, "q4_0"),
            _ => (4096, "f16")
        };

        if (trainingContextLength is > 0)
            contextSize = Math.Min(contextSize, trainingContextLength.Value);

        return new EngineOptionPreset(contextSize, kvCacheType);
    }
}
