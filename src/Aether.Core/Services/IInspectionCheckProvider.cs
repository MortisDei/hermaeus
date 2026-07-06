using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// A subsystem that contributes checks to the shared inspection engine.
/// Doctor, Trust, and Privacy Audit are each one provider; adding a new
/// inspection area means registering a new provider, not editing an
/// existing god-service.
/// </summary>
public interface IInspectionCheckProvider
{
    /// <summary>
    /// Views this provider's checks appear under (e.g. "doctor", "trust",
    /// "privacy"). A provider may contribute to more than one view.
    /// </summary>
    IReadOnlyList<string> Views { get; }

    Task<IReadOnlyList<InspectionCheck>> GetChecksAsync(CancellationToken ct = default);
}
