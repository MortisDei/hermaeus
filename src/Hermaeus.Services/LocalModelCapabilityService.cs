using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public sealed record LlamaRuntimeCapabilityFacts(
    bool HelpProbeSucceeded,
    bool SupportsDraftMtp,
    bool SupportsReasoningFormat,
    bool SupportsReasoningFlag,
    bool SupportsReasoningPreserve,
    bool PropsProbeSucceeded,
    bool? SupportsPreserveReasoningTemplate,
    IReadOnlyList<string> Modalities);

/// <summary>Combines bounded GGUF facts with executable and live /props evidence.</summary>
public sealed class LocalModelCapabilityService
{
    private readonly ISettingsService _settings;
    private readonly IRuntimeLogService _logs;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LocalModelCapabilityService(ISettingsService settings, IRuntimeLogService logs)
    {
        _settings = settings;
        _logs = logs;
    }

    public static LlamaRuntimeCapabilityFacts ParseHelp(string? help)
    {
        if (string.IsNullOrWhiteSpace(help))
            return new(false, false, false, false, false, false, null, []);

        return new(
            true,
            help.Contains("--spec-type", StringComparison.Ordinal) && help.Contains("draft-mtp", StringComparison.OrdinalIgnoreCase),
            help.Contains("--reasoning-format", StringComparison.Ordinal),
            help.Contains("--reasoning", StringComparison.Ordinal) && !help.Contains("--no-reasoning", StringComparison.Ordinal),
            help.Contains("--reasoning-preserve", StringComparison.Ordinal) && help.Contains("--no-reasoning-preserve", StringComparison.Ordinal),
            false,
            null,
            []);
    }

