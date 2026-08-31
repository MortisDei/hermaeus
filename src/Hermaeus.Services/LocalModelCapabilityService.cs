using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    IReadOnlyList<string> Modalities,
    IReadOnlyList<string> SpeculativeTypes,
    bool SupportsPromptThreads,
    bool SupportsBackendSampling,
    bool SupportsPerformanceInstrumentation,
    bool ModelSpecificMtpConfirmed = false,
    IReadOnlyList<string>? SupportedKvCacheTypes = null,
    bool SupportsFlashAttention = false,
    bool SupportsCpuMoePlacement = false,
    bool SupportsSpeculativeNMax = false,
    bool SupportsSpeculativeNMin = false,
    bool SupportsSpeculativePMin = false,
    bool SupportsSpeculativeDraftGpuLayers = false,
    bool SupportsLoadMode = false,
    bool SupportsCorsOrigins = false,
    IReadOnlyDictionary<string, CapabilityEvidence>? LaunchCapabilities = null);

/// <summary>A meaningful change between two capability snapshots, never a raw help-text diff.</summary>
public sealed record CapabilityDrift(string Capability, string Detail, bool AffectsConfiguredCapability = false);

public sealed record LocalModelCapabilityProbe(
    LocalModelCapabilities Capabilities,
    IReadOnlyList<CapabilityDrift> Drift);

/// <summary>Combines bounded GGUF facts with executable and live /props evidence.</summary>
public sealed class LocalModelCapabilityService
{
    /// <summary>
    /// Capability ids for the R32 launch contract. The list is deliberately
    /// fixed and bounded so a runtime cannot create an unreviewed setting by
    /// printing an arbitrary help token.
    /// </summary>
    public static IReadOnlyList<string> LaunchCapabilityIds { get; } =
    [
        "runtime.gpu-placement.cpu",
        "runtime.gpu-placement.auto",
        "runtime.gpu-placement.all",
        "runtime.gpu-placement.exact",
        "runtime.fit",
        "runtime.fit.target",
        "runtime.fit.minimum-context",
        "runtime.fit.report.effective",
        "runtime.device.list",
        "runtime.placement.device",
        "runtime.placement.split",
        "runtime.placement.split.none",
        "runtime.placement.split.layer",
        "runtime.placement.split.row",
        "runtime.placement.split.tensor",
        "runtime.placement.tensor-split",
        "runtime.placement.main-gpu",
        "runtime.kv-offload",
        "runtime.cache.host-ram",
        "runtime.context.checkpoints",
        "runtime.context.checkpoint-min-step",
        "runtime.kv-unified",
        "runtime.kv-unified-per-slot",
        "runtime.cache.idle-slots",
        "runtime.cache.slot-save"
    ];

    private readonly ISettingsService _settings;
    private readonly IRuntimeLogService _logs;
    private readonly IActivityRecorder? _activity;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LocalModelCapabilityService(ISettingsService settings, IRuntimeLogService logs, IActivityRecorder? activity = null)
    {
        _settings = settings;
        _logs = logs;
        _activity = activity;
    }

