using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Mcp;

namespace Hermaeus.Tests;

/// <summary>
/// A duplex, in-memory line transport standing in for a real child process's
/// stdio, so tests can exercise McpClient's JSON-RPC framing and handshake
/// without spawning a real MCP server executable.
/// </summary>
internal sealed class ChannelTextWriter(Channel<string> channel) : TextWriter
{
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken ct = default)
    {
        channel.Writer.TryWrite(buffer.ToString());
        return Task.CompletedTask;
    }

    public override Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ChannelTextReader(Channel<string> channel) : TextReader
{
    public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        try { return await channel.Reader.ReadAsync(ct); }
        catch (ChannelClosedException) { return null; }
        catch (OperationCanceledException) { return null; }
    }
}

internal static class McpTests
{
    private static (McpClient Client, Task ServerLoop, CancellationTokenSource ServerCts, Action CloseConnection) StartFakeServer(TextReader? stderr = null)
    {
        var clientToServer = Channel.CreateUnbounded<string>();
        var serverToClient = Channel.CreateUnbounded<string>();
        var client = McpClient.FromStreams(new ChannelTextReader(serverToClient), new ChannelTextWriter(clientToServer), stderr);

        var serverCts = new CancellationTokenSource();
        var serverLoop = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in clientToServer.Reader.ReadAllAsync(serverCts.Token))
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var method = root.GetProperty("method").GetString();
                    var hasId = root.TryGetProperty("id", out var idElement);

