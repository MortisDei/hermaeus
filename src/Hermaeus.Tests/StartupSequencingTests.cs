using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 01-startup-that-never-waits.md 1.1, 1.2, 1.5 and 1.6. Startup used to
/// await six steps in strict sequence, one of which auto-started every managed
/// server one at a time, each behind a five-minute health deadline, so the chat
/// model dropdown stayed empty until a 4.2 GB model and then a separate
/// embedding server had both reported healthy in order.
/// </summary>
public sealed class StartupSequencingTests
{
    private static ServerProcessViewModel NewServer(TempDir temp, string name, int port, bool autoStart, string modelPath = "model.gguf")
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var config = new ServerConfig { Name = name, Port = port, AutoStart = autoStart, ModelPath = modelPath };
        return new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings));
    }

    // ── 1.2 Concurrent auto-start, one server per port ──────────────────────

    [Fact]
    public void Auto_start_targets_include_every_configured_server_on_its_own_port()
    {
        using var temp = new TempDir();
        var servers = new[]
        {
            NewServer(temp, "Chat", 39201, autoStart: true),
            NewServer(temp, "Embeddings", 39202, autoStart: true)
        };

        var targets = ServicesViewModel.SelectAutoStartTargets(servers);

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.Name == "Chat");
        Assert.Contains(targets, t => t.Name == "Embeddings");
    }

    [Fact]
    public void Two_servers_configured_on_the_same_port_do_not_both_start()
    {
        using var temp = new TempDir();
        var servers = new[]
        {
            NewServer(temp, "Chat", 39201, autoStart: true),
            NewServer(temp, "Chat spare", 39201, autoStart: true)
        };

        var targets = ServicesViewModel.SelectAutoStartTargets(servers);

        // Starting both concurrently could let both pass StartAsync's port
        // preflight, which assumes it is looking at settled state.
        Assert.Single(targets);
        Assert.Equal(39201, Assert.Single(targets).Port);
    }

    [Fact]
    public void A_server_that_is_not_configured_to_auto_start_is_never_a_target()
    {
        using var temp = new TempDir();
        var servers = new[]
        {
            NewServer(temp, "Manual", 39201, autoStart: false),
            NewServer(temp, "No model", 39202, autoStart: true, modelPath: "")
        };

        Assert.Empty(ServicesViewModel.SelectAutoStartTargets(servers));
    }

    [Fact]
    public void A_manual_server_does_not_shadow_an_auto_start_server_on_the_same_port()
    {
        using var temp = new TempDir();
        var servers = new[]
        {
            NewServer(temp, "Manual", 39201, autoStart: false),
            NewServer(temp, "Auto", 39201, autoStart: true)
        };

        var target = Assert.Single(ServicesViewModel.SelectAutoStartTargets(servers));
        Assert.Equal("Auto", target.Name);
    }

    // ── 1.3 support: the server records when it started starting ────────────

    [Fact]
    public void A_server_records_when_it_entered_starting_and_clears_it_on_the_way_out()
    {
        using var temp = new TempDir();
        var server = NewServer(temp, "Chat", 39201, autoStart: true);

        Assert.Null(server.StartingSinceUtc);

        server.Status = ServerStatus.Starting;
        Assert.NotNull(server.StartingSinceUtc);

        server.Status = ServerStatus.Running;
        Assert.Null(server.StartingSinceUtc);
    }

    // ── 1.5 / 5.3 Phase formatting ──────────────────────────────────────────

    [Fact]
    public void The_recorded_phase_list_formats_in_order()
    {
        var line = StartupTimingFormatter.Format(new List<StartupPhase>
        {
            new("settings", 12),
            new("stores", 85),
            new("total", 97)
        });

        Assert.Equal("Startup: settings 12 ms, stores 85 ms, total 97 ms", line);
    }

    /// <summary>
    /// 5.3: three concurrent durations overlap and no longer sum to the phase
    /// that contains them. The line must say so rather than print three numbers
    /// that read as a sequence.
    /// </summary>
    [Fact]
    public void A_concurrent_block_is_labelled_rather_than_printed_as_a_sequence()
    {
        var line = StartupTimingFormatter.Format(new List<StartupPhase>
        {
            new("stores", 90, [new StartupPhase("agent", 80), new StartupPhase("RAG datasets", 85)], ChildrenRanConcurrently: true)
        });

        Assert.Contains("stores 90 ms (concurrent: agent 80 ms, RAG datasets 85 ms)", line);
    }

    [Fact]
    public void Server_auto_start_times_are_reported_separately_from_the_startup_total()
    {
        var line = StartupTimingFormatter.FormatServerStarts(
        [
            new StartupServerStart("Chat", 41000, ReachedHealthy: true),
            new StartupServerStart("Embeddings", 300000, ReachedHealthy: false)
        ]);

        Assert.Contains("Chat healthy in 41000 ms", line);
        Assert.Contains("Embeddings did not reach healthy after 300000 ms", line);
    }

    [Fact]
    public void Nothing_configured_still_produces_a_readable_server_line()
        => Assert.Equal("Server auto-start: nothing configured", StartupTimingFormatter.FormatServerStarts([]));

    // ── 1.5 The recorder ────────────────────────────────────────────────────

    [Fact]
    public void A_recorded_breakdown_reads_back_and_a_later_server_start_is_appended()
    {
        var timing = new StartupTimingService();
        Assert.Null(timing.Last);

        timing.Record(new StartupBreakdown(DateTime.UtcNow, [new StartupPhase("settings", 10)], 10, []));
        Assert.Equal(10, timing.Last!.TotalMs);
        Assert.Empty(timing.Last.ServerStarts);

        // Auto-start is off the critical path, so its result lands after the
        // breakdown has already been recorded.
        timing.RecordServerStart(new StartupServerStart("Chat", 41000, ReachedHealthy: true));
        var start = Assert.Single(timing.Last!.ServerStarts);
        Assert.Equal("Chat", start.ServerName);
        Assert.Equal(10, timing.Last.TotalMs);
    }

    [Fact]
    public void Recording_the_same_server_twice_replaces_rather_than_duplicates_it()
    {
        var timing = new StartupTimingService();
        timing.Record(new StartupBreakdown(DateTime.UtcNow, [], 0, []));

        timing.RecordServerStart(new StartupServerStart("Chat", 100, ReachedHealthy: false));
        timing.RecordServerStart(new StartupServerStart("Chat", 41000, ReachedHealthy: true));

        var start = Assert.Single(timing.Last!.ServerStarts);
        Assert.True(start.ReachedHealthy);
        Assert.Equal(41000, start.ElapsedMs);
    }

    // ── 1.6 The IsLoading that gated nothing ────────────────────────────────

    /// <summary>
    /// 1.6: MainWindowViewModel.IsLoading was set true and false around startup
    /// and bound by nothing in any axaml file in the repository. It never gated a
    /// control, and after 1.3 a whole-application "loading" flag would be lying
    /// about panels that are already usable.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_has_no_application_wide_loading_flag()
    {
        var property = typeof(MainWindowViewModel).GetProperty("IsLoading");
        Assert.Null(property);
    }
}
