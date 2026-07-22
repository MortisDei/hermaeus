using System.Globalization;

namespace Hermaeus.Services;

/// <summary>
/// Parses the round-trip ("O" format) DateTime strings every store writes
/// via DateTime.ToString("O"). Plain DateTime.Parse without
/// DateTimeStyles.RoundtripKind converts a UTC "...Z" string to Local-kind
/// on read (r11 3.4), so downstream arithmetic against DateTime.UtcNow (stale
/// windows, decay, dedupe cutoffs) silently drifts by the machine's UTC
/// offset. Every store's date parsing goes through here instead of a bespoke
/// DateTime.Parse call, so this can only be fixed once.
/// </summary>
public static class SqliteDateTime
{
    public static DateTime Parse(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTime? ParseNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Parse(value);
}
