using System.Collections.Concurrent;

namespace Hermaeus.Agent.Services;

/// <summary>
/// In-process ownership boundary for Agent work. It is deliberately small:
/// durable task state remains the source of truth, while these locks prevent
/// overlapping callbacks from operating on the same task or target.
/// </summary>
public sealed class AgentTaskCommandOwner : IAgentTaskCommandOwner
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task ExecuteTaskAsync(
        string taskId,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await ExecuteTaskAsync<object?>(taskId, async token =>
        {
            await action(token);
            return null;
        }, ct);
    }

    public async Task<T> ExecuteTaskAsync<T>(
        string taskId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task id is required.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(action);

        var gate = _taskGates.GetOrAdd(taskId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> ExecuteTargetAsync<T>(
        string workspaceRoot,
        string relativePath,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        ArgumentNullException.ThrowIfNull(action);

        var key = BuildTargetKey(workspaceRoot, relativePath);
        var gate = _targetGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildTargetKey(string workspaceRoot, string relativePath)
    {
        var root = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = string.IsNullOrWhiteSpace(relativePath)
            ? root
            : Path.GetFullPath(Path.Combine(root, relativePath));
        return target.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