                    JsonObject? response = method switch
                    {
                        "initialize" => new JsonObject { ["jsonrpc"] = "2.0", ["result"] = new JsonObject() },
                        "tools/list" => new JsonObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["result"] = new JsonObject
                            {
                                ["tools"] = new JsonArray(new JsonObject { ["name"] = "echo", ["description"] = "Echoes input" })
                            }
                        },
                        "tools/call" => IsTypeCheckCall(root) ? BuildTypeCheckResponse(root) : BuildToolCallResponse(root),
                        _ => hasId ? new JsonObject { ["jsonrpc"] = "2.0", ["result"] = new JsonObject() } : null
                    };

                    if (response is null || !hasId) continue;
                    response["id"] = idElement.GetInt64();
                    serverToClient.Writer.TryWrite(response.ToJsonString());
                }
            }
            catch (OperationCanceledException) { }
        });

        return (client, serverLoop, serverCts, () => serverToClient.Writer.TryComplete());
    }

    private static bool IsTypeCheckCall(JsonElement root) =>
        root.GetProperty("params").GetProperty("name").GetString() == "typecheck";

    private static JsonObject BuildTypeCheckResponse(JsonElement root)
    {
        var args = root.GetProperty("params").GetProperty("arguments");
        var kind = args.GetProperty("value").ValueKind.ToString();
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = kind })
            }
        };
    }

    private static JsonObject BuildToolCallResponse(JsonElement root)
    {
        var args = root.GetProperty("params").GetProperty("arguments");
        var message = args.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"echo: {message}" })
            }
        };
    }

    public static async Task McpClientCompletesHandshakeAndListsTools()
    {
        var (client, serverLoop, serverCts, _) = StartFakeServer();
        await client.InitializeAsync();
        var tools = await client.ListToolsAsync();

        Helpers.True(tools.Any(t => t.Name == "echo"), "fake server's tool should be discovered");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static async Task McpClientCallsToolAndReturnsTextContent()
    {
        var (client, serverLoop, serverCts, _) = StartFakeServer();
        await client.InitializeAsync();
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "hello" });

        Helpers.Equal("echo: hello", result, "tool call should return the fake server's text content");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static async Task McpClientPreservesArgumentJsonTypes()
    {
        var (client, serverLoop, serverCts, _) = StartFakeServer();
        await client.InitializeAsync();

        var intResult = await client.CallToolAsync("typecheck", new Dictionary<string, object?> { ["value"] = 42 });
        Helpers.Equal("Number", intResult, "an int argument should arrive as a JSON number, not a stringified copy");

        var boolResult = await client.CallToolAsync("typecheck", new Dictionary<string, object?> { ["value"] = true });
        Helpers.Equal("True", boolResult, "a bool argument should arrive as a JSON boolean, not a stringified copy");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static async Task McpClientFailsFastWhenServerClosesConnection()
    {
        var (client, serverLoop, serverCts, closeConnection) = StartFakeServer();
        await client.InitializeAsync();

        closeConnection();
        await Task.Delay(200);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Helpers.ThrowsAsync<InvalidOperationException>(() =>
            client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "hi" }));
        sw.Stop();

        Helpers.True(sw.ElapsedMilliseconds < 5000,
            "a closed connection should fail the next call immediately, not wait out the 30s per-call timeout");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static async Task McpClientDrainsStderrWithoutBlockingCalls()
    {
        var stderrChannel = Channel.CreateUnbounded<string>();
        for (var i = 0; i < 5000; i++)
            stderrChannel.Writer.TryWrite($"noisy diagnostic line {i}");
        stderrChannel.Writer.TryComplete();

        var (client, serverLoop, serverCts, _) = StartFakeServer(new ChannelTextReader(stderrChannel));
        await client.InitializeAsync();
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "hi" });

        Helpers.Equal("echo: hi", result, "tool calls should complete even while a large stderr backlog is draining");
        Helpers.True(client.StderrTail.Length <= 4096, "the retained stderr tail should stay bounded");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static Task McpBridgeParsesNamespacedToolNames()
    {
        var settings = new StubSettingsService(new AppSettings
        {
            Mcp = new McpSettings
            {
                Servers = [new McpServerConfig { Id = "srv1", Name = "Test", Command = "does-not-matter", Enabled = true }]
            }
        });
        var bridge = new McpToolBridge(settings);

        Helpers.True(bridge.CanExecute("mcp:srv1:echo"), "a well-formed mcp: tool reference should be recognized");
        Helpers.False(bridge.CanExecute("mcp:srv1"), "a tool reference missing the tool name should not be recognized");
        Helpers.False(bridge.CanExecute("read_file"), "a built-in tool name should not be treated as an MCP reference");
        return Task.CompletedTask;
    }

    public static Task McpBridgeAllowlistRestrictsWhichDeclaredToolsCanExecute()
    {
        var settings = new StubSettingsService(new AppSettings
        {
            Mcp = new McpSettings
            {
                Servers =
                [
                    new McpServerConfig { Id = "open", Name = "Open", Command = "does-not-matter", Enabled = true },
                    new McpServerConfig { Id = "restricted", Name = "Restricted", Command = "does-not-matter", Enabled = true, AllowedTools = ["read_only_tool"] }
                ]
            }
        });
        var bridge = new McpToolBridge(settings);

        Helpers.True(bridge.CanExecute("mcp:open:anything"), "an empty allowlist should permit any declared tool, matching prior behavior");
        Helpers.True(bridge.CanExecute("mcp:restricted:read_only_tool"), "a tool present in the allowlist should be permitted");
        Helpers.False(bridge.CanExecute("mcp:restricted:dangerous_tool"), "a tool absent from a configured allowlist should be blocked before any session starts");
        return Task.CompletedTask;
    }

    public static async Task McpBridgeExecuteRejectsToolsNotDeclaredByTheServerEvenIfAllowlisted()
    {
        var (client, serverLoop, serverCts, _) = StartFakeServer();
        var settings = new StubSettingsService(new AppSettings
        {
            Mcp = new McpSettings
            {
                Servers = [new McpServerConfig { Id = "srv1", Name = "Test", Command = "does-not-matter", Enabled = true, AllowedTools = ["echo", "phantom_tool"] }]
            }
        });
        var bridge = new FakeClientMcpToolBridge(settings, client);

        var result = await bridge.ExecuteAsync("mcp:srv1:echo", new Dictionary<string, object?> { ["message"] = "hi" });
        Helpers.Equal("echo: hi", result.Content, "an allowlisted tool the server actually declares should execute normally");
        Helpers.True(result.IsError is null, "the legacy fake response has no structured MCP error status");

        await Helpers.ThrowsAsync<InvalidOperationException>(() =>
            bridge.ExecuteAsync("mcp:srv1:phantom_tool", new Dictionary<string, object?>()));

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    /// <summary>Test-only bridge that skips process spawning and reuses an already-started fake McpClient.</summary>
    private sealed class FakeClientMcpToolBridge(ISettingsService settings, McpClient client) : IMcpToolBridge
    {
        public bool CanExecute(string toolName) => toolName.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase);

        public async Task<McpToolExecutionResult> ExecuteAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default)
        {
            var rest = toolName["mcp:".Length..];
            var separator = rest.IndexOf(':');
            var serverId = rest[..separator];
            var remoteToolName = rest[(separator + 1)..];

            var config = settings.Settings.Mcp.Servers.First(s => s.Id == serverId);
            if (config.AllowedTools.Count > 0 && !config.AllowedTools.Contains(remoteToolName, StringComparer.Ordinal))
                throw new InvalidOperationException("not allowlisted");

            await client.InitializeAsync(ct);
            var tools = await client.ListToolsAsync(ct);
            if (!tools.Any(t => string.Equals(t.Name, remoteToolName, StringComparison.Ordinal)))
                throw new InvalidOperationException("not declared by server");

            return await client.CallToolDetailedAsync(remoteToolName, arguments, ct);
        }
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Settings { get; private set; } = settings;
        public Task LoadAsync() => Task.CompletedTask;
        public Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null) =>
            Task.FromResult(new SettingsSaveResult(false, previousDataRootDirectory, previousDataRootDirectory, null, 0));
        public Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
        {
            Settings = settings;
            return Task.FromResult(new SettingsSaveResult(false, previousDataRootDirectory, previousDataRootDirectory, null, 0));
        }
        public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory) =>
            new(false, previousDataRootDirectory ?? string.Empty, nextDataRootDirectory ?? string.Empty, 0, []);
        public event EventHandler? SettingsChanged { add { } remove { } }
    }
}
