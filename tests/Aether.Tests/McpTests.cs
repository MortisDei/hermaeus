using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Mcp;

namespace Aether.Tests;

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
    private static (McpClient Client, Task ServerLoop, CancellationTokenSource ServerCts) StartFakeServer()
    {
        var clientToServer = Channel.CreateUnbounded<string>();
        var serverToClient = Channel.CreateUnbounded<string>();
        var client = McpClient.FromStreams(new ChannelTextReader(serverToClient), new ChannelTextWriter(clientToServer));

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
                        "tools/call" => BuildToolCallResponse(root),
                        _ => hasId ? new JsonObject { ["jsonrpc"] = "2.0", ["result"] = new JsonObject() } : null
                    };

                    if (response is null || !hasId) continue;
                    response["id"] = idElement.GetInt64();
                    serverToClient.Writer.TryWrite(response.ToJsonString());
                }
            }
            catch (OperationCanceledException) { }
        });

        return (client, serverLoop, serverCts);
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
        var (client, serverLoop, serverCts) = StartFakeServer();
        await client.InitializeAsync();
        var tools = await client.ListToolsAsync();

        Helpers.True(tools.Any(t => t.Name == "echo"), "fake server's tool should be discovered");

        await client.DisposeAsync();
        serverCts.Cancel();
        await Task.WhenAny(serverLoop, Task.Delay(1000));
    }

    public static async Task McpClientCallsToolAndReturnsTextContent()
    {
        var (client, serverLoop, serverCts) = StartFakeServer();
        await client.InitializeAsync();
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "hello" });

        Helpers.Equal("echo: hello", result, "tool call should return the fake server's text content");

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

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Settings { get; } = settings;
        public Task LoadAsync() => Task.CompletedTask;
        public Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null) =>
            Task.FromResult(new SettingsSaveResult(false, previousDataRootDirectory, previousDataRootDirectory, null, 0));
        public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory) =>
            new(false, previousDataRootDirectory ?? string.Empty, nextDataRootDirectory ?? string.Empty, 0, []);
        public event EventHandler? SettingsChanged { add { } remove { } }
    }
}
