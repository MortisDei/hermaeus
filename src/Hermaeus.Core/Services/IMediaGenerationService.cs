using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IMediaGenerationService
{
    Task<MediaGenerationResult> GenerateAsync(MediaGenerationRequest request,
        IProgress<MediaGenerationProgress>? progress = null, CancellationToken ct = default);
}
