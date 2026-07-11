using System.Collections.Concurrent;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Mcp;

public sealed class McpToolBridge : IMcpToolBridge, IAsyncDisposable
{
    private readonly ISettingsService _settings;
    private readonly ConcurrentDictionary<string, Lazy<Task<McpServerSession>>> _sessions = new();

    public McpToolBridge(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool CanExecute(string toolName)
    {
        if (!TryParse(toolName, out var serverId, out var remoteToolName))
            return false;

        var config = _settings.Settings.Mcp.Servers.FirstOrDefault(s => s.Id == serverId && s.Enabled);
        return config is not null && IsToolAllowed(config, remoteToolName);
    }

    public async Task<string> ExecuteAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default)
    {
        if (!TryParse(toolName, out var serverId, out var remoteToolName))
            throw new InvalidOperationException($"'{toolName}' is not a recognized mcp: tool reference.");

        var config = _settings.Settings.Mcp.Servers.FirstOrDefault(s => s.Id == serverId && s.Enabled)
            ?? throw new InvalidOperationException($"No enabled MCP server is configured with id '{serverId}'.");

        if (!IsToolAllowed(config, remoteToolName))
            throw new InvalidOperationException($"'{remoteToolName}' is not in the allowed-tools list configured for MCP server '{config.Name}'.");

        var session = await GetOrStartSessionAsync(serverId, ct);

        // Defense in depth beyond the configured allowlist: never forward a
        // tool name the server itself never declared via tools/list, even if
        // it happens to match the allowlist (a stale allowlist entry should
        // not turn into an arbitrary remote call).
        if (!session.Tools.Any(t => string.Equals(t.Name, remoteToolName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"MCP server '{config.Name}' does not declare a tool named '{remoteToolName}'.");

        return await session.Client.CallToolAsync(remoteToolName, arguments, ct);
    }

    private static bool IsToolAllowed(McpServerConfig config, string toolName) =>
        config.AllowedTools.Count == 0 || config.AllowedTools.Any(t => string.Equals(t, toolName, StringComparison.Ordinal));

    private static bool TryParse(string toolName, out string serverId, out string remoteToolName)
    {
        serverId = string.Empty;
        remoteToolName = string.Empty;
        var trimmed = toolName.Trim();
        if (!trimmed.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = trimmed[4..];
        var separator = rest.IndexOf(':');
        if (separator <= 0 || separator == rest.Length - 1)
            return false;

        serverId = rest[..separator];
        remoteToolName = rest[(separator + 1)..];
        return true;
    }

    private Task<McpServerSession> GetOrStartSessionAsync(string serverId, CancellationToken ct)
    {
        var lazy = _sessions.GetOrAdd(serverId, id => new Lazy<Task<McpServerSession>>(() => StartSessionAsync(id, ct)));
        return lazy.Value;
    }

    private async Task<McpServerSession> StartSessionAsync(string serverId, CancellationToken ct)
    {
        var config = _settings.Settings.Mcp.Servers.FirstOrDefault(s => s.Id == serverId && s.Enabled)
            ?? throw new InvalidOperationException($"No enabled MCP server is configured with id '{serverId}'.");

        var client = McpClient.Start(config.Command, config.Arguments, config.WorkingDirectory);
        await client.InitializeAsync(ct);
        var tools = await client.ListToolsAsync(ct);
        return new McpServerSession(client, tools);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _sessions.Values)
        {
            if (!lazy.IsValueCreated) continue;
            try
            {
                var session = await lazy.Value;
                await session.Client.DisposeAsync();
            }
            catch { }
        }

        _sessions.Clear();
    }
}

internal sealed class McpServerSession
{
    public McpServerSession(McpClient client, IReadOnlyList<McpTool> tools)
    {
        Client = client;
        Tools = tools;
    }

    public McpClient Client { get; }
    public IReadOnlyList<McpTool> Tools { get; }
}
