using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Single owner of everything Hermaeus speaks. A background worker drains a
/// priority queue one utterance at a time (serialization is what guarantees
/// two consumers never overlap audio, no mixer required), resolves the
/// speaking voice from the utterance override or the channel's configured
/// profile, and plays through the currently active <see cref="IVoiceProvider"/>.
/// </summary>
public sealed class VoiceOrchestrator : IVoiceOrchestrator, IDisposable
{
    private const int LowPriorityQueueCap = 3;
    private const int QueueCapacity = 16;

    private sealed record QueueEntry(VoiceUtterance Utterance);

    private readonly ISettingsService _settings;
    private readonly IVoiceProviderRegistry _voiceProviders;
    private readonly IToastService _toasts;
    private readonly object _gate = new();
    private readonly List<QueueEntry> _queue = [];
    private readonly HashSet<string> _toastedProviderFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _worker;
    private CancellationTokenSource? _playbackCts;
    private VoiceChannel? _currentChannel;
    private bool _disposed;

    public bool IsMuted { get; set; }
    public event Action<VoiceChannel, string>? UtteranceStarted;
    public event Action<VoiceChannel>? UtteranceCompleted;

    public bool IsSpeaking
    {
        get { lock (_gate) return _currentChannel is not null; }
    }

    public VoiceOrchestrator(ISettingsService settings, IVoiceProviderRegistry voiceProviders, IToastService toasts)
    {
        _settings = settings;
        _voiceProviders = voiceProviders;
        _toasts = toasts;
        _worker = Task.Run(() => WorkerLoopAsync(_lifetimeCts.Token));
    }

    public Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(utterance.Text))
            return Task.CompletedTask;
        if (IsMuted || !_settings.Settings.Tts.Enabled)
            return Task.CompletedTask;
        if (!IsChannelEnabled(utterance.Channel))
            return Task.CompletedTask;

        lock (_gate)
        {
            if (!string.IsNullOrEmpty(utterance.DedupeKey)
                && _queue.Any(e => e.Utterance.DedupeKey == utterance.DedupeKey))
                return Task.CompletedTask;

            if (utterance.Priority == VoicePriority.Critical)
            {
                _queue.RemoveAll(e => e.Utterance.Priority == VoicePriority.Low);
                if (_queue.Count >= QueueCapacity)
                {
                    var evicted = _queue.FindLastIndex(e => e.Utterance.Priority != VoicePriority.Critical);
                    _queue.RemoveAt(evicted >= 0 ? evicted : _queue.Count - 1);
                }
                _queue.Insert(0, new QueueEntry(utterance));
                _playbackCts?.Cancel();
            }
            else
            {
                if (utterance.Priority == VoicePriority.Low && _queue.Count >= LowPriorityQueueCap)
                    return Task.CompletedTask;
                if (_queue.Count >= QueueCapacity)
                    return Task.CompletedTask;
                _queue.Add(new QueueEntry(utterance));
            }
        }

        _signal.Release();
        return Task.CompletedTask;
    }

    public void StopChannel(VoiceChannel channel)
    {
        lock (_gate)
        {
            _queue.RemoveAll(e => e.Utterance.Channel == channel);
            if (_currentChannel == channel)
                _playbackCts?.Cancel();
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            _queue.Clear();
            _playbackCts?.Cancel();
        }
    }

    private bool IsChannelEnabled(VoiceChannel channel)
    {
        var tts = _settings.Settings.Tts;
        return tts.Channels.TryGetValue(channel.ToString(), out var config)
            ? config.Enabled
            : channel == VoiceChannel.Chat;
    }

    private string? ResolveVoice(VoiceUtterance utterance)
    {
        if (!string.IsNullOrWhiteSpace(utterance.VoiceOverride))
            return utterance.VoiceOverride;

        var tts = _settings.Settings.Tts;
        if (tts.Channels.TryGetValue(utterance.Channel.ToString(), out var channelConfig))
        {
            if (!string.IsNullOrWhiteSpace(channelConfig.VoiceId))
                return channelConfig.VoiceId;

            // Legacy (pre-r24): the channel still only names a Profiles entry.
            if (!string.IsNullOrWhiteSpace(channelConfig.ProfileName))
            {
                var profile = tts.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, channelConfig.ProfileName, StringComparison.OrdinalIgnoreCase));
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.VoiceId))
                    return profile.VoiceId;
            }
        }

        return string.IsNullOrWhiteSpace(tts.Speaker) ? null : tts.Speaker;
    }

    private async Task WorkerLoopAsync(CancellationToken lifetimeCt)
    {
        while (!lifetimeCt.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(lifetimeCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            QueueEntry? next;
            lock (_gate)
            {
                next = _queue.Count > 0 ? _queue[0] : null;
                if (next is not null)
                    _queue.RemoveAt(0);
            }

            if (next is null)
                continue;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt);
            lock (_gate)
            {
                _playbackCts = cts;
                _currentChannel = next.Utterance.Channel;
            }

            try
            {
                UtteranceStarted?.Invoke(next.Utterance.Channel, next.Utterance.Text);
                await PlayAsync(next.Utterance, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preempted by a Critical utterance or StopChannel/StopAll; expected.
            }
            catch (Exception ex)
            {
                HandleSynthesisFailure(ex.Message);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_playbackCts, cts))
                        _playbackCts = null;
                    _currentChannel = null;
                }
                UtteranceCompleted?.Invoke(next.Utterance.Channel);
            }
        }
    }

    private async Task PlayAsync(VoiceUtterance utterance, CancellationToken ct)
    {
        var provider = _voiceProviders.GetActiveVoiceProvider();
        var request = new VoiceSynthesisRequest(utterance.Text, Voice: ResolveVoice(utterance), PlayAudio: true);
        var result = await provider.GenerateSpeechAsync(request, ct).ConfigureAwait(false);
        if (result.Success)
        {
            // r11 4.6: _toastedProviderFailures never reset, so after one
            // failure toast for a provider, a later distinct failure (a
            // different root cause, hours later) stayed silent for the rest
            // of the app's lifetime. Reset on a subsequent successful
            // utterance so each failure episode toasts once, not each app run.
            _toastedProviderFailures.Remove(provider.DisplayName);
        }
        else
        {
            HandleSynthesisFailure(result.Message, provider.DisplayName);
        }
    }

    private void HandleSynthesisFailure(string message, string? providerName = null)
    {
        var key = providerName ?? "voice";
        if (!_toastedProviderFailures.Add(key))
            return;

        _toasts.Show("Voice playback failed", message, ToastKind.Warning, 6000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _lifetimeCts.Dispose();
        _signal.Dispose();
    }
}
