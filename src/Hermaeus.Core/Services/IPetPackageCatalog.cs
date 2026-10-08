using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// Lists validated local ChatGPT Pet v2 packages and imports a user-selected
/// package into the managed data root. The catalog never executes package
/// content or exposes a package-controlled navigation surface.
/// </summary>
public interface IPetPackageCatalog
{
    IReadOnlyList<PetPackage> GetAvailablePackages();

    Task<PetPackageResult> ImportAsync(
        string manifestPath,
        CancellationToken cancellationToken = default);
}
