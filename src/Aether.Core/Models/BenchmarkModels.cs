namespace Aether.Core.Models;

public sealed class BenchmarkSuite
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Benchmark";
    public string Description { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxCases { get; set; } = 0;
    public bool UseJudge { get; set; }
    public string JudgeModelId { get; set; } = string.Empty;
    public List<BenchmarkCase> Cases { get; set; } = [];
}

public sealed class BenchmarkCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Case";
    public string Prompt { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> ExpectedKeywords { get; set; } = [];
    public List<string> ExpectedRegexes { get; set; } = [];
    public bool ShouldRefuse { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class BenchmarkRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SuiteId { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string RuntimeSnapshot { get; set; } = string.Empty;
    public SystemSnapshot HardwareSnapshot { get; set; } = new();
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
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
    public double AverageFirstTokenMs => Results.Count == 0 ? 0 : Results.Average(r => r.FirstTokenMs);
    public double AverageTotalMs => Results.Count == 0 ? 0 : Results.Average(r => r.TotalMs);
    public double AverageCharsPerSecond => Results.Count == 0 ? 0 : Results.Average(r => r.CharsPerSecond);
    public double AverageApproxTokensPerSecond => Results.Count == 0 ? 0 : Results.Average(r => r.ApproxTokensPerSecond);
    public double QualityScore => Results.Count == 0 ? 0 : Results.Average(r => r.QualityScore);
    public double StabilityScore => Total == 0 ? 0 : Results.Count(r => !r.HasError && !r.TimedOut && !r.Cancelled) / (double)Total;
    public double ResourceScore => Results.Count == 0 ? 0 : Results.Average(r => r.ResourceScore);
    public double RankingScore => BenchmarkScoring.RankingScore(QualityScore, AverageApproxTokensPerSecond, StabilityScore, ResourceScore);
}

public sealed class BenchmarkResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> ExpectedKeywords { get; set; } = [];
    public List<string> ExpectedRegexes { get; set; } = [];
    public bool ShouldRefuse { get; set; }
    public string Output { get; set; } = string.Empty;
    public long FirstTokenMs { get; set; }
    public long TotalMs { get; set; }
    public int OutputChars { get; set; }
    public double CharsPerSecond { get; set; }
    public double ApproxTokensPerSecond { get; set; }
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

public static class BenchmarkScoring
{
    public static double RankingScore(double quality, double tokensPerSecond, double stability, double resource)
    {
        var speed = Math.Clamp(tokensPerSecond / 30d, 0, 1);
        return Math.Round((quality * 0.4) + (speed * 0.3) + (stability * 0.2) + (resource * 0.1), 4);
    }
}
