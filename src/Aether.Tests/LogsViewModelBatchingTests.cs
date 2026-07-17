using Aether.Core.Models;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r12 02-async-and-threading.md 2.4: LogsViewModel used to Clear+re-add the
/// whole visible list on every single log line (O(n) work per line, O(n^2)
/// for a burst), posting once per line too. It now appends incrementally and
/// coalesces a burst behind a pending-refresh flag into far fewer posts.
/// </summary>
public sealed class LogsViewModelBatchingTests
{
    [Fact]
    public void A_burst_of_added_lines_results_in_far_fewer_UI_thread_posts_than_lines()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var logs = new RuntimeLogService(settings);

        // Queue posts instead of running them inline: this simulates the real
        // scenario the fix targets (a burst of LogAdded callbacks, often from
        // a background reader thread, arriving faster than the UI thread's
        // dispatcher can drain its queue) and lets us assert the pending-flag
        // coalescing directly instead of it being masked by immediate execution.
        var sync = new QueueingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        LogsViewModel vm;
        try
        {
            vm = new LogsViewModel(logs, new RedactionService());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        const int burst = 1000;
        for (var i = 0; i < burst; i++)
            logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, $"line {i}"));

        Assert.True(sync.PostCount < burst / 10, $"expected far fewer than {burst} posts for a burst this size, got {sync.PostCount}");

        sync.DrainAll();

        Assert.Equal(burst, vm.VisibleEntries.Count);
    }

    [Fact]
    public void Filter_switching_still_rebuilds_the_full_list()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var logs = new RuntimeLogService(settings);
        var vm = new LogsViewModel(logs, new RedactionService());

        logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service, "boom"));
        logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, "fine"));

        vm.SelectedFilter = "Errors";

        Assert.Single(vm.VisibleEntries);
        Assert.Contains("boom", vm.VisibleEntries[0].Formatted);
    }
}
