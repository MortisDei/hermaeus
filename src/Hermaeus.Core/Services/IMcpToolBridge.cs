namespace Hermaeus.Core.Services;

/// <summary>
/// Bridges externally-supplied Model Context Protocol tools into the agent's
/// existing tool-execution seam. Tool names are namespaced "mcp:{serverId}:{toolName}"
/// so the agent's safety gate can recognize and always require approval for them.
/// </summary>
public interface IMcpToolBridge
{
    bool CanExecute(string toolName);
    Task<string> ExecuteAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default);
}
