using System.Linq;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Reports which parts of the current configuration can send data off the
/// local machine. Extracted from SystemOverviewViewModel so the checks are
/// testable and can feed the shared inspection engine.
/// </summary>
public sealed class PrivacyAuditService : IPrivacyAuditService, IInspectionCheckProvider
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IRuntimeLogService _logs;
    private readonly IVoiceProviderRegistry _voiceProviders;

    public IReadOnlyList<string> Views { get; } = ["privacy"];

    public PrivacyAuditService(ISettingsService settings, ISecretStore secrets, IRuntimeLogService logs, IVoiceProviderRegistry voiceProviders)
    {
        _settings = settings;
        _secrets = secrets;
        _logs = logs;
        _voiceProviders = voiceProviders;
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

        items.Add(new PrivacyAuditItem(
            "Remote providers",
            anyRemote ? "Review" : "Local",
            anyRemote
                ? string.Join(" ", remoteChatProviders
                    .Select(p => $"{p.DisplayName} chat endpoint configured at {settings.Llm.OpenAiBaseUrl}. Prompts may leave the machine when selected.")
                    .Concat(voiceRemote ? [$"Voice provider {activeVoiceProvider!.Name} sends audio off the machine."] : []))
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

        items.Add(new PrivacyAuditItem(
            "Features that may send data remotely",
            anyRemote ? "Review" : "Local",
            "Remote chat/voice providers can send prompt, document, or voice data outside the local machine when explicitly configured. RAG web ingest remains dataset-scoped and approval driven."));

        return items;
    }

    private static bool IsChatProviderEnabled(ProviderDescriptor descriptor, AppSettings settings) => descriptor.Tag switch
    {
        "openai" => settings.Llm.OpenAiEnabled,
        "llama.cpp" => settings.Llm.LlamaCppEnabled,
        "ollama" => settings.RuntimeProfiles.Any(p => p.Enabled && p.Kind == RuntimeKind.Ollama),
        _ => false
    };

    public async Task<IReadOnlyList<InspectionCheck>> GetChecksAsync(CancellationToken ct = default)
    {
        var scannedAt = DateTime.UtcNow;
        var items = await ScanAsync(ct);
        return items.Select(i => new InspectionCheck(
            Id: $"privacy-{i.Name.ToLowerInvariant().Replace(' ', '-')}",
            View: "privacy",
            Category: "Privacy",
            Title: i.Name,
            Severity: i.Status is "Review" or "Warning" or "Fallback" or "Needs setup" ? CheckSeverity.Warning : CheckSeverity.Info,
            Summary: i.Status,
            Detail: i.Detail,
            FixLabel: string.Empty,
            CanFix: false,
            Diagnostics: $"{i.Name}: {i.Status}\n{i.Detail}",
            DetailJson: $$"""{"scannedAt":"{{scannedAt:O}}"}""")).ToList();
    }

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
