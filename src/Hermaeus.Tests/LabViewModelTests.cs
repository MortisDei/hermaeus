using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LabViewModelTests
{
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
}
