using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// Computes a memory's effective importance from its stored importance and
/// how long it has gone unused, without any background rewrite job: this is
/// evaluated at read time from <see cref="Memory.LastRecalledAt"/> (or
/// <see cref="Memory.UpdatedAt"/> if it has never been recalled).
/// </summary>
public static class MemoryLifecycle
{
    /// <summary>Days for effective importance to halve for an unrecalled memory.</summary>
    public const double HalfLifeDays = 30.0;

    /// <summary>Pinned memories never decay; their stored importance is always the effective one.</summary>
    public static double ComputeEffectiveImportance(Memory memory, DateTime? now = null)
    {
        if (memory.IsPinned)
            return memory.ImportanceScore;

        var reference = memory.LastRecalledAt ?? memory.UpdatedAt;
        var days = Math.Max(0, ((now ?? DateTime.UtcNow) - reference).TotalDays);
        var decay = Math.Pow(0.5, days / HalfLifeDays);
        return memory.ImportanceScore * decay;
    }
}
