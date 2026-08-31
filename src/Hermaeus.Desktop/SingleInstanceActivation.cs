using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Desktop;

/// <summary>
/// Local per-user activation for the already-running desktop instance. The
/// file lock remains the data-safety gate; this channel only lets a second
/// normal launch ask the owner to show and activate its existing window.
/// </summary>
internal sealed class SingleInstanceActivationServer : IDisposable
{
    private const string ActivateCommand = "activate";
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listener;

    public SingleInstanceActivationServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    public void Start(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (_listener is not null)
            throw new InvalidOperationException("The activation listener has already started.");
        _listener = ListenAsync(activate, _cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _cts.Dispose();
    }

    private async Task ListenAsync(Action activate, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    256,
                    256);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                if (string.Equals(await reader.ReadLineAsync(ct), ActivateCommand, StringComparison.Ordinal))
                    activate();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // The activation channel is an optional convenience. The file
            // lock still protects shared data if a platform cannot host it.
        }
    }
}

internal static class SingleInstanceActivationClient
{
    private const string ActivateCommand = "activate";

    public static string DefaultPipeName =>
        "hermaeus-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserName))).ToLowerInvariant()[..16];

    public static async Task<bool> TryActivateExistingAsync(
        string pipeName,
        TimeSpan timeout = default,
        CancellationToken ct = default)
    {
        if (timeout == default)
            timeout = TimeSpan.FromMilliseconds(250);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(timeout, ct);
                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync(ActivateCommand.AsMemory(), ct);
                await writer.FlushAsync(ct);
                return true;
            }
            catch (TimeoutException) when (!ct.IsCancellationRequested)
            {
            }
            catch (IOException) when (!ct.IsCancellationRequested)
            {
            }

            if (attempt < 3)
                await Task.Delay(50, ct);
        }

        return false;
    }
}
