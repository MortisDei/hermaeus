using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using System.Text.Json;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LabViewModelTests
{
    [Fact]
    public void Lab_start_paths_disable_each_other_while_a_run_is_active()
    {
        using var temp = new TempDir();
        var (_, vm) = Build(temp);

        Assert.True(vm.CanStartRun);
        Assert.True(vm.CanRunRecipe);

        vm.IsRunActive = true;

        Assert.False(vm.CanStartRun);
        Assert.False(vm.CanRunRecipe);
        Assert.False(vm.FreezeAndStartCommand.CanExecute(null));
        Assert.False(vm.RunSelectedRecipeCommand.CanExecute(null));
    }

    private static (SqliteEmpiricalExperienceStore Store, LabViewModel ViewModel) Build(TempDir temp)
    {
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        return (store, new LabViewModel(store, new FakeToasts()));
    }

    private static EmpiricalExperienceDraft Draft(string domain, NormalizedOutcome outcome, EvidenceOrigin origin) => new()
    {
        Domain = domain,
        ContextJson = "{\"scope\":\"test\"}",
        ActionJson = "{\"action\":\"inspect\"}",
        Outcome = NormalizedToolOutcome.Create(outcome, "test", "detail"),
        Provenance = [new EmpiricalExperienceProvenance("evidence-1", new SourceReference(ProvenanceKind.Lab, "source", EvidenceOrigin: origin))]
    };

    [Fact]
    public async Task Filters_and_unknown_render_without_substituted_defaults()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        await store.AddAsync(Draft(EmpiricalExperienceDomains.AgentToolOutcome, NormalizedOutcome.Unknown, EvidenceOrigin.ModelInference));
        await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun, NormalizedOutcome.Succeeded, EvidenceOrigin.DirectObservation));
        vm.DomainFilter = EmpiricalExperienceDomains.AgentToolOutcome;
        vm.OutcomeFilter = nameof(NormalizedOutcome.Unknown);
        vm.OriginFilter = nameof(EvidenceOrigin.ModelInference);

        await vm.RefreshAsync();

        var row = Assert.Single(vm.Experiences);
        Assert.Equal("Unknown", row.OutcomeLabel);
        Assert.Equal("ModelInference", row.OriginLabel);
    }

    [Fact]
    public async Task Evidence_empty_state_distinguishes_no_records_from_filtered_records()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);

        await vm.RefreshAsync();

        Assert.False(vm.HasAnyEvidence);
        Assert.Equal("No evidence has been captured yet.", vm.EvidenceEmptyState);

        await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun, NormalizedOutcome.Succeeded, EvidenceOrigin.DirectObservation));
        vm.DomainFilter = EmpiricalExperienceDomains.AgentToolOutcome;
        await vm.RefreshAsync();

        Assert.True(vm.HasAnyEvidence);
        Assert.Equal("No evidence matches these filters.", vm.EvidenceEmptyState);
        Assert.Contains("Clear or broaden", vm.EvidenceEmptyHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_correction_supersedes_selected_record()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        var prior = await store.AddAsync(Draft(EmpiricalExperienceDomains.AgentToolOutcome, NormalizedOutcome.Unknown, EvidenceOrigin.DirectObservation));
        await vm.RefreshAsync();
        vm.CorrectionOutcome = nameof(NormalizedOutcome.Succeeded);
        vm.CorrectionDetail = "user verified";

        await vm.CorrectSelectedCommand.ExecuteAsync(null);

        Assert.Equal(EmpiricalExperienceStatus.Superseded, (await store.GetAsync(prior.Id))!.Status);
        Assert.Contains(vm.Experiences, row => row.Experience.CorrectsExperienceId == prior.Id && row.OutcomeLabel == "Succeeded");
    }

    [Fact]
    public async Task Removal_requires_confirmation_and_then_hard_deletes()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        var saved = await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun, NormalizedOutcome.NoEffect, EvidenceOrigin.Extracted));
        await vm.RefreshAsync();
        vm.ConfirmRemoval = _ => Task.FromResult(false);
        await vm.RemoveSelectedCommand.ExecuteAsync(null);
        Assert.NotNull(await store.GetAsync(saved.Id));

        vm.ConfirmRemoval = _ => Task.FromResult(true);
        await vm.RemoveSelectedCommand.ExecuteAsync(null);
        Assert.Null(await store.GetAsync(saved.Id));
    }

    [Fact]
    public async Task Export_uses_checked_rows_and_is_versioned()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        await store.AddAsync(Draft(EmpiricalExperienceDomains.AgentToolOutcome, NormalizedOutcome.Succeeded, EvidenceOrigin.DirectObservation));
        await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun, NormalizedOutcome.Failed, EvidenceOrigin.DirectObservation));
        await vm.RefreshAsync();
        vm.Experiences[0].IsExportSelected = true;

        await vm.ExportSelectedCommand.ExecuteAsync(null);

        Assert.Contains("\"schemaVersion\": 1", vm.ExportJson, StringComparison.Ordinal);
        Assert.Contains(vm.Experiences[0].Id, vm.ExportJson, StringComparison.Ordinal);
        Assert.DoesNotContain(vm.Experiences[1].Id, vm.ExportJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_recipe_run_reports_visible_status()
    {
        using var temp = new TempDir();
        var (_, vm) = Build(temp);

        await vm.RunSelectedRecipeCommand.ExecuteAsync(null);

        Assert.Equal("Lab recipes are unavailable in this session.", vm.StatusMessage);
    }

    [Fact]
    public async Task Recipe_failure_detail_is_not_replaced_by_the_evidence_count()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Id = "chat", Name = "Chat", ModelPath = "model.gguf" });
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var vm = new LabViewModel(store, new FakeToasts(), null, settings, new FailingRecipeService());

        await vm.RefreshRecipesCommand.ExecuteAsync(null);
        await vm.RunSelectedRecipeCommand.ExecuteAsync(null);

        Assert.Equal("Failed", vm.RunStatus);
        Assert.Equal("Lab recipe failed: isolated runtime rejected the configured executable.", vm.StatusMessage);
    }

    [Fact]
    public async Task Returned_failed_recipe_keeps_its_failure_detail_after_evidence_refresh()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Id = "chat", Name = "Chat", ModelPath = "model.gguf" });
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var vm = new LabViewModel(store, new FakeToasts(), null, settings, new FailingRecipeService(returnsFailedSnapshot: true));

        await vm.RefreshRecipesCommand.ExecuteAsync(null);
        await vm.RunSelectedRecipeCommand.ExecuteAsync(null);

        Assert.Equal("Failed", vm.RunStatus);
        Assert.Equal("Lab recipe failed: isolated runtime rejected the configured executable.", vm.StatusMessage);
    }

    [Fact]
    public async Task Failed_frozen_run_keeps_its_failure_detail_after_evidence_refresh()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Id = "chat", Name = "Chat", ModelPath = "model.gguf" });
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var vm = new LabViewModel(store, new FakeToasts(), new FailingExperimentService(), settings, null);

        await vm.FreezeAndStartCommand.ExecuteAsync(null);

        Assert.Equal("Failed", vm.RunStatus);
        Assert.Equal("Lab run failed: temporary runtime failed its health check.", vm.StatusMessage);
    }

    [Fact]
    public void Evidence_detail_exposes_a_concise_summary_before_raw_json()
    {
        var experience = new EmpiricalExperience
        {
            ContextJson = "{\"runId\":\"run-1\",\"configurationId\":\"baseline\",\"observations\":[1,2,3]}",
            ActionJson = "{\"status\":\"failed\",\"detail\":\"runtime unavailable\"}"
        };

        var row = new ExperienceRowViewModel(experience);

        Assert.Equal("runId: run-1; configurationId: baseline; observations: [3 item(s)]", row.ContextSummary);
        Assert.Equal("status: failed; detail: runtime unavailable", row.ActionSummary);
    }

    [Fact]
    public void Completed_lab_evidence_explains_the_tested_configurations_and_measurements()
    {
        var summary = new LabRunCompletionSummary(
            "run-1", "definition", LabRunStatus.Succeeded, new DateTime(2026, 8, 30, 1, 2, 3, DateTimeKind.Utc),
            new DateTime(2026, 8, 30, 1, 3, 3, DateTimeKind.Utc), [],
            [new LabComparisonDecision("baseline", "candidate", true, [],
                new LabEquivalenceResult(LabEquivalenceState.Equivalent, LabEquivalenceLevel.ExactUtf8, "a", "a", "Equivalent output.", ""),
                true, true, "")], ["slice-1"],
            [new LabConfiguration { Id = "baseline", Label = "Context 4,096" },
             new LabConfiguration { Id = "candidate", Label = "Context 8,192" }],
            [new LabComparison
            {
                BaselineConfigurationId = "baseline", CandidateConfigurationId = "candidate",
                CanShowHeadlineDelta = true,
                CorrectnessPassed = true,
                Equivalence = new LabEquivalenceResult(LabEquivalenceState.Equivalent, LabEquivalenceLevel.ExactUtf8, "a", "a", "Equivalent output.", ""),
                BaselineMetrics =
                [
                    new LabMetricSummary("decode.tokens_per_second", "tokens/s", 40, 39, 41, 3, "runtime"),
                    new LabMetricSummary("memory.ram.observed", "bytes", 2d * 1024 * 1024 * 1024, 1.9 * 1024 * 1024 * 1024, 2.1 * 1024 * 1024 * 1024, 3, "runtime"),
                    new LabMetricSummary("memory.gpu.predicted", "bytes", 4d * 1024 * 1024 * 1024, 4d * 1024 * 1024 * 1024, 4d * 1024 * 1024 * 1024, 1, "fit")
                ],
                CandidateMetrics =
                [
                    new LabMetricSummary("decode.tokens_per_second", "tokens/s", 50, 49, 51, 3, "runtime"),
                    new LabMetricSummary("memory.ram.observed", "bytes", 3d * 1024 * 1024 * 1024, 2.9 * 1024 * 1024 * 1024, 3.1 * 1024 * 1024 * 1024, 3, "runtime"),
                    new LabMetricSummary("memory.gpu.predicted", "bytes", 3d * 1024 * 1024 * 1024, 3d * 1024 * 1024 * 1024, 3d * 1024 * 1024 * 1024, 1, "fit")
                ]
            }], "Context sweep", "Qwen2 · Q4_K_M");
        var row = new ExperienceRowViewModel(new EmpiricalExperience
        {
            Domain = EmpiricalExperienceDomains.LabRun,
            ActionJson = System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        });

        Assert.Contains("Tested: Context 4,096 (baseline), Context 8,192 (candidate).", row.ResultSummary);
        Assert.Contains("Context 4,096 vs Context 8,192: correctness passed; Equivalent output.", row.ResultSummary);
        Assert.Contains("decode.tokens_per_second: 40 to 50 tokens/s (+10)", row.ResultSummary);
        Assert.Contains("Recommendation: Context 8,192 is the only correctness-eligible candidate", row.ResultSummary);
        Assert.Equal("Lab: Context sweep", row.DisplayDomain);
        Assert.Equal("Experiment result", row.RecordKindLabel);
        Assert.Contains("Evidence: 1 immutable configuration slice(s).", row.ResultSummary);
        Assert.NotNull(row.ResultDetails);
        var details = row.ResultDetails!;
        Assert.Equal("Qwen2 · Q4_K_M", details.ModelLabel);
        Assert.Equal("Succeeded", details.ResultStatus);
        Assert.Equal("Context 4,096 (baseline), Context 8,192 (candidate)", details.TestedConfigurations);
        Assert.Equal("40 -> 50 tokens/s (+10 tokens/s, +25.0%)", Assert.Single(details.Comparisons).ThroughputLabel);
        Assert.Contains("2.1 -> 3.1 GiB (+1 GiB, +47.6%); observed peak", Assert.Single(details.Comparisons).RamLabel);
        Assert.Contains("4 -> 3 GiB (-1 GiB, -25.0%); predicted", Assert.Single(details.Comparisons).VramLabel);
        Assert.Equal("Passed", Assert.Single(details.Comparisons).CorrectnessLabel);
        Assert.Contains("Recommended for review: Context 8,192", details.RecommendationLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_refresh_selects_the_complete_execution_with_structured_result_details()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        var summary = CompletedSummary("run-current", ["slice-current"]);
        await store.AddAsync(new EmpiricalExperienceDraft
        {
            Id = "summary-current",
            Domain = EmpiricalExperienceDomains.LabRun,
            ContextJson = "{\"runId\":\"run-current\"}",
            ActionJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "lab-run-completed", "completed"),
            Provenance = TestProvenance()
        });

        await vm.RefreshAsync();

        Assert.NotNull(vm.SelectedExperience);
        var selected = vm.SelectedExperience!;
        Assert.Same(selected, Assert.Single(vm.Experiences));
        Assert.Equal("run-current", selected.LabRunId);
        Assert.NotNull(selected.ResultDetails);
        Assert.Contains("Context sweep", selected.DisplayDomain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lab_evidence_groups_all_persisted_records_for_one_execution_into_one_top_level_entry()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        var summary = CompletedSummary("run-1", ["slice-1", "slice-2"]);
        await store.AddBatchAsync([
            new EmpiricalExperienceDraft
            {
                Id = "slice-1", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-1\",\"configurationId\":\"baseline\"}",
                ActionJson = "{\"runId\":\"run-1\",\"configurationId\":\"baseline\",\"chunkIndex\":0}",
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "slice", "baseline evidence"),
                Provenance = TestProvenance()
            },
            new EmpiricalExperienceDraft
            {
                Id = "slice-2", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-1\",\"configurationId\":\"candidate\"}",
                ActionJson = "{\"runId\":\"run-1\",\"configurationId\":\"candidate\",\"chunkIndex\":0}",
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "slice", "candidate evidence"),
                Provenance = TestProvenance()
            },
            new EmpiricalExperienceDraft
            {
                Id = "summary", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-1\"}",
                ActionJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "lab-run-completed", "completed"),
                Provenance = TestProvenance()
            }
        ]);

        await vm.RefreshAsync();

        var row = Assert.Single(vm.Experiences);
        Assert.Equal("run-1", row.LabRunId);
        Assert.Equal(3, row.EvidenceRecords.Count);
        Assert.Equal("Experiment result", row.RecordKindLabel);
        Assert.Contains("Context sweep", row.DisplayDomain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_preserves_historical_and_current_runs_with_legacy_slices_and_apply_evidence()
    {
        using var temp = new TempDir();
        var (store, vm) = Build(temp);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var historical = CompletedSummary("run-historical", ["historical-slice"]);
        var legacySummary = JsonSerializer.Serialize(new
        {
            historical.RunId, historical.DefinitionHash, historical.Status,
            historical.StartedAtUtc, historical.CompletedAtUtc, historical.Failures,
            historical.Comparisons, historical.EvidenceSliceIds
        }, options);
        var current = CompletedSummary("run-current", ["current-slice"]);
        var apply = JsonSerializer.Serialize(new LabApplyEvidence(
            "run-current", "definition", "review", "chat", "candidate", []), options);
        await store.AddBatchAsync([
            SnapshotDraft("run-historical", "historical-start"),
            SliceDraft("run-historical", "historical-slice"),
            new EmpiricalExperienceDraft
            {
                Id = "historical-summary", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-historical\"}", ActionJson = legacySummary,
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "completed", "historical"),
                Provenance = TestProvenance()
            },
            SnapshotDraft("run-current", "current-start"),
            SliceDraft("run-current", "current-slice"),
            new EmpiricalExperienceDraft
            {
                Id = "current-summary", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-current\"}",
                ActionJson = JsonSerializer.Serialize(current, options),
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "completed", "current"),
                Provenance = TestProvenance()
            },
            new EmpiricalExperienceDraft
            {
                Id = "current-apply", Domain = EmpiricalExperienceDomains.LabRun,
                ContextJson = "{\"runId\":\"run-current\"}", ActionJson = apply,
                Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "apply", "reviewed"),
                Provenance = TestProvenance()
            }
        ]);

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Experiences.Count);
        var historicalRow = Assert.Single(vm.Experiences, row => row.LabRunId == "run-historical");
        Assert.Equal(3, historicalRow.EvidenceRecords.Count);
        Assert.NotNull(historicalRow.ResultDetails);
        var currentRow = Assert.Single(vm.Experiences, row => row.LabRunId == "run-current");
        Assert.Equal(4, currentRow.EvidenceRecords.Count);
        Assert.Equal("Experiment result", currentRow.RecordKindLabel);
        Assert.Contains("Context sweep", currentRow.DisplayDomain, StringComparison.Ordinal);
        Assert.Same(currentRow, vm.SelectedExperience);
    }

    [Fact]
    public async Task Baseline_refresh_failure_cancels_the_owned_run_before_source_restore()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Id = "chat", Name = "Chat", ModelPath = "model.gguf" });
        var inner = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var store = new ThrowingQueryExperienceStore(inner);
        var experiments = new RunningExperimentService();
        var vm = new LabViewModel(store, new FakeToasts(), experiments, settings, null);

        await vm.FreezeAndStartCommand.ExecuteAsync(null);

        Assert.Equal(1, experiments.CancelCount);
        Assert.False(vm.IsRunActive);
        Assert.Contains("evidence query failed", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evidence_review_resolves_selected_execution_and_surfaces_the_review_state()
    {
        using var temp = new TempDir();
        var (store, _) = Build(temp);
        var experiments = new ReviewableExperimentService();
        var vm = new LabViewModel(store, new FakeToasts(), experiments, null, null);
        var summary = CompletedSummary("run-1", ["slice-1"]);
        await store.AddAsync(new EmpiricalExperienceDraft
        {
            Id = "summary", Domain = EmpiricalExperienceDomains.LabRun,
            ContextJson = "{\"runId\":\"run-1\"}",
            ActionJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "lab-run-completed", "completed"),
            Provenance = TestProvenance()
        });
        var confirmed = false;
        vm.ConfirmApply = _ =>
        {
            confirmed = true;
            return Task.FromResult(false);
        };

        await vm.RefreshAsync();
        Assert.True(vm.CanReviewCurrentRun);

        vm.ReviewApplyCommand.Execute(null);
        Assert.True(experiments.ReviewRequested);
        Assert.Contains("Review ready", vm.ApplyReviewSummary, StringComparison.Ordinal);
        Assert.Contains("ContextSize", vm.ApplyReviewSummary, StringComparison.Ordinal);
        Assert.True(vm.CanConfirmApply);

        await vm.ConfirmApplyCommand.ExecuteAsync(null);
        Assert.True(confirmed);
    }

    private static LabRunCompletionSummary CompletedSummary(string runId, IReadOnlyList<string> slices) =>
        new(runId, "definition", LabRunStatus.Succeeded, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            [],
            [new LabComparisonDecision("baseline", "candidate", true, [],
                new LabEquivalenceResult(LabEquivalenceState.Equivalent, LabEquivalenceLevel.ExactUtf8,
                    "a", "a", "Equivalent output.", ""), true, true, "")],
            slices,
            [new LabConfiguration { Id = "baseline", Label = "Baseline" },
             new LabConfiguration { Id = "candidate", Label = "Candidate" }],
            [new LabComparison
            {
                BaselineConfigurationId = "baseline", CandidateConfigurationId = "candidate",
                CanShowHeadlineDelta = true,
                Equivalence = new LabEquivalenceResult(LabEquivalenceState.Equivalent, LabEquivalenceLevel.ExactUtf8,
                    "a", "a", "Equivalent output.", "")
            }],
            "Context sweep");

    private static IReadOnlyList<EmpiricalExperienceProvenance> TestProvenance() =>
        [new EmpiricalExperienceProvenance("source-1",
            new SourceReference(ProvenanceKind.Lab, "test evidence", EvidenceOrigin: EvidenceOrigin.DirectObservation))];

    private static EmpiricalExperienceDraft SnapshotDraft(string runId, string id) => new()
    {
        Id = id, Domain = EmpiricalExperienceDomains.LabRun,
        ContextJson = "{\"definitionId\":\"definition\"}",
        ActionJson = LabCanonicalJson.Serialize(new LabRunSnapshot
        {
            Id = runId,
            Definition = new LabExperimentDefinition { Name = "Historical experiment" },
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-2)
        }),
        Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Unknown, "started", "start"),
        Provenance = TestProvenance()
    };

    private static EmpiricalExperienceDraft SliceDraft(string runId, string id) => new()
    {
        Id = id, Domain = EmpiricalExperienceDomains.LabRun,
        ContextJson = $"{{\"runId\":\"{runId}\",\"configurationId\":\"baseline\"}}",
        ActionJson = LabCanonicalJson.Serialize(new LabRunEvidenceSlice(runId, "definition", "baseline", [], [])),
        Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "slice", "evidence"),
        Provenance = TestProvenance()
    };

    [Fact]
    public void Lab_evidence_does_not_present_measurements_when_comparison_is_refused()
    {
        var summary = new LabRunCompletionSummary(
            "run-2", "definition", LabRunStatus.PartiallySucceeded, DateTime.UtcNow, DateTime.UtcNow,
            ["quality evidence missing"],
            [new LabComparisonDecision("baseline", "candidate", true, [],
                new LabEquivalenceResult(LabEquivalenceState.Equivalent, LabEquivalenceLevel.ExactUtf8, "a", "a", "Equivalent output.", ""),
                false, false, "No quality evidence was supplied.")], [],
            [new LabConfiguration { Id = "baseline", Label = "Baseline" },
             new LabConfiguration { Id = "candidate", Label = "Candidate" }],
            [new LabComparison
            {
                BaselineConfigurationId = "baseline", CandidateConfigurationId = "candidate",
                CanShowHeadlineDelta = false, RefusalReason = "No quality evidence was supplied.",
                BaselineMetrics = [new LabMetricSummary("decode.tokens_per_second", "tokens/s", 40, 39, 41, 3, "runtime")],
                CandidateMetrics = [new LabMetricSummary("decode.tokens_per_second", "tokens/s", 50, 49, 51, 3, "runtime")]
            }]);
        var row = new ExperienceRowViewModel(new EmpiricalExperience
        {
            ActionJson = System.Text.Json.JsonSerializer.Serialize(summary,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        });

        Assert.Contains("Tested: Baseline (baseline), Candidate (candidate).", row.ResultSummary);
        Assert.Contains("No quality evidence was supplied.", row.ResultSummary);
        Assert.DoesNotContain("Measurements:", row.ResultSummary, StringComparison.Ordinal);
        Assert.Contains("no controlled candidate conclusion was established", row.ResultSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_chat_server_comes_from_live_services_cards()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        var services = Helpers.NewServicesViewModel(settings);
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());

        var vm = new LabViewModel(store, new FakeToasts(), null, settings, null, services);

        var server = Assert.Single(vm.ConfiguredServers);
        Assert.Equal(services.Servers.Single(s => !s.EmbeddingsMode).Id, server.Id);
        Assert.Equal(server.Id, vm.SelectedServer!.Id);
        Assert.Contains("only configured Chat server", vm.ConfiguredServerHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lab_refreshes_when_services_later_rebuilds_the_eventual_canonical_chat_card()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig
        {
            Id = "legacy-chat",
            Name = "Legacy Chat",
            Port = GetFreePort(),
            ModelPath = "legacy.gguf"
        });
        settings.Settings.ManagedServers.Add(new ServerConfig
        {
            Id = "embeddings",
            Name = "Embeddings",
            Port = GetFreePort(),
            EmbeddingsMode = true,
            ModelPath = "embedding.gguf"
        });

        var services = Helpers.NewServicesViewModel(settings);
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var vm = new LabViewModel(store, new FakeToasts(), null, settings, null, services);

        Assert.Equal("legacy-chat", vm.SelectedServer!.Id);
        Assert.Equal("legacy-chat", Assert.Single(vm.ConfiguredServers).Id);

        settings.Settings.ManagedServers[0] = new ServerConfig
        {
            Id = "canonical-chat",
            Name = "Chat",
            Port = GetFreePort(),
            ModelPath = "chat.gguf"
        };
        await settings.SaveAsync();
        await Helpers.WaitForAsync(
            () => vm.ConfiguredServers.Count == 1
                && vm.ConfiguredServers.Any(server => server.Id == "canonical-chat"),
            "Lab to receive the rebuilt canonical Chat server");

        var selected = Assert.Single(vm.ConfiguredServers);
        Assert.Equal("canonical-chat", selected.Id);
        Assert.Equal("canonical-chat", vm.SelectedServer!.Id);
        Assert.Equal("Chat", selected.Name);
        Assert.False(selected.EmbeddingsMode);
        Assert.DoesNotContain(vm.ConfiguredServers, server => server.EmbeddingsMode);
        Assert.Equal("canonical-chat", services.Servers.Single(server => !server.EmbeddingsMode).BuildConfig().Id);
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FailingRecipeService(bool returnsFailedSnapshot = false) : ILabRecipeService
    {
        private static readonly LabRecipePlan Plan = new(
            "failure-test", "Failure test", LabRecipeKind.Context, CapabilityState.Available,
            "test", new LabConfiguration { Id = "baseline", Label = "Baseline", ContextSize = 4096 },
            [new LabConfiguration { Id = "candidate", Label = "Candidate", ContextSize = 8192 }],
            2, false, [], [], LabCorrectnessRequirement.ExactEquivalence);

        public Task<IReadOnlyList<LabRecipePlan>> InspectAsync(ServerConfig source, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LabRecipePlan>>([Plan]);

        public Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source, string prompt, CancellationToken ct = default) =>
            returnsFailedSnapshot
                ? Task.FromResult(new LabRunSnapshot
                {
                    Definition = new LabExperimentDefinition { Candidates = [Plan.Baseline] },
                    Status = LabRunStatus.Failed,
                    Failures = ["isolated runtime rejected the configured executable."]
                })
                : Task.FromException<LabRunSnapshot>(new InvalidOperationException(
                    "isolated runtime rejected the configured executable."));
    }

    private sealed class FailingExperimentService : ILabExperimentService
    {
        public Task<LabExperimentDefinition> CreateDefinitionAsync(
            string name, string protocolId, ServerConfig source, LabConfiguration baseline,
            IReadOnlyList<LabConfiguration> candidates, int repetitions,
            LabCorrectnessRequirement correctness, CancellationToken ct = default) =>
            Task.FromResult(new LabExperimentDefinition
            {
                Name = name,
                ProtocolId = protocolId,
                TargetServerId = source.Id,
                Baseline = baseline,
                Candidates = candidates
            });

        public Task<LabRunSnapshot> StartAsync(LabExperimentDefinition definition, ServerConfig source, CancellationToken ct = default) =>
            Task.FromResult(new LabRunSnapshot
            {
                Definition = definition,
                Status = LabRunStatus.Failed,
                Failures = ["temporary runtime failed its health check."]
            });

        public Task<LabRunSnapshot> CompleteAsync(string runId, IReadOnlyList<LabObservation> observations,
            IReadOnlyList<LabOutputEvidence> outputs, IReadOnlyList<string>? failures = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> SwitchConfigurationAsync(string runId, ServerConfig source,
            string configurationId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> CancelAsync(string runId, CancellationToken ct = default) => throw new NotSupportedException();
        public LabRunSnapshot? GetRun(string runId) => null;
        public LabApplyReview CreateApplyReview(string runId, string candidateId) => throw new NotSupportedException();
        public Task ApplyAsync(LabApplyReview review, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RunningExperimentService : ILabExperimentService
    {
        private LabRunSnapshot? _run;
        public int CancelCount { get; private set; }

        public Task<LabExperimentDefinition> CreateDefinitionAsync(
            string name, string protocolId, ServerConfig source, LabConfiguration baseline,
            IReadOnlyList<LabConfiguration> candidates, int repetitions,
            LabCorrectnessRequirement correctness, CancellationToken ct = default)
        {
            var definition = new LabExperimentDefinition
            {
                Name = name, ProtocolId = protocolId, TargetServerId = source.Id,
                Baseline = baseline, Candidates = candidates
            };
            return Task.FromResult(definition);
        }

        public Task<LabRunSnapshot> StartAsync(LabExperimentDefinition definition, ServerConfig source, CancellationToken ct = default)
        {
            _run = new LabRunSnapshot { Definition = definition, Status = LabRunStatus.Running };
            return Task.FromResult(_run);
        }

        public Task<LabRunSnapshot> CompleteAsync(string runId, IReadOnlyList<LabObservation> observations,
            IReadOnlyList<LabOutputEvidence> outputs, IReadOnlyList<string>? failures = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> SwitchConfigurationAsync(string runId, ServerConfig source,
            string configurationId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> CancelAsync(string runId, CancellationToken ct = default)
        {
            CancelCount++;
            _run = _run! with { Status = LabRunStatus.Cancelled, CompletedAtUtc = DateTime.UtcNow };
            return Task.FromResult(_run);
        }

        public LabRunSnapshot? GetRun(string runId) => _run?.Id == runId ? _run : null;
        public LabApplyReview CreateApplyReview(string runId, string candidateId) => throw new NotSupportedException();
        public Task ApplyAsync(LabApplyReview review, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingQueryExperienceStore(IEmpiricalExperienceStore inner) : IEmpiricalExperienceStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => inner.InitializeAsync(ct);
        public Task<EmpiricalExperience> AddAsync(EmpiricalExperienceDraft draft, CancellationToken ct = default) => inner.AddAsync(draft, ct);
        public Task<IReadOnlyList<EmpiricalExperience>> AddBatchAsync(IReadOnlyList<EmpiricalExperienceDraft> drafts, CancellationToken ct = default) => inner.AddBatchAsync(drafts, ct);
        public Task<EmpiricalExperience?> GetAsync(string id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<EmpiricalExperience>> QueryAsync(EmpiricalExperienceQuery query, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<EmpiricalExperience>>(new InvalidOperationException("evidence query failed"));
        public Task<EmpiricalExperience> CorrectAsync(string priorId, EmpiricalExperienceDraft replacement, CancellationToken ct = default) => inner.CorrectAsync(priorId, replacement, ct);
        public Task RemoveAsync(string id, CancellationToken ct = default) => inner.RemoveAsync(id, ct);
        public Task<string> ExportAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.ExportAsync(ids, ct);
    }

    private sealed class ReviewableExperimentService : ILabExperimentService
    {
        private readonly LabRunSnapshot _run = new()
        {
            Id = "run-1",
            Definition = new LabExperimentDefinition
            {
                TargetServerId = "server-1",
                Baseline = new LabConfiguration { Id = "baseline", Label = "Baseline" },
                Candidates = [new LabConfiguration { Id = "candidate", Label = "Candidate", ContextSize = 8192 }]
            },
            Status = LabRunStatus.Succeeded,
            Comparisons = [new LabComparison
            {
                BaselineConfigurationId = "baseline", CandidateConfigurationId = "candidate",
                CanShowHeadlineDelta = true
            }]
        };

        public bool ReviewRequested { get; private set; }

        public Task<LabExperimentDefinition> CreateDefinitionAsync(
            string name, string protocolId, ServerConfig source, LabConfiguration baseline,
            IReadOnlyList<LabConfiguration> candidates, int repetitions,
            LabCorrectnessRequirement correctness, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LabRunSnapshot> StartAsync(LabExperimentDefinition definition, ServerConfig source, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LabRunSnapshot> CompleteAsync(string runId, IReadOnlyList<LabObservation> observations,
            IReadOnlyList<LabOutputEvidence> outputs, IReadOnlyList<string>? failures = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> SwitchConfigurationAsync(string runId, ServerConfig source,
            string configurationId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LabRunSnapshot> CancelAsync(string runId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public LabRunSnapshot? GetRun(string runId) => runId == _run.Id ? _run : null;

        public LabApplyReview CreateApplyReview(string runId, string candidateId)
        {
            ReviewRequested = true;
            return new LabApplyReview
            {
                RunId = runId,
                CandidateConfigurationId = candidateId,
                CanApply = true,
                Changes = [new LabApplyChange("ContextSize", "4096", "8192")]
            };
        }

        public Task ApplyAsync(LabApplyReview review, CancellationToken ct = default) => Task.CompletedTask;
    }
}
