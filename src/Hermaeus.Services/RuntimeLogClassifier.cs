using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// Shared runtime-line policy. This keeps known low-value llama scheduler
/// chatter out of the persistent sink while retaining aggregate timing and
/// meaningful failures.
/// </summary>
public static class RuntimeLogClassifier
{
    public static RuntimeLogLevel ClassifyLevel(string line)
    {
        var lowered = line.ToLowerInvariant();
        if (lowered.Contains("error", StringComparison.Ordinal)
            || lowered.Contains("failed", StringComparison.Ordinal))
        {
            // llama.cpp uses Error for the deliberately failed context probe
            // used to size Gemma's extra model. The explicit fallback wording
            // makes this a recoverable warning, not an unresolved failure.
            if (IsRecoverableMemoryFitProbe(lowered))
                return RuntimeLogLevel.Warning;
            return RuntimeLogLevel.Error;
        }

        return lowered.Contains("warn", StringComparison.Ordinal)
            ? RuntimeLogLevel.Warning : RuntimeLogLevel.Info;
    }

    public static bool ShouldPersist(RuntimeLogEntry entry)
    {
        if (entry.Level != RuntimeLogLevel.Debug || entry.Category != RuntimeLogCategory.Service)
            return true;

        var lowered = entry.Message.ToLowerInvariant();
        if (!lowered.Contains("slot", StringComparison.Ordinal))
            return true;

        // These are per-request scheduler lifecycle lines. print_timing is an
        // aggregate useful for diagnosing throughput and remains persistent.
        return !lowered.Contains("get_availabl", StringComparison.Ordinal)
            && !lowered.Contains("launch_slot", StringComparison.Ordinal)
            && !lowered.Contains("release", StringComparison.Ordinal);
    }

    private static bool IsRecoverableMemoryFitProbe(string lowered) =>
        lowered.Contains("ctx_other", StringComparison.Ordinal)
            && lowered.Contains("warning is normal during memory fitting", StringComparison.Ordinal)
        || lowered.Contains("failed to measure the memory of the extra model", StringComparison.Ordinal)
            && lowered.Contains("fitting without it", StringComparison.Ordinal);
}
