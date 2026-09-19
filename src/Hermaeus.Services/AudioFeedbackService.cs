using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Voice;

namespace Hermaeus.Services;

public sealed class AudioFeedbackService : IAudioFeedbackService, IAsyncDisposable
{
    private const int QueueCapacity = 4;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TtsRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly ISettingsService _settings;
    private readonly IVoiceOrchestrator? _voice;
    private readonly IRuntimeLogService? _logs;
    private readonly Func<string, CancellationToken, Task> _playback;
    private readonly bool _usesDefaultPlayback;
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
        _usesDefaultPlayback = playback is null;
        _playback = playback ?? ((path, token) => AudioPlayback.PlayAsync(path, token));
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
            var deferredForTts = false;
            while (settings.SuppressWhileTtsSpeaking && _voice?.IsSpeaking == true)
            {
                if (!deferredForTts)
                {
                    RecordDiagnostic(kind, "policy", "tts-speaking-deferred");
                    deferredForTts = true;
                }

                try { await Task.Delay(TtsRetryDelay, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                settings = _settings.Settings.Tts.AudioFeedback;
            }

            if (ct.IsCancellationRequested)
                break;
            if (!settings.Enabled || settings.Muted || !settings.IsEnabled(kind) || settings.Volume <= 0)
                continue;

            var path = Path.Combine(Path.GetTempPath(), $"hermaeus-audio-feedback-{Guid.NewGuid():N}.wav");
            try
            {
                try
                {
                    var cue = AudioFeedbackAssets.Resolve(kind);
                    var wav = AudioFeedbackAssets.CreateWav(kind, settings.Volume);
                    await File.WriteAllBytesAsync(path, wav, ct);
                    RecordDiagnostic(kind, "resource",
                        $"cue={cue.Id}; pattern={string.Join(",", cue.Frequencies)}Hz; path={path}; detail=generated-pcm-wav");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    ReportFailure(kind, "asset", ex);
                    continue;
                }

                try
                {
                    if (_usesDefaultPlayback)
                        await AudioPlayback.PlayAsync(
                            path,
                            ct,
                            backend => RecordDiagnostic(kind, "backend", $"selected={backend}; fallback=false"),
                            (backend, succeeded) => RecordDiagnostic(kind, "backend-attempt",
                                $"backend={backend}; result={(succeeded ? "success" : "fallback")}; reason={(succeeded ? "selected" : "backend-unavailable-or-nonzero-exit")}"));
                    else
                    {
                        RecordDiagnostic(kind, "backend", "selected=custom-playback; fallback=false");
                        await _playback(path, ct);
                    }
                    RecordDiagnostic(kind, "result", "played");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    ReportFailure(kind, "playback", ex);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
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

    private void ReportFailure(AudioFeedbackEventKind kind, string stage, Exception ex)
    {
        lock (_gate)
        {
            if (!_reportedFailures.Add(kind))
                return;
        }

        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Warning,
            RuntimeLogCategory.Service,
            $"Audio feedback failed: event={kind}; stage={stage}; error={ex.GetType().Name}: {ex.Message}"));
    }

    private void RecordDiagnostic(AudioFeedbackEventKind kind, string stage, string detail)
    {
        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Service,
            $"Audio feedback: event={kind}; stage={stage}; detail={detail}"));
    }
}
