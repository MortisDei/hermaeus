using Aether.Core.Models;
using Aether.Rag.Eval;
using Aether.Rag.Models;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class EvalStoreTests
{
    private static SqliteEvalStore NewStore(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new SqliteEvalStore(settings);
    }

    private static EvalRun NewRun(string id, EvalMode mode = EvalMode.Suite, DateTime? startedAt = null) => new(
        Id: id,
        Mode: mode,
        Target: new EvalTarget("model-a", Label: "Model A"),
        CaseResults: [new CaseResult("case-1", "answer", LatencyMs: 120)],
        StartedAt: startedAt ?? DateTime.UtcNow,
        FinishedAt: DateTime.UtcNow);

    [Fact]
    public async Task Save_and_retrieve_run_roundtrips()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        await store.SaveRunAsync(NewRun("r1"));

        var fetched = await store.GetRunAsync("r1");
        Assert.NotNull(fetched);
        Assert.Equal("model-a", fetched!.Target.ModelId);
        Assert.Single(fetched.CaseResults);
        Assert.Equal(120, fetched.CaseResults[0].LatencyMs);
    }

    [Fact]
    public async Task GetRuns_filters_by_mode()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.SaveRunAsync(NewRun("suite-1", EvalMode.Suite));
        await store.SaveRunAsync(NewRun("compare-1", EvalMode.QuickCompare));

        var suiteRuns = await store.GetRunsAsync(EvalMode.Suite);

        Assert.Single(suiteRuns);
        Assert.Equal("suite-1", suiteRuns[0].Id);
    }

    [Fact]
    public async Task Retention_keeps_only_the_newest_window()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var now = DateTime.UtcNow;

        for (var i = 0; i < SqliteEvalStore.MaxSavedRuns + 5; i++)
            await store.SaveRunAsync(NewRun($"r{i}", startedAt: now.AddMinutes(i)));

        var runs = await store.GetRunsAsync();

        Assert.Equal(SqliteEvalStore.MaxSavedRuns, runs.Count);
        Assert.DoesNotContain(runs, r => r.Id == "r0");
    }

    [Fact]
    public void BenchmarkRun_projects_onto_shared_eval_shape()
    {
        var run = new BenchmarkRun
        {
            Id = "b1",
            SuiteId = "suite-x",
            ModelId = "model-a",
            ModelName = "Model A",
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Results =
            [
                new BenchmarkResult { CaseId = "case-1", Output = "hi", TotalMs = 250, FirstTokenMs = 50, QualityScore = 0.8 },
                new BenchmarkResult { CaseId = "case-2", HasError = true, Error = "timeout" }
            ]
        };

        var evalRun = BenchmarkService.ToEvalRun(run);

        Assert.Equal(EvalMode.Suite, evalRun.Mode);
        Assert.Equal("model-a", evalRun.Target.ModelId);
        Assert.Equal("suite-x", evalRun.SuiteId);
        Assert.Equal(2, evalRun.CaseResults.Count);
        Assert.Equal(250, evalRun.CaseResults[0].LatencyMs);
        Assert.Equal("timeout", evalRun.CaseResults[1].Error);
    }

    [Fact]
    public void RagEvalRun_projects_onto_shared_eval_shape()
    {
        var run = new RagEvalRun
        {
            Id = "rag-1",
            DatasetId = "dataset-a",
            EvalName = "Smoke eval",
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Results =
            [
                new RagEvalResult
                {
                    CaseId = "case-1",
                    Answer = "the answer",
                    LatencyMs = 180,
                    Passed = true,
                    RecallAtK = 1.0,
                    ReciprocalRank = 1.0,
                    CitationHit = true,
                    RefusalCorrect = true,
                    KeywordHit = true,
                    RetrievalHit = true
                },
                new RagEvalResult { CaseId = "case-2", Passed = false, Notes = "expected source missing" }
            ]
        };

        var evalRun = RagEvalService.ToEvalRun(run);

        Assert.Equal(EvalMode.Retrieval, evalRun.Mode);
        Assert.Equal("dataset-a", evalRun.Target.ModelId);
        Assert.Equal("dataset-a", evalRun.Target.DatasetId);
        Assert.Equal(2, evalRun.CaseResults.Count);
        Assert.Equal(180, evalRun.CaseResults[0].LatencyMs);
        Assert.Equal(1.0, evalRun.CaseResults[0].Scores!["recall_at_k"]);
        Assert.Null(evalRun.CaseResults[0].Error);
        Assert.Equal("expected source missing", evalRun.CaseResults[1].Error);
    }
}
