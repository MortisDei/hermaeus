namespace Hermaeus.Core.Models;

public sealed class BenchmarkSuite
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Benchmark";
    public string Description { get; set; } = string.Empty;
    public string SuiteVersion { get; set; } = "1.1.0";
    public string ScoringProfile { get; set; } = "balanced-v1";
    public string BaselineModelId { get; set; } = string.Empty;
    public string BaselineModelName { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxCases { get; set; } = 0;
    public int IterationsPerCase { get; set; } = 1;
    public bool UseJudge { get; set; }
    public string JudgeModelId { get; set; } = string.Empty;
    public List<BenchmarkCase> Cases { get; set; } = [];
}

public sealed class BenchmarkCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Case";
    public string CaseVersion { get; set; } = "1.0.0";
    public string ExpectedBehaviourVersion { get; set; } = "1.1.0";
    public string Prompt { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> ExpectedKeywords { get; set; } = [];
    public List<List<string>> ExpectedKeywordAlternatives { get; set; } = [];
    public List<string> ExpectedRegexes { get; set; } = [];
    public bool ShouldRefuse { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class BenchmarkRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SuiteId { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public string SuiteVersion { get; set; } = string.Empty;
    public string ScoringProfile { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string RuntimeSnapshot { get; set; } = string.Empty;
    public SystemSnapshot HardwareSnapshot { get; set; } = new();
    public BenchmarkRunMetadata Metadata { get; set; } = new();
    public string RunMode { get; set; } = BenchmarkRunMode.ColdWarm.ToString();
    public int IterationsPerCase { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int TimeoutSeconds { get; set; } = 120;
    public bool UseJudge { get; set; }
    public string JudgeModelId { get; set; } = string.Empty;
    public List<BenchmarkResult> Results { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public string Error { get; set; } = string.Empty;

    public int Total => Results.Count;
    public int Passed => Results.Count(r => r.Passed);
    public int FailureCount => Results.Count(r => !string.IsNullOrWhiteSpace(r.FailureCategory) || r.HasError || r.TimedOut || r.Cancelled);
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
    public double AverageFirstTokenMs => Results.Count == 0 ? 0 : Results.Average(r => r.FirstTokenMs);
    public double AverageTotalMs => Results.Count == 0 ? 0 : Results.Average(r => r.TotalMs);
    public double AverageCharsPerSecond => Results.Count == 0 ? 0 : Results.Average(r => r.CharsPerSecond);
    public double AverageApproxTokensPerSecond => Results.Count == 0 ? 0 : Results.Average(r => r.ApproxTokensPerSecond);
    public double MedianFirstTokenMs => Median(Results.Select(r => (double)r.FirstTokenMs));
    public double MinFirstTokenMs => Results.Count == 0 ? 0 : Results.Min(r => r.FirstTokenMs);
    public double MaxFirstTokenMs => Results.Count == 0 ? 0 : Results.Max(r => r.FirstTokenMs);
    public double P95FirstTokenMs => Percentile(Results.Select(r => (double)r.FirstTokenMs), 0.95);
    public double StdDevFirstTokenMs => StdDev(Results.Select(r => (double)r.FirstTokenMs));
    public double MedianTotalMs => Median(Results.Select(r => (double)r.TotalMs));
    public double MinTotalMs => Results.Count == 0 ? 0 : Results.Min(r => r.TotalMs);
    public double MaxTotalMs => Results.Count == 0 ? 0 : Results.Max(r => r.TotalMs);
    public double P95TotalMs => Percentile(Results.Select(r => (double)r.TotalMs), 0.95);
    public double StdDevTotalMs => StdDev(Results.Select(r => (double)r.TotalMs));
    public double MedianApproxTokensPerSecond => Median(Results.Select(r => r.ApproxTokensPerSecond));
    public double QualityScore => Results.Count == 0 ? 0 : Results.Average(r => r.QualityScore);
    public double StabilityScore => Total == 0 ? 0 : Results.Count(r => !r.HasError && !r.TimedOut && !r.Cancelled) / (double)Total;
    public double ResourceScore => Results.Count == 0 ? 0 : Results.Average(r => r.ResourceScore);
    public double RankingScore => BenchmarkScoring.RankingScore(QualityScore, AverageApproxTokensPerSecond, StabilityScore, ResourceScore);

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 0) return 0;
        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2d : ordered[mid];
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 0) return 0;
        var position = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return ordered[lower];
        var fraction = position - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * fraction);
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var array = values.ToArray();
        if (array.Length == 0) return 0;
        var mean = array.Average();
        var variance = array.Sum(v => Math.Pow(v - mean, 2)) / array.Length;
        return Math.Sqrt(variance);
    }
}

public sealed class BenchmarkResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public string CaseVersion { get; set; } = string.Empty;
    public string ExpectedBehaviourVersion { get; set; } = string.Empty;
    public int IterationIndex { get; set; }
    public string Phase { get; set; } = BenchmarkPhase.Cold.ToString();
    public string Prompt { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> ExpectedKeywords { get; set; } = [];
    public List<List<string>> ExpectedKeywordAlternatives { get; set; } = [];
    public List<string> ExpectedRegexes { get; set; } = [];
    public bool ShouldRefuse { get; set; }
    public List<string> Tags { get; set; } = [];
    public string Output { get; set; } = string.Empty;
    public long FirstTokenMs { get; set; }
    public long TotalMs { get; set; }
    public int OutputChars { get; set; }
    public double CharsPerSecond { get; set; }
    public double ApproxTokensPerSecond { get; set; }
    /// <summary>llama-server's own prompt-processing token count/duration, when the provider
    /// reported timings (r17 02-benchmark-truth.md 2.1). Null for providers that don't.</summary>
    public int? PromptTokens { get; set; }
    public double? PromptMs { get; set; }
    /// <summary>prompt_n / prompt_ms * 1000, for display alongside <see cref="ApproxTokensPerSecond"/>.</summary>
    public double? PromptTokensPerSecond { get; set; }
    /// <summary>llama-server's own decode token count/duration; <see cref="ApproxTokensPerSecond"/>
    /// is computed from these when present instead of the chars/4 fallback.</summary>
    public int? GeneratedTokens { get; set; }
    public double? DecodeMs { get; set; }
    /// <summary>"server-timings" when <see cref="ApproxTokensPerSecond"/> came from the
    /// provider's own timings, "chars-approx" when it is the chars/4 estimate. Empty for runs
    /// recorded before this field existed, which keeps old stored JSON loading cleanly.</summary>
    public string MeasurementSource { get; set; } = string.Empty;
    /// <summary>
    /// Tokens the speculative decoder drafted, and how many the target model
    /// accepted, straight from llama-server's timings (r28 doc 02 2.1/2.4).
    /// Null when the provider reported no draft counters at all, which is a
    /// different fact from a measured zero and is displayed differently:
    /// <c>0 drafted</c> means drafting never engaged and a comparison was run
    /// between two identical configurations.
    /// </summary>
    public int? DraftTokens { get; set; }
    public int? DraftTokensAccepted { get; set; }
    public bool KeywordHit { get; set; }
    public bool RegexHit { get; set; }
    public bool RefusalCorrect { get; set; }
    public bool Passed { get; set; }
    public double QualityScore { get; set; }
    public double ResourceScore { get; set; } = 1;
    public double? JudgeScore { get; set; }
    public string JudgeReason { get; set; } = string.Empty;
    public bool HasError { get; set; }
    public bool TimedOut { get; set; }
    public bool Cancelled { get; set; }
    public string FailureCategory { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public long ProcessMemoryBeforeBytes { get; set; }
    public long ProcessMemoryAfterBytes { get; set; }
    public long ManagedMemoryBeforeBytes { get; set; }
    public long ManagedMemoryAfterBytes { get; set; }
    public long? VramUsedBeforeBytes { get; set; }
    public long? VramUsedAfterBytes { get; set; }
}

public sealed class SystemSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = string.Empty;
    public string OSDescription { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string CpuName { get; set; } = string.Empty;
    public string GpuProbeMethod { get; set; } = string.Empty;
    public string GpuProbeError { get; set; } = string.Empty;
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public long ProcessMemoryBytes { get; set; }
    public long ManagedMemoryBytes { get; set; }
    public string DataRoot { get; set; } = string.Empty;
    public long DataRootTotalBytes { get; set; }
    public long DataRootFreeBytes { get; set; }
    public long DatabaseBytes { get; set; }
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<ComponentStatus> Components { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
}

public sealed class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public long? MemoryTotalBytes { get; set; }
    public long? MemoryUsedBytes { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ComponentStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// Cheap, process-lifetime-cached hardware facts for repeated per-model checks
/// (fits-on-your-hardware, HF browser). Hardware does not hot-change, so
/// this is captured once and reused instead of re-running CaptureAsync's
/// process spawns per row (r13 01-system-truth.md 1.5).
/// </summary>
public sealed record HardwareProfile(long TotalRamBytes, long MaxGpuVramBytes, string? GpuName);

public static class BenchmarkScoring
{
    /// <summary>
    /// The <paramref name="resource"/> slot is reserved but currently always neutral (1.0):
    /// it used to be the Hermaeus process's own RSS delta, which is noise for a model running in
    /// llama-server (a different process) or on a remote endpoint (r17 02-benchmark-truth.md
    /// 2.3). Kept in the weighted sum rather than dropped so historical <c>RankingScore</c>
    /// values (recomputed from stored results at read time) shift minimally; a future round can
    /// give it a real, honest signal.
    /// </summary>
    public static double RankingScore(double quality, double tokensPerSecond, double stability, double resource)
    {
        var speed = Math.Clamp(tokensPerSecond / 30d, 0, 1);
        return Math.Round((quality * 0.4) + (speed * 0.3) + (stability * 0.2) + (resource * 0.1), 4);
    }
}
