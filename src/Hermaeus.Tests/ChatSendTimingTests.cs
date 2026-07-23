using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ChatSendTimingTests
{
    [Fact]
    public void Format_accounts_for_every_stage_with_no_unlabeled_time()
    {
        var timing = new ChatSendTiming(RecallMs: 240, SelectMs: 3, LessonMs: 1, PromptBuildMs: 2, FirstTokenMs: 950, TotalMs: 1400);

        var formatted = timing.Format();

        Assert.Contains("recall 240 ms", formatted);
        Assert.Contains("select 3 ms", formatted);
        Assert.Contains("lesson 1 ms", formatted);
        Assert.Contains("rag 0 ms", formatted);
        Assert.Contains("prompt build 2 ms", formatted);
        Assert.Contains("first token 950 ms", formatted);
        Assert.Contains("total 1400 ms", formatted);
    }

    [Fact]
    public void Format_handles_all_zero_stages()
    {
        var timing = new ChatSendTiming(0, 0, 0, 0, 0, 0);

        var formatted = timing.Format();

        Assert.Equal("recall 0 ms, select 0 ms, lesson 0 ms, rag 0 ms, prompt build 0 ms, first token 0 ms, total 0 ms", formatted);
    }

    [Fact]
    public void Format_appends_server_timings_when_present()
    {
        var timing = new ChatSendTiming(0, 0, 0, 0, FirstTokenMs: 8500, TotalMs: 9000, new ChatServerTimings(17, 120.5, 229, 8000.2));

        Assert.Contains("server prompt 17 tok / 121 ms", timing.Format());
    }

    [Fact]
    public void Format_omits_server_timings_when_provider_does_not_report_them()
    {
        var timing = new ChatSendTiming(0, 0, 0, 0, FirstTokenMs: 500, TotalMs: 900);

        Assert.DoesNotContain("server", timing.Format());
    }

    [Fact]
    public void IsSlow_fires_for_a_fabricated_slow_send()
    {
        // Field report shape (r10 03-field-follow-ups.md 3.2): most stages are
        // fast, first token alone is the bulk of a 10+ second wait.
        var timing = new ChatSendTiming(RecallMs: 3000, SelectMs: 0, LessonMs: 0, PromptBuildMs: 0, FirstTokenMs: 14188, TotalMs: 14688);

        Assert.True(timing.IsSlow, "A send with 17188 ms before the first token must be flagged slow.");
        Assert.Equal(17188, timing.PreFirstTokenMs);
    }

    [Fact]
    public void IsSlow_does_not_fire_for_a_fast_send()
    {
        var timing = new ChatSendTiming(RecallMs: 240, SelectMs: 3, LessonMs: 1, PromptBuildMs: 2, FirstTokenMs: 950, TotalMs: 1400);

        Assert.False(timing.IsSlow, "A sub-second send must not be flagged slow.");
    }

    [Fact]
    public void IsSlow_boundary_is_exclusive_at_the_threshold()
    {
        var atThreshold = new ChatSendTiming(0, 0, 0, 0, FirstTokenMs: ChatSendTiming.SlowSendThresholdMs, TotalMs: ChatSendTiming.SlowSendThresholdMs);
        var overThreshold = atThreshold with { FirstTokenMs = ChatSendTiming.SlowSendThresholdMs + 1 };

        Assert.False(atThreshold.IsSlow, "Exactly at the threshold must not fire.");
        Assert.True(overThreshold.IsSlow, "One millisecond over the threshold must fire.");
    }
}
