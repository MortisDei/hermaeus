using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Owns everything Aether says out loud. Consumers declare what to say,
/// which channel it belongs to, and how urgent it is; the orchestrator
/// decides voice, ordering, preemption, and playback so at most one
/// utterance ever plays at a time.
/// </summary>
public interface IVoiceOrchestrator
{
    Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default);
    void StopChannel(VoiceChannel channel);
    void StopAll();
    bool IsMuted { get; set; }
    event Action<VoiceChannel, string>? UtteranceStarted;

    /// <summary>r19 4.4: thread-safe "something is playing right now" flag driving the
    /// speak/stop icon swap; true whenever an utterance is actively being synthesized or played.</summary>
    bool IsSpeaking { get; }

    /// <summary>Fires once per utterance dequeue, on finish, stop (StopChannel/StopAll/preemption), or
    /// synthesis failure - always exactly once, from the worker loop's finally (r19 4.4).</summary>
    event Action<VoiceChannel>? UtteranceCompleted;
}
