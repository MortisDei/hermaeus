using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Desktop;

/// <summary>
/// A same-user, activation-only companion to the exclusive application lock.
/// It accepts one fixed byte, never arguments, paths, settings or commands.
/// </summary>
internal sealed class SingleInstanceActivation : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Task _worker;
    private readonly NamedPipeServerStream _server;
    private Action? _activate;
    private bool _pending;

    internal SingleInstanceActivation(string? lockPath = null)
    {
        _pipeName = PipeName(lockPath);
        _server = CreateServer();
        _worker = RunAsync();
    }

    internal void SetHandler(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        bool pending;
        lock (_gate)
        {
            _activate = activate;
            pending = _pending;
            _pending = false;
        }
        if (pending)
            activate();
    }

    internal static async Task<bool> TryRequestAsync(string? lockPath = null, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(1));
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName(lockPath), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(deadline.Token);
            await client.WriteAsync(new byte[] { 1 }, deadline.Token);
            var response = new byte[1];
            return await client.ReadAsync(response, deadline.Token) == 1 && response[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private NamedPipeServerStream CreateServer() => new(_pipeName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await _server.WaitForConnectionAsync(_lifetime.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(1));
                var request = new byte[1];
                if (await _server.ReadAsync(request, deadline.Token) == 1 && request[0] == 1)
                {
                    Action? activate;
                    lock (_gate)
                    {
                        activate = _activate;
                        if (activate is null)
                            _pending = true;
                    }
                    activate?.Invoke();
                    await _server.WriteAsync(new byte[] { 1 }, deadline.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Disconnected or stalled clients must not disable later recovery.
            }
            if (_lifetime.IsCancellationRequested)
                break;
            // EOF can mark IsConnected false while the server is still in a
            // broken connection state. Disconnect also resets that state.
            try { _server.Disconnect(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }
    }

    internal static string PipeName(string? lockPath) => "hermaeus-activate-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.UserName + "\n" + Path.GetFullPath(lockPath ?? SingleInstanceGuard.DefaultLockFilePath))));

    public void Dispose()
    {
        _lifetime.Cancel();
        _worker.GetAwaiter().GetResult();
        _server.Dispose();
        _lifetime.Dispose();
    }
}
