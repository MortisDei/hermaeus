using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r19 1.3: tail Program.cs's crash logs so Doctor can name the exception that killed the previous session.</summary>
public sealed class CrashLogReaderTests
{
    [Fact]
    public void FindNewestEntry_returns_the_most_recent_of_several_appended_entries()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("hermaeus_unhandled.log");
        var earlier = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(path,
            $"{earlier}: UNHANDLED: System.InvalidOperationException: first\n   at Foo.Bar()\n" +
            $"{later}: UNHANDLED: System.ArgumentNullException: second (Parameter 'source')\n   at Baz.Qux()\n");

        var entry = CrashLogReader.FindNewestEntry([path]);

        Assert.NotNull(entry);
        Assert.StartsWith("System.ArgumentNullException: second", entry!.FirstLine);
    }

    [Fact]
    public void FindNewestEntry_ignores_entries_at_or_before_the_cutoff()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("hermaeus_unhandled.log");
        var oldTimestamp = DateTime.UtcNow.AddDays(-1);
        File.WriteAllText(path, $"{oldTimestamp}: UNHANDLED: System.Exception: stale\n   at Foo.Bar()\n");

        var entry = CrashLogReader.FindNewestEntry([path], afterUtc: DateTime.UtcNow);

        Assert.Null(entry);
    }

    [Fact]
    public void FindNewestEntry_returns_null_for_a_missing_file()
    {
        var entry = CrashLogReader.FindNewestEntry([Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log")]);
        Assert.Null(entry);
    }

    [Fact]
    public void ParseLine_extracts_only_the_first_line_of_a_multiline_exception()
    {
        var ts = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var entry = CrashLogReader.ParseLine($"{ts}: UNOBSERVED: System.Exception: boom");
        Assert.NotNull(entry);
        Assert.Equal("System.Exception: boom", entry!.FirstLine);
    }

    [Fact]
    public void ParseLine_returns_null_for_a_stack_trace_continuation_line()
    {
        Assert.Null(CrashLogReader.ParseLine("   at System.Linq.Enumerable.Take(...)"));
    }
}
