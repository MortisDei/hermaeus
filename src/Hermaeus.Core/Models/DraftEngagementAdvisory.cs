namespace Hermaeus.Core.Models;

/// <summary>What the last Speed Check says about whether drafting engaged.</summary>
public enum DraftEngagementState
{
    /// <summary>Speculative decoding is off, so there is nothing to report.</summary>
    NotConfigured,

    /// <summary>Configured, but this model has never been through a Speed Check.</summary>
    NeverMeasured,

    /// <summary>Configured, measured, and the server drafted nothing.</summary>
    ConfiguredButNeverEngaged,

    /// <summary>Configured, measured, and the server drafted.</summary>
    Engaged
}

public sealed record DraftEngagementFinding(DraftEngagementState State, int? DraftTokens, int? DraftTokensAccepted);

/// <summary>
/// Compares a setting against a recorded number (r28 doc 02 2.5). It runs
/// nothing, diagnoses nothing and proposes no fix; "never measured" and
/// "measured and found dead" are deliberately separate answers, because they
/// are separate facts and only one of them is worth acting on.
/// </summary>
public static class DraftEngagementAdvisory
{
    /// <param name="speculative">The server's speculative-decoding configuration, or null when it has none.</param>
    /// <param name="latestSpeedCheck">The most recent Speed Check run for the model, or null when there is none.</param>
    public static DraftEngagementFinding Evaluate(SpeculativeDecodingConfig? speculative, BenchmarkRun? latestSpeedCheck)
    {
        if (speculative is not { Types.Count: > 0 })
            return new DraftEngagementFinding(DraftEngagementState.NotConfigured, null, null);

        // A run whose results carry no draft counters at all is not evidence
        // that drafting did nothing: it is a server that did not report, which
        // is the same as never having measured.
        var reported = latestSpeedCheck?.Results.Where(r => r.DraftTokens.HasValue).ToList() ?? [];
        if (reported.Count == 0)
            return new DraftEngagementFinding(DraftEngagementState.NeverMeasured, null, null);

        var drafted = reported.Sum(r => r.DraftTokens!.Value);
        var accepted = reported.Sum(r => r.DraftTokensAccepted ?? 0);
        return new DraftEngagementFinding(
            drafted == 0 ? DraftEngagementState.ConfiguredButNeverEngaged : DraftEngagementState.Engaged,
            drafted,
            accepted);
    }
}
