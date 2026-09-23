using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>Owns one identity-scoped polling session, including its first capture.</summary>
public sealed class LiveModelTelemetrySampler : IAsyncDisposable
{
    private readonly IRuntimeTelemetrySource _source;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;
    private RuntimeTelemetrySeries? _series;
    private long _generation;
    private bool _disposed;

    public event Action<RuntimeTelemetrySeries>? SeriesChanged;
    public bool IsSampling { get { lock (_gate) return _samplingTask is { IsCompleted: false }; } }
    public RuntimeTelemetrySeries? CurrentSeries { get { lock (_gate) return _series; } }

    public LiveModelTelemetrySampler(IRuntimeTelemetrySource source, TimeSpan? interval = null)
    {
        _source = source;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public async Task StartAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
    {
        var firstCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCoreAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var series = RuntimeTelemetrySeries.Start(request);
            lock (_gate)
            {
                _series = series;
                var generation = ++_generation;
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _samplingCts = cts;
                // Native probes must not execute synchronously under the UI dispatcher.
                _samplingTask = Task.Run(() => SampleLoopAsync(request, generation, cts.Token, firstCapture));
            }
        }
        finally { _lifecycle.Release(); }
        await firstCapture.Task.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            ++_generation;
            cts = _samplingCts;
            task = _samplingTask;
            _samplingCts = null;
            _samplingTask = null;
            _series = null;
        }
        if (cts is null) return;
        try
        {
            cts.Cancel();
            try { if (task is not null) await task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        }
        finally { cts.Dispose(); }
    }

    private async Task SampleLoopAsync(RuntimeTelemetryRequest request, long generation,
        CancellationToken ct, TaskCompletionSource firstCapture)
    {
        try
        {
            await CaptureOnceAsync(request, generation, ct).ConfigureAwait(false);
            firstCapture.TrySetResult();
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await CaptureOnceAsync(request, generation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            firstCapture.TrySetCanceled(ct);
        }
        catch (Exception ex)
        {
            firstCapture.TrySetException(ex);
            throw;
        }
    }

    private async Task CaptureOnceAsync(RuntimeTelemetryRequest request, long generation, CancellationToken ct)
    {
        var samples = await _source.CaptureAsync(request, ct).ConfigureAwait(false);
        RuntimeTelemetrySeries next;
        lock (_gate)
        {
            if (ct.IsCancellationRequested || generation != _generation || _series is null) return;
            next = _series.Append(samples);
            _series = next;
        }
        SeriesChanged?.Invoke(next);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }
}
