using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ActivityViewModelTests
{
    private static (ActivityViewModel Vm, SqliteTraceStore Store, ActivityRecorder Recorder) New(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);
        var recorder = new ActivityRecorder(new RedactionService(), store);
        var vm = new ActivityViewModel(new FakeToasts(), store);
        return (vm, store, recorder);
    }

    [Fact]
    public async Task RefreshAsync_maps_outcome_title_and_reason_from_recorded_rows()
    {
        using var temp = new TempDir();
        var (vm, _, recorder) = New(temp);
        await recorder.RecordAsync("rag.ingest", "ds1", ActivityOutcome.Partial, "Ingest into docs", "2 file(s) errored");

        await vm.RefreshAsync();

        var row = Assert.Single(vm.Events);
        Assert.Equal(ActivityOutcome.Partial, row.Outcome);
        Assert.Equal("Ingest into docs", row.Title);
        Assert.Equal("2 file(s) errored", row.Reason);
        Assert.True(row.HasReason);
    }

    [Fact]
    public async Task Partial_outcome_is_not_collapsed_into_succeeded()
    {
        using var temp = new TempDir();
        var (vm, _, recorder) = New(temp);
        await recorder.RecordAsync("rag.ingest", "ds1", ActivityOutcome.Partial, "Partial ingest");
        await recorder.RecordAsync("rag.ingest", "ds2", ActivityOutcome.Succeeded, "Clean ingest");

        await vm.RefreshAsync();

        Assert.Contains(vm.Events, e => e.Outcome == ActivityOutcome.Partial);
        Assert.Contains(vm.Events, e => e.Outcome == ActivityOutcome.Succeeded);
    }

    [Fact]
    public async Task Clear_requires_confirmation_and_removes_events_when_confirmed()
    {
        using var temp = new TempDir();
        var (vm, _, recorder) = New(temp);
        await recorder.RecordAsync("services.server-start", "chat", ActivityOutcome.Succeeded, "Chat started");
        await vm.RefreshAsync();
        Assert.Single(vm.Events);

        vm.RequestConfirmClear = () => Task.FromResult(false);
        await vm.ClearCommand.ExecuteAsync(null);
        await vm.RefreshAsync();
        Assert.Single(vm.Events);

        vm.RequestConfirmClear = () => Task.FromResult(true);
        await vm.ClearCommand.ExecuteAsync(null);

        Assert.Empty(vm.Events);
    }

    [Fact]
    public async Task ProjectFilter_scopes_events_to_that_project_only()
    {
        using var temp = new TempDir();
        var (vm, _, recorder) = New(temp);
        await recorder.RecordAsync("rag.ingest", "ds1", ActivityOutcome.Succeeded, "Project A ingest", projectId: "p1");
        await recorder.RecordAsync("rag.ingest", "ds2", ActivityOutcome.Succeeded, "Project B ingest", projectId: "p2");

        vm.ProjectFilter = "p1";
        await vm.RefreshAsync();

        var row = Assert.Single(vm.Events);
        Assert.Equal("Project A ingest", row.Title);
    }
}