    public static LlamaRuntimeCapabilityFacts ParseHelp(string? help)
    {
        if (string.IsNullOrWhiteSpace(help))
            return new(false, false, false, false, false, false, null, [], [], false, false, false);

        return new(
            true,
            help.Contains("--spec-type", StringComparison.Ordinal) && help.Contains("draft-mtp", StringComparison.OrdinalIgnoreCase),
            help.Contains("--reasoning-format", StringComparison.Ordinal),
            help.Contains("--reasoning", StringComparison.Ordinal) && !help.Contains("--no-reasoning", StringComparison.Ordinal),
            help.Contains("--reasoning-preserve", StringComparison.Ordinal) && help.Contains("--no-reasoning-preserve", StringComparison.Ordinal),
            false,
            null,
            [],
            ParseSpeculativeTypes(help),
            help.Contains("--threads-batch", StringComparison.Ordinal),
            // Backend sampling has no stable, installed-runtime contract in
            // this r30 environment. Do not infer it from generic sampler flags.
            false,
            help.Contains("--perf", StringComparison.Ordinal),
            SupportedKvCacheTypes: ParseKvCacheTypes(help),
            SupportsFlashAttention: help.Contains("--flash-attn", StringComparison.Ordinal),
            SupportsCpuMoePlacement: help.Contains("--cpu-moe", StringComparison.Ordinal)
                && help.Contains("--n-cpu-moe", StringComparison.Ordinal),
            SupportsSpeculativeNMax: help.Contains("--spec-draft-n-max", StringComparison.Ordinal),
            SupportsSpeculativeNMin: help.Contains("--spec-draft-n-min", StringComparison.Ordinal),
            SupportsSpeculativePMin: help.Contains("--spec-draft-p-min", StringComparison.Ordinal),
            SupportsSpeculativeDraftGpuLayers: help.Contains("--spec-draft-ngl", StringComparison.Ordinal)
                || help.Contains("--gpu-layers-draft", StringComparison.Ordinal)
                || help.Contains("-ngld", StringComparison.Ordinal),
            SupportsLoadMode: help.Contains("--load-mode", StringComparison.Ordinal),
            SupportsCorsOrigins: help.Contains("--cors-origins", StringComparison.Ordinal),
            LaunchCapabilities: ParseLaunchCapabilities(help));
    }

    /// <summary>
    /// Parses only the reviewed R32 launch capabilities from an authoritative
    /// help response. Exact option matching prevents --fit-target from
    /// accidentally proving --fit, and --n-gpu-layers-draft from proving the
    /// main placement option.
    /// </summary>
    public static IReadOnlyDictionary<string, CapabilityEvidence> ParseLaunchCapabilities(string? help)
    {
        var result = new Dictionary<string, CapabilityEvidence>(StringComparer.Ordinal);
        var placement = HelpOptionWindow(help, "--n-gpu-layers");
        var exact = HasPhrase(placement, "exact number");
        var automatic = exact && HasWord(placement, "auto");
        var all = exact && HasWord(placement, "all");
        AddPlacement("runtime.gpu-placement.cpu", exact,
            "The runtime advertises an exact numeric layer count; zero is the explicit CPU form, subject to effective-placement observation.");
        AddPlacement("runtime.gpu-placement.exact", exact,
            "The runtime advertises an exact numeric GPU-layer count.");
        AddPlacement("runtime.gpu-placement.auto", automatic,
            "The runtime advertises automatic GPU-layer placement.");
        AddPlacement("runtime.gpu-placement.all", all,
            "The runtime advertises all-layer GPU placement.");

        AddFlag("runtime.fit", "--fit", "The runtime advertises explicit fit on/off ownership.");
        AddFlag("runtime.fit.target", "--fit-target", "The runtime advertises a per-device fit target.");
        AddFlag("runtime.fit.minimum-context", "--fit-ctx", "The runtime advertises a minimum context bound for fit.");
        AddUnknown("runtime.fit.report.effective",
            "Help can advertise fit, but cannot prove that effective fit changes are reported.");
        AddFlag("runtime.device.list", "--list-devices", "The runtime advertises device enumeration.");
        AddFlag("runtime.placement.device", "--device", "The runtime advertises an explicit offload device list.");

        var split = HelpOptionWindow(help, "--split-mode");
        AddFlag("runtime.placement.split", "--split-mode", "The runtime advertises explicit split-mode selection.");
        AddPhrase("runtime.placement.split.none", split, "none",
            "The runtime advertises no split across devices.");
        AddPhrase("runtime.placement.split.layer", split, "layer",
            "The runtime advertises layer splitting across devices.");
        AddPhrase("runtime.placement.split.row", split, "row",
            "The runtime advertises row splitting across devices.");
        AddPhrase("runtime.placement.split.tensor", split, "tensor",
            "The runtime advertises tensor splitting across devices; upstream marks this experimental.");
        AddFlag("runtime.placement.tensor-split", "--tensor-split", "The runtime advertises explicit tensor proportions.");
        AddFlag("runtime.placement.main-gpu", "--main-gpu", "The runtime advertises a main-device index.");
        AddFlag("runtime.kv-offload", "--kv-offload", "The runtime advertises explicit KV offload control.");
        AddFlag("runtime.cache.host-ram", "--cache-ram", "The runtime advertises a bounded host prompt-cache RAM control.");
        AddFlag("runtime.context.checkpoints", "--ctx-checkpoints", "The runtime advertises context checkpoint retention.", "--swa-checkpoints");
        AddFlag("runtime.context.checkpoint-min-step", "--checkpoint-min-step", "The runtime advertises checkpoint spacing control.");
        AddFlag("runtime.kv-unified", "--kv-unified", "The runtime advertises unified KV control.");
        AddFlag("runtime.kv-unified-per-slot", "--kv-unified-per-slot", "The runtime advertises a per-slot unified KV context limit.");
        AddFlag("runtime.cache.idle-slots", "--cache-idle-slots", "The runtime advertises idle-slot prompt-cache control.");
        AddFlag("runtime.cache.slot-save", "--slot-save-path", "The runtime advertises persistent slot-cache output.");

        return result;

        void AddFlag(string id, string option, string detail, params string[] aliases)
        {
            var supported = HasOption(help, option) || aliases.Any(alias => HasOption(help, alias));
            result[id] = HelpEvidence(help, supported, option, detail);
        }

        void AddPhrase(string id, string section, string phrase, string detail) =>
            result[id] = HelpEvidence(help, !string.IsNullOrWhiteSpace(help) && HasWord(section, phrase), "--split-mode", detail);

        void AddPlacement(string id, bool supported, string detail) =>
            result[id] = HelpEvidence(help, supported, "--n-gpu-layers", detail);

        void AddUnknown(string id, string detail) =>
            result[id] = string.IsNullOrWhiteSpace(help)
                ? Unknown("runtime-help-unknown", "A successful runtime help probe is required.")
                : Unknown("runtime-effective-placement-unobserved", detail);
    }

