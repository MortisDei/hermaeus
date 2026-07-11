using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aether.Mcp;

/// <summary>
/// A minimal MCP client speaking JSON-RPC 2.0 over the stdio of a locally
/// spawned server process. Messages are newline-delimited JSON objects, per
/// the MCP stdio transport. This client only implements the handshake plus
/// tools/list and tools/call, which is all the agent tool bridge needs.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    private readonly Process? _process;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _readLoop;
    private readonly Task? _stderrDrain;
    private readonly CancellationTokenSource _readLoopCts = new();
    private readonly StringBuilder _stderrTail = new();
    private readonly object _stderrLock = new();
    private const int MaxStderrTailChars = 4096;
    private long _nextId;
    private bool _disposed;
    private volatile bool _serverClosed;

    private McpClient(TextReader input, TextWriter output, Process? process, TextReader? stderr = null)
    {
        _input = input;
        _output = output;
        _process = process;
        _readLoop = Task.Run(ReadLoopAsync);
        var stderrReader = stderr ?? process?.StandardError;
        _stderrDrain = stderrReader is null ? null : Task.Run(() => DrainStderrAsync(stderrReader, _readLoopCts.Token));
    }

    /// <summary>
    /// Last few KB the server wrote to stderr. Chatty MCP servers (common for
    /// Node/Python implementations) can otherwise block on an unread stderr
    /// pipe once the OS buffer fills, hanging every in-flight call; draining
    /// it continuously avoids that deadlock and gives failures useful context
    /// (docs/review/01-code-audit.md P2-2).
    /// </summary>
    public string StderrTail
    {
        get { lock (_stderrLock) return _stderrTail.ToString(); }
    }

    private async Task DrainStderrAsync(TextReader stderr, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await stderr.ReadLineAsync(ct);
                if (line is null) break;

                lock (_stderrLock)
                {
                    _stderrTail.AppendLine(line);
                    if (_stderrTail.Length > MaxStderrTailChars)
                        _stderrTail.Remove(0, _stderrTail.Length - MaxStderrTailChars);
                }
            }
        }
        catch { }
    }

    public static McpClient Start(string command, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start MCP server '{command}'.");
        return new McpClient(process.StandardOutput, process.StandardInput, process);
    }

    /// <summary>
    /// Wires the client directly to a pair of streams instead of spawning a
    /// process. Exists so tests can exercise the JSON-RPC framing and
    /// handshake logic against an in-memory fake server. An optional stderr
    /// reader lets tests simulate a chatty server to exercise the stderr
    /// drain (docs/review/01-code-audit.md P2-2).
    /// </summary>
    public static McpClient FromStreams(TextReader input, TextWriter output, TextReader? stderr = null) => new(input, output, process: null, stderr);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var initParams = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "Aether", ["version"] = "1.0" }
        };
        await SendRequestAsync("initialize", initParams, ct);
        await SendNotificationAsync("notifications/initialized", new JsonObject(), ct);
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        var result = await SendRequestAsync("tools/list", new JsonObject(), ct);
        var tools = new List<McpTool>();
        if (result.TryGetProperty("tools", out var toolsArray) && toolsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                var name = tool.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var description = tool.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                    tools.Add(new McpTool(name, description));
            }
        }

        return tools;
    }

    public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        var argsNode = new JsonObject();
        foreach (var (key, value) in arguments)
            argsNode[key] = ToJsonNode(value);

        var callParams = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = argsNode
        };

        var result = await SendRequestAsync("tools/call", callParams, ct);
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var texts = content.EnumerateArray()
                .Where(part => part.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(part => part.TryGetProperty("text", out var txt) ? txt.GetString() ?? string.Empty : string.Empty);
            return string.Join("\n", texts);
        }

        return result.GetRawText();
    }

    /// <summary>
    /// Maps a tool argument by its runtime CLR type instead of always calling
    /// <c>ToString()</c>, so a server's declared JSON schema (integer,
    /// boolean, object) receives the matching JSON type rather than a string
    /// (docs/review/01-code-audit.md P2-1).
    /// </summary>
    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => element.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(element.GetRawText()),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        _ => JsonSerializer.SerializeToNode(value, McpJson.Options)
    };

    private async Task<JsonElement> SendRequestAsync(string method, JsonNode paramsNode, CancellationToken ct)
    {
        if (_serverClosed)
            throw new InvalidOperationException(BuildClosedMessage());

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = paramsNode
        };
        await WriteLineAsync(message, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        await using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(string method, JsonNode paramsNode, CancellationToken ct)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = paramsNode
        };
        await WriteLineAsync(message, ct);
    }

    private async Task WriteLineAsync(JsonNode message, CancellationToken ct)
    {
        var json = message.ToJsonString(McpJson.Options);
        await _output.WriteLineAsync(json.AsMemory(), ct);
        await _output.FlushAsync(ct);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_readLoopCts.IsCancellationRequested)
            {
                var line = await _input.ReadLineAsync(_readLoopCts.Token);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch { continue; }

                using (doc)
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
                        continue;

                    var id = idElement.GetInt64();
                    if (!_pending.TryGetValue(id, out var tcs)) continue;

                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var m) ? m.GetString() : "MCP server returned an error.";
                        tcs.TrySetException(new InvalidOperationException(message));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        tcs.TrySetResult(result.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            // The server closed stdout (exited or crashed): fault every
            // outstanding and future call immediately instead of leaving
            // them to hang for the full 30-second timeout
            // (docs/review/01-code-audit.md P2-3).
            _serverClosed = true;
            var message = BuildClosedMessage();
            foreach (var id in _pending.Keys.ToList())
            {
                if (_pending.TryRemove(id, out var tcs))
                    tcs.TrySetException(new InvalidOperationException(message));
            }
        }
    }

    private string BuildClosedMessage()
    {
        var tail = StderrTail;
        return string.IsNullOrWhiteSpace(tail)
            ? "MCP server closed the connection."
            : $"MCP server closed the connection. Last stderr output:\n{tail}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _readLoopCts.Cancel();
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();

        try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { }

        if (_stderrDrain is not null)
        {
            try { await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }

        _readLoopCts.Dispose();
        _process?.Dispose();
    }
}
