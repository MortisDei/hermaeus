using System.Globalization;

namespace Hermaeus.Services;

/// <summary>
/// Tails the crash logs <c>Program.cs</c> writes (<c>hermaeus_unhandled.log</c>,
/// <c>hermaeus_unobserved.log</c>) to find the most recent entry, so Doctor's
/// clean-shutdown check (r19 1.3) can name the actual exception instead of
/// just "it did not exit cleanly last time" - the owner never found the file
/// that contained their crash's exact stack until this round searched the
/// disk for it directly.
/// </summary>
public static class CrashLogReader
{
    private static readonly string[] Markers = [": UNHANDLED: ", ": UNOBSERVED: "];

    public sealed record CrashLogEntry(DateTime TimestampUtc, string FirstLine);

    /// <summary>
    /// Scans one or more crash log files for the newest entry, optionally
    /// restricted to entries at or after <paramref name="afterUtc"/>. Missing
    /// or unreadable files are treated as empty; never throws.
    /// </summary>
    public static CrashLogEntry? FindNewestEntry(IEnumerable<string> filePaths, DateTime? afterUtc = null)
    {
        CrashLogEntry? newest = null;
        foreach (var path in filePaths)
        {
            foreach (var entry in ParseFile(path))
            {
                if (afterUtc is { } cutoff && entry.TimestampUtc < cutoff)
                    continue;
                if (newest is null || entry.TimestampUtc > newest.TimestampUtc)
                    newest = entry;
            }
        }
        return newest;
    }

    internal static IEnumerable<CrashLogEntry> ParseFile(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path)) yield break;
            lines = File.ReadAllLines(path);
        }
        catch
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var entry = ParseLine(line);
            if (entry is not null)
                yield return entry;
        }
    }

    /// <summary>
    /// Each crash entry begins a new physical line of the form
    /// "{timestamp}: UNHANDLED: {exception.ToString()}"; continuation lines
    /// from a multi-line stack trace do not match this shape, so only the
    /// entry's own first line (type + message) is ever returned.
    /// </summary>
    internal static CrashLogEntry? ParseLine(string line)
    {
        foreach (var marker in Markers)
        {
            var idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx <= 0) continue;
            var tsText = line[..idx];
            if (!DateTime.TryParse(tsText, CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                continue;
            var firstLine = line[(idx + marker.Length)..].Trim();
            if (firstLine.Length == 0) continue;
            return new CrashLogEntry(ts, firstLine);
        }
        return null;
    }
}