    /// <summary>
    /// Reads only the type names printed in the <c>--spec-type</c> section. It
    /// intentionally does not carry a permanent upstream list: unknown names
    /// remain visible as runtime facts but are not made configurable until
    /// Hermaeus has complete semantics for them.
    /// </summary>
    public static IReadOnlyList<string> ParseSpeculativeTypes(string help)
    {
        if (string.IsNullOrWhiteSpace(help)) return [];

        var start = help.IndexOf("--spec-type", StringComparison.Ordinal);
        if (start < 0) return [];

        var section = help[start..Math.Min(help.Length, start + 1800)];
        var nextOption = Regex.Match(section[1..], @"\r?\n\s*-{1,2}[A-Za-z][\w-]*\b");
        if (nextOption.Success)
            section = section[..(nextOption.Index + 1)];

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(section, @"\b[a-z][a-z0-9]*(?:-[a-z0-9]+)+\b", RegexOptions.IgnoreCase))
        {
            var value = match.Value.ToLowerInvariant();
            if (!string.Equals(value, "spec-type", StringComparison.Ordinal))
                types.Add(value);
        }

        // Current upstream names without a separator still occur in some help
        // renderings. They are facts, not a promise that Hermaeus configures
        // them.
        foreach (var name in new[] { "eagle3", "dspark", "dflash" })
            if (Regex.IsMatch(section, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
                types.Add(name);

        return types.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ParseKvCacheTypes(string help)
    {
        if (string.IsNullOrWhiteSpace(help)) return [];
        var start = help.IndexOf("--cache-type-k", StringComparison.Ordinal);
        if (start < 0) return [];
        var section = help[start..Math.Min(help.Length, start + 1400)];
        var known = new[] { "f32", "f16", "bf16", "q8_0", "q5_0", "q5_1", "q4_0", "q4_1", "iq4_nl" };
        return known.Where(type => Regex.IsMatch(section, $@"\b{Regex.Escape(type)}\b", RegexOptions.IgnoreCase)).ToArray();
    }

    public static async Task<LlamaRuntimeCapabilityFacts> ProbeRuntimeAsync(string executablePath, CancellationToken ct = default) =>
        ParseHelp(await ReadHelpAsync(executablePath, ct));

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

            var modelSpecificMtp = HasPositiveDraftCount(root)
                || HasCapability(root, "speculative.draft.mtp");

            var launchCapabilities = NormalizeLaunchCapabilities(facts.LaunchCapabilities);
            var effectiveFields = ParseEffectivePlacementFields(root);
            launchCapabilities["runtime.fit.report.effective"] = effectiveFields.Count == 0
                ? Unknown("runtime-effective-placement-unobserved",
                    "The running server /props response did not expose effective fit or placement fields.")
                : Available("runtime-effective-placement",
                    $"The running server /props response exposed bounded effective field(s): {string.Join(", ", effectiveFields)}.");

            return facts with
            {
                PropsProbeSucceeded = true,
                SupportsPreserveReasoningTemplate = preserve,
                Modalities = modalities,
                ModelSpecificMtpConfirmed = modelSpecificMtp,
                LaunchCapabilities = launchCapabilities
            };
        }
        catch (JsonException)
        {
            return facts with { PropsProbeSucceeded = true, SupportsPreserveReasoningTemplate = null, Modalities = [] };
        }
    }

