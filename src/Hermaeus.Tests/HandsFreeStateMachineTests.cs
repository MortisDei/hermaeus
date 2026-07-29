using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r24 doc 05 5.5: hands-free state machine transitions, driven entirely by a
/// synthetic level source and explicit outcome calls - no real audio, capture, or ONNX
/// session needed, matching the doc's own testing note.</summary>
public sealed class HandsFreeStateMachineTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static HandsFreeStateMachine New(int silenceMs = 1200, int minUtteranceMs = 400, int maxUtteranceSeconds = 60) =>
        new(amplitudeThreshold: 0.02f, silenceThresholdMs: silenceMs, minUtteranceMs: minUtteranceMs, maxUtteranceSeconds: maxUtteranceSeconds);

    [Fact]
    public void Start_moves_Idle_to_Listening_and_is_a_no_op_from_any_other_state()
    {
        var sm = New();
        Assert.Equal(HandsFreeState.Idle, sm.State);

        sm.Start(T0);
        Assert.Equal(HandsFreeState.Listening, sm.State);

        sm.Start(T0.AddSeconds(1));
        Assert.Equal(HandsFreeState.Listening, sm.State);
    }

    [Fact]
    public void Stop_returns_to_Idle_from_any_state_immediately()
    {
        var sm = New();
        sm.Start(T0);
        sm.OnLevel(0.5f, T0);
        Assert.Equal(HandsFreeState.Listening, sm.State);

        sm.Stop();
        Assert.Equal(HandsFreeState.Idle, sm.State);
    }

    [Fact]
    public void Sustained_silence_after_the_minimum_utterance_length_endpoints_and_raises_the_event()
    {
        var sm = New(silenceMs: 1000, minUtteranceMs: 400);
        var raised = 0;
        sm.UtteranceEndpointed += () => raised++;
        sm.Start(T0);

        sm.OnLevel(0.5f, T0.AddMilliseconds(100));   // speech
        sm.OnLevel(0.01f, T0.AddMilliseconds(500));  // silence starts (past min utterance)
        sm.OnLevel(0.01f, T0.AddMilliseconds(1400)); // 900ms of silence - not yet
        Assert.Equal(HandsFreeState.Listening, sm.State);

        sm.OnLevel(0.01f, T0.AddMilliseconds(1600)); // 1100ms of silence - endpoint
        Assert.Equal(HandsFreeState.Transcribing, sm.State);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_breath_before_the_minimum_utterance_length_does_not_end_the_turn()
    {
        var sm = New(silenceMs: 200, minUtteranceMs: 1000);
        sm.Start(T0);

        // Silence for well over the silence threshold, but still inside minUtteranceMs.
        sm.OnLevel(0.01f, T0.AddMilliseconds(50));
        sm.OnLevel(0.01f, T0.AddMilliseconds(500));

        Assert.Equal(HandsFreeState.Listening, sm.State);
    }

    [Fact]
    public void A_noisy_room_that_never_falls_silent_still_endpoints_at_the_maximum_utterance_length()
    {
        var sm = New(silenceMs: 1200, minUtteranceMs: 400, maxUtteranceSeconds: 5);
        sm.Start(T0);

        sm.OnLevel(0.9f, T0.AddSeconds(1));
        sm.OnLevel(0.9f, T0.AddSeconds(3));
        Assert.Equal(HandsFreeState.Listening, sm.State);

        sm.OnLevel(0.9f, T0.AddSeconds(5));
        Assert.Equal(HandsFreeState.Transcribing, sm.State);
    }

    [Fact]
    public void An_empty_or_low_confidence_transcript_never_advances_to_Sending_and_returns_to_Listening()
    {
        var sm2 = New(silenceMs: 1, minUtteranceMs: 1);
        sm2.Start(T0);
        sm2.OnLevel(0.01f, T0.AddMilliseconds(5));
        sm2.OnLevel(0.01f, T0.AddMilliseconds(10));
        Assert.Equal(HandsFreeState.Transcribing, sm2.State);

        sm2.OnTranscriptReady("", isLowConfidence: false, T0.AddMilliseconds(20));
        Assert.True(sm2.State == HandsFreeState.Listening, "an empty transcript must never advance to Sending");

        sm2.OnLevel(0.01f, T0.AddMilliseconds(25));
        sm2.OnLevel(0.01f, T0.AddMilliseconds(30));
        Assert.Equal(HandsFreeState.Transcribing, sm2.State);
        sm2.OnTranscriptReady("mumble mumble", isLowConfidence: true, T0.AddMilliseconds(40));
        Assert.True(sm2.State == HandsFreeState.Listening, "a low-confidence transcript must never advance to Sending");
    }

    [Fact]
    public void A_real_transcript_advances_through_Sending_Speaking_and_back_to_Listening()
    {
        var sm = New(silenceMs: 1, minUtteranceMs: 1);
        sm.Start(T0);
        sm.OnLevel(0.01f, T0.AddMilliseconds(5));
        sm.OnLevel(0.01f, T0.AddMilliseconds(10));
        Assert.Equal(HandsFreeState.Transcribing, sm.State);

        sm.OnTranscriptReady("what's the weather", isLowConfidence: false, T0.AddMilliseconds(20));
        Assert.Equal(HandsFreeState.Sending, sm.State);

        sm.OnReplyReady(willSpeak: true, T0.AddMilliseconds(30));
        Assert.Equal(HandsFreeState.Speaking, sm.State);

        sm.OnSpeakingComplete(T0.AddSeconds(2));
        Assert.Equal(HandsFreeState.Listening, sm.State);
    }

    [Fact]
    public void A_muted_reply_skips_Speaking_and_returns_directly_to_Listening()
    {
        var sm = New(silenceMs: 1, minUtteranceMs: 1);
        sm.Start(T0);
        sm.OnLevel(0.01f, T0.AddMilliseconds(5));
        sm.OnLevel(0.01f, T0.AddMilliseconds(10));
        sm.OnTranscriptReady("hello", isLowConfidence: false, T0.AddMilliseconds(20));

        sm.OnReplyReady(willSpeak: false, T0.AddMilliseconds(30));
        Assert.Equal(HandsFreeState.Listening, sm.State);
    }

    [Fact]
    public void Level_samples_outside_Listening_are_ignored()
    {
        var sm = New();
        // Idle: never transitions on its own.
        sm.OnLevel(0.9f, T0);
        Assert.Equal(HandsFreeState.Idle, sm.State);
    }
}
