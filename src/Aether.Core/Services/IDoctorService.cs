using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IDoctorService
{
    Task<DoctorReport> ScanAsync(CancellationToken ct = default);

    Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default);
    Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default);
    Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default);
    Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default);
    Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default);
    Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default);
}