    public static LocalModelCapabilities Combine(string modelPath, GgufModelInfo? gguf, LlamaRuntimeCapabilityFacts runtime)
    {
        var runtimeIdentity = RuntimeIdentityFactory.Unknown("llama.cpp");
        var modelIdentity = RuntimeIdentityFactory.CreateModelIdentity(modelPath, gguf);
        return Combine(modelPath, gguf, runtime, runtimeIdentity, modelIdentity, DateTime.UtcNow);
    }

    public static LocalModelCapabilities Combine(
        string modelPath,
        GgufModelInfo? gguf,
        LlamaRuntimeCapabilityFacts runtime,
        RuntimeIdentityV2 runtimeIdentity,
        ModelIdentityV2 modelIdentity,
        DateTime observedAtUtc)
    {
        var mtp = runtime.ModelSpecificMtpConfirmed
            ? Available("runtime-model-mtp-confirmed", "The selected model/runtime pair reported direct MTP drafting evidence.")
            : gguf?.NextnPredictLayers > 0
            ? runtime.HelpProbeSucceeded
                ? runtime.SupportsDraftMtp
                    ? Unknown("model-mtp-engagement-unknown", "NextN metadata and generic draft-mtp support do not prove that this model/runtime pair engages MTP.")
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

        var promptThreads = runtime.HelpProbeSucceeded
            ? runtime.SupportsPromptThreads
                ? Available("runtime-threads-batch", "llama-server advertises separate prompt-processing threads.")
                : Unavailable("runtime-no-threads-batch", "The selected llama-server does not advertise --threads-batch.")
            : Unknown("runtime-help-unknown", "A successful llama-server help probe is required.");
        var backendSampling = runtime.HelpProbeSucceeded
            ? runtime.SupportsBackendSampling
                ? Available("runtime-backend-sampling", "llama-server advertises backend sampling.")
                : Unknown("runtime-backend-sampling-unverified", "No stable backend-sampling capability was established from the selected runtime.")
            : Unknown("runtime-help-unknown", "A successful llama-server help probe is required.");
        var perf = runtime.HelpProbeSucceeded
            ? runtime.SupportsPerformanceInstrumentation
                ? Available("runtime-perf", "llama-server advertises --perf diagnostics.")
                : Unknown("runtime-perf-unverified", "The selected runtime does not advertise a stable --perf diagnostic flag.")
            : Unknown("runtime-help-unknown", "A successful llama-server help probe is required.");
        var speculative = runtime.SpeculativeTypes
            .Select(type => new RuntimeSpeculativeCapability(type, ClassifyDrafter(type), IsConfigurableType(type)))
            .ToArray();

        var surface = new RuntimeCapabilitySurface(speculative, promptThreads, backendSampling, perf);
        var observations = BuildObservations(
            runtime, mtp, reasoning, preserve, vision, promptThreads, backendSampling, perf,
            runtimeIdentity, modelIdentity, observedAtUtc);
        return new(modelPath, mtp, reasoning, preserve, vision, observedAtUtc, surface, observations);
    }

    public async Task<LocalModelCapabilities> ProbeAsync(string modelPath, string executablePath, string? propsJson = null, CancellationToken ct = default)
        => (await ProbeWithDriftAsync(modelPath, executablePath, propsJson, ct)).Capabilities;

