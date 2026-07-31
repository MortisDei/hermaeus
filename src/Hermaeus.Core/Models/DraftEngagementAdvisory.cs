namespace Hermaeus.Core.Models;

/// <summary>What the last Speed Check says about whether drafting engaged.</summary>
public enum DraftEngagementState
{
    /// <summary>Speculative decoding is off, so there is nothing to report.</summary>
    NotConfigured,

    /// <summary>Configured, but this model has never been through a Speed Check.</summary>
    NeverMeasured,

    /// <summary>
    /// Configured, and a Speed Check did run, and the server it talked to
    /// never mentioned drafting at all. Distinct from a measured zero and from
    /// never having measured: llama-server reports draft counters whenever
    /// speculative decoding is active, so a run with no counters at all points
    /// at the server having been started without the setting rather than at
    /// drafting having failed to help. Changing the setting does not restart a
    /// running server.
    /// </summary>
    ConfiguredButNotReported,

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

        if (latestSpeedCheck is null || latestSpeedCheck.Results.Count == 0)
            return new DraftEngagementFinding(DraftEngagementState.NeverMeasured, null, null);

        // A run that reported no counters at all is not "drafting did nothing"
        // and not "never measured" either. It is a run whose server never
        // mentioned drafting, which is what a server started before the
        // setting changed looks like.
        var reported = latestSpeedCheck.Results.Where(r => r.DraftTokens.HasValue).ToList();
        if (reported.Count == 0)
            return new DraftEngagementFinding(DraftEngagementState.ConfiguredButNotReported, null, null);

        var drafted = reported.Sum(r => r.DraftTokens!.Value);
        var accepted = reported.Sum(r => r.DraftTokensAccepted ?? 0);
        return new DraftEngagementFinding(
            drafted == 0 ? DraftEngagementState.ConfiguredButNeverEngaged : DraftEngagementState.Engaged,
            drafted,
            accepted);
    }
}