    public static LlamaRuntimeCapabilityFacts ParseProps(string? propsJson, LlamaRuntimeCapabilityFacts facts)
    {
        if (string.IsNullOrWhiteSpace(propsJson))
            return facts;

        try
        {
            using var document = JsonDocument.Parse(propsJson);
            var root = document.RootElement;
            bool? preserve = null;
            if (root.TryGetProperty("chat_template_caps", out var caps)
                && caps.ValueKind == JsonValueKind.Object
                && caps.TryGetProperty("supports_preserve_reasoning", out var preserveElement)
                && (preserveElement.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                preserve = preserveElement.GetBoolean();
            }

            var modalities = new List<string>();
            if (root.TryGetProperty("modalities", out var modalityElement))
            {
                if (modalityElement.ValueKind == JsonValueKind.Array)
                    modalities.AddRange(modalityElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Take(16));
                else if (modalityElement.ValueKind == JsonValueKind.String)
                    modalities.Add(modalityElement.GetString()!);
            }

            return facts with
            {
                PropsProbeSucceeded = true,
                SupportsPreserveReasoningTemplate = preserve,
                Modalities = modalities
            };
        }
        catch (JsonException)
        {
            return facts with { PropsProbeSucceeded = true, SupportsPreserveReasoningTemplate = null, Modalities = [] };
        }
    }

    public static LocalModelCapabilities Combine(string modelPath, GgufModelInfo? gguf, LlamaRuntimeCapabilityFacts runtime)
    {
        var mtp = gguf?.NextnPredictLayers > 0
            ? runtime.HelpProbeSucceeded
                ? runtime.SupportsDraftMtp
                    ? Available("gguf-nextn+runtime-draft-mtp", $"{Path.GetFileName(modelPath)} reports NextN layers and llama-server advertises draft-mtp.")
                    : Unavailable("runtime-no-draft-mtp", "The selected llama-server does not advertise draft-mtp.")
                : Unknown("runtime-help-unknown", "A successful llama-server help probe is required.")
            : Unknown("gguf-nextn-unknown", "The GGUF does not provide positive NextN metadata.");

        var reasoning = runtime.HelpProbeSucceeded
            ? runtime.SupportsReasoningFormat
                ? Available("runtime-reasoning-format", "llama-server advertises a separate reasoning format.")
                : Unavailable("runtime-no-reasoning-format", "The selected llama-server does not advertise a separate reasoning format.")
            : Unknown("runtime-help-unknown", "A successful llama-server help probe is required.");

        var preserve = runtime.HelpProbeSucceeded && !runtime.SupportsReasoningPreserve
            ? Unavailable("runtime-no-reasoning-preserve", "The selected llama-server does not advertise paired reasoning-preserve flags.")
            : runtime.SupportsPreserveReasoningTemplate is true
                ? Available("runtime-props-preserve-reasoning", "The matching llama-server /props result reports supports_preserve_reasoning=true.")
                : Unknown("template-capability-unknown", "A healthy llama-server /props probe has not confirmed reasoning preservation.");

        var vision = runtime.PropsProbeSucceeded
            ? runtime.Modalities.Any(m => m.Contains("vision", StringComparison.OrdinalIgnoreCase) || m.Contains("image", StringComparison.OrdinalIgnoreCase))
                ? Available("runtime-props-modalities", "The running server reports image or vision modality support.")
                : Unknown("runtime-props-no-vision", "The running server did not report a vision modality.")
            : Unknown("runtime-props-unknown", "Vision capability is unknown until a managed server is healthy.");

        return new(modelPath, mtp, reasoning, preserve, vision, DateTime.UtcNow);
    }

    public async Task<LocalModelCapabilities> ProbeAsync(string modelPath, string executablePath, string? propsJson = null, CancellationToken ct = default)
    {
        var cached = await TryGetCachedAsync(modelPath, executablePath, ct);
        var gguf = await Task.Run(() => GgufMetadataReader.TryRead(modelPath), ct);
        var help = await ReadHelpAsync(executablePath, ct);
        if (help is null && string.IsNullOrWhiteSpace(propsJson) && cached is not null)
            return cached;
        var runtime = ParseProps(propsJson, ParseHelp(help));
        var result = Combine(modelPath, gguf, runtime);
        try { await SaveCacheAsync(modelPath, executablePath, result, ct); }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service, $"Capability cache write failed: {ex.Message}"));
        }
        return result;
    }

    public async Task<LocalModelCapabilities?> TryGetCachedAsync(string modelPath, string executablePath, CancellationToken ct = default)
    {
        var cachePath = Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "capability-cache.json");
        if (!File.Exists(cachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(cachePath);
            var entries = await JsonSerializer.DeserializeAsync<List<CapabilityCacheEntry>>(stream, JsonOptions, ct) ?? [];
            var identity = Identity(modelPath, executablePath);
            return entries.LastOrDefault(e =>
                string.Equals(e.ModelPath, identity.ModelPath, StringComparison.Ordinal)
                && e.ModelSize == identity.ModelSize
                && e.ModelMtime == identity.ModelMtime
                && string.Equals(e.ExecutablePath, identity.ExecutablePath, StringComparison.Ordinal)
                && e.ExecutableSize == identity.ExecutableSize
                && e.ExecutableMtime == identity.ExecutableMtime)?.Capabilities;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> ReadHelpAsync(string configuredPath, CancellationToken ct)
    {
        var resolution = ExecutableResolver.Resolve(configuredPath, "llama-server");
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.Path))
            return null;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = resolution.Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--help");
        if (!process.Start())
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return output + Environment.NewLine + error;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
    }

    private async Task SaveCacheAsync(string modelPath, string executablePath, LocalModelCapabilities capabilities, CancellationToken ct)
    {
        var cachePath = Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "capability-cache.json");
        var entries = new List<CapabilityCacheEntry>();
        if (File.Exists(cachePath))
        {
            try
            {
                await using var stream = File.OpenRead(cachePath);
                entries = await JsonSerializer.DeserializeAsync<List<CapabilityCacheEntry>>(stream, JsonOptions, ct) ?? [];
            }
            catch (JsonException) { entries = []; }
        }

        var identity = Identity(modelPath, executablePath);
        entries.RemoveAll(e => e.ModelPath.Equals(identity.ModelPath, StringComparison.Ordinal)
            && e.ExecutablePath.Equals(identity.ExecutablePath, StringComparison.Ordinal));
        entries.Add(new CapabilityCacheEntry(identity.ModelPath, identity.ModelSize, identity.ModelMtime, identity.ExecutablePath, identity.ExecutableSize, identity.ExecutableMtime, capabilities));
        await AtomicFile.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(entries, JsonOptions), ct);
    }

    private static (string ModelPath, long ModelSize, DateTime ModelMtime, string ExecutablePath, long ExecutableSize, DateTime ExecutableMtime) Identity(string modelPath, string executablePath)
    {
        var model = new FileInfo(modelPath);
        var resolvedExecutable = ExecutableResolver.Resolve(executablePath, "llama-server").Path ?? executablePath;
        var executable = new FileInfo(resolvedExecutable);
        return (Path.GetFullPath(modelPath), model.Exists ? model.Length : 0, model.Exists ? model.LastWriteTimeUtc : DateTime.MinValue,
            Path.GetFullPath(resolvedExecutable), executable.Exists ? executable.Length : 0, executable.Exists ? executable.LastWriteTimeUtc : DateTime.MinValue);
    }

    private static CapabilityEvidence Available(string code, string detail) => new(CapabilityState.Available, code, detail);
    private static CapabilityEvidence Unavailable(string code, string detail) => new(CapabilityState.Unavailable, code, detail);
    private static CapabilityEvidence Unknown(string code, string detail) => new(CapabilityState.Unknown, code, detail);

    private sealed record CapabilityCacheEntry(
        string ModelPath,
        long ModelSize,
        DateTime ModelMtime,
        string ExecutablePath,
        long ExecutableSize,
        DateTime ExecutableMtime,
        LocalModelCapabilities Capabilities);
}
