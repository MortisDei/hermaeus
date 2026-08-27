using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Tests
{
    internal static class Helpers
    {
        public static SettingsService NewSettings(TempDir temp) => new(temp.PathFor("settings/settings.json"));

        public static TtsSettingsViewModel NewTtsSettingsViewModel(ISettingsService settings) =>
            new(new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(), new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

        public static SettingsViewModel NewSettingsViewModel(
            ISettingsService settings,
            ISecretStore secrets,
            TtsSettingsViewModel? tts = null,
            Func<TimeSpan, CancellationToken, Task>? autoSaveDelay = null,
            Action? autoSaveLifecycleCompleted = null) =>
            new(settings, tts ?? NewTtsSettingsViewModel(settings), new FakeToasts(), new BackupService(settings), secrets, new XttsProcessManager(), new KokoroProcessManager(), new LocalApiProcessManager(), new LocalAiSetupService(new PythonHealthValidator()), new TrustService(), autoSaveDelay: autoSaveDelay, autoSaveLifecycleCompleted: autoSaveLifecycleCompleted);

        public static ServicesViewModel NewServicesViewModel(ISettingsService settings, TtsSettingsViewModel? tts = null) =>
            new(settings, new RuntimeProfileService(settings), new FakeToasts(), new RedactionService(), new TrustService(), new RuntimeLogService(settings), tts ?? NewTtsSettingsViewModel(settings));

        /// <summary>
        /// Polls until <paramref name="condition"/> holds, then asserts it.
        ///
        /// Needed because <c>RunOnUi</c> posts rather than running inline under
        /// xUnit's AsyncTestSyncContext: a handler fired from deep inside an
        /// awaited call chain (e.g. ISettingsService.SettingsChanged during
        /// SaveAsync) can land after the awaited call already returned. See
        /// r12 02-async-and-threading.md.
        ///
        /// r25 consolidated four near-identical private copies of this. Two of
        /// them returned silently on timeout, which turned a genuine failure into
        /// a confusing downstream assertion or, worse, a silent pass. This one
        /// always asserts, and says what it was waiting for.
        /// </summary>
        public static async Task WaitForAsync(
            Func<bool> condition, string description = "condition", int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (Evaluate(condition))
                    return;
                await Task.Delay(10);
            }

            True(Evaluate(condition), $"{description} was not met within {timeoutMs}ms.");
        }

        /// <summary>A condition that reads a UI-bound collection can throw while that
        /// collection is mid-mutation; that means "not settled yet", not "failed".</summary>
        private static bool Evaluate(Func<bool> condition)
        {
            try
            {
                return condition();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

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

        public static void NotEqual<T>(T notExpected, T actual, string message)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new InvalidOperationException($"{message}. Did not expect '{actual}'.");
        }

        public static void True(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }

        public static void False(bool value, string message) => True(!value, message);

        /// <summary>
        /// The fingerprint AppendApprovalAsync's caller is expected to pass
        /// (r23 4.1): reloads the task fresh and computes it the same way the
        /// real UI does, so happy-path tests do not need to hand-compute one.
        /// </summary>
        public static async Task<string> PendingFingerprintAsync(IAgentTaskStateStore store, string taskId, CancellationToken ct = default)
        {
            var state = await store.LoadAsync(taskId, ct);
            return AgentApprovalFingerprint.Resolve(state?.PendingToolAction);
        }

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

    /// <summary>
    /// Counts <see cref="Post"/> calls and executes the callback inline (so
    /// tests stay synchronous) - used to assert RunOnUi coalescing/posting
    /// behavior (r12 02-async-and-threading.md 2.4, 2.6) without a real
    /// message loop.
    /// </summary>
    sealed class CountingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>
    /// Queues posted callbacks instead of running them inline, so a test can
    /// issue several Post calls back-to-back (simulating overlapping
    /// background-thread callbacks racing a UI thread that has not yet had a
    /// turn to process its queue) and then drain them explicitly to observe
    /// coalescing behavior (r12 02-async-and-threading.md 2.4).
    /// </summary>
    sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        // A real SynchronizationContext is posted to from arbitrary threads, and
        // this one is too: a view model that finishes background work (a GGUF
        // header read on the thread pool, a process status callback) marshals
        // back through Post while the test thread is inside DrainAll. An
        // unsynchronized Queue<T> mutated concurrently corrupts its internal
        // array and surfaces as a NullReferenceException out of Dequeue, which
        // is an intermittent failure with no connection to the test that
        // observes it. Every access is under one lock, and callbacks run outside
        // it so a callback that posts again cannot deadlock.
        private readonly Lock _sync = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private int _postCount;

        public int PostCount
        {
            get { lock (_sync) return _postCount; }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_sync)
            {
                _postCount++;
                _queue.Enqueue((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        /// <summary>
        /// Runs queued callbacks until the queue is empty, including anything a
        /// callback posts while draining, which is the behaviour the coalescing
        /// tests rely on (r12 02-async-and-threading.md 2.4).
        /// </summary>
        public void DrainAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) next;
                lock (_sync)
                {
                    if (_queue.Count == 0)
                        return;
                    next = _queue.Dequeue();
                }

                next.Callback(next.State);
            }
        }
    }

    sealed class TempDir : IDisposable
    {
        // r25: temp roots that were still locked when their test finished. Deleted
        // once at process exit instead of being waited on inside the test, so
        // cleanup never costs test time and never fails a test.
        private static readonly System.Collections.Concurrent.ConcurrentBag<string> _deferred = [];

        static TempDir() =>
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                SqliteConnection.ClearAllPools();
                foreach (var path in _deferred)
                    TryDelete(path);
            };

        /// <summary>
        /// RUNNER_TEMP when a GitHub Actions runner set it, Path.GetTempPath()
        /// otherwise. The runner's own temp directory is the path CI excludes
        /// from Defender (r29 doc 04 4.2) and the path the runner cleans between
        /// jobs; Path.GetTempPath() is not necessarily either of those. A
        /// developer machine has no RUNNER_TEMP and is unaffected.
        /// </summary>
        private static string TempRoot()
        {
            var runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            return !string.IsNullOrWhiteSpace(runnerTemp) && Directory.Exists(runnerTemp)
                ? runnerTemp
                : Path.GetTempPath();
        }

        private readonly string _root = Path.Combine(TempRoot(), $"hermaeus-tests-{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(_root);

        public string PathFor(string relative) => Path.Combine(_root, relative);

        /// <summary>
        /// Pooled SQLite connections keep file handles open on Windows, and a
        /// fire-and-forget background task a test never awaited (e.g.
        /// ChatViewModel's memory-status refresh) can still be mid-query against a
        /// db under this root when the test method returns. An atomic temp+move
        /// write to a plain file (Agent's task_state.json) can also still be
        /// settling, and CI's shared Windows runners hold a freshly-written file
        /// open a beat longer than a dev machine does (observed in r23/r24 CI).
        ///
        /// This used to retry with a growing backoff, up to 10 attempts and 3.4
        /// SECONDS of Thread.Sleep per temp root, and still rethrew on the last
        /// attempt. That made cleanup the single largest cost in the suite
        /// (AgentPatchReviewServiceTests averaged 1.7s per test doing almost no
        /// work) while leaving the failure mode it was added to prevent.
        ///
        /// Deleting a temp directory is housekeeping, not an assertion: a leftover
        /// directory under %TEMP% harms nothing, and no test result depends on it.
        /// So try briefly, then hand it to process exit and move on.
        /// </summary>
        public void Dispose()
        {
            if (!Directory.Exists(_root))
                return;

            SqliteConnection.ClearAllPools();
            if (TryDelete(_root))
                return;

            // One short breath covers the overwhelmingly common case: a handle
            // that is already closing as the test returns.
            Thread.Sleep(25);
            if (TryDelete(_root))
                return;

            _deferred.Add(_root);
        }

        private static bool TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
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

    /// <summary>r17 02-benchmark-truth.md 2.1/2.6: emits a final event carrying
    /// <see cref="ChatServerTimings"/> (predicted 20 tokens / 200 ms, prompt 10 tokens / 100 ms -
    /// round numbers so tok/s math is exact) and records every call's <see cref="LlmChatOptions"/>
    /// so tests can assert what each benchmark iteration actually sent.</summary>
    sealed class FakeCapturingTimingsLlm : ILlmService
    {
        public string ProviderName => "FakeTimings";
        public bool IsConfigured => true;
        public List<LlmChatOptions> CapturedOptions { get; } = new();

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "timings", Name = "Timings", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CapturedOptions.Add(options ?? LlmChatOptions.Default);
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("hello");
            yield return new LlmStreamEvent(string.Empty, IsFinal: true, ServerTimings: new ChatServerTimings(10, 100, 20, 200));
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

    /// <summary>In-memory <see cref="IConversationStore"/> whose SaveAsync can be made to throw on demand (r12 02-async-and-threading.md 2.2).</summary>
    sealed class ThrowingSaveConversationStore : IConversationStore
    {
        private readonly Dictionary<string, Conversation> _items = new();
        public bool ThrowOnSave { get; set; }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<List<Conversation>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.ToList());
        public Task<Conversation?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));
        public Task SaveAsync(Conversation conversation, CancellationToken ct = default)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("simulated locked conversation database");
            _items[conversation.Id] = conversation;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _items.Remove(id);
            return Task.CompletedTask;
        }
        public Task<List<Conversation>> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    sealed class FakeConversationMemoryService : IConversationMemoryService
    {
        public Task RunAutoSummaryAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ApplyInjectedMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, CancellationToken ct = default) =>
            Task.FromResult(responseText);
        public Task<string> ApplyMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, string? conversationId, int maxNewMemories = 3, CancellationToken ct = default) =>
            Task.FromResult(responseText);
    }

    /// <summary>
    /// In-memory <see cref="IMemoryStore"/> for tests that construct a
    /// <see cref="ChatViewModel"/> but do not exercise memory persistence
    /// itself. A real <c>MemoryStore</c> opens a SQLite connection to
    /// memories.db from ChatViewModel's fire-and-forget
    /// <c>RefreshMemoryStatusAsync</c> background task (constructor,
    /// NewConversation, LoadConversationAsync); that task is never awaited
    /// by design (a status label should not block UI actions), so a test's
    /// own <see cref="TempDir"/> can be disposed while it is still mid-flight,
    /// racing SQLite's pooled file handle against the temp directory delete.
    /// Using this fake instead removes that race at the source rather than
    /// papering over it with longer retries.
    /// </summary>
    sealed class FakeMemoryStore : IMemoryStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Memory?>(null);
        public Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task SaveAsync(Memory memory, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Dictionary<string, int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(conversationIds.ToDictionary(id => id, _ => 0));
        public Task MarkRecalledAsync(IEnumerable<string> ids, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> ArchiveStaleMemoriesAsync(double importanceFloor = 0.05, int unrecalledForDays = 180, CancellationToken ct = default) => Task.FromResult(0);
        public Task RunEmbeddingBackfillAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetEmbeddingMismatchCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ClearMismatchedEmbeddingsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>Returns a scripted list of models on every call, optionally gated behind a delay so tests can control interleaving (r12 02-async-and-threading.md 2.5).</summary>
    sealed class ScriptedModelsLlm : ILlmService
    {
        private readonly Func<List<LlmModel>> _modelsFactory;
        public TaskCompletionSource? DelayGate { get; set; }
        public int GetModelsCallCount { get; private set; }

        public ScriptedModelsLlm(Func<List<LlmModel>> modelsFactory) => _modelsFactory = modelsFactory;

        public string ProviderName => "Scripted";
        public bool IsConfigured => true;

        public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
        {
            GetModelsCallCount++;
            if (DelayGate is not null)
                await DelayGate.Task;
            return _modelsFactory();
        }

        public void InvalidateModelCache() { }

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("ok");
        }
    }

    sealed class FakeToasts : IToastService
    {
        public event Action<ToastMessage>? ToastRaised;
        public ToastMessage? LastShown { get; private set; }
        public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500)
        {
            var toast = new ToastMessage(title, message, kind, durationMs);
            LastShown = toast;
            ToastRaised?.Invoke(toast);
        }
    }

    sealed class FakeVoiceOrchestrator : IVoiceOrchestrator
    {
        public List<VoiceUtterance> Enqueued { get; } = [];
        public List<VoiceChannel> StoppedChannels { get; } = [];
        public bool IsMuted { get; set; }
        public bool IsSpeaking { get; set; }
        public event Action<VoiceChannel, string>? UtteranceStarted;
        public event Action<VoiceChannel>? UtteranceCompleted;

        public Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default)
        {
            Enqueued.Add(utterance);
            IsSpeaking = true;
            UtteranceStarted?.Invoke(utterance.Channel, utterance.Text);
            return Task.CompletedTask;
        }

        public void StopChannel(VoiceChannel channel)
        {
            StoppedChannels.Add(channel);
            IsSpeaking = false;
            UtteranceCompleted?.Invoke(channel);
        }

        public void StopAll() => IsSpeaking = false;

        /// <summary>Test hook: simulates the orchestrator finishing an utterance on its own (not via StopChannel).</summary>
        public void RaiseUtteranceCompleted(VoiceChannel channel)
        {
            IsSpeaking = false;
            UtteranceCompleted?.Invoke(channel);
        }
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
            Task.FromResult(new List<LlmModel>
            {
                new() { Id = "fake-sequenced-agent", Name = "Fake Sequenced Agent", Provider = "Test" },
                new() { Id = "test-model", Name = "Scenario Test Model", Provider = "Test" }
            });

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

    /// <summary>Like <see cref="FakeSequencedAgentLlm"/>, but once the scripted queue is exhausted it throws instead of returning a canned final answer - for testing that a step calling into a failed model call is handled, not just a parse failure.</summary>
    sealed class FakeSequencedThenThrowingAgentLlm : ILlmService
    {
        private readonly Queue<string> _responses;

        public FakeSequencedThenThrowingAgentLlm(IEnumerable<string> responses) => _responses = new Queue<string>(responses);

        public string ProviderName => "FakeSequencedThenThrowingAgent";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-sequenced-then-throwing-agent", Name = "Fake Sequenced Then Throwing Agent", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            if (_responses.Count == 0)
                throw new InvalidOperationException("Simulated provider failure.");
            yield return new LlmStreamEvent(_responses.Dequeue());
        }
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

    /// <summary>Simulates a tool-calling provider that also streams prose alongside two tool calls in one turn.</summary>
    sealed class FakeMultiToolCallingAgentLlm : ILlmService
    {
        public string ProviderName => "FakeMultiToolCallingAgent";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-multi-tool-calling-agent", Name = "Fake Multi Tool Calling Agent", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("I will list files, then read the readme.");
            yield return new LlmStreamEvent(
                IsFinal: true,
                ToolCalls:
                [
                    new LlmToolCallRequest("call_1", "list_files", "{}"),
                    new LlmToolCallRequest("call_2", "read_file", """{"relative_path":"README.md"}""")
                ]);
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

        public Task<HardwareProfile> GetHardwareProfileAsync(CancellationToken ct = default) =>
            Task.FromResult(new HardwareProfile(0, 0, null));
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

        public Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Records native-Kokoro install invocations so the wizard's "Install now" (r8 2.2)
    /// can be asserted as calling the exact same IDoctorService entry point as Settings/Doctor.</summary>
    sealed class FakeDoctorServiceWithKokoroInstallTracking : IDoctorService
    {
        public int InstallCallCount { get; private set; }
        public bool InstallResult { get; set; } = true;
        public List<string> ProgressMessages { get; } = [];

        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new DoctorReport([], DateTime.UtcNow, "ok"));

        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(InstallResult);

        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default)
        {
            InstallCallCount++;
            var message = "Installing Kokoro (native)...";
            ProgressMessages.Add(message);
            progress.Report(message);
            return Task.FromResult(InstallResult);
        }

        public Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(InstallResult);
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
