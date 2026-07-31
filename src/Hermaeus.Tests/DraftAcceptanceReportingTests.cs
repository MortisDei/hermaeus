using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r27's first recorded Speed Check came back null and could not be read: a
/// flat tok/s is equally consistent with drafting engaging and not helping,
/// and with drafting never engaging at all. r28 doc 02 makes those two
/// distinguishable. Everything here is a number the server produced or a
/// count the app kept; nothing here grades, scores, or recommends.
/// </summary>
public sealed class DraftAcceptanceReportingTests
{
    // ── 2.1 the server's own counters ──

    private static ChatServerTimings? Timings(string timingsJson) =>
        LlamaCppService.ParseStreamEvent($$"""
            {"choices":[{"delta":{"content":""},"finish_reason":"stop"}],"timings":{{timingsJson}}}
            """)?.ServerTimings;

    [Fact]
    public void Draft_counters_are_read_when_the_server_reports_them()
    {
        // The exact shape read off the installed b10195 with --spec-type ngram-mod.
        var timings = Timings("""{"prompt_n":140,"prompt_ms":285.3,"predicted_n":286,"predicted_ms":2921.5,"draft_n":64,"draft_n_accepted":31}""");

        Assert.NotNull(timings);
        Assert.Equal(64, timings!.DraftTokens);
        Assert.Equal(31, timings.DraftTokensAccepted);
    }

    [Fact]
    public void A_payload_without_draft_counters_reports_null_rather_than_zero()
    {
        var timings = Timings("""{"prompt_n":140,"prompt_ms":285.3,"predicted_n":286,"predicted_ms":2921.5}""");

        Assert.NotNull(timings);
        Assert.Null(timings!.DraftTokens);
        Assert.Null(timings.DraftTokensAccepted);
        // The four fields that were already parsed are untouched.
        Assert.Equal(140, timings.PromptTokens);
        Assert.Equal(286, timings.PredictedTokens);
    }

    /// <summary>
    /// The benchmark path streams, and the earlier verification of these field
    /// names was done against a non-streaming request. This is a real final
    /// SSE chunk captured from b10195 with `--spec-type ngram-mod`: an empty
    /// choices array, usage, and timings all on one chunk. It is here because
    /// "the field exists on the non-streaming response" would not have proved
    /// the streaming path works, and the streaming path is the one that runs.
    /// </summary>
    [Fact]
    public void The_real_streaming_final_chunk_yields_draft_counters()
    {
        const string realChunk = """
            {"choices":[],"usage":{"completion_tokens":300,"prompt_tokens":140,"total_tokens":440},"timings":{"cache_n":139,"prompt_n":1,"prompt_ms":246.21,"predicted_n":300,"predicted_ms":1047.107,"draft_n":267,"draft_n_accepted":220}}
            """;

        var evt = LlamaCppService.ParseStreamEvent(realChunk);

        Assert.NotNull(evt);
        Assert.True(evt!.IsFinal);
        Assert.Equal(267, evt.ServerTimings!.DraftTokens);
        Assert.Equal(220, evt.ServerTimings.DraftTokensAccepted);
    }

    [Fact]
    public void A_measured_zero_is_not_a_missing_measurement()
    {
        var timings = Timings("""{"predicted_n":10,"predicted_ms":100,"draft_n":0,"draft_n_accepted":0}""");

        Assert.Equal(0, timings!.DraftTokens);
        Assert.NotNull(timings.DraftTokens);
    }

    // ── 2.2 iterations ──

    [Fact]
    public void The_speed_check_runs_more_than_one_iteration_per_case()
    {
        var suite = SpeedCheck.Suite();

        Assert.Equal(SpeedCheck.IterationsPerCase, suite.IterationsPerCase);
        Assert.True(suite.IterationsPerCase > 1);
    }

    // ── 2.3 observed spread ──

    [Fact]
    public void Median_is_the_middle_value_not_the_mean()
    {
        // A mean would be dragged by the cold outlier; the median is not.
        Assert.Equal(70, SpeedCheckComparer.Median([10, 69, 70, 71, 72]));
        Assert.Equal(70.5, SpeedCheckComparer.Median([69, 70, 71, 72]));
        Assert.Equal(0, SpeedCheckComparer.Median([]));
    }

    private static BenchmarkRun Run(string id, string modelId, IEnumerable<(double Tps, int? Drafted, int? Accepted)> results) => new()
    {
        Id = id,
        ModelId = modelId,
        ModelName = modelId,
        SuiteId = SpeedCheck.SuiteId,
        SuiteName = SpeedCheck.SuiteName,
        StartedAt = DateTime.UtcNow,
        Status = "Complete",
        Results = [.. results.Select(r => new BenchmarkResult
        {
            CaseId = "c",
            ApproxTokensPerSecond = r.Tps,
            DraftTokens = r.Drafted,
            DraftTokensAccepted = r.Accepted
        })]
    };

    [Fact]
    public void A_side_reports_the_range_it_actually_saw()
    {
        var run = Run("a", "m", [(66.8, null, null), (70.2, null, null), (71.9, null, null)]);

        var side = SpeedCheckComparer.Compare(run, Run("b", "m", [(70.0, null, null)])).Comparison!.Baseline;

        Assert.Equal(70.2, side.TokensPerSecond);
        Assert.Equal(66.8, side.TokensPerSecondMin);
        Assert.Equal(71.9, side.TokensPerSecondMax);
        Assert.Equal(3, side.IterationCount);
        Assert.Equal("70.2 tok/s (66.8 to 71.9 over 3 runs)", side.SpreadLabel);
    }

