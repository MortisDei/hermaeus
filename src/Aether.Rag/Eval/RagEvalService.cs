using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aether.Core.Services;
using Aether.Rag.Models;

namespace Aether.Rag.Eval;

public sealed class RagEvalService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly RagQueryService _query;
    private readonly ISettingsService _settings;

    public RagEvalService(RagQueryService query, ISettingsService settings)
    {
        _query = query;
        _settings = settings;
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
        return run;
    }

    private async Task<RagEvalResult> RunRetrievalCaseAsync(string datasetId, RagEvalCase test, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var retrieval = await _query.RetrieveAsync(datasetId, test.Question, new RagQueryOptions(TopK: 5), ct);
        sw.Stop();
        var retrieved = retrieval.Selected.Select((r, i) => ToTraceChunk(r, i + 1)).ToList();
        var hit = MatchesExpectedSource(retrieved, test.ExpectedSources);

        return new RagEvalResult
        {
            CaseId = test.Id,
            Question = test.Question,
            RetrievalHit = hit,
            KeywordHit = !test.AnswerKeywords.Any(),
            RefusalCorrect = !test.ShouldRefuse,
            Passed = hit && !test.ShouldRefuse,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            Retrieved = retrieved,
            Notes = hit ? "Expected source found in top K." : "Expected source missing from top K."
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
                retrieved = ParseSources(token);
                continue;
            }
            if (token.StartsWith("__RAG_TRACE__"))
                continue;
            answer.Append(token);
        }

        sw.Stop();
        var answerText = answer.ToString();
        var context = string.Join(" ", retrieved.Select(r => r.Content));
        var retrievalHit = MatchesExpectedSource(retrieved, test.ExpectedSources);
        var keywordHit = !test.AnswerKeywords.Any()
                         || test.AnswerKeywords.All(k => answerText.Contains(k, StringComparison.OrdinalIgnoreCase));
        var refusalCorrect = !test.ShouldRefuse || LooksLikeRefusal(answerText);

        return new RagEvalResult
        {
            CaseId = test.Id,
            Question = test.Question,
            RetrievalHit = retrievalHit,
            KeywordHit = keywordHit,
            RefusalCorrect = refusalCorrect,
            Passed = retrievalHit && keywordHit && refusalCorrect,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            GroundingScore = RagQueryService.GroundingScore(answerText, context),
            Answer = answerText,
            Retrieved = retrieved,
            Notes = BuildNotes(retrievalHit, keywordHit, refusalCorrect)
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
        var configured = _settings.Settings.DataRootDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        var dir = Path.Combine(root, "eval-runs", run.Id);
        Directory.CreateDirectory(dir);

        await File.WriteAllLinesAsync(
            Path.Combine(dir, "run.jsonl"),
            run.Results.Select(r => JsonSerializer.Serialize(r)),
            ct);

        var md = new StringBuilder();
        md.AppendLine($"# {run.EvalName}");
        md.AppendLine();
        md.AppendLine($"- Dataset: `{run.DatasetId}`");
        md.AppendLine($"- Mode: {(run.FullAnswer ? "full answer" : "retrieval only")}");
        md.AppendLine($"- Passed: {run.Passed}/{run.Total} ({run.PassRate:P0})");
        md.AppendLine();
        foreach (var result in run.Results)
        {
            md.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} - {result.Question}");
            md.AppendLine();
            md.AppendLine($"- Retrieval hit: {result.RetrievalHit}");
            md.AppendLine($"- Keyword hit: {result.KeywordHit}");
            md.AppendLine($"- Refusal correct: {result.RefusalCorrect}");
            md.AppendLine($"- Latency: {result.LatencyMs:F0} ms");
            md.AppendLine($"- Grounding: {result.GroundingScore:P0}");
            md.AppendLine($"- Notes: {result.Notes}");
            md.AppendLine();
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "report.md"), md.ToString(), ct);
    }

    private static bool MatchesExpectedSource(IEnumerable<RagTraceChunk> chunks, IEnumerable<string> expectedSources)
    {
        var expected = expectedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (expected.Count == 0) return true;

        return chunks.Any(chunk => expected.Any(expectedSource =>
            chunk.Title.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
            || chunk.File.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)
            || chunk.Path.Contains(expectedSource, StringComparison.OrdinalIgnoreCase)));
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

    private static List<RagTraceChunk> ParseSources(string token)
    {
        var start = "__RAG_SOURCES__";
        var end = "__END_SOURCES__";
        var json = token[start.Length..token.IndexOf(end, StringComparison.Ordinal)];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(el => new RagTraceChunk
        {
            Rank = el.GetProperty("rank").GetInt32(),
            Title = el.GetProperty("title").GetString() ?? string.Empty,
            File = el.GetProperty("file").GetString() ?? string.Empty,
            Path = el.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty : string.Empty,
            Score = el.GetProperty("score").GetSingle(),
            Content = el.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty
        }).ToList();
    }
}
