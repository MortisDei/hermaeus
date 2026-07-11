using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using Microsoft.Data.Sqlite;

namespace Aether.Tests
{
    internal static class Helpers
    {
        public static SettingsService NewSettings(TempDir temp) => new(temp.PathFor("settings/settings.json"));

        public static SettingsViewModel NewSettingsViewModel(ISettingsService settings, ISecretStore secrets) =>
            new(settings, new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(), new BackupService(settings), secrets, new XttsProcessManager(), new KokoroProcessManager(), new LocalApiProcessManager(), new LocalAiSetupService(new PythonHealthValidator()), new TrustService());

        public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }

        public static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
        }

        public static void True(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }

        public static void False(bool value, string message) => True(!value, message);

        public static void ContainsInOrder(IReadOnlyList<string> values, string first, string second, string message)
        {
            for (var i = 0; i < values.Count - 1; i++)
            {
                if (values[i] == first && values[i + 1] == second)
                    return;
            }

            throw new InvalidOperationException(message);
        }
    }

    sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"aether-tests-{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(_root);

        public string PathFor(string relative) => Path.Combine(_root, relative);

        public void Dispose()
        {
            if (!Directory.Exists(_root))
                return;

            // Pooled SQLite connections keep file handles open on Windows;
            // clear pools so temp databases can be deleted, and retry once
            // to absorb slow handle release.
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(150);
                Directory.Delete(_root, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(150);
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    sealed class FakeLlm : ILlmService
    {
        public string ProviderName => "Fake";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake", Name = "Fake", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("local ");
            yield return new LlmStreamEvent("ready alpha beta 42");
        }
    }

    sealed class CapturingLlm : ILlmService
    {
        public string ProviderName => "Capture";
        public bool IsConfigured => true;
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = Array.Empty<ChatMessage>();
        public LlmChatOptions? LastOptions { get; private set; }

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "capture", Name = "Capture", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("captured");
        }
    }

    sealed class UsageLlm : ILlmService
    {
        public string ProviderName => "Usage";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "usage", Name = "Usage", Provider = "Test", DefaultContextSize = 100 } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("ok");
            yield return new LlmStreamEvent(Usage: new ChatTokenUsage(30, 10, 40), IsFinal: true);
        }
    }

    sealed class MemoryMarkerLlm : ILlmService
    {
        private readonly string _response;

        public MemoryMarkerLlm(string response)
        {
            _response = response;
        }

        public string ProviderName => "MemoryMarker";
        public bool IsConfigured => true;

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "memory-test", Name = "Memory Test", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(_response);
        }
    }

    sealed class FakeTts : ITtsService
    {
        public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
            Task.FromResult(displayName);
        public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(new List<string> { "default" });
    }

    sealed class FakeVoiceProvider : IVoiceProvider
    {
        public VoiceProvider Id => VoiceProvider.Kokoro;
        public string DisplayName => "Fake Voice";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
        public (int Major, int Minor)? RequiredPythonVersion => (3, 12);
        public bool IsInstalled => true;

        public VoiceProviderDetection Detect() => new VoiceProviderDetection(true, "Available", "Fake provider available", null);

        public VoiceInstallPlan InstallPlan() => new VoiceInstallPlan("No install needed", new List<VoiceInstallStep>(), "Low");

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "Healthy", "Fake provider is healthy"));

        public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VoiceDefinition>>(new List<VoiceDefinition> { new VoiceDefinition("default", "Default", "English", false) });

        public Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default) =>
            Task.FromResult(new VoiceSynthesisResult(true, "Synthesis complete", "/tmp/audio.wav"));
    }

    sealed class FakeVoiceProviderRegistry : IVoiceProviderRegistry
    {
        private readonly ISettingsService _settings;
        private readonly KokoroVoiceProvider _kokoroProvider;

        public FakeVoiceProviderRegistry(ISettingsService settings)
        {
            _settings = settings;
            _kokoroProvider = new KokoroVoiceProvider(settings);
        }

        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
            new List<VoiceProviderInfo>
            {
                new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro", "Fast local readback.", VoiceProviderCategory.Recommended, true, VoiceCapability.TextToSpeech | VoiceCapability.Local),
                new VoiceProviderInfo(VoiceProvider.F5Tts, "F5-TTS", "Advanced cloning.", VoiceProviderCategory.Advanced, false, VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Remote),
                new VoiceProviderInfo(VoiceProvider.XttsV2, "XTTS v2", "Legacy cloning.", VoiceProviderCategory.Legacy, true, VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Local)
            };

        public VoiceProvider GetActiveProvider() => Enum.TryParse<VoiceProvider>(_settings.Settings.Tts.VoiceProvider, out var provider)
            ? provider
            : VoiceProvider.Kokoro;

        public IVoiceProvider GetActiveVoiceProvider() => new FakeVoiceProvider();

        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => provider == VoiceProvider.Kokoro ? _kokoroProvider : new FakeVoiceProvider();

        public Task SetActiveProviderAsync(VoiceProvider provider)
        {
            _settings.Settings.Tts.VoiceProvider = provider.ToString();
            return Task.CompletedTask;
        }

        public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

        public ITtsService GetActiveTtsService() => new FakeTts();
    }

    sealed class FakeVoiceProviderRegistryLimited : IVoiceProviderRegistry
    {
        private readonly ISettingsService _settings;

        public FakeVoiceProviderRegistryLimited(ISettingsService settings) => _settings = settings;

        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
            new List<VoiceProviderInfo>
            {
                // XTTS v2 present but intentionally missing TextToSpeech and Local capabilities
                new VoiceProviderInfo(VoiceProvider.XttsV2, "XTTS v2", "Legacy cloning but missing flags.", VoiceProviderCategory.Legacy, true, VoiceCapability.VoiceCloning)
            };

        public VoiceProvider GetActiveProvider() => Enum.TryParse<VoiceProvider>(_settings.Settings.Tts.VoiceProvider, out var provider)
            ? provider
            : VoiceProvider.Kokoro;

        public IVoiceProvider GetActiveVoiceProvider() => new FakeVoiceProvider();

        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => new FakeVoiceProvider();

        public Task SetActiveProviderAsync(VoiceProvider provider)
        {
            _settings.Settings.Tts.VoiceProvider = provider.ToString();
            return Task.CompletedTask;
        }

        public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

        public ITtsService GetActiveTtsService() => new FakeTts();
    }

    sealed class FakeVoiceProviderRegistryKokoroInstall : IVoiceProviderRegistry
    {
        private readonly ISettingsService _settings;
        private readonly KokoroVoiceProvider _kokoro;

        public FakeVoiceProviderRegistryKokoroInstall(ISettingsService settings)
        {
            _settings = settings;
            _kokoro = new KokoroVoiceProvider(settings);
        }

        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
            new List<VoiceProviderInfo>
            {
                new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro", "Fast local readback.", VoiceProviderCategory.Recommended, false, VoiceCapability.TextToSpeech | VoiceCapability.Local)
            };

        public VoiceProvider GetActiveProvider() => VoiceProvider.Kokoro;

        public IVoiceProvider GetActiveVoiceProvider() => _kokoro;

        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => _kokoro;

        public Task SetActiveProviderAsync(VoiceProvider provider)
        {
            _settings.Settings.Tts.VoiceProvider = provider.ToString();
            return Task.CompletedTask;
        }

        public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

        public ITtsService GetActiveTtsService() => _kokoro;
    }

    sealed class CapturingRangeHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingRangeHttpHandler(string content)
        {
            _content = System.Text.Encoding.UTF8.GetBytes(content);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var body = _content;
            var response = new HttpResponseMessage(request.Headers.Range is null
                ? HttpStatusCode.OK
                : HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentLength = body.Length;
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }
    }

    sealed class FakeToasts : IToastService
    {
        public event Action<ToastMessage>? ToastRaised;
        public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) =>
            ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
    }

    sealed class FakeSecretStore : ISecretStore
    {
        public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) =>
            Task.FromResult($"secret:{name}");
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) =>
            Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Fake");
    }

    sealed class FakeEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 4;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, text.Length % 7, text.Length % 11, 0.5f });

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
    }

    sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public FakeHttpHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "text/html")
            };
            return Task.FromResult(response);
        }
    }

    sealed class FakeAgentContextBuilder : IAgentContextBuilder
    {
        public Task<AgentContextPack> BuildAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct = default) =>
            Task.FromResult(new AgentContextPack
            {
                CurrentGoal = state.Goal,
                ActiveStep = state.ActiveStep,
                Constraints = state.Constraints,
                TaskStateSummary = state.Summary,
                RetrievedFiles =
                [
                    new AgentRetrievedItem("workspace", "README.md", "agent docs", 0, DateTime.UtcNow)
                ]
            });
    }

    sealed class FakeAgentLlm : ILlmService
    {
        public string ProviderName => "FakeAgent";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("""
                {
                  "thought_summary": "Read the available context and found the docs.",
                  "current_step": "Wait for approval before any write.",
                  "next_action": {
                    "type": "tool",
                    "tool_name": "draft_patch",
                    "arguments": { "path": "README.md" },
                    "requires_approval": true,
                    "risk_level": "medium"
                  },
                  "state_update": {
                    "completed": ["inspected context"],
                    "pending": ["draft patch"],
                    "new_facts": ["workspace has README"],
                    "blockers": []
                  },
                  "user_message": "I found the relevant docs and can draft a patch for review."
                }
                """);
        }
    }

    /// <summary>Returns a fixed sequence of planner responses, one per call, for testing multi-step loops.</summary>
    sealed class FakeSequencedAgentLlm : ILlmService
    {
        private readonly Queue<string> _responses;

        public FakeSequencedAgentLlm(IEnumerable<string> responses) => _responses = new Queue<string>(responses);

        public string ProviderName => "FakeSequencedAgent";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-sequenced-agent", Name = "Fake Sequenced Agent", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(_responses.Count > 0 ? _responses.Dequeue() : _finalResponse);
        }

        private const string _finalResponse = """
            {
              "thought_summary": "Nothing left to do.",
              "current_step": "Done.",
              "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
              "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
              "user_message": "Finished."
            }
            """;
    }

    /// <summary>Simulates a provider with native tool-calling support: no JSON text, just a structured tool call.</summary>
    sealed class FakeToolCallingAgentLlm : ILlmService
    {
        private readonly string _toolName;
        private readonly string _argumentsJson;

        public FakeToolCallingAgentLlm(string toolName, string argumentsJson)
        {
            _toolName = toolName;
            _argumentsJson = argumentsJson;
        }

        public string ProviderName => "FakeToolCallingAgent";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-tool-calling-agent", Name = "Fake Tool Calling Agent", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(
                IsFinal: true,
                ToolCalls: [new LlmToolCallRequest("call_1", _toolName, _argumentsJson)]);
        }
    }

    sealed class FakeSystemInfo : ISystemInfoService
    {
        public Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default) => Task.FromResult(new SystemSnapshot
        {
            AppVersion = "test",
            Components =
            [
                new ComponentStatus { Name = "Fake component", Status = "Ready", Detail = "test" }
            ]
        });
    }

    sealed class FakeEvalStore : IEvalStore
    {
        private readonly Dictionary<string, EvalRun> _runs = new();

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveRunAsync(EvalRun run, CancellationToken ct = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EvalRun>> GetRunsAsync(EvalMode? mode = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EvalRun>>(_runs.Values.Where(r => mode is null || r.Mode == mode).ToList());

        public Task<EvalRun?> GetRunAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_runs.GetValueOrDefault(id));
    }

    sealed class FakeDoctorService : IDoctorService
    {
        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new DoctorReport([], DateTime.UtcNow, "ok"));

        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
    }

    internal static class PdfHelpers
    {
        public static void WriteSimplePdf(string path, string text)
        {
            var safeText = EscapePdfText(text);
            var content = $"BT /F1 24 Tf 72 100 Td ({safeText}) Tj ET";

            var objects = new[]
            {
                "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
                "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
                "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n",
                $"4 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n",
                "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
            };

            var offsets = new List<int> { 0 };
            var builder = new StringBuilder();
            builder.Append("%PDF-1.4\n");

            foreach (var obj in objects)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
                builder.Append(obj);
            }

            var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
            builder.AppendLine("xref");
            builder.AppendLine("0 6");
            builder.AppendLine("0000000000 65535 f ");
            for (var i = 1; i <= 5; i++)
                builder.AppendLine($"{offsets[i]:0000000000} 00000 n ");
            builder.AppendLine("trailer");
            builder.AppendLine("<< /Size 6 /Root 1 0 R >>");
            builder.AppendLine("startxref");
            builder.AppendLine(xrefOffset.ToString());
            builder.Append("%%EOF");

            File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
        }

        private static string EscapePdfText(string text) => text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }
}
