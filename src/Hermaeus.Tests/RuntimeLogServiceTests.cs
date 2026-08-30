using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RuntimeLogServiceTests
{
    [Fact]
    public void Repeated_persistent_failures_are_suppressed_until_state_changes()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(LogSettings(temp));
        var warning = new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Network, "server failed");

        log.Add(warning);
        log.Add(warning);
        log.Add(warning);

        Assert.Single(log.GetEntries());

        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Network, "server recovered"));

        var entries = log.GetEntries();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry => entry.Message.Contains("Suppressed 2", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Message == "server recovered");
    }

    [Fact]
    public void Low_level_slot_scheduler_chatter_is_not_persisted_but_timing_is()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(LogSettings(temp));

        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Debug, RuntimeLogCategory.Service,
            "slot get_availabl: id 0 task 1"));
        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Debug, RuntimeLogCategory.Service,
            "slot launch_slot_: id 0"));
        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Debug, RuntimeLogCategory.Service,
            "slot release: id 0"));
        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Debug, RuntimeLogCategory.Service,
            "slot print_timing: prompt 10 ms, generation 20 ms"));

        var entries = log.GetEntries();
        Assert.Single(entries);
        Assert.Contains("print_timing", entries[0].Message, StringComparison.Ordinal);
        var persisted = File.ReadAllText(log.GetLogFilePath());
        Assert.DoesNotContain("get_availabl", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("launch_slot", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("slot release", persisted, StringComparison.Ordinal);
        Assert.Contains("print_timing", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_test_settings_route_runtime_logs_to_the_temporary_data_root()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var log = new RuntimeLogService(settings);
        var expected = Path.GetFullPath(temp.PathFor("data/logs/runtime.log"));

        Assert.Equal(Path.GetFullPath(temp.PathFor("data")), SettingsService.ResolveDataRoot(settings.Settings));
        Assert.Equal(expected, log.GetLogFilePath());

        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, "isolated"));
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Rotation_prunes_old_archives_in_fifo_order()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(LogSettings(temp));
        var directory = log.GetLogDirectory();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12; i++)
        {
            var stamp = now.AddHours(-i).ToString("yyyyMMddTHHmmss'Z'");
            File.WriteAllText(Path.Combine(directory, $"runtime.{stamp}.log"), "old");
        }
        File.WriteAllBytes(log.GetLogFilePath(), new byte[1024 * 1024]);

        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, "rotate"));

        var archives = Directory.GetFiles(directory, "runtime.*.log");
        Assert.Equal(7, archives.Length);
        Assert.True(new FileInfo(log.GetLogFilePath()).Length > 0);
    }

    [Fact]
    public void Archive_retention_removes_old_entries_and_never_the_active_log()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(LogSettings(temp));
        var directory = log.GetLogDirectory();
        var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        var old = Path.Combine(directory, "runtime.20260821T000000Z.log");
        var recent = Path.Combine(directory, "runtime.20260829T000000Z.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(log.GetLogFilePath(), "active");

        RuntimeLogService.PruneArchives(directory, "runtime", ".log", 6, now);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent), string.Join(", ", Directory.GetFiles(directory).Select(Path.GetFileName)));
        Assert.True(File.Exists(log.GetLogFilePath()));
    }

    [Theory]
    [InlineData("E llama_init_from_model: Gemma4Assistant requires ctx_other to be set (this warning is normal during memory fitting)", RuntimeLogLevel.Warning)]
    [InlineData("W operator(): failed to measure the memory of the extra model, fitting without it: failed to create llama_context from model", RuntimeLogLevel.Warning)]
    [InlineData("failed to create llama_context from model", RuntimeLogLevel.Error)]
    public void Memory_fit_probe_severity_preserves_recovered_context_diagnostics(string line, RuntimeLogLevel expected)
    {
        Assert.Equal(expected, RuntimeLogClassifier.ClassifyLevel(line));
    }

    private static SettingsService LogSettings(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return settings;
    }

    [Fact]
    public void Locked_runtime_log_does_not_drop_the_in_memory_evidence_or_throw()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(LogSettings(temp));
        var path = log.GetLogFilePath();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

        var entry = new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service, "locked failure");
        log.Add(entry);

        Assert.Contains(log.GetEntries(), item => item.Message == entry.Message);
    }
}
