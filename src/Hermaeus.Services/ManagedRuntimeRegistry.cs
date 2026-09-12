using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Owns the process manager for each configured managed server. The stable
/// server id is the lifetime key, so rebuilding a Services projection does
/// not create a second manager for the same authoritative runtime.
/// </summary>
public sealed class ManagedRuntimeRegistry : IDisposable
{
    private readonly IManagedRuntimeProcessFactory _factory;
    private readonly object _gate = new();
    private readonly Dictionary<string, ServerProcessManager> _managers = new(StringComparer.Ordinal);
    private int _disposed;

    public ManagedRuntimeRegistry(IManagedRuntimeProcessFactory factory)
    {
        _factory = factory;
    }

    public ServerProcessManager GetOrCreate(string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_managers.TryGetValue(serverId, out var existing))
                return existing;

            var manager = _factory.Create();
            _managers.Add(serverId, manager);
            return manager;
        }
    }

    public void Release(string serverId, ServerProcessManager manager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(manager);

        var shouldDispose = false;
        lock (_gate)
        {
            if (_managers.TryGetValue(serverId, out var current)
                && ReferenceEquals(current, manager))
            {
                _managers.Remove(serverId);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
            manager.Dispose();
    }

    public void Dispose()
    {
        ServerProcessManager[] managers;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            managers = _managers.Values.ToArray();
            _managers.Clear();
        }

        foreach (var manager in managers)
            manager.Dispose();
    }
}
