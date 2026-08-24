using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public static class LabRecipeCatalog
{
    private static readonly int[] ContextLadder = [2048, 4096, 8192, 16384, 32768, 65536, 131072];
    private static readonly string[] ReviewedKvTypes = ["f16", "q8_0", "q4_0"];

    public static LabRecipePlan Build(LabRecipeKind kind, ServerConfig source,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? gguf = null)
    {
        var baseline = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");
        var plan = kind switch
        {
            LabRecipeKind.EngineProfile => EngineProfile(baseline, gguf),
            LabRecipeKind.Context => Context(baseline),
            LabRecipeKind.KvCache => Kv(baseline, capabilities),
            LabRecipeKind.FlashAttention => Flash(baseline, capabilities),
            LabRecipeKind.CpuMoePlacement => CpuMoe(baseline, capabilities, gguf),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Validate(plan);
        return plan;
    }

    public static void Validate(LabRecipePlan plan)
    {
        if (plan.MaximumRunCount is < 2 or > 8 || plan.Candidates.Count is < 1 or > 7
            || plan.Candidates.Count + 1 > plan.MaximumRunCount)
            throw new InvalidOperationException("Lab recipes require a bounded baseline-plus-candidate run count between 2 and 8.");
        var ids = plan.Candidates.Select(item => item.Id).Append(plan.Baseline.Id).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Lab recipe configuration ids must be unique.");
        foreach (var candidate in plan.Candidates)
        {
            var differences = Differences(plan.Baseline, candidate);
            if (differences.Count == 0)
                throw new InvalidOperationException("A Lab recipe candidate must differ from its baseline.");
            if (!plan.TestsInteraction && differences.Except(AllowedFields(plan.Kind), StringComparer.Ordinal).Any())
                throw new InvalidOperationException("A one-at-a-time Lab recipe changed a field outside its declared dimension.");
        }
    }

    private static LabRecipePlan EngineProfile(LabConfiguration baseline, GgufModelInfo? gguf)
    {
        var layers = new List<int> { 0, -1 };
        if (gguf?.BlockCount is > 2) layers.Insert(1, gguf.BlockCount.Value / 2);
        var candidates = layers.Distinct().Where(value => value != baseline.GpuLayers)
            .Select((value, index) => baseline with { Id = $"gpu-{index + 1}", Label = value switch { 0 => "CPU", -1 => "All GPU layers", _ => $"{value} GPU layers" }, GpuLayers = value })
            .Take(3).ToArray();
        return Plan("engine-profile-v1", "GPU layer placement", LabRecipeKind.EngineProfile,
            CapabilityState.Available, "GPU layer placement is a first-class managed runtime setting.", baseline, candidates, []);
    }

    private static LabRecipePlan Context(LabConfiguration baseline)
    {
        var candidates = ContextLadder.OrderBy(value => Math.Abs((long)value - baseline.ContextSize))
            .Where(value => value != baseline.ContextSize).Take(3)
            .Order().Select((value, index) => baseline with
            {
                Id = $"context-{index + 1}", Label = $"Context {value:N0}", ContextSize = value
            }).ToArray();
        return Plan("context-v1", "Context size", LabRecipeKind.Context, CapabilityState.Available,
            "Context candidates come from the reviewed bounded ladder.", baseline, candidates, []);
    }

    private static LabRecipePlan Kv(LabConfiguration baseline, IReadOnlyCollection<RuntimeCapabilityObservation> capabilities)
    {
        var available = ReviewedKvTypes.Where(type => HasAvailable(capabilities, $"runtime.kv.type.{type}"))
            .ToArray();
        var baselineAvailable = HasAvailable(capabilities, $"runtime.kv.type.{baseline.KvCacheTypeK.ToLowerInvariant()}")
            && HasAvailable(capabilities, $"runtime.kv.type.{baseline.KvCacheTypeV.ToLowerInvariant()}");
        var candidates = available.Where(type => type != baseline.KvCacheTypeK || type != baseline.KvCacheTypeV)
            .Select((type, index) => baseline with
            {
                Id = $"kv-{index + 1}", Label = $"KV {type}", KvCacheTypeK = type, KvCacheTypeV = type
            }).Take(3).ToArray();
        var state = baselineAvailable && candidates.Length > 0 ? CapabilityState.Available : CapabilityState.Unknown;
        var detail = state == CapabilityState.Available
            ? "Every offered KV representation was advertised by this exact runtime."
            : "The exact runtime has not advertised both the baseline and an alternate reviewed KV representation.";
        return Plan("kv-cache-v1", "KV cache representation", LabRecipeKind.KvCache, state, detail,
            baseline, candidates.Length == 0 ? [baseline with
            {
                Id = "kv-unavailable", Label = "Unavailable placeholder",
                KvCacheTypeK = "unknown", KvCacheTypeV = "unknown"
            }] : candidates,
            available.Select(type => $"runtime.kv.type.{type}").ToArray(), lowBitQuality: true);
    }

    private static LabRecipePlan Flash(LabConfiguration baseline, IReadOnlyCollection<RuntimeCapabilityObservation> capabilities)
    {
        var capability = State(capabilities, "runtime.flash-attention");
        var candidates = new[] { "off", "on" }.Where(value => value != baseline.FlashAttention)
            .Select((value, index) => baseline with { Id = $"flash-{index + 1}", Label = $"Flash Attention {value}", FlashAttention = value })
            .ToArray();
        return Plan("flash-attention-v1", "Flash Attention", LabRecipeKind.FlashAttention,
            capability, Detail(capabilities, "runtime.flash-attention"), baseline, candidates, ["runtime.flash-attention"]);
    }

    private static LabRecipePlan CpuMoe(LabConfiguration baseline,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? gguf)
    {
        var capability = State(capabilities, "runtime.moe.cpu-placement");
        var values = new List<int> { 0, -1 };
        if (gguf?.BlockCount is > 2) values.Insert(1, gguf.BlockCount.Value / 2);
        var candidates = values.Distinct().Where(value => value != baseline.CpuMoeLayers)
            .Select((value, index) => baseline with
            {
                Id = $"cpu-moe-{index + 1}",
                Label = value switch { 0 => "No CPU expert placement", -1 => "All experts on CPU", _ => $"{value} expert layers on CPU" },
                CpuMoeLayers = value
            }).ToArray();
        return Plan("cpu-moe-placement-v1", "CPU-MoE placement", LabRecipeKind.CpuMoePlacement,
            capability, Detail(capabilities, "runtime.moe.cpu-placement"), baseline, candidates,
            ["runtime.moe.cpu-placement"]);
    }

    private static LabRecipePlan Plan(string id, string label, LabRecipeKind kind, CapabilityState state,
        string detail, LabConfiguration baseline, IReadOnlyList<LabConfiguration> candidates,
        IReadOnlyList<string> capabilities, bool lowBitQuality = false) => new(
            id, label, kind, state, detail, baseline, candidates, Math.Min(8, candidates.Count + 1), false,
            capabilities,
            ["prompt.tokens_per_second", "decode.tokens_per_second", "ttft.milliseconds", "memory.ram.observed", "memory.gpu.observed",
             "memory.ram.predicted", "memory.gpu.predicted", "context.accepted.tokens", .. lowBitQuality ? new[] { "quality.score" } : []],
            LabCorrectnessRequirement.ExactEquivalence);

    private static IReadOnlySet<string> AllowedFields(LabRecipeKind kind) => kind switch
    {
        LabRecipeKind.EngineProfile => new HashSet<string>([nameof(LabConfiguration.GpuLayers)], StringComparer.Ordinal),
        LabRecipeKind.Context => new HashSet<string>([nameof(LabConfiguration.ContextSize)], StringComparer.Ordinal),
        LabRecipeKind.KvCache => new HashSet<string>([nameof(LabConfiguration.KvCacheTypeK), nameof(LabConfiguration.KvCacheTypeV)], StringComparer.Ordinal),
        LabRecipeKind.FlashAttention => new HashSet<string>([nameof(LabConfiguration.FlashAttention)], StringComparer.Ordinal),
        LabRecipeKind.CpuMoePlacement => new HashSet<string>([nameof(LabConfiguration.CpuMoeLayers)], StringComparer.Ordinal),
        _ => new HashSet<string>(StringComparer.Ordinal)
    };

    private static IReadOnlyList<string> Differences(LabConfiguration left, LabConfiguration right)
    {
        var differences = new List<string>();
        Add(nameof(LabConfiguration.ContextSize), left.ContextSize, right.ContextSize);
        Add(nameof(LabConfiguration.GpuLayers), left.GpuLayers, right.GpuLayers);
        Add(nameof(LabConfiguration.Threads), left.Threads, right.Threads);
        Add(nameof(LabConfiguration.PromptThreads), left.PromptThreads, right.PromptThreads);
        Add(nameof(LabConfiguration.Slots), left.Slots, right.Slots);
        Add(nameof(LabConfiguration.KvCacheTypeK), left.KvCacheTypeK, right.KvCacheTypeK);
        Add(nameof(LabConfiguration.KvCacheTypeV), left.KvCacheTypeV, right.KvCacheTypeV);
        Add(nameof(LabConfiguration.FlashAttention), left.FlashAttention, right.FlashAttention);
        Add(nameof(LabConfiguration.CpuMoeLayers), left.CpuMoeLayers, right.CpuMoeLayers);
        Add(nameof(LabConfiguration.ExtraArgumentsSha256), left.ExtraArgumentsSha256, right.ExtraArgumentsSha256);
        return differences;
        void Add<T>(string field, T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) differences.Add(field); }
    }

    private static bool HasAvailable(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.Any(value => value.CapabilityId == id && value.State == CapabilityState.Available);
    private static CapabilityState State(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.FirstOrDefault(value => value.CapabilityId == id)?.State ?? CapabilityState.Unknown;
    private static string Detail(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.FirstOrDefault(value => value.CapabilityId == id)?.Detail ?? "The exact runtime capability is Unknown.";
}

public sealed record LabWorkloadRequest(
    string RunId,
    int Port,
    LabConfiguration Configuration,
    EmpiricalProfileFingerprintV2 Fingerprint,
    string Prompt,
    int Seed,
    int MaximumTokens,
    string CaseId,
    int Repetition,
    TimeSpan Timeout);

public sealed record LabWorkloadResult(
    IReadOnlyList<LabObservation> Observations,
    LabOutputEvidence? Output,
    string? Failure);

public interface ILabWorkloadExecutor
{
    Task<LabWorkloadResult> ExecuteAsync(LabWorkloadRequest request, CancellationToken ct = default);
}

public sealed class LlamaServerLabWorkloadExecutor : ILabWorkloadExecutor
{
    public async Task<LabWorkloadResult> ExecuteAsync(LabWorkloadRequest request, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = request.Timeout };
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await http.PostAsJsonAsync($"http://127.0.0.1:{request.Port}/v1/chat/completions", new
            {
                model = "local",
                messages = new[] { new { role = "user", content = request.Prompt } },
                temperature = 0,
                seed = request.Seed,
                max_tokens = request.MaximumTokens,
                stream = false
            }, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            timer.Stop();
            if (!response.IsSuccessStatusCode)
                return new([], null, $"HTTP {(int)response.StatusCode} from isolated workload endpoint.");
            return ParseSuccessfulResponse(request, body, timer.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new([], null, ex is TaskCanceledException && ct.IsCancellationRequested
                ? "Workload cancelled." : $"Isolated workload failed: {ex.Message}");
        }
    }

    public static LabWorkloadResult ParseSuccessfulResponse(LabWorkloadRequest request, string body, TimeSpan elapsed)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var text = ReadContent(root);
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
        var timings = root.TryGetProperty("timings", out var timingsElement) ? timingsElement : default;
        var observations = new List<LabObservation>
        {
            Metric(request, "request.total.milliseconds", elapsed.TotalMilliseconds, "ms", "wall-clock", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.tokens", Number(usage, "prompt_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "completion.tokens", Number(usage, "completion_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "tokens.served", Number(usage, "total_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "context.accepted.tokens", Number(usage, "prompt_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.tokens_per_second", Number(timings, "prompt_per_second"), "tokens/s", "runtime-timings", EvidenceOrigin.DirectObservation),
            Metric(request, "decode.tokens_per_second", Number(timings, "predicted_per_second"), "tokens/s", "runtime-timings", EvidenceOrigin.DirectObservation),
            Missing(request, "ttft.milliseconds", "ms", "The buffered llama-server response does not expose trustworthy TTFT.")
        };
        return new(observations, LabCorrectnessEvaluator.Capture(
            request.Configuration.Id, request.CaseId, request.Repetition, text), null);
    }

    private static string ReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0) return string.Empty;
        var first = choices[0];
        return first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty : string.Empty;
    }

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number : null;

    private static LabObservation Metric(LabWorkloadRequest request, string metric, double? value,
        string unit, string source, EvidenceOrigin origin) => new()
    {
        RunId = request.RunId, ConfigurationId = request.Configuration.Id, CaseId = request.CaseId,
        Repetition = request.Repetition, MetricId = metric, Value = value, Unit = unit, Source = source,
        Origin = origin, Trust = "TrustedRuntime", MissingReason = value.HasValue ? string.Empty : "The runtime response omitted this counter.",
        RuntimeFingerprint = request.Fingerprint.Runtime.StableId, ModelFingerprint = request.Fingerprint.Model.StableId,
        HardwareFingerprint = request.Fingerprint.Hardware.StableId,
        ConfigurationFingerprint = request.Fingerprint.Configuration.StableId
    };

    private static LabObservation Missing(LabWorkloadRequest request, string metric, string unit, string reason) =>
        Metric(request, metric, null, unit, "unavailable", EvidenceOrigin.DirectObservation) with { MissingReason = reason, Trust = "Unknown" };
}

public sealed class LabRecipeRunner
{
    private readonly ILabExperimentService _experiments;
    private readonly ILabWorkloadExecutor _workload;
    private readonly IRuntimeTelemetrySource _telemetry;
    private readonly ISystemInfoService _systemInfo;

    public LabRecipeRunner(ILabExperimentService experiments, ILabWorkloadExecutor workload,
        IRuntimeTelemetrySource telemetry, ISystemInfoService systemInfo)
    {
        _experiments = experiments;
        _workload = workload;
        _telemetry = telemetry;
        _systemInfo = systemInfo;
    }

    public async Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source,
        LocalModelCapabilities capabilities, string prompt, CancellationToken ct = default)
    {
        if (plan.Availability != CapabilityState.Available)
            throw new InvalidOperationException($"Recipe {plan.Label} is {plan.Availability}: {plan.AvailabilityDetail}");
        LabRecipeCatalog.Validate(plan);
        var definition = await _experiments.CreateDefinitionAsync(plan.Label, plan.Id, source,
            plan.Baseline, plan.Candidates, 3, plan.CorrectnessRequirement, ct);
        definition = definition with
        {
            WorkloadId = "greedy-chat-completion-v1",
            PromptHashes = [LabCanonicalJson.Hash(prompt)],
            SamplingPolicy = "greedy-temperature-zero",
            Seed = 1,
            Repetitions = 3,
            TimeoutSeconds = 180,
            RequiredMetrics = plan.RequiredMetrics,
            RequestedCapabilityIds = plan.RequiredCapabilityIds
        };
        var plannedPredictions = new Dictionary<string, ModelFitPrediction>(StringComparer.Ordinal);
        foreach (var configuration in plan.Candidates.Prepend(plan.Baseline))
        {
            var fingerprint = definition.ProfileFingerprint with
            {
                Configuration = definition.ConfigurationIdentities[configuration.Id]
            };
            plannedPredictions[configuration.Id] = await PredictModelAsync(source, configuration, fingerprint, capabilities, ct);
        }
        var run = await _experiments.StartAsync(definition, source, ct);
        if (run.Status != LabRunStatus.Running) return run;

        try
        {
            var observations = new List<LabObservation>();
            var outputs = new List<LabOutputEvidence>();
            var failures = new List<string>();
            var consecutiveFailures = 0;
            var configurations = plan.Candidates.Prepend(plan.Baseline).ToArray();
            foreach (var configuration in configurations)
            {
                if (configuration.Id != plan.Baseline.Id)
                {
                    try { run = await _experiments.SwitchConfigurationAsync(run.Id, source, configuration.Id, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add($"{configuration.Id}: launch failed: {ex.Message}");
                        if (++consecutiveFailures >= 2) break;
                        continue;
                    }
                }

                var fingerprint = definition.ProfileFingerprint with
                {
                    Configuration = definition.ConfigurationIdentities[configuration.Id]
                };
                observations.AddRange(PredictionObservations(run.Id, configuration.Id, fingerprint, plannedPredictions[configuration.Id]));
                if (plan.RequiredMetrics.Contains("quality.score", StringComparer.Ordinal))
                    observations.Add(MissingQualityObservation(run.Id, configuration.Id, fingerprint));
                for (var repetition = 0; repetition < definition.Repetitions; repetition++)
                {
                    var result = await _workload.ExecuteAsync(new LabWorkloadRequest(
                        run.Id, run.TemporaryPort!.Value, configuration, fingerprint, prompt,
                        definition.Seed, 128, "greedy-reference", repetition,
                        TimeSpan.FromSeconds(definition.TimeoutSeconds)), ct);
                    observations.AddRange(result.Observations);
                    if (result.Output is not null) outputs.Add(result.Output);
                    observations.AddRange(await CaptureTelemetryAsync(run, configuration, fingerprint, ct));
                    if (result.Failure is not null)
                    {
                        failures.Add($"{configuration.Id} repetition {repetition}: {result.Failure}");
                        consecutiveFailures++;
                        break;
                    }
                    consecutiveFailures = 0;
                }
                if (consecutiveFailures >= 2) break;
                if (configuration.Id != plan.Baseline.Id)
                {
                    var comparison = LabCorrectnessEvaluator.Compare(
                        outputs.FirstOrDefault(item => item.ConfigurationId == plan.Baseline.Id),
                        outputs.FirstOrDefault(item => item.ConfigurationId == configuration.Id));
                    if (comparison.State == LabEquivalenceState.Different) break;
                }
            }
            return await _experiments.CompleteAsync(run.Id, observations, outputs, failures, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await _experiments.CancelAsync(run.Id, CancellationToken.None);
        }
    }

    private async Task<ModelFitPrediction> PredictModelAsync(ServerConfig source,
        LabConfiguration configuration, EmpiricalProfileFingerprintV2 fingerprint,
        LocalModelCapabilities capabilities, CancellationToken ct)
    {
        var hardware = await _systemInfo.GetHardwareProfileAsync(ct);
        var info = GgufMetadataReader.TryRead(source.ModelPath);
        var modelBytes = File.Exists(source.ModelPath) ? new FileInfo(source.ModelPath).Length : 0;
        return ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            fingerprint, modelBytes, configuration.ContextSize, configuration.GpuLayers, configuration.Slots,
            configuration.KvCacheTypeK, configuration.KvCacheTypeV,
            KvState(capabilities, configuration.KvCacheTypeK), KvState(capabilities, configuration.KvCacheTypeV),
            KvCacheMath.HasSwaFull(source.ExtraArgs), configuration.CpuMoeLayers, hardware, []), info);
    }

    private static IReadOnlyList<LabObservation> PredictionObservations(string runId,
        string configurationId, EmpiricalProfileFingerprintV2 fingerprint, ModelFitPrediction prediction)
    {
        return
        [
            PredictionObservation(runId, configurationId, fingerprint, "memory.gpu.predicted", prediction.GpuRequiredBytes),
            PredictionObservation(runId, configurationId, fingerprint, "memory.ram.predicted", prediction.SystemRamRequiredBytes),
            new LabObservation
            {
                RunId = runId, ConfigurationId = configurationId, CaseId = "fit", Repetition = 0,
                MetricId = "fit.tier", Value = (double)prediction.Tier, Unit = "enum", Source = "gpu-fit-v1",
                Origin = EvidenceOrigin.DeterministicCalculation, Trust = "DeterministicCalculation",
                RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
                HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
            }
        ];
    }

    private async Task<IReadOnlyList<LabObservation>> CaptureTelemetryAsync(LabRunSnapshot run,
        LabConfiguration configuration, EmpiricalProfileFingerprintV2 fingerprint, CancellationToken ct)
    {
        if (run.RuntimeProcessId is not int processId || run.RuntimeProcessStartedAtUtc is not DateTime started)
            return [MissingObservation(run.Id, configuration.Id, fingerprint, "memory.ram.observed", "Runtime process identity is unavailable."),
                MissingObservation(run.Id, configuration.Id, fingerprint, "memory.gpu.observed", "Runtime process identity is unavailable.")];
        var samples = await _telemetry.CaptureAsync(new RuntimeTelemetryRequest(
            $"lab-{run.Id}-{configuration.Id}", processId, started, fingerprint.Runtime, fingerprint), ct);
        var ram = samples.FirstOrDefault(sample => sample.Metric == RuntimeTelemetryMetric.ProcessWorkingSetBytes);
        var gpu = samples.FirstOrDefault(sample => sample.Metric is RuntimeTelemetryMetric.ProcessGpuMemoryBytes or RuntimeTelemetryMetric.RuntimeReportedGpuMemoryBytes);
        return [TelemetryObservation(run.Id, configuration.Id, fingerprint, "memory.ram.observed", ram),
            TelemetryObservation(run.Id, configuration.Id, fingerprint, "memory.gpu.observed", gpu)];
    }

    private static CapabilityState KvState(LocalModelCapabilities capabilities, string type) =>
        capabilities.Observations?.FirstOrDefault(value => value.CapabilityId == $"runtime.kv.type.{type.ToLowerInvariant()}")?.State
        ?? CapabilityState.Unknown;

    private static LabObservation PredictionObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, long? value) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "fit", Repetition = 0,
        MetricId = metric, Value = value, Unit = "bytes", Source = "gpu-fit-v1",
        Origin = EvidenceOrigin.DeterministicCalculation, Trust = "DeterministicCalculation",
        MissingReason = value.HasValue ? string.Empty : "GPU Fit withheld this total because a material input is Unknown.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };

    private static LabObservation TelemetryObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, RuntimeTelemetrySample? sample) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "runtime-memory", Repetition = 0,
        MetricId = metric, Value = sample?.ValueBytes, Unit = "bytes",
        Source = sample?.Source.ToString() ?? "unavailable", Origin = EvidenceOrigin.DirectObservation,
        Trust = sample?.Trust.ToString() ?? "Unknown",
        MissingReason = sample?.ValueBytes.HasValue == true ? string.Empty : sample?.Detail ?? "No trustworthy process-scoped measurement is available.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };

    private static LabObservation MissingObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, string reason) =>
        TelemetryObservation(runId, configurationId, fingerprint, metric, null) with { MissingReason = reason };

    private static LabObservation MissingQualityObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "quality", Repetition = 0,
        MetricId = "quality.score", Value = null, Unit = "suite-score", Source = "unavailable",
        Origin = EvidenceOrigin.DirectObservation, Trust = "Unknown",
        MissingReason = "No referenced benchmark/quality run was supplied; loading success is not quality evidence.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };
}

public sealed class LabRecipeService : ILabRecipeService
{
    private readonly LocalModelCapabilityService _capabilities;
    private readonly LabRecipeRunner _runner;

    public LabRecipeService(LocalModelCapabilityService capabilities, LabRecipeRunner runner)
    {
        _capabilities = capabilities;
        _runner = runner;
    }

    public async Task<IReadOnlyList<LabRecipePlan>> InspectAsync(ServerConfig source, CancellationToken ct = default)
    {
        var capabilities = await _capabilities.ProbeAsync(source.ModelPath, source.ExecutablePath, ct: ct);
        var observations = capabilities.Observations ?? [];
        var gguf = GgufMetadataReader.TryRead(source.ModelPath);
        return Enum.GetValues<LabRecipeKind>()
            .Select(kind => LabRecipeCatalog.Build(kind, source, observations, gguf))
            .ToArray();
    }

    public async Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source,
        string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4096)
            throw new InvalidOperationException("A Lab recipe prompt must contain 1 to 4,096 characters.");
        var capabilities = await _capabilities.ProbeAsync(source.ModelPath, source.ExecutablePath, ct: ct);
        return await _runner.RunAsync(plan, source, capabilities, prompt, ct);
    }
}
