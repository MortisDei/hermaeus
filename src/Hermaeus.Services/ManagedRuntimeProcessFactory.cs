using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// The production construction boundary for managed runtime processes. Lab
/// owns the session lifecycle, while this factory ensures every process gets
/// the shared resource coordinator.
/// </summary>
public interface IManagedRuntimeProcessFactory
{
    ServerProcessManager Create();
}

public sealed class ManagedRuntimeProcessFactory : IManagedRuntimeProcessFactory
{
    private readonly RedactionService _redaction;
    private readonly IResourceCoordinator _resourceCoordinator;

    public ManagedRuntimeProcessFactory(
        RedactionService redaction,
        IResourceCoordinator resourceCoordinator)
    {
        _redaction = redaction;
        _resourceCoordinator = resourceCoordinator;
    }

    public ServerProcessManager Create() =>
        new(_redaction, resourceCoordinator: _resourceCoordinator);
}
