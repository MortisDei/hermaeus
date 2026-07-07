using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Models;

namespace Aether.Rag.Eval;

public sealed class RagEvalService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly RagQueryService _query;
    private readonly ISettingsService _settings;
    private readonly IEvalStore _evalStore;

    public RagEvalService(RagQueryService query, ISettingsService settings, IEvalStore evalStore)
    {
        _query = query;
        _settings = settings;
        _evalStore = evalStore;
    }

    public async Task<RagEvalRun> RunAsync(
        string datasetId,
        string evalPath,
        bool fullAnswer,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var evalSet = await LoadAsync(evalPath, ct);
        var run = new RagEvalRun
        {
            DatasetId = datasetId,
            EvalName = string.IsNullOrWhiteSpace(evalSet.Name)
                ? Path.GetFileNameWithoutExtension(evalPath)
                : evalSet.Name,
            FullAnswer = fullAnswer,
            StartedAt = DateTime.UtcNow
        };

        var cases = evalSet.Cases.Where(c => !string.IsNullOrWhiteSpace(c.Question)).ToList();
        for (var i = 0; i < cases.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var test = cases[i];
            progress?.Report($"{i + 1}/{cases.Count}: {test.Question}");
            run.Results.Add(fullAnswer
                ? await RunFullAnswerCaseAsync(datasetId, test, ct)
                : await RunRetrievalCaseAsync(datasetId, test, ct));
        }

        run.FinishedAt = DateTime.UtcNow;
        await ExportAsync(run, ct);
        await _evalStore.SaveRunAsync(ToEvalRun(run), ct);
        return run;
    }

    /// <summary>Projects a retrieval run onto the shared eval shape. Retrieval metrics
    /// (recall, MRR, citation hit, and so on) become score entries rather than engine
    /// features, so the shared store stays agnostic to how they were computed.</summary>
    internal static EvalRun ToEvalRun(RagEvalRun run) => new(
        Id: run.Id,
        Mode: EvalMode.Retrieval,
        Target: new EvalTarget(run.DatasetId, DatasetId: run.DatasetId, Label: run.EvalName),
        CaseResults: run.Results.Select(r => new CaseResult(
            CaseId: r.CaseId,
            Output: r.Answer,
            LatencyMs: (long)r.LatencyMs,
            Scores: ToScores(r),
            Error: r.Passed ? null : r.Notes)).ToList(),
        StartedAt: run.StartedAt,
        FinishedAt: run.FinishedAt);

    private static Dictionary<string, double> ToScores(RagEvalResult r) => new()
    {
        ["recall_at_k"] = r.RecallAtK,
        ["reciprocal_rank"] = r.ReciprocalRank,
        ["citation_hit"] = r.CitationHit ? 1d : 0d,
        ["unsupported_answer"] = r.UnsupportedAnswer ? 1d : 0d,
        ["refusal_correct"] = r.RefusalCorrect ? 1d : 0d,
        ["keyword_hit"] = r.KeywordHit ? 1d : 0d,
        ["retrieval_hit"] = r.RetrievalHit ? 1d : 0d,
        ["grounding"] = r.GroundingScore,
        ["reranker_delta"] = r.RerankerDelta
    };

    private async Task<RagEvalResult> RunRetrievalCaseAsync(string datasetId, RagEvalCase test, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var retrieval = await _query.RetrieveAsync(datasetId, test.Question, new RagQueryOptions(TopK: 5), ct);
        sw.Stop();
        var retrieved = retrieval.Selected.Select((r, i) => ToTraceChunk(r, i + 1)).ToList();
        var expectedCount = test.ExpectedSources.Count;
        var hitCount = CountExpectedHits(retrieved, test.ExpectedSources);
        var retrievalRank = FindFirstExpectedRank(retrieved, test.ExpectedSources);
        var semanticRank = FindFirstExpectedRank(retrieval.SemanticCandidates.Select((r, i) => ToTraceChunk(r, i + 1)), test.ExpectedSources);

        return new RagEvalResult
        {
            CaseId = test.Id,
            Question = test.Question,
            RetrievalHit = hitCount > 0,
            KeywordHit = !test.AnswerKeywords.Any(),
            RefusalCorrect = !test.ShouldRefuse,
            Passed = hitCount > 0 && !test.ShouldRefuse,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            Retrieved = retrieved,
            RecallAtK = expectedCount == 0 ? 1d : (double)hitCount / expectedCount,
            ReciprocalRank = retrievalRank <= 0 ? 0d : 1d / retrievalRank,
            ExpectedSourceHits = hitCount,
            ExpectedSourceCount = expectedCount,
            CitationHit = hitCount > 0,
            UnsupportedAnswer = false,
            SemanticRank = semanticRank,
            SelectedRank = retrievalRank,
            RerankerDelta = semanticRank > 0 && retrievalRank > 0 ? semanticRank - retrievalRank : 0,
            Notes = hitCount > 0 ? "Expected source found in top K." : "Expected source missing from top K."
        };
    }

    private async Task<RagEvalResult> RunFullAnswerCaseAsync(string datasetId, RagEvalCase test, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var answer = new StringBuilder();
        var retrieved = new List<RagTraceChunk>();

        await foreach (var token in _query.StreamQueryAsync(datasetId, test.Question, new RagQueryOptions(TopK: 5), ct))
        {
            if (token.StartsWith("__RAG_SOURCES__"))
            {
                retrieved = RagStreamProtocol.ParseSources(token);
                continue;
            }
            if (token.StartsWith("__RAG_TRACE__"))
                continue;
            answer.Append(token);
        }

        sw.Stop();
        var answerText = answer.ToString();
        var context = string.Join(" ", retrieved.Select(r => r.Content));
        var expectedCount = test.ExpectedSources.Count;
        var hitCount = CountExpectedHits(retrieved, test.ExpectedSources);
        var retrievalRank = FindFirstExpectedRank(retrieved, test.ExpectedSources);
        var keywordHit = !test.AnswerKeywords.Any()
                         || test.AnswerKeywords.All(k => answerText.Contains(k, StringComparison.OrdinalIgnoreCase));
        var refusalCorrect = !test.ShouldRefuse || LooksLikeRefusal(answerText);
        var citationHit = HasCitation(answerText, retrieved, test.ExpectedSources);
        var unsupportedAnswer = !test.ShouldRefuse && !refusalCorrect && hitCount == 0;

        return new RagEvalResult
        {
            CaseId = test.Id,
            Question = test.Question,
            RetrievalHit = hitCount > 0,
            KeywordHit = keywordHit,
            RefusalCorrect = refusalCorrect,
            Passed = hitCount > 0 && keywordHit && refusalCorrect,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            GroundingScore = RagQueryService.GroundingScore(answerText, context),
            RecallAtK = expectedCount == 0 ? 1d : (double)hitCount / expectedCount,
            ReciprocalRank = retrievalRank <= 0 ? 0d : 1d / retrievalRank,
            ExpectedSourceHits = hitCount,
            ExpectedSourceCount = expectedCount,
            CitationHit = citationHit,
            UnsupportedAnswer = unsupportedAnswer,
            SemanticRank = retrievalRank,
            SelectedRank = retrievalRank,
            RerankerDelta = 0,
            Answer = answerText,
            Retrieved = retrieved,
            Notes = BuildNotes(hitCount > 0, keywordHit, refusalCorrect)
        };
    }

    private static async Task<RagEvalSet> LoadAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        var direct = JsonSerializer.Deserialize<RagEvalSet>(json, JsonOpts);
        if (direct?.Cases.Count > 0) return direct;

        var cases = JsonSerializer.Deserialize<List<RagEvalCase>>(json, JsonOpts) ?? [];
        return new RagEvalSet { Name = Path.GetFileNameWithoutExtension(path), Cases = cases };
    }

    private async Task ExportAsync(RagEvalRun run, CancellationToken ct)
    {
        var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        var dir = Path.Combine(root, "eval-runs", run.Id);

        await AtomicFile.WriteAllTextAsync(
            Path.Combine(dir, "run.jsonl"),
            string.Join(Environment.NewLine, run.Results.Select(r => JsonSerializer.Serialize(r))),
            ct);

        var md = new StringBuilder();
        md.AppendLine($"# {run.EvalName}");
        md.AppendLine();
        md.AppendLine($"- Dataset: `{run.DatasetId}`");
        md.AppendLine($"- Mode: {(run.FullAnswer ? "full answer" : "retrieval only")}");
        md.AppendLine($"- Passed: {run.Passed}/{run.Total} ({run.PassRate:P0})");
        md.AppendLine($"- Recall@K: {run.AverageRecallAtK:P0}");
        md.AppendLine($"- MRR: {run.MeanReciprocalRank:F3}");
        md.AppendLine($"- Citation hit rate: {run.CitationHitRate:P0}");
        md.AppendLine($"- Unsupported answer rate: {run.UnsupportedAnswerRate:P0}");
        md.AppendLine($"- Refusal accuracy: {run.RefusalAccuracy:P0}");
        md.AppendLine($"- Avg latency: {run.AverageLatencyMs:F0} ms");
        md.AppendLine();
        foreach (var result in run.Results)
        {
            md.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} - {result.Question}");
            md.AppendLine();
            md.AppendLine($"- Retrieval hit: {result.RetrievalHit}");
            md.AppendLine($"- Keyword hit: {result.KeywordHit}");
            md.AppendLine($"- Refusal correct: {result.RefusalCorrect}");
            md.AppendLine($"- Recall@K: {result.RecallAtK:P0}");
            md.AppendLine($"- MRR: {result.ReciprocalRank:F3}");
            md.AppendLine($"- Citation hit: {result.CitationHit}");
            md.AppendLine($"- Unsupported answer: {result.UnsupportedAnswer}");
            md.AppendLine($"- Latency: {result.LatencyMs:F0} ms");
            md.AppendLine($"- Grounding: {result.GroundingScore:P0}");
            md.AppendLine($"- Reranker delta: {result.RerankerDelta}");
            md.AppendLine($"- Notes: {result.Notes}");
            md.AppendLine();
        }
        await AtomicFile.WriteAllTextAsync(Path.Combine(dir, "report.md"), md.ToString(), ct);
    }

    private static int CountExpectedHits(IEnumerable<RagTraceChunk> chunks, IEnumerable<string> expectedSources)
    {
        var expected = expectedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (expected.Count == 0) return 0;

        return chunks.Count(chunk => expected.Any(expectedSource =>
            chunk.Title.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
            || chunk.File.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
            || chunk.Path.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)));
    }

    private static int FindFirstExpectedRank(IEnumerable<RagTraceChunk> chunks, IEnumerable<string> expectedSources)
    {
        var expected = expectedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (expected.Count == 0) return 0;

        foreach (var chunk in chunks)
        {
            if (expected.Any(expectedSource =>
                chunk.Title.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
                || chunk.File.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
                || chunk.Path.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)))
            {
                return chunk.Rank;
            }
        }

        return 0;
    }

    private static bool HasCitation(string answer, IEnumerable<RagTraceChunk> retrieved, IEnumerable<string> expectedSources)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return false;

        if (retrieved.Any(chunk => answer.Contains($"[{chunk.Rank}]", StringComparison.OrdinalIgnoreCase)))
            return true;

        return expectedSources.Where(s => !string.IsNullOrWhiteSpace(s)).Any(expectedSource =>
            answer.Contains(expectedSource, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeRefusal(string answer) =>
        answer.Contains("not enough", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("does not contain", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("cannot answer", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("insufficient", StringComparison.OrdinalIgnoreCase);

    private static string BuildNotes(bool retrievalHit, bool keywordHit, bool refusalCorrect)
    {
        var notes = new List<string>();
        if (!retrievalHit) notes.Add("expected source missing");
        if (!keywordHit) notes.Add("answer keywords missing");
        if (!refusalCorrect) notes.Add("refusal expectation failed");
        return notes.Count == 0 ? "ok" : string.Join("; ", notes);
    }

    private static RagTraceChunk ToTraceChunk(Aether.Rag.Models.ScoredChunk scored, int rank) => new()
    {
        Rank = rank,
        ChunkId = scored.Chunk.Id,
        Title = scored.Chunk.SourceTitle,
        File = scored.Chunk.SourceFile,
        Path = scored.Chunk.SourcePath,
        Score = scored.Score,
        Content = scored.Chunk.Content
    };

}
