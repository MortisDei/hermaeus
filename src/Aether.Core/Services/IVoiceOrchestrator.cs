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
}
