using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Bounded, identity-scoped sampler shared by GPU Fit and live telemetry.
/// It owns the timer and delegates all platform measurement to the existing
/// <see cref="IRuntimeTelemetrySource"/>.
/// </summary>
public sealed class LiveModelTelemetrySampler : IAsyncDisposable
{
    private readonly IRuntimeTelemetrySource _source;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _samplingCts;
    private Task? _samplingTask;
    private RuntimeTelemetryRequest? _request;
    private RuntimeTelemetrySeries? _series;

    public event Action<RuntimeTelemetrySeries>? SeriesChanged;
    public bool IsSampling { get { lock (_gate) return _samplingCts is not null; } }
    public RuntimeTelemetrySeries? CurrentSeries { get { lock (_gate) return _series; } }

    public LiveModelTelemetrySampler(IRuntimeTelemetrySource source, TimeSpan? interval = null)
    {
        _source = source;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public async Task StartAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
    {
        await StopAsync();
        var series = RuntimeTelemetrySeries.Start(request);
        lock (_gate)
        {
            _request = request;
            _series = series;
            _samplingCts = new CancellationTokenSource();
            _samplingTask = SampleLoopAsync(_samplingCts.Token);
        }
        await CaptureOnceAsync(ct);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            cts = _samplingCts;
            task = _samplingTask;
            _samplingCts = null;
            _samplingTask = null;
            _request = null;
        }
        if (cts is null) return;
        cts.Cancel();
        try { if (task is not null) await task; } catch (OperationCanceledException) { }
        cts.Dispose();
    }

    private async Task SampleLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(ct))
            await CaptureOnceAsync(ct);
    }

    private async Task CaptureOnceAsync(CancellationToken ct)
    {
        RuntimeTelemetryRequest? request;
        lock (_gate) request = _request;
        if (request is null) return;

        var samples = await _source.CaptureAsync(request, ct);
        RuntimeTelemetrySeries? next;
        lock (_gate)
        {
            if (_series is null) return;
            next = _series.Append(samples);
            _series = next;
        }
        SeriesChanged?.Invoke(next);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
