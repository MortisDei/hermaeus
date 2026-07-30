namespace Hermaeus.Core.Services;

/// <summary>
/// One place that turns a stored timestamp into something to show the user.
///
/// Everything in this app persists UTC, which is correct, and most display sites
/// already called ToLocalTime before formatting. A handful did not, and printed
/// the raw UTC value with a literal " UTC" suffix instead: the agent review
/// queue, workspace file and task-created labels, the Doctor scan time, and every
/// runtime log line. Those read as the wrong time to anyone not sitting on UTC,
/// which is nearly everyone.
///
/// ToLocalTime is not enough on its own: it is a no-op on a DateTime whose Kind is
/// Unspecified, silently presenting UTC as local. Anything reaching these helpers
/// with an unspecified Kind is treated as UTC, because that is what every store in
/// this app writes.
/// </summary>
public static class LocalTimeFormat
{
    /// <summary>Normalizes to local time, treating Unspecified as UTC.</summary>
    public static DateTime ToLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value,
        DateTimeKind.Utc => value.ToLocalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
    };

    /// <summary>Date and time, e.g. "2026-07-30 18:20".</summary>
    public static string DateTimeMinutes(DateTime value) =>
        ToLocal(value).ToString("yyyy-MM-dd HH:mm");

    /// <summary>Clock time only, for log lines.</summary>
    public static string ClockSeconds(DateTime value) =>
        ToLocal(value).ToString("HH:mm:ss");
}
