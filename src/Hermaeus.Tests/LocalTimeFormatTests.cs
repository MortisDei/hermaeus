using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Turning stored UTC timestamps into what the user actually sees.
///
/// Several surfaces (agent review queue, workspace file and task labels, the
/// Doctor scan time, every runtime log line) printed raw UTC with a literal
/// " UTC" suffix, which reads as the wrong time to anyone not on UTC.
/// </summary>
public sealed class LocalTimeFormatTests
{
    [Fact]
    public void A_utc_value_is_converted_to_local()
    {
        var utc = new DateTime(2026, 7, 30, 8, 20, 0, DateTimeKind.Utc);

        Assert.Equal(utc.ToLocalTime(), LocalTimeFormat.ToLocal(utc));
    }

    /// <summary>
    /// The trap that makes a bare ToLocalTime insufficient: it is a no-op on an
    /// Unspecified-kind value, silently presenting UTC as though it were local.
    /// Every store in this app writes UTC, so that is what Unspecified means here.
    /// </summary>
    [Fact]
    public void An_unspecified_kind_value_is_treated_as_utc_not_as_local()
    {
        var unspecified = new DateTime(2026, 7, 30, 8, 20, 0, DateTimeKind.Unspecified);
        var asUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);

        Assert.Equal(asUtc.ToLocalTime(), LocalTimeFormat.ToLocal(unspecified));
        Assert.Equal(DateTimeKind.Local, LocalTimeFormat.ToLocal(unspecified).Kind);
    }

    [Fact]
    public void A_local_value_is_left_alone()
    {
        var local = new DateTime(2026, 7, 30, 18, 20, 0, DateTimeKind.Local);

        Assert.Equal(local, LocalTimeFormat.ToLocal(local));
    }

    [Fact]
    public void Formatters_render_the_local_value_and_never_say_utc()
    {
        var utc = new DateTime(2026, 7, 30, 8, 20, 56, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal(local.ToString("yyyy-MM-dd HH:mm"), LocalTimeFormat.DateTimeMinutes(utc));
        Assert.Equal(local.ToString("HH:mm:ss"), LocalTimeFormat.ClockSeconds(utc));
        Assert.DoesNotContain("UTC", LocalTimeFormat.DateTimeMinutes(utc), StringComparison.Ordinal);
    }
}
