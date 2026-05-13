using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IDoctorService
{
    Task<DoctorReport> ScanAsync(CancellationToken ct = default);
}
