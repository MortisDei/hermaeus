using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LiveModelTelemetrySamplerTests
{
    [Fact]
    public async Task Start_captures_once_and_stop_releases_sampling()
    {
        var source = new CapturingSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));

        await sampler.StartAsync(Request("one"));

        Assert.True(sampler.IsSampling);
        Assert.Single(sampler.CurrentSeries!.Samples);
        await sampler.StopAsync();
        Assert.False(sampler.IsSampling);
    }

    [Fact]
    public async Task Starting_a_new_identity_resets_the_bounded_series()
    {
        var source = new CapturingSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));

        await sampler.StartAsync(Request("one"));
        await sampler.StartAsync(Request("two"));

        Assert.Equal("two", sampler.CurrentSeries!.Fingerprint.Model.ManifestIdentity);
        Assert.Single(sampler.CurrentSeries.Samples);
        Assert.All(sampler.CurrentSeries.Samples, sample => Assert.Equal(sampler.CurrentSeries.ProcessInstanceId, sample.ProcessInstanceId));
    }

    [Fact]
    public async Task Telemetry_flyout_keeps_unknown_process_vram_and_exposes_its_evidence_reason()
    {
        await using var viewModel = new LiveModelTelemetryViewModel(new LiveModelTelemetrySampler(
            new GpuUnknownSource(), TimeSpan.FromHours(1)));

        await viewModel.OpenAsync(Request("one"));

        Assert.Equal("Unknown", viewModel.ProcessGpuMemory);
        Assert.Contains("process-gpu-unavailable", viewModel.ProcessGpuMemoryDetail, StringComparison.Ordinal);
        Assert.Contains("No trustworthy", viewModel.ProcessGpuMemoryDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_joins_first_capture_and_discards_late_result()
    {
        var source = new DelayedSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));
        var notifications = 0;
        sampler.SeriesChanged += _ => notifications++;
        var start = sampler.StartAsync(Request("old"));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = sampler.StopAsync();
        Assert.False(stop.IsCompleted);
        source.Release.SetResult();
        await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, notifications);
        Assert.Null(sampler.CurrentSeries);
        Assert.False(sampler.IsSampling);
    }

    [Fact]
    public async Task Concurrent_restart_waits_for_previous_capture_and_owns_new_series()
    {
        var source = new DelayedSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));
        var first = sampler.StartAsync(Request("old"));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = sampler.StartAsync(Request("new"));
        Assert.False(second.IsCompleted);
        source.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("new", sampler.CurrentSeries!.Fingerprint.Model.ManifestIdentity);
        Assert.Single(sampler.CurrentSeries.Samples);
        Assert.Equal(1, source.MaximumConcurrent);
    }

    [Fact]
    public async Task Closing_during_first_capture_does_not_repopulate_view()
    {
        var source = new DelayedSource();
        await using var vm = new LiveModelTelemetryViewModel(
            new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1)));
        var open = vm.OpenAsync(Request("old"));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = vm.CloseAsync();
        source.Release.SetResult();
        await Task.WhenAll(open, close).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Samples);
        Assert.Equal("Telemetry is closed.", vm.Status);
        Assert.Equal("Unknown", vm.ProcessRam);
    }

    [Fact]
    public async Task Disposal_prevents_restarting_polling()
    {
        var sampler = new LiveModelTelemetrySampler(new CapturingSource());
        await sampler.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sampler.StartAsync(Request("one")));
    }

    [Fact]
    public async Task Queued_dispatcher_callback_cannot_repopulate_closed_telemetry()
    {
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        LiveModelTelemetryViewModel vm;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            vm = new LiveModelTelemetryViewModel(new LiveModelTelemetrySampler(new CapturingSource(), TimeSpan.FromHours(1)));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await using (vm)
        {
            var opening = vm.OpenAsync(Request("one"));
            // SeriesChanged plus OpenAsync's completion publication both queue.
            await context.TwoCallbacks.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await vm.CloseAsync();
            context.Drain();
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.IsOpen);
            Assert.Empty(vm.Samples);
            Assert.Equal("Telemetry is closed.", vm.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_command_cancels_pending_identity_lookup_even_if_factory_ignores_token(bool returnsRequest)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        await using var telemetry = new LiveModelTelemetryViewModel(
            new LiveModelTelemetrySampler(new CapturingSource(), TimeSpan.FromHours(1)));
        var vm = new ChatViewModel(
            new CapturingLlm(), new ThrowingSaveConversationStore(),
            new MemoryStore(settings), settings, new FakeTts(),
            new ModelProfileService(settings), new FakeToasts(),
            new FakeConversationMemoryService(), new RuntimeLogService(settings),
            new ConversationExportService(), telemetry: telemetry);
        var result = new TaskCompletionSource<RuntimeTelemetryRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        vm.ManagedTelemetryRequestFactory = (_, ct) =>
        {
            received = ct;
            return result.Task;
        };
        var opening = vm.OpenTelemetryCommand.ExecuteAsync(null);
        await vm.CloseTelemetryCommand.ExecuteAsync(null);
        Assert.True(received.IsCancellationRequested);
        result.SetResult(returnsRequest ? Request("late") : null);
        await opening.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(telemetry.IsOpen);
        Assert.Empty(telemetry.Samples);
        Assert.Equal("Telemetry is closed.", telemetry.Status);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private int _posted;
        public TaskCompletionSource TwoCallbacks { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
            if (Interlocked.Increment(ref _posted) == 2) TwoCallbacks.TrySetResult();
        }
        public void Drain()
        {
            while (_callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    private sealed class DelayedSource : IRuntimeTelemetrySource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public int MaximumConcurrent { get; private set; }
        public async Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
        {
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            Entered.TrySetResult();
            // Deliberately ignore cancellation, like an already-entered native call.
            await Release.Task;
            Interlocked.Decrement(ref _active);
            return await new CapturingSource().CaptureAsync(request);
        }
    }

    private static RuntimeTelemetryRequest Request(string modelId)
    {
        var runtime = new RuntimeIdentityV2("test", "runtime", null, null, "1", "build", "compiler", "cpu", "", IdentityCompleteness.Complete);
        var model = new ModelIdentityV2(modelId, "model", null, null, "arch", "q", "", ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var hardware = new HardwareIdentityV2("test", "x64", "cpu", "device", null, null, "", "", IdentityCompleteness.Complete);
        var config = new ConfigurationIdentityV2(4096, 0, "cpu", 4, null, 1, null, null, "f16", "f16", "off", "none", "", "", null, new Dictionary<string, string>(), IdentityCompleteness.Complete);
        return new RuntimeTelemetryRequest("series", 1, DateTime.UnixEpoch, runtime, new EmpiricalProfileFingerprintV2(runtime, model, hardware, config));
    }

    private sealed class CapturingSource : IRuntimeTelemetrySource
    {
        public Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
        {
            var instance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
            return Task.FromResult<IReadOnlyList<RuntimeTelemetrySample>>([
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 123,
                    RuntimeTelemetrySourceKind.ProcessCounter, RuntimeTelemetryTrustState.ProcessScoped,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "test", "test")
            ]);
        }
    }

    private sealed class GpuUnknownSource : IRuntimeTelemetrySource
    {
        public Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
        {
            var instance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
            return Task.FromResult<IReadOnlyList<RuntimeTelemetrySample>>([
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 123,
                    RuntimeTelemetrySourceKind.ProcessCounter, RuntimeTelemetryTrustState.ProcessScoped,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "process-working-set", "RAM"),
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessGpuMemoryBytes, null,
                    RuntimeTelemetrySourceKind.Unknown, RuntimeTelemetryTrustState.Unknown,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "process-gpu-unavailable",
                    "No trustworthy per-process GPU memory counter is available from this source.")
            ]);
        }
    }
}
