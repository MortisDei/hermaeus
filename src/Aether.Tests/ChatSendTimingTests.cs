using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

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
        Assert.Contains("prompt build 2 ms", formatted);
        Assert.Contains("first token 950 ms", formatted);
        Assert.Contains("total 1400 ms", formatted);
    }

    [Fact]
    public void Format_handles_all_zero_stages()
    {
        var timing = new ChatSendTiming(0, 0, 0, 0, 0, 0);

        var formatted = timing.Format();

        Assert.Equal("recall 0 ms, select 0 ms, lesson 0 ms, prompt build 0 ms, first token 0 ms, total 0 ms", formatted);
    }
}