    [Fact]
    public void The_spread_label_makes_no_statistical_claim()
    {
        var side = SpeedCheckComparer.Compare(
            Run("a", "m", [(66.8, null, null), (71.9, null, null)]),
            Run("b", "m", [(70.0, null, null)])).Comparison!.Baseline;

        foreach (var banned in new[] { "confiden", "significan", "margin", "better", "worse" })
            Assert.DoesNotContain(banned, side.SpreadLabel, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.4 acceptance beside the speed ──

    [Fact]
    public void Acceptance_is_summed_across_the_runs_iterations()
    {
        var side = SpeedCheckComparer.Compare(
            Run("a", "m", [(70.0, 40, 20), (70.0, 24, 11)]),
            Run("b", "m", [(70.0, null, null)])).Comparison!.Baseline;

        Assert.Equal(64, side.DraftTokens);
        Assert.Equal(31, side.DraftTokensAccepted);
        Assert.Contains("64 drafted", side.AcceptanceLabel, StringComparison.Ordinal);
        Assert.Contains("31 accepted", side.AcceptanceLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_drafted_says_drafting_did_not_engage()
    {
        var side = SpeedCheckComparer.Compare(
            Run("a", "m", [(70.0, 0, 0)]),
            Run("b", "m", [(70.0, null, null)])).Comparison!.Baseline;

        Assert.True(side.HasDraftCounters);
        Assert.Contains("did not engage", side.AcceptanceLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_the_server_never_counted_shows_nothing_rather_than_a_zero()
    {
        var side = SpeedCheckComparer.Compare(
            Run("a", "m", [(70.0, null, null)]),
            Run("b", "m", [(70.0, null, null)])).Comparison!.Baseline;

        Assert.False(side.HasDraftCounters);
        Assert.Null(side.DraftTokens);
        Assert.Equal(string.Empty, side.AcceptanceLabel);
    }

    [Fact]
    public void A_result_row_shows_acceptance_only_when_it_was_counted()
    {
        var counted = new BenchmarkResultViewModel(new BenchmarkResult { DraftTokens = 40, DraftTokensAccepted = 10 });
        var uncounted = new BenchmarkResultViewModel(new BenchmarkResult());

        Assert.True(counted.HasDraftAcceptance);
        Assert.Contains("25", counted.DraftAcceptance, StringComparison.Ordinal);
        Assert.False(uncounted.HasDraftAcceptance);
        Assert.Equal(string.Empty, uncounted.DraftAcceptance);
    }

    // ── 2.5 the Doctor rule ──

    private static SpeculativeDecodingConfig Drafting() => new() { Types = ["draft-mtp"] };

    [Fact]
    public void Drafting_switched_off_reports_nothing()
    {
        Assert.Equal(DraftEngagementState.NotConfigured, DraftEngagementAdvisory.Evaluate(null, null).State);
        Assert.Equal(DraftEngagementState.NotConfigured,
            DraftEngagementAdvisory.Evaluate(new SpeculativeDecodingConfig(), null).State);
    }

    [Fact]
    public void Never_measured_is_its_own_answer()
    {
        Assert.Equal(DraftEngagementState.NeverMeasured, DraftEngagementAdvisory.Evaluate(Drafting(), null).State);
    }

    /// <summary>
    /// The state that sent the owner looking for a number that was never
    /// there: the setting says drafting is on, a run exists, and the server it
    /// talked to reported no draft counters at all. llama-server emits them
    /// whenever speculative decoding is active (confirmed on b10195 and
    /// b10199, streaming and not), so this points at the server having been
    /// started before the setting changed. Changing the setting does not
    /// restart a running server.
    /// </summary>
    [Fact]
    public void A_run_whose_server_never_mentioned_drafting_is_its_own_answer()
    {
        var finding = DraftEngagementAdvisory.Evaluate(Drafting(), Run("a", "m", [(70.0, null, null)]));

        Assert.Equal(DraftEngagementState.ConfiguredButNotReported, finding.State);
        Assert.Null(finding.DraftTokens);
        // Not the same as a measured zero, and not the same as never measured.
        Assert.NotEqual(DraftEngagementState.ConfiguredButNeverEngaged, finding.State);
        Assert.NotEqual(DraftEngagementState.NeverMeasured, finding.State);
    }

    [Fact]
    public void Measured_and_found_dead_is_a_different_answer()
    {
        var finding = DraftEngagementAdvisory.Evaluate(Drafting(), Run("a", "m", [(70.0, 0, 0)]));

        Assert.Equal(DraftEngagementState.ConfiguredButNeverEngaged, finding.State);
        Assert.Equal(0, finding.DraftTokens);
    }

    [Fact]
    public void Drafting_that_engaged_reports_nothing_to_act_on()
    {
        var finding = DraftEngagementAdvisory.Evaluate(Drafting(), Run("a", "m", [(70.0, 64, 31)]));

        Assert.Equal(DraftEngagementState.Engaged, finding.State);
        Assert.Equal(64, finding.DraftTokens);
        Assert.Equal(31, finding.DraftTokensAccepted);
    }
}
