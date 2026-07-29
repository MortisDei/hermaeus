using System.Linq;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Services;

/// <summary>
/// Reports which parts of the current configuration can send data off the
/// local machine. Extracted from SystemOverviewViewModel so the checks are
/// testable and can feed the shared inspection engine.
/// </summary>
public sealed class PrivacyAuditService
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IRuntimeLogService _logs;
    private readonly IVoiceProviderRegistry _voiceProviders;
    private readonly ITraceStore _traces;
    private readonly SqliteRagStore? _ragStore;
    private readonly ModelManifestStore? _modelManifest;
    private readonly ISpeechRecognitionProviderRegistry? _sttProviders;
    private readonly IAudioCapture? _audioCapture;

    public PrivacyAuditService(
        ISettingsService settings,
        ISecretStore secrets,
        IRuntimeLogService logs,
        IVoiceProviderRegistry voiceProviders,
        ITraceStore traces,
        SqliteRagStore? ragStore = null,
        ModelManifestStore? modelManifest = null,
        ISpeechRecognitionProviderRegistry? sttProviders = null,
        IAudioCapture? audioCapture = null)
    {
        _settings = settings;
        _secrets = secrets;
        _logs = logs;
        _voiceProviders = voiceProviders;
        _traces = traces;
        _ragStore = ragStore;
        _modelManifest = modelManifest;
        _sttProviders = sttProviders;
        _audioCapture = audioCapture;
    }

    /// <summary>Whether the model manifest has at least one repo-linked entry, meaning the
    /// manual "Check for updates" / HF browser downloads are a live outbound surface for this
    /// install (r13 03-hugging-face.md 3.2). Manual-only, never on startup or a timer.</summary>
    private async Task<bool> HasHuggingFaceLinkedModelsAsync(CancellationToken ct)
    {
        if (_modelManifest is null)
            return false;

        var entries = await _modelManifest.LoadAsync(ct);
        return entries.Any(e => !string.IsNullOrWhiteSpace(e.RepoId));
    }

    /// <summary>
    /// One number for "can anything leave my machine": remote chat
    /// endpoints, a remote voice provider, RAG datasets with web ingest
    /// enabled, and configured MCP servers (r6 01-first-five-minutes.md
    /// 1.3). Counts configuration, not activity - an entry counts once it
    /// is configured, whether or not it has been used yet.
    /// </summary>
    public async Task<int> CountOutboundDestinationsAsync(CancellationToken ct = default)
    {
        var settings = _settings.Settings;
        var count = CompositeLlmService.Providers.Count(p => p.IsRemote && IsChatProviderEnabled(p, settings));

        var activeVoiceProvider = Enum.TryParse<VoiceProvider>(settings.Tts.VoiceProvider, ignoreCase: true, out var voiceId)
            ? _voiceProviders.GetAvailableProviders().FirstOrDefault(p => p.Id == voiceId)
            : null;
        if (activeVoiceProvider?.Capabilities.HasFlag(VoiceCapability.Remote) == true)
            count++;

        if (_ragStore is not null)
        {
            var datasets = await _ragStore.GetDatasetsAsync(ct);
            count += datasets.Count(d => d.Config.EnableWebLoader);
        }

        count += settings.Mcp.Servers.Count;

        if (await HasHuggingFaceLinkedModelsAsync(ct))
            count++;

        return count;
    }

    public async Task<IReadOnlyList<PrivacyAuditItem>> ScanAsync(CancellationToken ct = default)
    {
        var items = new List<PrivacyAuditItem>();
        var settings = _settings.Settings;

        var remoteChatProviders = CompositeLlmService.Providers
            .Where(p => p.IsRemote && IsChatProviderEnabled(p, settings))
            .ToList();
        var activeVoiceProvider = Enum.TryParse<VoiceProvider>(settings.Tts.VoiceProvider, ignoreCase: true, out var voiceId)
            ? _voiceProviders.GetAvailableProviders().FirstOrDefault(p => p.Id == voiceId)
            : null;
        var voiceRemote = activeVoiceProvider?.Capabilities.HasFlag(VoiceCapability.Remote) ?? false;
        var anyRemote = remoteChatProviders.Count > 0 || voiceRemote;

        // r21 3.4: disclosure describes surface, not the current toggle state
        // (matches the image-attachment entry above it) - it appears whenever
        // the RAG subsystem is available and a remote chat provider is
        // selected, not only when a conversation currently has a dataset
        // attached.
        var ragInjectionCapable = _ragStore is not null && remoteChatProviders.Count > 0;
        items.Add(new PrivacyAuditItem(
            "Remote providers",
            anyRemote ? "Review" : "Local",
            anyRemote
                ? string.Join(" ", remoteChatProviders
                    .Select(p => $"{p.DisplayName} chat endpoint configured at {settings.Llm.OpenAiBaseUrl}. Prompts, and any images attached to a chat message, may leave the machine when selected.")
                    .Concat(voiceRemote ? [$"Voice provider {activeVoiceProvider!.Name} sends audio off the machine."] : [])
                    .Concat(ragInjectionCapable ? ["Chat knowledge context: excerpts from local RAG datasets are included in prompts sent to the remote chat provider."] : []))
                : "No enabled remote chat or voice provider detected in settings."));

        items.Add(new PrivacyAuditItem(
            "Local providers",
            "Local",
            string.Join("; ", CompositeLlmService.Providers
                .Where(p => !p.IsRemote)
                .Select(p => $"{p.DisplayName} {(IsChatProviderEnabled(p, settings) ? "enabled" : "disabled")}"))
                + $"; TTS provider {settings.Tts.VoiceProvider}; RAG {(settings.Rag.Enabled ? "enabled" : "available")}."));

        var exposedServers = settings.ManagedServers.Where(HasNetworkExposureFlag).ToList();
        items.Add(new PrivacyAuditItem(
            "Exposed local servers",
            exposedServers.Count == 0 ? "Local only" : "Warning",
            exposedServers.Count == 0
                ? "Managed llama-server entries do not include network-facing host flags."
                : string.Join("; ", exposedServers.Select(s => $"{s.Name} port {s.Port}: {s.ExtraArgs.Trim()}"))));

        var secretBackend = await _secrets.BackendLabelAsync();
        items.Add(new PrivacyAuditItem(
            "Secret health",
            secretBackend.Contains("fallback", StringComparison.OrdinalIgnoreCase) ? "Fallback" : "Protected",
            $"Secret backend: {secretBackend}."));

        items.Add(new PrivacyAuditItem(
            "Log redaction",
            "Enabled",
            $"{_logs.GetEntries().Count} in-memory runtime log entries. Diagnostics export uses redaction services."));

        items.Add(new PrivacyAuditItem(
            "Data root backup",
            Directory.Exists(settings.DataManagement.DataRootDirectory) ? "Configured" : "Needs setup",
            string.IsNullOrWhiteSpace(settings.DataManagement.DataRootDirectory)
                ? "Data root is using default resolution. Configure and back it up before relying on long-term history."
                : settings.DataManagement.DataRootDirectory));

        items.Add(await BuildLocalApiActivityItemAsync(settings, ct));

        items.Add(new PrivacyAuditItem(
            "Model usage counters",
            "Local only",
            "Per-feature daily call and token counts are stored locally in traces.db (model_usage table) to power usage-aware benchmark insights. Never transmitted."));

        if (await HasHuggingFaceLinkedModelsAsync(ct))
        {
            items.Add(new PrivacyAuditItem(
                "Hugging Face model search/download",
                "Review",
                "At least one local model is linked to a Hugging Face repo for update checks. Update checks and downloads (huggingface.co) only happen when you press \"Check for updates\", \"Update\", or search/download in the Get Models browser - never on startup or a timer, anonymous access only."));
        }

        if (voiceRemote)
        {
            var enabledChannels = Enum.GetValues<VoiceChannel>()
                .Where(c => IsVoiceChannelEnabled(settings.Tts, c))
                .ToList();
            if (enabledChannels.Count > 0)
            {
                items.Add(new PrivacyAuditItem(
                    "Voice channels sending text remotely",
                    "Review",
                    $"{enabledChannels.Count} channel(s) ({string.Join(", ", enabledChannels)}) speak through {activeVoiceProvider!.Name}, which sends the spoken text to a remote voice provider."));
            }
        }

        if (_sttProviders is not null && settings.Stt.Enabled)
            items.Add(BuildSpeechRecognitionItem(settings));

        items.Add(new PrivacyAuditItem(
            "Features that may send data remotely",
            anyRemote ? "Review" : "Local",
            "Remote chat/voice providers can send prompt, document, image, or voice data outside the local machine when explicitly configured. RAG web ingest remains dataset-scoped and approval driven."));

        return items;
    }

    /// <summary>r24 doc 05 5.6: voice input is a strictly higher-sensitivity case than
    /// the image-attachment disclosure above it, so it gets a line of its own naming
    /// where microphone audio goes, plus current microphone access state.</summary>
    private PrivacyAuditItem BuildSpeechRecognitionItem(AppSettings settings)
    {
        var provider = _sttProviders!.GetActiveProvider();
        var isRemote = provider == SttProvider.OpenAi;
        var deviceConfigured = _audioCapture?.IsAvailable ?? false;
        var deviceDetail = deviceConfigured
            ? "A microphone is configured and available."
            : $"No microphone available{(_audioCapture?.UnavailableReason is { Length: > 0 } reason ? $": {reason}" : ".")}";

        var detail = isRemote
            ? $"Speech recognition provider: OpenAI-compatible (remote, model {settings.Stt.RemoteModel}). Microphone audio leaves this machine and is sent to {settings.Llm.OpenAiBaseUrl} for transcription. {deviceDetail}"
            : $"Speech recognition provider: native (in-process ONNX, local). Audio never leaves this machine; transcription runs fully offline. {deviceDetail}";

        return new PrivacyAuditItem("Speech recognition", isRemote ? "Review" : "Local", detail);
    }

    /// <summary>
    /// Mirrors <c>VoiceOrchestrator.IsChannelEnabled</c>: Chat defaults on,
    /// every other channel defaults off unless explicitly enabled.
    /// </summary>
    private static bool IsVoiceChannelEnabled(TtsSettings tts, VoiceChannel channel) =>
        tts.Channels.TryGetValue(channel.ToString(), out var config)
            ? config.Enabled
            : channel == VoiceChannel.Chat;

    /// <summary>
    /// Per-app data-flow visibility for the optional local API host: which
    /// apps (self-reported via X-Hermaeus-Client) have called Hermaeus, and how
    /// often, sourced from the shared TraceKind.LocalApi trace history.
    /// </summary>
    private async Task<PrivacyAuditItem> BuildLocalApiActivityItemAsync(AppSettings settings, CancellationToken ct)
    {
        if (!settings.LocalApi.Enabled)
            return new PrivacyAuditItem(
                "Local API activity",
                "Disabled",
                "The local API host is off; no other app can reach Hermaeus through it.");

        var recent = await _traces.GetRecentAsync(TraceKind.LocalApi, 200, ct);
        if (recent.Count == 0)
            return new PrivacyAuditItem(
                "Local API activity",
                "No calls yet",
                $"Local API is enabled on port {settings.LocalApi.Port}. No calls have been recorded yet.");

        var byClient = recent
            .GroupBy(r => string.IsNullOrWhiteSpace(r.SourceId) ? "unknown" : r.SourceId)
            .OrderByDescending(g => g.Max(r => r.CreatedAt))
            .Select(g => $"{g.Key}: {g.Count()} call(s), last {g.Max(r => r.CreatedAt):u}")
            .ToList();

        return new PrivacyAuditItem(
            "Local API activity",
            "Review",
            $"{recent.Count} recent call(s) from {byClient.Count} distinct token(s) (each identifies a verified per-app token, not just a self-reported name): {string.Join("; ", byClient)}");
    }

    private static bool IsChatProviderEnabled(ProviderDescriptor descriptor, AppSettings settings) =>
        CompositeLlmService.IsProviderEnabled(descriptor.Tag, settings);

    private static bool HasNetworkExposureFlag(ServerConfig server)
    {
        var args = server.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--listen", StringComparison.OrdinalIgnoreCase))
                return true;

            if ((arg.Equals("--host", StringComparison.OrdinalIgnoreCase) || arg.Equals("--host-address", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length)
            {
                var value = args[i + 1];
                if (!value.Equals("127.0.0.1", StringComparison.Ordinal)
                    && !value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("::1", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
