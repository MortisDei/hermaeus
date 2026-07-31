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
    public static BenchmarkSuite Suite() => new()
    {
        Id = SuiteId,
        Name = SuiteName,
        Description = "Fixed prompts for measuring tokens per second. Reports speed only; it does not judge the answers.",
        ScoringProfile = "fast-chat-v1",
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
public sealed record SpeedCheckSide(
    string RunId,
    DateTime StartedAt,
    string ConfigurationSummary,
    double TokensPerSecond,
    double PromptTokensPerSecond,
    double FirstTokenMs);

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
