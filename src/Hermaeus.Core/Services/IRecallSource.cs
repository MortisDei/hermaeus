using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// One of the four federated sources Recall fans a query out to (r24 doc 02
/// 2.4). <see cref="RecallService"/> in Hermaeus.Services fuses their results
/// with reciprocal rank fusion; a source that throws or exceeds its timeout
/// is omitted and named in the result footer, never silently dropped.
/// </summary>
public interface IRecallSource
{
    /// <summary>Shown in the "omitted" footer when this source times out or fails.</summary>
    string Name { get; }

    Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct);
}
