namespace Aether.Core.Services;

public interface ISecretStore
{
    Task<string> StoreAsync(string name, string secret, CancellationToken ct = default);
    Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default);
    Task<string> BackendLabelAsync(CancellationToken ct = default);
    bool IsReference(string value);
}
