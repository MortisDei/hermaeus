using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IDoctorService
{
    Task<DoctorReport> ScanAsync(CancellationToken ct = default);

    Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default);
    Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default);
    Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default);
    Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default);
    Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default);
    Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default);

    /// <summary>
    /// Installs the latest llama.cpp build honouring the configured runtime
    /// variant, with launch verification and CPU fallback, returning the
    /// superseded version directories the caller may offer to prune (r14
    /// 1.2/3.2/3.3/3.4). The default delegates to the simple update path for
    /// doubles that do not model the detailed flow.
    /// </summary>
    Task<LlamaUpdateOutcome> InstallLlamaServerUpdateDetailedAsync(IProgress<string>? progress, CancellationToken ct = default)
        => Task.FromResult(LlamaUpdateOutcome.Failed("Detailed update not supported by this service."));

    /// <summary>
    /// Deletes the given superseded version directories, returning bytes
    /// reclaimed; locked directories are skipped (r14 3.2).
    /// </summary>
    long PruneLlamaServerVersions(IReadOnlyList<string> versionDirectories) => 0;
    Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default);
    Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default);
}
