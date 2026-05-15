using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Services;

public sealed class BenchmarkService : IBenchmarkService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly ISettingsService _settings;
    private readonly ILlmService _llm;
    private readonly ISystemInfoService _system;
    private string _initializedPath = string.Empty;

    public BenchmarkService(ISettingsService settings, ILlmService llm, ISystemInfoService system)
    {
        _settings = settings;
        _llm = llm;
        _system = system;
    }

    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "benchmarks.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public async Task InitializeAsync(CancellationToken ct = default) => await EnsureInitializedAsync(ct);

    public async Task<IReadOnlyList<BenchmarkSuite>> GetSuitesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT suite_json FROM benchmark_suites ORDER BY name";
        var suites = new List<BenchmarkSuite>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            suites.Add(JsonSerializer.Deserialize<BenchmarkSuite>(r.GetString(0), JsonOpts) ?? new BenchmarkSuite());
        return suites;
    }

    public async Task<IReadOnlyList<BenchmarkRun>> GetRunsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT run_json FROM benchmark_runs ORDER BY started_at DESC";
        var runs = new List<BenchmarkRun>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            runs.Add(JsonSerializer.Deserialize<BenchmarkRun>(r.GetString(0), JsonOpts) ?? new BenchmarkRun());
        return runs;
    }

    public async Task SaveSuiteAsync(BenchmarkSuite suite, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO benchmark_suites (id,name,suite_json,updated_at)
            VALUES ($id,$name,$json,$updated)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name, suite_json=excluded.suite_json, updated_at=excluded.updated_at";
        cmd.Parameters.AddWithValue("$id", suite.Id);
        cmd.Parameters.AddWithValue("$name", suite.Name);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(suite, JsonOpts));
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BenchmarkRun?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT run_json FROM benchmark_runs WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", runId);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<BenchmarkRun>(json, JsonOpts) : null;
    }

    public async Task DeleteRunAsync(string runId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM benchmark_runs WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", runId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BenchmarkRun> RunAsync(
        BenchmarkSuite suite,
        LlmModel model,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var cases = suite.Cases.Where(c => !string.IsNullOrWhiteSpace(c.Prompt)).ToList();
        if (suite.MaxCases > 0)
            cases = cases.Take(suite.MaxCases).ToList();

        var run = new BenchmarkRun
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            ModelId = model.Id,
            ModelName = string.IsNullOrWhiteSpace(model.ProfileDisplayName) ? model.Name : model.ProfileDisplayName,
            Provider = model.Provider,
            RuntimeSnapshot = model.Provider,
            Temperature = suite.Temperature,
            TimeoutSeconds = suite.TimeoutSeconds <= 0 ? 120 : suite.TimeoutSeconds,
            UseJudge = suite.UseJudge,
            JudgeModelId = suite.JudgeModelId,
            StartedAt = DateTime.UtcNow,
            Status = "Running",
            HardwareSnapshot = await _system.CaptureAsync(ct)
        };

        try
        {
            for (var i = 0; i < cases.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{i + 1}/{cases.Count}: {cases[i].Name}");
                run.Results.Add(await RunCaseAsync(suite, cases[i], model, run.TimeoutSeconds, ct));
                await SaveRunAsync(run, ct);
            }

            run.Status = "Completed";
        }
        catch (OperationCanceledException)
        {
            run.Status = "Cancelled";
            run.Error = "Benchmark cancelled.";
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.Error = ex.Message;
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            await SaveRunAsync(run, CancellationToken.None);
        }

        return run;
    }

    public async Task<BenchmarkRun> RerunAsync(string runId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var previous = await GetRunAsync(runId, ct) ?? throw new InvalidOperationException("Benchmark run was not found.");
        var suite = new BenchmarkSuite
        {
            Id = previous.SuiteId,
            Name = previous.SuiteName,
            Temperature = previous.Temperature,
            TimeoutSeconds = previous.TimeoutSeconds,
            UseJudge = previous.UseJudge,
            JudgeModelId = previous.JudgeModelId,
            Cases = previous.Results.Select(r => new BenchmarkCase
            {
                Id = r.CaseId,
                Name = r.CaseName,
                Prompt = r.Prompt,
                SystemPrompt = r.SystemPrompt,
                ExpectedKeywords = r.ExpectedKeywords.ToList(),
                ExpectedRegexes = r.ExpectedRegexes.ToList(),
                ShouldRefuse = r.ShouldRefuse
            }).ToList()
        };
        var model = new LlmModel { Id = previous.ModelId, Name = previous.ModelName, Provider = previous.Provider };
        return await RunAsync(suite, model, progress, ct);
    }

    public async Task<string> ExportAsync(string runId, string targetDirectory, CancellationToken ct = default)
    {
        var run = await GetRunAsync(runId, ct) ?? throw new InvalidOperationException("Benchmark run was not found.");
        Directory.CreateDirectory(targetDirectory);
        var basePath = Path.Combine(targetDirectory, $"benchmark-{Sanitize(run.SuiteName)}-{run.StartedAt:yyyyMMdd-HHmmss}");
        await File.WriteAllTextAsync($"{basePath}.json", JsonSerializer.Serialize(run, JsonOpts), ct);
        await File.WriteAllTextAsync($"{basePath}.md", ToMarkdown(run), ct);
        await File.WriteAllTextAsync($"{basePath}.csv", ToCsv(run), ct);
        return $"{basePath}.md";
    }

    public IReadOnlyList<BenchmarkRun> Rank(IEnumerable<BenchmarkRun> runs) =>
        runs.OrderByDescending(r => r.RankingScore)
            .ThenByDescending(r => r.StartedAt)
            .ToList();

    public static BenchmarkResult ScoreDeterministic(BenchmarkCase test, string output)
    {
        var keywordHit = test.ExpectedKeywords.Count == 0
            || test.ExpectedKeywords.All(k => output.Contains(k, StringComparison.OrdinalIgnoreCase));
        var regexHit = test.ExpectedRegexes.Count == 0
            || test.ExpectedRegexes.All(rx => IsRegexMatch(output, rx));
        var refusalCorrect = !test.ShouldRefuse || LooksLikeRefusal(output);
        var checks = new[] { keywordHit, regexHit, refusalCorrect };
        var quality = checks.Count(x => x) / (double)checks.Length;
        return new BenchmarkResult
        {
            CaseId = test.Id,
            CaseName = test.Name,
            Prompt = test.Prompt,
            SystemPrompt = test.SystemPrompt,
            ExpectedKeywords = test.ExpectedKeywords.ToList(),
            ExpectedRegexes = test.ExpectedRegexes.ToList(),
            ShouldRefuse = test.ShouldRefuse,
            Output = output,
            OutputChars = output.Length,
            KeywordHit = keywordHit,
            RegexHit = regexHit,
            RefusalCorrect = refusalCorrect,
            QualityScore = Math.Round(quality, 4),
            Passed = keywordHit && regexHit && refusalCorrect
        };
    }

    private async Task<BenchmarkResult> RunCaseAsync(
        BenchmarkSuite suite,
        BenchmarkCase test,
        LlmModel model,
        int timeoutSeconds,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var before = await _system.CaptureAsync(ct);
        var sw = Stopwatch.StartNew();
        long firstTokenMs = 0;
        var output = new StringBuilder();

        try
        {
            await foreach (var token in _llm.StreamChatAsync(
                               model.Id,
                               [new ChatMessage("user", test.Prompt)],
                               string.IsNullOrWhiteSpace(test.SystemPrompt) ? null : test.SystemPrompt,
                               suite.Temperature,
                               timeout.Token))
            {
                if (firstTokenMs == 0 && !string.IsNullOrEmpty(token))
                    firstTokenMs = sw.ElapsedMilliseconds;
                output.Append(token);
            }

            sw.Stop();
            var result = ScoreDeterministic(test, output.ToString());
            FillTiming(result, firstTokenMs, sw.ElapsedMilliseconds);
            FillResources(result, before, await _system.CaptureAsync(ct));
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            var result = ScoreDeterministic(test, output.ToString());
            FillTiming(result, firstTokenMs, sw.ElapsedMilliseconds);
            FillResources(result, before, await _system.CaptureAsync(CancellationToken.None));
            result.HasError = true;
            result.TimedOut = true;
            result.Passed = false;
            result.QualityScore = 0;
            result.Error = $"Timed out after {timeoutSeconds} seconds.";
            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var result = ScoreDeterministic(test, output.ToString());
            FillTiming(result, firstTokenMs, sw.ElapsedMilliseconds);
            result.Cancelled = true;
            result.Passed = false;
            result.QualityScore = 0;
            result.Error = "Cancelled.";
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var result = ScoreDeterministic(test, output.ToString());
            FillTiming(result, firstTokenMs, sw.ElapsedMilliseconds);
            FillResources(result, before, await _system.CaptureAsync(CancellationToken.None));
            result.HasError = true;
            result.Passed = false;
            result.QualityScore = 0;
            result.Error = ex.Message;
            return result;
        }
    }

    private async Task SaveRunAsync(BenchmarkRun run, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO benchmark_runs (id,suite_id,suite_name,model_id,model_name,provider,started_at,finished_at,status,ranking_score,run_json)
            VALUES ($id,$sid,$suite,$mid,$model,$provider,$started,$finished,$status,$score,$json)
            ON CONFLICT(id) DO UPDATE SET finished_at=excluded.finished_at,status=excluded.status,ranking_score=excluded.ranking_score,run_json=excluded.run_json";
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$sid", run.SuiteId);
        cmd.Parameters.AddWithValue("$suite", run.SuiteName);
        cmd.Parameters.AddWithValue("$mid", run.ModelId);
        cmd.Parameters.AddWithValue("$model", run.ModelName);
        cmd.Parameters.AddWithValue("$provider", run.Provider);
        cmd.Parameters.AddWithValue("$started", run.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$finished", run.FinishedAt?.ToString("O") ?? string.Empty);
        cmd.Parameters.AddWithValue("$status", run.Status);
        cmd.Parameters.AddWithValue("$score", run.RankingScore);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(run, JsonOpts));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        var dbPath = DbPath;
        if (_initializedPath == dbPath && File.Exists(dbPath)) return;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS benchmark_suites (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                suite_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS benchmark_runs (
                id TEXT PRIMARY KEY,
                suite_id TEXT NOT NULL,
                suite_name TEXT NOT NULL,
                model_id TEXT NOT NULL,
                model_name TEXT NOT NULL,
                provider TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL,
                ranking_score REAL NOT NULL DEFAULT 0,
                run_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_benchmark_runs_started ON benchmark_runs(started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_benchmark_runs_model ON benchmark_runs(model_id);";
        await cmd.ExecuteNonQueryAsync(ct);
        _initializedPath = dbPath;

        await EnsureStarterSuitesAsync(ct);
    }

    private async Task EnsureStarterSuitesAsync(CancellationToken ct)
    {
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM benchmark_suites";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    existing.Add(reader.GetString(0));
            }
        }

        foreach (var suite in StarterSuites())
        {
            if (!existing.Contains(suite.Id))
                await SaveSuiteAsync(suite, ct);
        }
    }

    public static IReadOnlyList<BenchmarkSuite> StarterSuites() =>
    [
        new()
        {
            Id = "speed-smoke",
            Name = "Speed Smoke",
            Description = "Short prompts for latency and throughput sanity checks.",
            Cases =
            [
                new BenchmarkCase { Name = "Tiny greeting", Prompt = "Reply with one friendly sentence.", ExpectedKeywords = ["friendly"] },
                new BenchmarkCase { Name = "Three bullets", Prompt = "List three benefits of local-first AI in bullet points.", ExpectedKeywords = ["local"] }
            ]
        },
        new()
        {
            Id = "instruction-following",
            Name = "Instruction Following",
            Description = "Checks formatting and direct instruction adherence.",
            Cases =
            [
                new BenchmarkCase { Name = "Exact JSON", Prompt = "Return JSON only with keys name and status. Use name Aether and status ready.", ExpectedKeywords = ["Aether", "ready"], ExpectedRegexes = ["\\{.*name.*status.*\\}"] },
                new BenchmarkCase { Name = "Limit words", Prompt = "Explain model quantization in exactly two short sentences.", ExpectedKeywords = ["quant"] }
            ]
        },
        new()
        {
            Id = "reasoning-light",
            Name = "Reasoning Light",
            Description = "Small reasoning prompts that should not require long context.",
            Cases =
            [
                new BenchmarkCase { Name = "Arithmetic", Prompt = "If a benchmark has 5 cases and 2 fail, what is the pass rate?", ExpectedKeywords = ["60"] },
                new BenchmarkCase { Name = "Ordering", Prompt = "Rank these from smallest to largest: 2.7, 2.07, 2.17.", ExpectedKeywords = ["2.07", "2.17", "2.7"] }
            ]
        },
        new()
        {
            Id = "rag-answer-style",
            Name = "RAG Answer Style",
            Description = "Prompts for concise grounded-answer style.",
            Cases =
            [
                new BenchmarkCase { Name = "Cite uncertainty", Prompt = "Answer as if using retrieved documents: explain what you know and say when evidence is missing.", ExpectedKeywords = ["evidence"] }
            ]
        },
        new()
        {
            Id = "refusal-safety",
            Name = "Refusal Safety",
            Description = "Simple insufficient-context refusal behavior.",
            Cases =
            [
                new BenchmarkCase { Name = "Insufficient context", Prompt = "Using only the provided context, tell me the serial number of the hidden device. Context: none.", ShouldRefuse = true }
            ]
        }
    ];

    private static void FillTiming(BenchmarkResult result, long firstTokenMs, long totalMs)
    {
        result.FirstTokenMs = firstTokenMs;
        result.TotalMs = totalMs;
        result.OutputChars = result.Output.Length;
        var seconds = Math.Max(totalMs / 1000d, 0.001d);
        result.CharsPerSecond = Math.Round(result.OutputChars / seconds, 2);
        result.ApproxTokensPerSecond = Math.Round((result.OutputChars / 4d) / seconds, 2);
    }

    private static void FillResources(BenchmarkResult result, SystemSnapshot before, SystemSnapshot after)
    {
        result.ProcessMemoryBeforeBytes = before.ProcessMemoryBytes;
        result.ProcessMemoryAfterBytes = after.ProcessMemoryBytes;
        result.ManagedMemoryBeforeBytes = before.ManagedMemoryBytes;
        result.ManagedMemoryAfterBytes = after.ManagedMemoryBytes;
        result.VramUsedBeforeBytes = before.Gpus.FirstOrDefault(g => g.MemoryUsedBytes.HasValue)?.MemoryUsedBytes;
        result.VramUsedAfterBytes = after.Gpus.FirstOrDefault(g => g.MemoryUsedBytes.HasValue)?.MemoryUsedBytes;
        var processDelta = Math.Max(0, result.ProcessMemoryAfterBytes - result.ProcessMemoryBeforeBytes);
        result.ResourceScore = processDelta <= 0 ? 1 : Math.Clamp(1d - (processDelta / (512d * 1024 * 1024)), 0, 1);
    }

    private static bool LooksLikeRefusal(string answer) =>
        answer.Contains("not enough", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("cannot determine", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("cannot answer", StringComparison.OrdinalIgnoreCase)
        || answer.Contains("no context", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegexMatch(string output, string pattern)
    {
        try
        {
            return Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ToMarkdown(BenchmarkRun run)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {run.SuiteName}");
        md.AppendLine();
        md.AppendLine($"- Model: `{run.ModelName}`");
        md.AppendLine($"- Provider: `{run.Provider}`");
        md.AppendLine($"- Status: {run.Status}");
        md.AppendLine($"- Ranking: {run.RankingScore:P0}");
        md.AppendLine($"- Pass rate: {run.PassRate:P0}");
        md.AppendLine($"- Avg first token: {run.AverageFirstTokenMs:F0} ms");
        md.AppendLine($"- Avg tokens/sec: {run.AverageApproxTokensPerSecond:F2}");
        md.AppendLine();
        foreach (var result in run.Results)
        {
            md.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} - {result.CaseName}");
            md.AppendLine();
            md.AppendLine($"- First token: {result.FirstTokenMs} ms");
            md.AppendLine($"- Total: {result.TotalMs} ms");
            md.AppendLine($"- Approx tokens/sec: {result.ApproxTokensPerSecond:F2}");
            md.AppendLine($"- Quality: {result.QualityScore:P0}");
            if (!string.IsNullOrWhiteSpace(result.Error))
                md.AppendLine($"- Error: {result.Error}");
            md.AppendLine();
            md.AppendLine("```text");
            md.AppendLine(result.Output);
            md.AppendLine("```");
            md.AppendLine();
        }
        return md.ToString();
    }

    private static string ToCsv(BenchmarkRun run)
    {
        var csv = new StringBuilder();
        csv.AppendLine("case,passed,first_token_ms,total_ms,approx_tokens_per_second,quality,error");
        foreach (var result in run.Results)
            csv.AppendLine($"{Csv(result.CaseName)},{result.Passed},{result.FirstTokenMs},{result.TotalMs},{result.ApproxTokensPerSecond:F2},{result.QualityScore:F4},{Csv(result.Error)}");
        return csv.ToString();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(clean) ? "benchmark" : clean;
    }
}
