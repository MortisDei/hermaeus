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
        var log = new RuntimeLogService(NewSettings(temp));
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
    public void Rotation_prunes_old_archives_in_fifo_order()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(NewSettings(temp));
        var directory = log.GetLogDirectory();
        for (var i = 0; i < 12; i++)
            File.WriteAllText(Path.Combine(directory, $"runtime.20260101T000{i:00}00Z.log"), "old");
        File.WriteAllBytes(log.GetLogFilePath(), new byte[10 * 1024 * 1024]);

        log.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, "rotate"));

        var archives = Directory.GetFiles(directory, "runtime.*.log");
        Assert.Equal(10, archives.Length);
        Assert.True(new FileInfo(log.GetLogFilePath()).Length > 0);
    }

    [Fact]
    public void Locked_runtime_log_does_not_drop_the_in_memory_evidence_or_throw()
    {
        using var temp = new TempDir();
        var log = new RuntimeLogService(NewSettings(temp));
        var path = log.GetLogFilePath();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

        var entry = new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service, "locked failure");
        log.Add(entry);

        Assert.Contains(log.GetEntries(), item => item.Message == entry.Message);
    }
}
