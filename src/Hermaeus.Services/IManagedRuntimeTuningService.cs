using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public interface IManagedRuntimeTuningService
{
    Task<ServerTuneResult> RunAsync(
        ServerConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        GgufModelInfo? ggufInfo = null,
        HardwareProfile? hardware = null);
}
