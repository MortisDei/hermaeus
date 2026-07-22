using System.Text.Json.Serialization;

namespace Hermaeus.Rag.Models;

public sealed class RagEvalSet
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Eval";
    [JsonPropertyName("cases")] public List<RagEvalCase> Cases { get; set; } = [];
    [JsonPropertyName("questions")] public List<RagEvalCase> Questions
    {
        get => Cases;
        set => Cases = value ?? [];
    }
}

public sealed class RagEvalCase
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("question")] public string Question { get; set; } = string.Empty;
    [JsonPropertyName("expected_sources")] public List<string> ExpectedSources { get; set; } = [];
    [JsonPropertyName("expected_source_titles")] public List<string> ExpectedSourceTitles
    {
        get => ExpectedSources;
        set => ExpectedSources = value ?? [];
    }
    [JsonPropertyName("answer_keywords")] public List<string> AnswerKeywords { get; set; } = [];
    [JsonPropertyName("should_refuse")] public bool ShouldRefuse { get; set; }
}

public sealed class RagEvalRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DatasetId { get; set; } = string.Empty;
    public string EvalName { get; set; } = string.Empty;
    public bool FullAnswer { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;
    public List<RagEvalResult> Results { get; set; } = [];
    public int Total => Results.Count;
    public int Passed => Results.Count(r => r.Passed);
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
    public double AverageRecallAtK => Total == 0 ? 0 : Results.Average(r => r.RecallAtK);
    public double MeanReciprocalRank => Total == 0 ? 0 : Results.Average(r => r.ReciprocalRank);
    public double CitationHitRate => Total == 0 ? 0 : Results.Count(r => r.CitationHit) / (double)Total;
    public double UnsupportedAnswerRate => Total == 0 ? 0 : Results.Count(r => r.UnsupportedAnswer) / (double)Total;
    public double RefusalAccuracy => Total == 0 ? 0 : Results.Count(r => r.RefusalCorrect) / (double)Total;
    public double AverageLatencyMs => Total == 0 ? 0 : Results.Average(r => r.LatencyMs);
}

public sealed class RagEvalResult
{
    public string CaseId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public bool RetrievalHit { get; set; }
    public bool KeywordHit { get; set; }
    public bool RefusalCorrect { get; set; } = true;
    public bool Passed { get; set; }
    public double LatencyMs { get; set; }
    public float GroundingScore { get; set; }
    public double RecallAtK { get; set; }
    public double ReciprocalRank { get; set; }
    public int ExpectedSourceHits { get; set; }
    public int ExpectedSourceCount { get; set; }
    public bool CitationHit { get; set; }
    public bool UnsupportedAnswer { get; set; }
    public int SemanticRank { get; set; }
    public int SelectedRank { get; set; }
    public int RerankerDelta { get; set; }
    public string Answer { get; set; } = string.Empty;
    public List<RagTraceChunk> Retrieved { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
}
