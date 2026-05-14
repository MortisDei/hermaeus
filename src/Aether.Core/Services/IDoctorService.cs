using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IDoctorService
{
    Task<DoctorReport> ScanAsync(CancellationToken ct = default);

    Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default);
    Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default);
}
