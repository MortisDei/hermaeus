namespace Hermaeus.Core.Models;

/// <summary>
/// r27 03-drafting-and-proof.md 3.5 and 3.6: the speed check, and the
/// comparison between two of its runs.
/// Enabling speculative decoding without a way to measure it produces a knob
/// that is believed rather than known, on hardware where the answer genuinely
/// varies. This is composition over what BenchmarkService already measures from
/// llama-server's own timings, not new measurement.
/// </summary>
public static class SpeedCheck
{
    public const string SuiteId = "speculative-speed-check";
    public const string SuiteName = "Speed Check";

    /// <summary>
    /// A fixed, short suite whose cases are chosen for the shapes where drafting
    /// behaves differently: repetitive and structured output, where a draft's
    /// acceptance rate is high, and free prose, where it is not.
    /// It is a speed measurement, so no case asserts anything about quality:
    /// no expected keywords, no expected regexes, no refusal expectation.
    /// </summary>
    /// <summary>
    /// Iterations per case (r28 doc 02 2.2). r27's first recorded result was
    /// one cold iteration per case, and a 1.6% difference from a single sample
    /// says nothing. Five is enough for a spread to be visible and keeps four
    /// cases at roughly 20 generations a side, which is a few minutes on the
    /// hardware this runs on rather than an afternoon.
    /// </summary>
    public const int IterationsPerCase = 5;

    public static BenchmarkSuite Suite() => new()
    {
        Id = SuiteId,
        Name = SuiteName,
        Description = "Fixed prompts for measuring tokens per second. Reports speed only; it does not judge the answers.",
        ScoringProfile = "fast-chat-v1",
        IterationsPerCase = IterationsPerCase,
        Cases =
        [
            new BenchmarkCase
            {
                Id = "speed-check-structured",
                Name = "Structured output",
                Prompt = "Output a JSON array of the numbers 1 through 40, each as an object with a single key n. No explanation."
            },
            new BenchmarkCase
            {
                Id = "speed-check-repetitive",
                Name = "Repetitive output",
                Prompt = "Write the numbers from 1 to 60, one per line, each followed by the word item."
            },
            new BenchmarkCase
            {
                Id = "speed-check-code",
                Name = "Code",
                Prompt = "Write a C# class called Ledger with an Add method, a Remove method, and a Total property. Output only the code."
            },
            new BenchmarkCase
            {
                Id = "speed-check-prose",
                Name = "Free prose",
                Prompt = "Describe, in three paragraphs of ordinary prose, what a local-first application is and why someone might want one."
            }
        ]
    };
}

/// <summary>
/// One side of a speed-check comparison: the measured numbers and the
/// configuration that produced them.
/// </summary>
/// <param name="TokensPerSecond">Median across the run's iterations, not the mean, so one slow cold pass does not move it.</param>
/// <param name="TokensPerSecondMin">Slowest iteration observed.</param>
/// <param name="TokensPerSecondMax">Fastest iteration observed.</param>
/// <param name="IterationCount">How many measured generations the numbers above came from.</param>
/// <param name="DraftTokens">
/// Tokens the server drafted across the run, or null when it reported no
/// draft counters at all (r28 doc 02 2.4). Zero and null are different facts:
/// zero means drafting was configured and never engaged, so the comparison
/// was between two identical configurations.
/// </param>
/// <param name="DraftTokensAccepted">Drafted tokens the target model accepted.</param>
public sealed record SpeedCheckSide(
    string RunId,
    DateTime StartedAt,
    string ConfigurationSummary,
    double TokensPerSecond,
    double PromptTokensPerSecond,
    double FirstTokenMs,
    double TokensPerSecondMin = 0,
    double TokensPerSecondMax = 0,
    int IterationCount = 1,
    int? DraftTokens = null,
    int? DraftTokensAccepted = null)
{
    /// <summary>
    /// What was seen, phrased as what was seen: "70.2 tok/s (66.8 to 71.9
    /// over 5 runs)". Not a confidence interval and not a significance claim.
    /// If the two sides overlap, the reader can see that for themselves,
    /// which is where the app's job ends.
    /// </summary>
    public string SpreadLabel => IterationCount <= 1
        ? $"{TokensPerSecond:F1} tok/s (1 run)"
        : $"{TokensPerSecond:F1} tok/s ({TokensPerSecondMin:F1} to {TokensPerSecondMax:F1} over {IterationCount} runs)";

    /// <summary>
    /// Whether the server reported draft counters at all. False means nobody
    /// counted, which is never displayed as a zero.
    /// </summary>
    public bool HasDraftCounters => DraftTokens.HasValue;

    /// <summary>
    /// Drafted, accepted and the ratio between them, or empty when the server
    /// reported nothing. No recommendation is attached: "12%" is a fact,
    /// "12%, consider disabling drafting" is a recommendation.
    /// </summary>
    public string AcceptanceLabel
    {
        get
        {
            if (DraftTokens is not { } drafted)
                return string.Empty;
            if (drafted == 0)
                return "0 drafted (drafting did not engage)";

            var accepted = DraftTokensAccepted ?? 0;
            return $"{drafted:N0} drafted, {accepted:N0} accepted ({(double)accepted / drafted:P0})";
        }
    }
}

/// <summary>
/// Two speed-check runs of the same suite against the same model, with the
/// configuration difference that separates them.
/// Deliberately absent, and not oversights: no verdict, grade, score or
/// recommendation (settled by r23 2.3), and no significance claim, because a
/// handful of runs on a desktop under unknown load does not support one.
/// </summary>
public sealed record SpeedCheckComparison(
    string ModelId,
    string SuiteId,
    SpeedCheckSide Baseline,
    SpeedCheckSide Candidate)
{
    public double TokensPerSecondDelta => Candidate.TokensPerSecond - Baseline.TokensPerSecond;
    public double PromptTokensPerSecondDelta => Candidate.PromptTokensPerSecond - Baseline.PromptTokensPerSecond;
    public double FirstTokenMsDelta => Candidate.FirstTokenMs - Baseline.FirstTokenMs;

    /// <summary>
    /// The configuration difference, which is the entire reason two runs of the
    /// same suite against the same model are worth putting side by side.
    /// </summary>
    public string ConfigurationDelta =>
        string.Equals(Baseline.ConfigurationSummary, Candidate.ConfigurationSummary, StringComparison.Ordinal)
            ? "no configuration difference recorded"
            : $"{Baseline.ConfigurationSummary} -> {Candidate.ConfigurationSummary}";
}

/// <summary>Why a pair of runs could not be compared, or the comparison itself.</summary>
public sealed record SpeedCheckComparisonResult(SpeedCheckComparison? Comparison, string Refusal)
{
    public bool Compared => Comparison is not null;

    public static SpeedCheckComparisonResult Refuse(string reason) => new(null, reason);
    public static SpeedCheckComparisonResult From(SpeedCheckComparison comparison) => new(comparison, string.Empty);
}
