using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Voice;

namespace Hermaeus.Services;

public sealed class AudioFeedbackService : IAudioFeedbackService, IAsyncDisposable
{
    private const int QueueCapacity = 4;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);
    private readonly ISettingsService _settings;
    private readonly IVoiceOrchestrator? _voice;
    private readonly IRuntimeLogService? _logs;
    private readonly Func<string, CancellationToken, Task> _playback;
    private readonly object _gate = new();
    private readonly Queue<AudioFeedbackEventKind> _queue = new();
    private readonly Dictionary<AudioFeedbackEventKind, DateTime> _lastPublished = [];
    private readonly HashSet<AudioFeedbackEventKind> _reportedFailures = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;

    public AudioFeedbackService(ISettingsService settings, IVoiceOrchestrator? voice = null, IRuntimeLogService? logs = null,
        Func<string, CancellationToken, Task>? playback = null)
    {
        _settings = settings;
        _voice = voice;
        _logs = logs;
        _playback = playback ?? AudioPlayback.PlayAsync;
        _worker = Task.Run(() => WorkerAsync(_lifetime.Token));
    }

    public Task PublishAsync(AudioFeedbackEventKind kind, CancellationToken ct = default)
    {
        var settings = _settings.Settings.Tts.AudioFeedback;
        if (!settings.Enabled || settings.Muted || !settings.IsEnabled(kind))
            return Task.CompletedTask;
        if (ct.IsCancellationRequested || settings.Volume <= 0)
            return Task.CompletedTask;

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_lastPublished.TryGetValue(kind, out var last) && now - last < Cooldown)
                return Task.CompletedTask;
            if (_queue.Count >= QueueCapacity)
                return Task.CompletedTask;
            _lastPublished[kind] = now;
            _queue.Enqueue(kind);
        }
        _signal.Release();
        return Task.CompletedTask;
    }

    private async Task WorkerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }
            AudioFeedbackEventKind kind;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    continue;
                kind = _queue.Dequeue();
            }
            var settings = _settings.Settings.Tts.AudioFeedback;
            if (settings.SuppressWhileTtsSpeaking && _voice?.IsSpeaking == true)
                continue;

            var path = Path.Combine(Path.GetTempPath(), $"hermaeus-audio-feedback-{Guid.NewGuid():N}.wav");
            try
            {
                await File.WriteAllBytesAsync(path, AudioFeedbackAssets.CreateWav(kind, settings.Volume), ct);
                await _playback(path, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (!_reportedFailures.Add(kind)) continue;
                }
                _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Service, "Audio feedback could not be played; the visual notification remains active."));
                _ = ex;
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _signal.Release();
        try { await _worker; } catch (OperationCanceledException) { }
        _signal.Dispose();
        _lifetime.Dispose();
    }
}
