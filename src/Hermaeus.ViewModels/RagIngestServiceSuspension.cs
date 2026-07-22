using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.ViewModels;

/// <summary>
/// Suspends services that compete for GPU/CPU with an embedding-heavy RAG
/// ingest (the managed embedding server plus TTS process managers) and
/// restores whatever was running afterward. Extracted from
/// RagViewModel.IngestAsync's suspend/restore block.
/// </summary>
public sealed class RagIngestServiceSuspension
{
    private readonly ServicesViewModel? _services;
    private readonly XttsProcessManager? _xtts;
    private readonly KokoroProcessManager? _kokoro;
    private readonly ISettingsService _settings;

    public RagIngestServiceSuspension(
        ServicesViewModel? services,
        XttsProcessManager? xtts,
        KokoroProcessManager? kokoro,
        ISettingsService settings)
    {
        _services = services;
        _xtts = xtts;
        _kokoro = kokoro;
        _settings = settings;
    }

    /// <summary>Suspends competing services and returns the restore action to run when ingest finishes.</summary>
    public async Task<Func<Task<IReadOnlyList<string>>>> SuspendAsync()
    {
        if (_services is null)
            return () => Task.FromResult<IReadOnlyList<string>>([]);

        var suspendedServerIds = await _services.PrepareEmbeddingServerForWorkAsync();
        var xttsWasRunning = _xtts?.IsRunning == true;
        var kokoroWasRunning = _kokoro?.IsRunning == true;

        if (_xtts?.IsRunning == true) _xtts.Stop();
        if (_kokoro?.IsRunning == true) _kokoro.Stop();

        return async () =>
        {
            var errors = new List<string>();
            try { await _services.RestartServersAsync(suspendedServerIds); }
            catch (Exception ex) { errors.Add($"LLM: {ex.Message}"); }
            try { if (xttsWasRunning && _xtts is not null) await _xtts.StartAsync(_settings.Settings, CancellationToken.None); }
            catch (Exception ex) { errors.Add($"XTTS: {ex.Message}"); }
            try { if (kokoroWasRunning && _kokoro is not null) await _kokoro.StartAsync(_settings.Settings, CancellationToken.None); }
            catch (Exception ex) { errors.Add($"Kokoro: {ex.Message}"); }
            return errors;
        };
    }
}
