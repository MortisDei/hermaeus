namespace Hermaeus.Core.Services;

/// <summary>
/// Structured MCP call evidence. A missing IsError value means the bridge has
/// no trustworthy completion contract; response prose cannot fill that gap.
/// </summary>
public sealed record McpToolExecutionResult(string Content, bool? IsError);

/// <summary>
/// Bridges externally-supplied Model Context Protocol tools into the agent's
/// existing tool-execution seam. Tool names are namespaced "mcp:{serverId}:{toolName}"
/// so the agent's safety gate can recognize and always require approval for them.
/// </summary>
public interface IMcpToolBridge
{
    bool CanExecute(string toolName);
    Task<McpToolExecutionResult> ExecuteAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default);
}
