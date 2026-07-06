using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IPrivacyAuditService
{
    Task<IReadOnlyList<PrivacyAuditItem>> ScanAsync(CancellationToken ct = default);
}