    public async Task<LocalModelCapabilityProbe> ProbeWithDriftAsync(string modelPath, string executablePath, string? propsJson = null, CancellationToken ct = default)
    {
        var cached = await TryGetCachedAsync(modelPath, executablePath, ct);
        var gguf = await Task.Run(() => GgufMetadataReader.TryRead(modelPath), ct);
        var help = await ReadHelpAsync(executablePath, ct);
        if (help is null && string.IsNullOrWhiteSpace(propsJson) && cached is not null)
            return new LocalModelCapabilityProbe(cached, []);

        var previous = await TryGetPreviousSnapshotAsync(modelPath, executablePath, ct);
        var runtime = ParseProps(propsJson, ParseHelp(help));
        var observedAtUtc = DateTime.UtcNow;
        var runtimeIdentity = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(executablePath, help, ct);
        var modelIdentity = RuntimeIdentityFactory.CreateModelIdentity(modelPath, gguf);
        var result = Combine(modelPath, gguf, runtime, runtimeIdentity, modelIdentity, observedAtUtc);
        try { await SaveCacheAsync(modelPath, executablePath, result, ct, runtimeIdentity, modelIdentity); }
        catch (Exception ex)
        {
                _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service, $"Capability cache write failed: {ex.Message}"));
        }
        var drift = Compare(previous, result);
        if (drift.Count > 0)
        {
            _activity.RecordSafe(
                "services.capability-drift",
                modelPath,
                ActivityOutcome.Succeeded,
                "llama.cpp capabilities changed",
                string.Join(" ", drift.Select(change => change.Detail)));
        }
        return new LocalModelCapabilityProbe(result, drift);
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
            var runtimeIdentity = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(executablePath, null, ct);
            var modelIdentity = RuntimeIdentityFactory.CreateModelIdentity(modelPath, GgufMetadataReader.TryRead(modelPath));
            return entries.LastOrDefault(e =>
                e.RuntimeIdentity is not null && e.ModelIdentity is not null
                    ? e.RuntimeIdentity.IdentifiesSameRuntime(runtimeIdentity)
                        && string.Equals(e.ModelIdentity.StableId, modelIdentity.StableId, StringComparison.Ordinal)
                    : ModelPathSafety.AreSameLocalPath(e.ModelPath, identity.ModelPath)
                        && e.ModelSize == identity.ModelSize
                        && e.ModelMtime == identity.ModelMtime
                        && ModelPathSafety.AreSameLocalPath(e.ExecutablePath, identity.ExecutablePath)
                        && e.ExecutableSize == identity.ExecutableSize
                        && e.ExecutableMtime == identity.ExecutableMtime)?.Capabilities;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<LocalModelCapabilities?> TryGetPreviousSnapshotAsync(string modelPath, string executablePath, CancellationToken ct)
    {
        var cachePath = Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "capability-cache.json");
        if (!File.Exists(cachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(cachePath);
            var entries = await JsonSerializer.DeserializeAsync<List<CapabilityCacheEntry>>(stream, JsonOptions, ct) ?? [];
            return entries
                .Where(entry => ModelPathSafety.AreSameLocalPath(entry.ModelPath, modelPath))
                .OrderByDescending(entry => entry.Capabilities.ProbedAtUtc)
                .Select(entry => entry.Capabilities)
                .FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadHelpAsync(string configuredPath, CancellationToken ct)
    {
        var resolution = ExecutableResolver.Resolve(configuredPath, "llama-server");
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.Path))
            return null;

        try
        {
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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(
        string modelPath,
        string executablePath,
        LocalModelCapabilities capabilities,
        CancellationToken ct,
        RuntimeIdentityV2? knownRuntimeIdentity = null,
        ModelIdentityV2? knownModelIdentity = null)
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
        var runtimeIdentity = knownRuntimeIdentity
            ?? await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(executablePath, null, ct);
        var modelIdentity = knownModelIdentity
            ?? RuntimeIdentityFactory.CreateModelIdentity(modelPath, GgufMetadataReader.TryRead(modelPath));
        entries.RemoveAll(e => e.RuntimeIdentity is not null && e.ModelIdentity is not null
            ? e.RuntimeIdentity.IdentifiesSameRuntime(runtimeIdentity)
                && string.Equals(e.ModelIdentity.StableId, modelIdentity.StableId, StringComparison.Ordinal)
            : ModelPathSafety.AreSameLocalPath(e.ModelPath, identity.ModelPath)
                && ModelPathSafety.AreSameLocalPath(e.ExecutablePath, identity.ExecutablePath));
        entries.Add(new CapabilityCacheEntry(
            identity.ModelPath, identity.ModelSize, identity.ModelMtime,
            identity.ExecutablePath, identity.ExecutableSize, identity.ExecutableMtime,
            capabilities, runtimeIdentity, modelIdentity, 2));
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

    private static SpeculativeDrafterKind ClassifyDrafter(string type) =>
        type.StartsWith("ngram-", StringComparison.OrdinalIgnoreCase)
            ? SpeculativeDrafterKind.Self
            : string.Equals(type, "draft-mtp", StringComparison.OrdinalIgnoreCase)
                ? SpeculativeDrafterKind.EmbeddedMtp
                : type is not null && (string.Equals(type, "draft-simple", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "draft-eagle3", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "eagle3", StringComparison.OrdinalIgnoreCase))
                    ? SpeculativeDrafterKind.External
                    : SpeculativeDrafterKind.Unknown;

    private static bool IsConfigurableType(string type) =>
        type.StartsWith("ngram-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "draft-mtp", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "draft-simple", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "draft-eagle3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "eagle3", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<RuntimeCapabilityObservation> BuildObservations(
        LlamaRuntimeCapabilityFacts runtime,
        CapabilityEvidence mtp,
        CapabilityEvidence reasoning,
        CapabilityEvidence preserve,
        CapabilityEvidence vision,
        CapabilityEvidence promptThreads,
        CapabilityEvidence backendSampling,
        CapabilityEvidence perf,
        RuntimeIdentityV2 runtimeIdentity,
        ModelIdentityV2 modelIdentity,
        DateTime observedAtUtc)
    {
        var result = new List<RuntimeCapabilityObservation>();
        Add("speculative.draft.mtp", mtp, modelIdentity);
        Add("reasoning.separate-output", reasoning, modelIdentity);
        Add("reasoning.preserve-template", preserve, modelIdentity);
        Add("modality.vision", vision, modelIdentity);
        Add("runtime.prompt-threads", promptThreads, null);
        Add("runtime.prompt-cache.reused-token-counter",
            Unknown("runtime-no-stable-reuse-counter",
                "No stable machine-readable reused-token counter schema is proven for this runtime."), null);
        Add("runtime.backend-sampling", backendSampling, null);
        Add("runtime.performance-metrics", perf, null);

        foreach (var type in runtime.SpeculativeTypes)
        {
            var id = CapabilityIdForSpeculativeType(type);
            if (string.Equals(id, "speculative.draft.mtp", StringComparison.Ordinal))
                continue;
            var evidence = Available("runtime-spec-type", $"The selected runtime advertises speculative type {type}.");
            Add(id, evidence, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["runtime_type"] = type });
        }

        foreach (var type in runtime.SupportedKvCacheTypes ?? [])
        {
            var evidence = Available("runtime-kv-cache-type", $"The selected runtime advertises KV cache type {type}.");
            Add($"runtime.kv.type.{type.ToLowerInvariant()}", evidence, null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["cache_type"] = type });
        }

        var flash = runtime.HelpProbeSucceeded
            ? runtime.SupportsFlashAttention
                ? Available("runtime-flash-attention", "The selected runtime advertises --flash-attn.")
                : Unavailable("runtime-no-flash-attention", "The selected runtime help does not advertise --flash-attn.")
            : Unknown("runtime-help-unknown", "A successful runtime help probe is required.");
        Add("runtime.flash-attention", flash, null);

        var cpuMoe = runtime.HelpProbeSucceeded
            ? runtime.SupportsCpuMoePlacement
                ? Available("runtime-cpu-moe", "The selected runtime advertises CPU-MoE placement controls.")
                : Unavailable("runtime-no-cpu-moe", "The selected runtime help does not advertise CPU-MoE placement controls.")
            : Unknown("runtime-help-unknown", "A successful runtime help probe is required.");
        Add("runtime.moe.cpu-placement", cpuMoe, null);

        AddRuntimeFlag("runtime.speculative.parameter.n-max", runtime.SupportsSpeculativeNMax, "--spec-draft-n-max");
        AddRuntimeFlag("runtime.speculative.parameter.n-min", runtime.SupportsSpeculativeNMin, "--spec-draft-n-min");
        AddRuntimeFlag("runtime.speculative.parameter.p-min", runtime.SupportsSpeculativePMin, "--spec-draft-p-min");
        AddRuntimeFlag("runtime.speculative.parameter.draft-gpu-layers", runtime.SupportsSpeculativeDraftGpuLayers, "--spec-draft-ngl/-ngld");

        var launchCapabilities = NormalizeLaunchCapabilities(runtime.LaunchCapabilities);
        foreach (var capabilityId in LaunchCapabilityIds)
            Add(capabilityId, launchCapabilities[capabilityId], null);

        return result;

        void Add(string id, CapabilityEvidence evidence, ModelIdentityV2? observedModel, IReadOnlyDictionary<string, string>? parameters = null) =>
            result.Add(RuntimeCapabilityObservation.Create(
                id, evidence.State, evidence.EvidenceCode, evidence.Detail, runtimeIdentity,
                observedModel, parameters, observedAtUtc));

        void AddRuntimeFlag(string id, bool supported, string flag)
        {
            var evidence = runtime.HelpProbeSucceeded
                ? supported
                    ? Available("runtime-help-flag", $"The selected runtime advertises {flag}.")
                    : Unavailable("runtime-help-no-flag", $"The selected runtime help does not advertise {flag}.")
                : Unknown("runtime-help-unknown", "A successful runtime help probe is required.");
            Add(id, evidence, null, supported
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = flag }
                : null);
        }
    }

    public static string CapabilityIdForSpeculativeType(string type)
    {
        var normalized = Regex.Replace(type.Trim().ToLowerInvariant(), "[^a-z0-9]+", ".").Trim('.');
        return normalized switch
        {
            "draft.simple" => "speculative.draft.simple",
            "draft.mtp" => "speculative.draft.mtp",
            "ngram.mod" => "speculative.ngram.mod",
            "eagle3" or "draft.eagle3" => "speculative.draft.eagle3",
            "draft.dflash" => "speculative.draft.dflash",
            "draft.dspark" => "speculative.draft.dspark",
            "dflash" => "speculative.draft.dflash",
            _ => $"speculative.observed.{normalized}"
        };
    }

    private static bool HasPositiveDraftCount(JsonElement root)
    {
        if (TryPositiveNumber(root, "draft_n") || TryPositiveNumber(root, "draft_tokens"))
            return true;
        return root.TryGetProperty("speculative", out var speculative)
            && speculative.ValueKind == JsonValueKind.Object
            && (TryPositiveNumber(speculative, "draft_n") || TryPositiveNumber(speculative, "draft_tokens"));
    }

    private static bool TryPositiveNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
        && number > 0;

    private static bool HasCapability(JsonElement root, string capabilityId) =>
        root.TryGetProperty("capabilities", out var capabilities)
        && capabilities.ValueKind == JsonValueKind.Array
        && capabilities.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String
            && string.Equals(item.GetString(), capabilityId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<CapabilityDrift> Compare(LocalModelCapabilities? previous, LocalModelCapabilities current)
    {
        if (previous is null) return [];

        var drift = new List<CapabilityDrift>();
        CompareEvidence("embedded MTP", previous.EmbeddedMtp, current.EmbeddedMtp, drift);
        CompareEvidence("reasoning output", previous.ReasoningOutput, current.ReasoningOutput, drift);
        CompareEvidence("reasoning preservation", previous.ReasoningPreservation, current.ReasoningPreservation, drift);
        CompareEvidence("vision", previous.Vision, current.Vision, drift);
        CompareEvidence("prompt-processing threads", previous.RuntimeSurface?.PromptThreads, current.RuntimeSurface?.PromptThreads, drift);
        CompareEvidence("performance diagnostics", previous.RuntimeSurface?.PerformanceInstrumentation, current.RuntimeSurface?.PerformanceInstrumentation, drift);

        var before = previous.RuntimeSurface?.Speculative.Select(item => item.Type).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var after = current.RuntimeSurface?.Speculative.Select(item => item.Type).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var added in after.Except(before, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            drift.Add(new CapabilityDrift("speculative", $"llama-server now advertises speculative type {added}."));
        foreach (var removed in before.Except(after, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            drift.Add(new CapabilityDrift("speculative", $"llama-server no longer advertises speculative type {removed}.", true));

        foreach (var capabilityId in LaunchCapabilityIds)
        {
            var previousObservation = previous.Observations?.LastOrDefault(item =>
                string.Equals(item.CapabilityId, capabilityId, StringComparison.Ordinal));
            var currentObservation = current.Observations?.LastOrDefault(item =>
                string.Equals(item.CapabilityId, capabilityId, StringComparison.Ordinal));
            if (previousObservation is null || currentObservation is null
                || previousObservation.State == currentObservation.State)
                continue;

            drift.Add(new CapabilityDrift(
                capabilityId,
                $"{capabilityId} changed from {previousObservation.State} to {currentObservation.State}.",
                previousObservation.State == CapabilityState.Available
                    && currentObservation.State != CapabilityState.Available));
        }

        return drift;
    }

    private static void CompareEvidence(string name, CapabilityEvidence? previous, CapabilityEvidence? current, ICollection<CapabilityDrift> drift)
    {
        if (previous is null || current is null || previous.State == current.State)
            return;
        drift.Add(new CapabilityDrift(name, $"{name} changed from {previous.State} to {current.State}.", previous.State == CapabilityState.Available && current.State != CapabilityState.Available));
    }

    private static CapabilityEvidence HelpEvidence(string? help, bool supported, string option, string detail) =>
        string.IsNullOrWhiteSpace(help)
            ? Unknown("runtime-help-unknown", "A successful runtime help probe is required.")
            : supported
                ? Available("runtime-help-flag", $"{detail} ({option}).")
                : Unavailable("runtime-help-no-flag", $"The selected runtime help does not advertise {option}.");

    private static Dictionary<string, CapabilityEvidence> NormalizeLaunchCapabilities(
        IReadOnlyDictionary<string, CapabilityEvidence>? capabilities)
    {
        var normalized = new Dictionary<string, CapabilityEvidence>(StringComparer.Ordinal);
        foreach (var capabilityId in LaunchCapabilityIds)
        {
            normalized[capabilityId] = capabilities is not null && capabilities.TryGetValue(capabilityId, out var evidence)
                ? evidence
                : Unknown("runtime-capability-unknown", "No bounded evidence was supplied for this R32 launch capability.");
        }

        return normalized;
    }

    private static bool HasOption(string? help, string option) =>
        !string.IsNullOrWhiteSpace(help)
        && Regex.IsMatch(help, $@"(?<![A-Za-z0-9_-]){Regex.Escape(option)}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);

    private static string HelpOptionWindow(string? help, string option)
    {
        if (string.IsNullOrWhiteSpace(help) || !HasOption(help, option))
            return string.Empty;

        var match = Regex.Match(help, $@"(?<![A-Za-z0-9_-]){Regex.Escape(option)}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);
        return help[match.Index..Math.Min(help.Length, match.Index + 640)];
    }

    private static bool HasPhrase(string text, string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        var pattern = $@"(?<![A-Za-z0-9_-]){string.Join(@"\s+", words)}(?![A-Za-z0-9_-])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![A-Za-z0-9_-])['`]?{Regex.Escape(word)}['`]?(?![A-Za-z0-9_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> ParseEffectivePlacementFields(JsonElement root)
    {
        var knownFields = new[] { "n_gpu_layers", "gpu_layers", "split_mode", "tensor_split", "main_gpu", "fit", "fit_target", "fit_ctx" };
        return knownFields.Where(field => root.TryGetProperty(field, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Object)).ToArray();
    }

    private sealed record CapabilityCacheEntry(
        string ModelPath,
        long ModelSize,
        DateTime ModelMtime,
        string ExecutablePath,
        long ExecutableSize,
        DateTime ExecutableMtime,
        LocalModelCapabilities Capabilities,
        RuntimeIdentityV2? RuntimeIdentity = null,
        ModelIdentityV2? ModelIdentity = null,
        int SchemaVersion = 1);
}
