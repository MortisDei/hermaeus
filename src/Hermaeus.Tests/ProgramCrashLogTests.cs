using Xunit;

namespace Hermaeus.Tests;

public sealed class ProgramCrashLogTests
{
    [Fact]
    public void Crash_logs_are_separate_bounded_and_archived_at_the_size_limit()
    {
        using var temp = new TempDir();
        var unhandled = temp.PathFor("logs/hermaeus_unhandled.log");
        var unobserved = temp.PathFor("logs/hermaeus_unobserved.log");
        Directory.CreateDirectory(Path.GetDirectoryName(unhandled)!);

        Hermaeus.Desktop.Program.AppendCrashLogAtPath(unhandled, "UNHANDLED", new string('x', 140_000));
        Hermaeus.Desktop.Program.AppendCrashLogAtPath(unobserved, "UNOBSERVED", "task failure");
        Hermaeus.Desktop.Program.AppendCrashLogAtPath(unhandled, "UNHANDLED", "second failure");

        var unhandledText = File.ReadAllText(unhandled);
        var unobservedText = File.ReadAllText(unobserved);

        Assert.Contains("UNHANDLED", unhandledText, StringComparison.Ordinal);
        Assert.Contains("[crash detail truncated]", unhandledText, StringComparison.Ordinal);
        Assert.DoesNotContain("UNOBSERVED", unhandledText, StringComparison.Ordinal);
        Assert.Contains("UNOBSERVED", unobservedText, StringComparison.Ordinal);
        Assert.DoesNotContain("UNHANDLED", unobservedText, StringComparison.Ordinal);
        Assert.True(new FileInfo(unhandled).Length <= 512 * 1024);
        Assert.True(new FileInfo(unobserved).Length <= 512 * 1024);

        File.WriteAllBytes(unhandled, new byte[512 * 1024]);
        Hermaeus.Desktop.Program.AppendCrashLogAtPath(unhandled, "UNHANDLED", "rotated");

        Assert.True(File.Exists(unhandled + ".previous"));
        Assert.Contains("rotated", File.ReadAllText(unhandled), StringComparison.Ordinal);
        Assert.Equal(512 * 1024, new FileInfo(unhandled + ".previous").Length);
    }
}
