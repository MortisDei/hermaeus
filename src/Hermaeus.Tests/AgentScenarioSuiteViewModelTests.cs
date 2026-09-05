using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentScenarioSuiteViewModelTests
{
    private static AgentScenario BuildScenario(string id, string title, List<string> tags, bool isBuiltIn) => new(
        new AgentScenarioManifest { Id = id, Title = title, Goal = "goal", Tags = tags },
        SourceDirectory: "unused",
        WorkspaceDirectory: "unused",
        IsBuiltIn: isBuiltIn);

    private sealed class FakeAgentScenarioStore : IAgentScenarioStore
    {
        private readonly IReadOnlyList<AgentScenario> _scenarios;
        private readonly List<string> _warnings;

        public FakeAgentScenarioStore(IReadOnlyList<AgentScenario> scenarios, List<string>? warnings = null)
        {
            _scenarios = scenarios;
            _warnings = warnings ?? [];
        }

        public Task<IReadOnlyList<AgentScenario>> LoadAllAsync(ICollection<string>? warnings = null, CancellationToken ct = default)
        {
            if (warnings is not null)
                foreach (var w in _warnings) warnings.Add(w);
            return Task.FromResult(_scenarios);
        }
    }

    private sealed class FakeAgentScenarioRunner : IAgentScenarioRunner
    {
        public Func<AgentScenario, string, Task<AgentScenarioRunResult>>? OnRunScenario { get; set; }
        public Func<IReadOnlyList<AgentScenario>, string, Task<AgentScenarioSuiteResult>>? OnRunSuite { get; set; }
        public Func<IReadOnlyList<AgentScenario>, string, IProgress<string>?, CancellationToken, Task<AgentScenarioSuiteResult>>? OnRunSuiteWithProgress { get; set; }

        public Task<AgentScenarioRunResult> RunScenarioAsync(AgentScenario scenario, string modelId, IProgress<string>? progress = null, CancellationToken ct = default) =>
            OnRunScenario?.Invoke(scenario, modelId)
            ?? Task.FromResult(new AgentScenarioRunResult(scenario.Manifest.Id, scenario.Manifest.Title, true, [], 1, 10, "Complete", null));

        public Task<AgentScenarioSuiteResult> RunSuiteAsync(IReadOnlyList<AgentScenario> scenarios, string modelId, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (OnRunSuiteWithProgress is not null)
                return OnRunSuiteWithProgress(scenarios, modelId, progress, ct);
            return OnRunSuite?.Invoke(scenarios, modelId) ?? Task.FromResult(DefaultSuite(scenarios, modelId));
        }

        private static AgentScenarioSuiteResult DefaultSuite(IReadOnlyList<AgentScenario> scenarios, string modelId) => new()
        {
            ModelId = modelId,
            Results = scenarios.Select(s => new AgentScenarioRunResult(s.Manifest.Id, s.Manifest.Title, true, [], 1, 5, "Complete", null)).ToList(),
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task LoadScenariosAsync_populates_rows_from_the_store()
    {
        var scenarios = new List<AgentScenario> { BuildScenario("s1", "Scenario One", ["tag-a", "tag-b"], isBuiltIn: true) };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), new FakeAgentScenarioRunner(), new FakeToasts());

        await vm.LoadScenariosAsync();

        var row = Assert.Single(vm.Scenarios);
        Assert.Equal("s1", row.Id);
        Assert.Equal("Scenario One", row.Title);
        Assert.Equal("tag-a, tag-b", row.Tags);
        Assert.Equal("built-in", row.SourceLabel);
        Assert.Null(row.Passed);
        Assert.Equal("Unknown", row.StatusLabel);
        Assert.Equal(string.Empty, vm.RunningHeadline);
    }

    [Fact]
    public async Task LoadScenariosAsync_surfaces_loader_warnings_as_a_toast()
    {
        var store = new FakeAgentScenarioStore([], warnings: ["bad scenario"]);
        var toasts = new FakeToasts();
        ToastMessage? captured = null;
        toasts.ToastRaised += t => captured = t;
        var vm = new AgentScenarioSuiteViewModel(store, new FakeAgentScenarioRunner(), toasts);

        await vm.LoadScenariosAsync();

        Assert.NotNull(captured);
        Assert.Contains("bad scenario", captured!.Message);
    }

    [Fact]
    public async Task RunSuiteCommand_cannot_execute_without_a_selected_model()
    {
        var scenarios = new List<AgentScenario> { BuildScenario("s1", "One", [], isBuiltIn: true) };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), new FakeAgentScenarioRunner(), new FakeToasts());
        await vm.LoadScenariosAsync();

        Assert.False(vm.RunSuiteCommand.CanExecute(null));

        vm.ModelId = "some-model";
        Assert.True(vm.RunSuiteCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunScenarioCommand_tracks_the_same_execution_gate_as_the_suite()
    {
        var scenario = BuildScenario("s1", "One", [], isBuiltIn: true);
        var vm = new AgentScenarioSuiteViewModel(
            new FakeAgentScenarioStore([scenario]), new FakeAgentScenarioRunner(), new FakeToasts());

        await vm.LoadScenariosAsync();
        var row = Assert.Single(vm.Scenarios);
        Assert.False(vm.RunScenarioCommand.CanExecute(row));

        vm.ModelId = "some-model";
        Assert.True(vm.RunScenarioCommand.CanExecute(row));
    }

    [Fact]
    public async Task RunSuiteCommand_cannot_execute_with_no_scenarios_loaded()
    {
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore([]), new FakeAgentScenarioRunner(), new FakeToasts())
        {
            ModelId = "some-model"
        };
        await vm.LoadScenariosAsync();

        Assert.False(vm.RunSuiteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Running_the_suite_updates_rows_in_place_and_sets_the_headline()
    {
        var scenarios = new List<AgentScenario>
        {
            BuildScenario("s1", "One", [], isBuiltIn: true),
            BuildScenario("s2", "Two", [], isBuiltIn: true)
        };
        var runner = new FakeAgentScenarioRunner
        {
            OnRunSuite = (_, modelId) => Task.FromResult(new AgentScenarioSuiteResult
            {
                Id = "suite-1",
                ModelId = modelId,
                Results =
                [
                    new AgentScenarioRunResult("s1", "One", true, [], 2, 15, "Complete", null),
                    new AgentScenarioRunResult("s2", "Two", false, [new AgentScenarioCheckResult("check-x", false, "boom")], 3, 20, "Blocked", null)
                ],
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow
            })
        };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), runner, new FakeToasts()) { ModelId = "m1" };
        await vm.LoadScenariosAsync();

        await vm.RunSuiteCommand.ExecuteAsync(null);

        var row1 = vm.Scenarios.Single(r => r.Id == "s1");
        var row2 = vm.Scenarios.Single(r => r.Id == "s2");
        Assert.True(row1.Passed);
        Assert.False(row2.Passed);
        Assert.Contains("check-x", row2.FailedCheckSummary);
        Assert.Equal("1/2 passed - report in eval-runs/suite-1", vm.HeadlineResult);
        Assert.Equal("2 / 2 complete", vm.ScenarioProgressLabel);
        Assert.Equal("1 passed · 1 failed", vm.RunningCountsLabel);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Running_suite_exposes_partial_waiting_state_and_cancel()
    {
        var scenarios = new List<AgentScenario>
        {
            BuildScenario("s1", "One", [], isBuiltIn: true),
            BuildScenario("s2", "Two", [], isBuiltIn: true)
        };
        var runner = new FakeAgentScenarioRunner
        {
            OnRunSuiteWithProgress = async (_, _, progress, ct) =>
            {
                progress!.Report("1/2: s1");
                progress.Report("s1 step 3: WaitingForUser");
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("cancellation should have ended the wait");
            }
        };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), runner, new FakeToasts()) { ModelId = "m1" };
        await vm.LoadScenariosAsync();

        // Progress<T> captures the current context. The xUnit context can
        // leave posted callbacks behind while this fake runner waits, so use
        // the repository's inline context to observe the actual Report calls
        // deterministically without changing production scheduling.
        var progressContext = new CountingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        Task run;
        SynchronizationContext.SetSynchronizationContext(progressContext);
        try
        {
            run = vm.RunSuiteCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        await WaitForAsync(() => vm.CurrentStepLabel.Contains("WaitingForUser", StringComparison.Ordinal), "visible scenario step");

        Assert.True(vm.IsRunning);
        Assert.Equal("Running scenario suite", vm.RunningHeadline);
        Assert.Equal("0 / 2 complete", vm.ScenarioProgressLabel);
        Assert.Equal("Current scenario: One (s1)", vm.CurrentScenarioLabel);
        Assert.Equal("Current step: 3 - WaitingForUser", vm.CurrentStepLabel);
        Assert.Equal("0 passed · 0 failed", vm.RunningCountsLabel);

        vm.CancelSuiteCommand.Execute(null);
        await run;

        Assert.False(vm.IsRunning);
        Assert.Equal("Suite run canceled.", vm.StatusMessage);
        Assert.Equal("0 / 2 complete", vm.ScenarioProgressLabel);
    }

    [Fact]
    public async Task Cancellation_during_a_run_leaves_the_view_model_in_a_clean_state()
    {
        var scenarios = new List<AgentScenario> { BuildScenario("s1", "One", [], isBuiltIn: true) };
        var runner = new FakeAgentScenarioRunner { OnRunSuite = (_, _) => throw new OperationCanceledException() };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), runner, new FakeToasts()) { ModelId = "m1" };
        await vm.LoadScenariosAsync();

        await vm.RunSuiteCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
        Assert.Equal("Suite run canceled.", vm.StatusMessage);
        Assert.Equal("0 / 1 complete", vm.ScenarioProgressLabel);
    }

    [Fact]
    public async Task RunScenarioCommand_updates_only_the_selected_row()
    {
        var scenarios = new List<AgentScenario>
        {
            BuildScenario("s1", "One", [], isBuiltIn: true),
            BuildScenario("s2", "Two", [], isBuiltIn: true)
        };
        var runner = new FakeAgentScenarioRunner
        {
            OnRunScenario = (scenario, modelId) =>
                Task.FromResult(new AgentScenarioRunResult(scenario.Manifest.Id, scenario.Manifest.Title, true, [], 1, 5, "Complete", null))
        };
        var vm = new AgentScenarioSuiteViewModel(new FakeAgentScenarioStore(scenarios), runner, new FakeToasts()) { ModelId = "m1" };
        await vm.LoadScenariosAsync();
        var row1 = vm.Scenarios.Single(r => r.Id == "s1");
        var row2 = vm.Scenarios.Single(r => r.Id == "s2");

        await vm.RunScenarioCommand.ExecuteAsync(row1);

        Assert.True(row1.Passed);
        Assert.Null(row2.Passed);
    }

    [Fact]
    public async Task LoadScenariosAsync_restores_failed_evidence_and_marks_it_stale_after_model_change()
    {
        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        await File.WriteAllTextAsync(modelPath, "model version one");
        var scenario = BuildScenario("s1", "One", [], isBuiltIn: true);
        var failed = new AgentScenarioRunResult(
            "s1",
            "One",
            false,
            [new AgentScenarioCheckResult("check-x", false, "failed evidence")],
            3,
            20,
            "Blocked",
            null,
            AgentScenarioEvidenceContract.Create(
                scenario,
                modelPath,
                await AgentScenarioEvidenceContract.ComputeModelContentHashAsync(modelPath),
                "Fake",
                DateTime.UtcNow));
        var evalStore = new FakeEvalStore();
        await evalStore.SaveRunAsync(new EvalRun(
            "suite-1",
            EvalMode.AgentScenario,
            new EvalTarget(modelPath, Label: "agent-scenarios"),
            [new CaseResult(
                "s1",
                "check-x",
                20,
                Error: null,
                Metadata: new Dictionary<string, string>
                {
                    [AgentScenarioEvidenceContract.ResultJsonKey] = JsonSerializer.Serialize(
                        failed,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
                })],
            DateTime.UtcNow,
            DateTime.UtcNow,
            "suite-1"));

        var vm = new AgentScenarioSuiteViewModel(
            new FakeAgentScenarioStore([scenario]),
            new FakeAgentScenarioRunner(),
            new FakeToasts(),
            evalStore)
        {
            ModelId = modelPath
        };

        await vm.LoadScenariosAsync();

        var row = Assert.Single(vm.Scenarios);
        Assert.Equal(AgentScenarioEvidenceStatus.Fail, row.EvidenceStatus);
        Assert.False(row.Passed);
        Assert.Contains("failed evidence", row.FailedCheckSummary);

        await File.WriteAllTextAsync(modelPath, "model version two");
        await vm.LoadScenariosAsync();

        row = Assert.Single(vm.Scenarios);
        Assert.Equal(AgentScenarioEvidenceStatus.Stale, row.EvidenceStatus);
        Assert.Equal("STALE", row.StatusLabel);
        Assert.False(row.Passed);
        Assert.Contains("failed evidence", row.FailedCheckSummary);
    }
}
