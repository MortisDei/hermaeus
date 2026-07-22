using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class BenchmarkInsightsServiceTests
{
    [Fact]
    public async Task LoadReportAsync_joins_case_tags_onto_older_untagged_runs_via_the_surviving_suite()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        await benchmarks.InitializeAsync();

        var testCase = new BenchmarkCase { Id = "case-1", Name = "case-1", Prompt = "hello", Tags = ["coding"] };
        var suite = new BenchmarkSuite { Id = "suite-1", Name = "Coding suite", Cases = [testCase] };
        await benchmarks.SaveSuiteAsync(suite);

        // Two runs predating tag propagation (results have no Tags of their own),
        // inserted directly so BenchmarkService.RunAsync's own tag-stamping never runs.
        for (var i = 0; i < 2; i++)
            await InsertLegacyRunAsync(settings, suite, testCase, runIndex: i);

        var insights = new BenchmarkInsightsService(benchmarks, new FakeSystemInfo());
        var report = await insights.LoadReportAsync();

        var board = Assert.Single(report.TagLeaderboards, b => b.Tag == "coding");
        Assert.Contains(board.Ranked, m => m.ModelId == "model-a");
    }

    [Fact]
    public async Task LoadReportAsync_counts_a_run_toward_totals_even_when_its_suite_was_deleted()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        await benchmarks.InitializeAsync();

        var testCase = new BenchmarkCase { Id = "case-1", Name = "case-1", Prompt = "hello", Tags = ["coding"] };
        var suite = new BenchmarkSuite { Id = "orphan-suite", Name = "Deleted suite", Cases = [testCase] };
        // Intentionally never saved: the run below references a suite id that does not exist.
        await InsertLegacyRunAsync(settings, suite, testCase, runIndex: 0);

        var insights = new BenchmarkInsightsService(benchmarks, new FakeSystemInfo());
        var report = await insights.LoadReportAsync();

        Assert.Equal(1, report.TotalRuns);
        Assert.DoesNotContain(report.TagLeaderboards, b => b.Tag == "coding");
    }

    private static async Task InsertLegacyRunAsync(ISettingsService settings, BenchmarkSuite suite, BenchmarkCase testCase, int runIndex)
    {
        var run = new BenchmarkRun
        {
            Id = $"legacy-run-{runIndex}",
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            ModelId = "model-a",
            ModelName = "Model A",
            Provider = "Test",
            Status = "Completed",
            StartedAt = DateTime.UtcNow.AddDays(-runIndex),
            Metadata = new BenchmarkRunMetadata { AppVersion = "0.10.0.0" },
            Results = Enumerable.Range(0, 10).Select(_ => new BenchmarkResult
            {
                CaseId = testCase.Id,
                QualityScore = 0.8,
                ApproxTokensPerSecond = 20,
                ResourceScore = 1
                // Tags intentionally left empty to simulate a pre-r5 stored run.
            }).ToList()
        };

        var dir = SettingsService.ResolveDataRoot(settings.Settings);
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "benchmarks.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO benchmark_runs (id,suite_id,suite_name,model_id,model_name,provider,started_at,finished_at,status,ranking_score,run_json)
            VALUES ($id,$sid,$suite,$mid,$model,$provider,$started,$finished,$status,$score,$json)";
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$sid", run.SuiteId);
        cmd.Parameters.AddWithValue("$suite", run.SuiteName);
        cmd.Parameters.AddWithValue("$mid", run.ModelId);
        cmd.Parameters.AddWithValue("$model", run.ModelName);
        cmd.Parameters.AddWithValue("$provider", run.Provider);
        cmd.Parameters.AddWithValue("$started", run.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$finished", string.Empty);
        cmd.Parameters.AddWithValue("$status", run.Status);
        cmd.Parameters.AddWithValue("$score", run.RankingScore);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(run));
        await cmd.ExecuteNonQueryAsync();
    }
}
