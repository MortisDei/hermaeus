using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Agent.Services;

public interface IAgentScenarioRunner
{
    Task<AgentScenarioRunResult> RunScenarioAsync(AgentScenario scenario, string modelId, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<AgentScenarioSuiteResult> RunSuiteAsync(IReadOnlyList<AgentScenario> scenarios, string modelId, IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Executes scenarios against a real, isolated <see cref="AgentService"/> in a
/// throwaway sandbox: a copied workspace, a fresh agent data root pointed at
/// a temp directory, and never-deny-only approvals (see the docs/review r7
/// pack). This never touches the caller's real agent data (task store,
/// lesson store, workspace memory) - the only shared dependency is the real
/// <see cref="ILlmService"/> (the model under test) and, for export, the
/// real <see cref="ISettingsService"/>'s configured data root.
/// </summary>
public sealed class AgentScenarioRunner : IAgentScenarioRunner
{
    private readonly ILlmService _llm;
    private readonly ISettingsService _userSettings;
    private readonly IEvalStore? _evalStore;

    public AgentScenarioRunner(ILlmService llm, ISettingsService userSettings, IEvalStore? evalStore = null)
    {
        _llm = llm;
        _userSettings = userSettings;
        _evalStore = evalStore;
    }

    public Task<AgentScenarioRunResult> RunScenarioAsync(AgentScenario scenario, string modelId, IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunScenarioCoreAsync(scenario, modelId, Guid.NewGuid().ToString("N"), progress, ct);

    public async Task<AgentScenarioSuiteResult> RunSuiteAsync(IReadOnlyList<AgentScenario> scenarios, string modelId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var suite = new AgentScenarioSuiteResult { ModelId = modelId, StartedAt = DateTime.UtcNow };
        for (var i = 0; i < scenarios.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var scenario = scenarios[i];
            progress?.Report($"{i + 1}/{scenarios.Count}: {scenario.Manifest.Id}");
            var result = await RunScenarioCoreAsync(scenario, modelId, suite.Id, progress, ct);
            suite.Results.Add(result);
        }

        suite.FinishedAt = DateTime.UtcNow;
        await ExportSuiteAsync(suite, ct);
        return suite;
    }

    private async Task<AgentScenarioRunResult> RunScenarioCoreAsync(
        AgentScenario scenario,
        string modelId,
        string runId,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "hermaeus-scenario-runs", runId, scenario.Manifest.Id);
        var workspaceDir = Path.Combine(sandboxRoot, "workspace");
        var dataDir = Path.Combine(sandboxRoot, "data");
        var outsideDir = Path.Combine(sandboxRoot, "outside");

        var sw = Stopwatch.StartNew();
        AgentTaskState? finalState = null;
        AgentPlannerResponse? finalResponse = null;
        string? runError = null;
        IReadOnlyDictionary<string, string> beforeHashes = new Dictionary<string, string>();
        FileAgentTaskStateStore? taskStore = null;
        string? taskId = null;

        try
        {
            CopyWorkspace(scenario.WorkspaceDirectory, workspaceDir);
            WriteOutsideFiles(scenario.Manifest.OutsideFiles, outsideDir);
            beforeHashes = HashWorkspace(workspaceDir);

            var scenarioSettings = new ScenarioSettings(dataDir, scenario.Manifest.MaxSteps, scenario.Manifest.MaxOrchestrationSteps);
            taskStore = new FileAgentTaskStateStore(scenarioSettings);
            var lessons = new SqliteLessonStore(scenarioSettings);
            await taskStore.InitializeAsync(ct);
            await lessons.InitializeAsync(ct);

            var options = new AgentWorkspaceOptions(WorkspaceRoot: workspaceDir, RagDatasetId: null, ModelId: modelId);
            var scopeId = Path.GetFullPath(workspaceDir);
            foreach (var seed in scenario.Manifest.SeedLessons)
                await SeedLessonAsync(lessons, scopeId, seed, ct);

            var seededMemory = new SeededWorkspaceMemoryStore(scenario.Manifest.SeedMemory);
            var retrieval = new NullAgentRetrievalService();
            var tools = new AgentWorkspaceTools();
            var gate = new AgentSafetyGate();
            var executor = new AgentToolExecutor(tools, mcpBridge: null);
            var manifests = new WorkspaceManifestService();
            var profiles = new FileWorkspaceProfileStore(scenarioSettings);
            var activation = new WorkspaceActivationService(manifests, profiles);
            var contextBuilder = new AgentContextBuilder(tools, retrieval, seededMemory, activation, taskStore, scenarioSettings, lessons);
            var agent = new AgentService(taskStore, contextBuilder, gate, executor, _llm, traces: null, manifests, scenarioSettings, lessons, tools);

            var task = await agent.CreateTaskAsync(scenario.Manifest.Goal, options, ct);
            taskId = task.TaskId;

            void OnStep(AgentStepResult r) =>
                progress?.Report($"{scenario.Manifest.Id} step {r.State.StepCount}: {r.State.Status}");

            var maxOuterIterations = scenario.Manifest.MaxSteps + 3;
            AgentStepResult? lastResult = null;
            for (var outer = 0; outer < maxOuterIterations; outer++)
            {
                ct.ThrowIfCancellationRequested();
                lastResult = await agent.RunAsync(taskId, options, OnStep, ct);
                var status = lastResult.State.Status;

                if (status is AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Blocked)
                    break;

                if (status == AgentTaskStatus.WaitingForUser)
                {
                    var pending = lastResult.State.PendingToolAction;
                    if (pending is not null
                        && scenario.Manifest.AutoApprove.Any(t => string.Equals(t, pending.ToolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Never deny: a denial would write a rejection lesson via
                        // AgentService.RecordApprovalRejectionLessonAsync, which
                        // would corrupt max_new_lessons checks. Leaving an
                        // unapproved action pending IS the observable outcome the
                        // rest of this scenario's checks look for.
                        //
                        // The pending action can belong to a CHILD task once
                        // orchestration is running (lastResult.State is then
                        // the child's state, not the parent's) - approve on
                        // whichever task id actually holds it, then resume
                        // via the PARENT's task id so the orchestration loop
                        // re-enters and continues that same child.
                        await agent.AppendApprovalAsync(lastResult.State.TaskId, pending.ToolName, approved: true, AgentApprovalFingerprint.Resolve(pending), options, ct);
                        continue;
                    }

                    break;
                }

                break;
            }

            finalState = lastResult?.State;
            finalResponse = lastResult?.PlannerResponse;
        }
        catch (OperationCanceledException)
        {
            CleanupSandbox(sandboxRoot);
            throw;
        }
        catch (Exception ex)
        {
            runError = ex.Message;

            // A thrown step still saves task state before rethrowing
            // (AgentService's tool-execution and model-call catch blocks
            // both do this), so the persisted state - not just the
            // exception message - is what evaluation should see.
            if (taskStore is not null && taskId is not null)
            {
                try { finalState = await taskStore.LoadAsync(taskId, ct); }
                catch { /* best-effort; stateForChecks below falls back to an empty state */ }
            }
        }

        sw.Stop();

        var afterHashes = HashWorkspace(workspaceDir);
        var diff = DiffWorkspace(beforeHashes, afterHashes);
        var stateForChecks = finalState ?? new AgentTaskState { Status = AgentTaskStatus.New };
        var checks = new List<AgentScenarioCheckResult>(
            AgentScenarioChecks.Evaluate(scenario.Manifest.Expect, stateForChecks, finalResponse, diff));

        if (runError is not null && !scenario.Manifest.AllowRunError)
            checks.Insert(0, new AgentScenarioCheckResult("run_error", false, runError));

        CleanupSandbox(sandboxRoot);

        return new AgentScenarioRunResult(
            ScenarioId: scenario.Manifest.Id,
            Title: scenario.Manifest.Title,
            Passed: checks.All(c => c.Passed),
            Checks: checks,
            Steps: finalState?.StepCount ?? 0,
            DurationMs: sw.ElapsedMilliseconds,
            FinalStatus: finalState?.Status.ToString() ?? "error",
            RunError: runError);
    }

    private static void CopyWorkspace(string sourceWorkspaceDir, string destWorkspaceDir)
    {
        Directory.CreateDirectory(destWorkspaceDir);
        if (!Directory.Exists(sourceWorkspaceDir))
            return;

        foreach (var dir in Directory.EnumerateDirectories(sourceWorkspaceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceWorkspaceDir, dir);
            Directory.CreateDirectory(Path.Combine(destWorkspaceDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceWorkspaceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceWorkspaceDir, file);
            var destPath = Path.Combine(destWorkspaceDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }
    }

    private static void WriteOutsideFiles(IReadOnlyDictionary<string, string> files, string outsideDir)
    {
        foreach (var (name, content) in files)
        {
            if (Path.IsPathRooted(name) || name.Contains(".."))
                throw new InvalidOperationException($"Scenario outside_files key '{name}' is not a safe relative filename.");

            var path = Path.Combine(outsideDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static async Task SeedLessonAsync(ILessonStore lessons, string scopeId, AgentScenarioSeedLesson seed, CancellationToken ct)
    {
        var outcome = Enum.TryParse<AgentLessonOutcome>(seed.Outcome, ignoreCase: true, out var parsed)
            ? parsed
            : AgentLessonOutcome.Observation;
        var signature = string.IsNullOrWhiteSpace(seed.Signature)
            ? "stated:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed.Claim.ToLowerInvariant())))[..16]
            : seed.Signature;

        await lessons.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, scopeId, AgentLessonKind.Stated, signature, seed.Claim, seed.Guidance, outcome), ct);
    }

    private static Dictionary<string, string> HashWorkspace(string workspaceDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(workspaceDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(workspaceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(workspaceDir, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            result[relative] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return result;
    }

    private static AgentScenarioFileDiff DiffWorkspace(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var changed = new List<string>();
        var created = new List<string>();
        var deleted = new List<string>();

        foreach (var (path, hash) in after)
        {
            if (!before.TryGetValue(path, out var oldHash))
                created.Add(path);
            else if (!string.Equals(oldHash, hash, StringComparison.Ordinal))
                changed.Add(path);
        }

        foreach (var path in before.Keys)
            if (!after.ContainsKey(path))
                deleted.Add(path);

        return new AgentScenarioFileDiff(changed, created, deleted);
    }

    private static void CleanupSandbox(string sandboxRoot)
    {
        try
        {
            // Pooled SQLite connections (the sandbox lesson store) keep file
            // handles open on Windows; clear pools first so the temp
            // directory can actually be deleted, same reason as
            // Hermaeus.Tests.TempDir.Dispose.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(sandboxRoot))
                Directory.Delete(sandboxRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover temp sandbox is not fatal.
        }
    }

    private async Task ExportSuiteAsync(AgentScenarioSuiteResult suite, CancellationToken ct)
    {
        var configured = _userSettings.Settings.DataManagement.DataRootDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
        var dir = Path.Combine(root, "eval-runs", suite.Id);

        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(dir, "run.jsonl"),
            string.Join(Environment.NewLine, suite.Results.Select(r => JsonSerializer.Serialize(r, AgentJson.CompactOptions))),
            ct);

        var md = new StringBuilder();
        md.AppendLine("# Agent Scenario Suite");
        md.AppendLine();
        md.AppendLine($"- Model: `{suite.ModelId}`");
        md.AppendLine($"- Started: {suite.StartedAt:O}");
        md.AppendLine($"- Finished: {suite.FinishedAt:O}");
        md.AppendLine($"- Passed: {suite.PassedCount}/{suite.Total}");
        md.AppendLine();
        foreach (var result in suite.Results)
        {
            md.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} - {result.Title} ({result.ScenarioId})");
            md.AppendLine();
            md.AppendLine($"- Steps: {result.Steps}");
            md.AppendLine($"- Duration: {result.DurationMs} ms");
            md.AppendLine($"- Final status: {result.FinalStatus}");
            if (result.RunError is not null)
                md.AppendLine($"- Run error: {result.RunError}");
            foreach (var check in result.Checks)
                md.AppendLine(check.Passed ? $"- [x] {check.CheckId}" : $"- [ ] {check.CheckId}: {check.Detail}");
            md.AppendLine();
        }

        await AtomicFileWriter.WriteAllTextAsync(Path.Combine(dir, "report.md"), md.ToString(), ct);

        if (_evalStore is not null)
            await _evalStore.SaveRunAsync(ToEvalRun(suite), ct);
    }

    private static EvalRun ToEvalRun(AgentScenarioSuiteResult suite) => new(
        Id: suite.Id,
        Mode: EvalMode.AgentScenario,
        Target: new EvalTarget(suite.ModelId, Label: "agent-scenarios"),
        CaseResults: suite.Results.Select(r => new CaseResult(
            CaseId: r.ScenarioId,
            Output: r.Passed ? "pass" : string.Join("; ", r.Checks.Where(c => !c.Passed).Select(c => c.CheckId)),
            LatencyMs: r.DurationMs,
            Scores: new Dictionary<string, double>
            {
                ["passed"] = r.Passed ? 1 : 0,
                ["checks_passed"] = r.Checks.Count(c => c.Passed),
                ["checks_total"] = r.Checks.Count,
                ["steps"] = r.Steps
            },
            Error: r.RunError)).ToList(),
        StartedAt: suite.StartedAt,
        FinishedAt: suite.FinishedAt);

    private sealed class ScenarioSettings : ISettingsService
    {
        public AppSettings Settings { get; private set; }

        public ScenarioSettings(string dataRoot, int maxAutoSteps, int? maxOrchestrationSteps = null)
        {
            Settings = new AppSettings();
            Settings.DataManagement.DataRootDirectory = dataRoot;
            Settings.Agent.MaxAutoSteps = Math.Max(maxAutoSteps, 1);
            if (maxOrchestrationSteps is { } ceiling)
                Settings.Agent.MaxOrchestrationSteps = Math.Max(ceiling, 1);
        }

        public Task LoadAsync() => Task.CompletedTask;

        public Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null) =>
            Task.FromResult(new SettingsSaveResult(false, null, null, null, 0));

        public Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
        {
            Settings = settings;
            return Task.FromResult(new SettingsSaveResult(false, null, null, null, 0));
        }

        public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory) =>
            new(false, previousDataRootDirectory ?? string.Empty, nextDataRootDirectory ?? string.Empty, 0, []);

        // No consumer of a sandbox settings instance ever mutates it after
        // construction, so this event never needs to fire; the empty
        // accessor pair avoids CS0067 under the zero-warning build policy.
        public event EventHandler? SettingsChanged { add { } remove { } }
    }

    private sealed class SeededWorkspaceMemoryStore : IAgentWorkspaceMemoryStore
    {
        private readonly List<AgentWorkspaceMemoryEntry> _entries;

        public SeededWorkspaceMemoryStore(IEnumerable<AgentScenarioSeedMemory> seed) =>
            _entries = seed.Select(s => new AgentWorkspaceMemoryEntry { Title = s.Title, Body = s.Body }).ToList();

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentWorkspaceMemoryEntry>> ListAsync(string workspaceRoot, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentWorkspaceMemoryEntry>>(_entries);

        public Task<AgentWorkspaceMemoryEntry> UpsertAsync(AgentWorkspaceMemoryEntry entry, CancellationToken ct = default)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            _entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task DeleteAsync(string workspaceRoot, string id, CancellationToken ct = default)
        {
            _entries.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }

        public string GetWorkspaceDirectory(string workspaceRoot) => workspaceRoot;
    }

    private sealed class NullAgentRetrievalService : IAgentRetrievalService
    {
        public Task<bool> DatasetExistsAsync(string datasetId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string datasetId, string query, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);
    }
}
