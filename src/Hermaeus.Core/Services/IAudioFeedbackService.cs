using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IAudioFeedbackService
{
    Task PublishAsync(AudioFeedbackEventKind kind, CancellationToken ct = default);
}
