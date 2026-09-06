using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Rag.Embeddings;

/// <summary>
/// Calls the llama.cpp server's /v1/embeddings endpoint (OpenAI-compatible).
/// Default: RAG EmbeddingBaseUrl, which now defaults to a separate localhost port.
/// Model: Qwen3-Embedding-0.6B by default (1024 dims). Existing Nomic
/// installations remain supported (768 dims).
/// </summary>
public sealed class LlamaCppEmbeddingService : IEmbeddingService, IBackgroundEmbeddingService, IDisposable
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly IRuntimeLogService? _runtimeLogs;
    private bool _fallbackLogged;
    private readonly object _fallbackLogGate = new();
    private readonly ConcurrentDictionary<EmbeddingRequestKey, Lazy<SharedEmbeddingRequest>> _inFlightQueries = new();
    private readonly PriorityEmbeddingGate _physicalRequestGate = new();
    private int _dimensions;

    /// <summary>
    /// The last observed embedding dimensionality. A known default is supplied
    /// before the first response so the local API remains useful at startup;
    /// the server response remains authoritative for custom models.
    /// </summary>
    public int Dimensions => System.Threading.Volatile.Read(ref _dimensions);

    public LlamaCppEmbeddingService(ISettingsService settings, HttpClient? http = null, IRuntimeLogService? runtimeLogs = null)
    {
        _settings = settings;
        _http = http ?? SharedHttp;
        _runtimeLogs = runtimeLogs;
        _dimensions = GetKnownDimensions(settings.Settings.Rag.EmbeddingModel);
    }

    private string Base
    {
        get
        {
            var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            return _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        await EmbedQueryAsync(text, background: false, ct);

    public async Task<float[]> EmbedBackgroundAsync(string text, CancellationToken ct = default) =>
        await EmbedQueryAsync(text, background: true, ct);

    private async Task<float[]> EmbedQueryAsync(string text, bool background, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LogFallbackOnce();
        var logicalClock = Stopwatch.StartNew();

        var key = new EmbeddingRequestKey(Base, _settings.Settings.Rag.EmbeddingModel, text, background);
        var sharedExisting = _inFlightQueries.TryGetValue(key, out var existingRequest);
        var lazyRequest = _inFlightQueries.GetOrAdd(
            key,
            _ => new Lazy<SharedEmbeddingRequest>(
                () => new SharedEmbeddingRequest(async token =>
                    (await EmbedBatchCoreAsync([text], token, background))[0]),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var request = lazyRequest.Value;

        try
        {
            var result = await request.WaitAsync(ct);
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Rag,
                $"Embedding logical query completed; priority={(background ? "background" : "foreground")}, coalesced={(sharedExisting || existingRequest is not null)}, logical_total_ms={logicalClock.ElapsedMilliseconds}, dimensions={result.Length}."));
            return result;
        }
        finally
        {
            if (request.Completion.IsCompleted
                && _inFlightQueries.TryRemove(new KeyValuePair<EmbeddingRequestKey, Lazy<SharedEmbeddingRequest>>(key, lazyRequest)))
            {
                request.Dispose();
            }
        }
    }

    public async Task<List<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        LogFallbackOnce();

        return await EmbedBatchCoreAsync(texts, ct, background: false);
    }

    private async Task<List<float[]>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct,
        bool background)
    {
        var totalClock = Stopwatch.StartNew();
        var gateClock = Stopwatch.StartNew();
        var gateWaitMs = 0L;
        var requestClock = Stopwatch.StartNew();
        var responseMs = 0L;
        var parseMs = 0L;
        var physicalStartedUtc = "not_started";
        var serverTiming = "unavailable";
        var inputChars = texts.Sum(text => text.Length);
        var inputBytes = texts.Sum(text => Encoding.UTF8.GetByteCount(text));

        var payload = new
        {
            model  = _settings.Settings.Rag.EmbeddingModel,
            input  = texts,
            encoding_format = "float"
        };

        try
        {
            using var gateLease = await _physicalRequestGate.EnterAsync(background, ct);
            gateWaitMs = gateClock.ElapsedMilliseconds;
            requestClock.Restart();
            physicalStartedUtc = DateTime.UtcNow.ToString("O");
            using var resp = await _http.PostAsJsonAsync($"{Base}/v1/embeddings", payload, ct);
            responseMs = requestClock.ElapsedMilliseconds;
            if (resp.Headers.TryGetValues("Server-Timing", out var serverTimingValues))
                serverTiming = string.Join('|', serverTimingValues);
            if (!resp.IsSuccessStatusCode)
                throw await CreateEmbeddingEndpointExceptionAsync(resp, ct);

            var data = await resp.Content.ReadFromJsonAsync<EmbedResponse>(ct)
                ?? throw new InvalidOperationException("Null response from embedding endpoint");
            parseMs = requestClock.ElapsedMilliseconds - responseMs;

            var embeddings = data.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToList();

            if (embeddings.Count > 0 && embeddings[0].Length > 0)
                System.Threading.Volatile.Write(ref _dimensions, embeddings[0].Length);

            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Rag,
                $"Embedding request completed; endpoint={Base}, priority={(background ? "background" : "foreground")}, input_items={texts.Count}, input_chars={inputChars}, input_utf8_bytes={inputBytes}, physical_started_utc={physicalStartedUtc}, endpoint_gate_wait_ms={gateWaitMs}, physical_request_ms={responseMs}, request_ms={responseMs}, server_timing={serverTiming}, parse_ms={parseMs}, persistence_cache=not_applicable, total_ms={totalClock.ElapsedMilliseconds}, dimensions={(embeddings.Count > 0 ? embeddings[0].Length : 0)}."));

            return embeddings;
        }
        catch (OperationCanceledException)
        {
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Rag,
                $"Embedding request canceled; endpoint={Base}, priority={(background ? "background" : "foreground")}, input_items={texts.Count}, input_chars={inputChars}, endpoint_gate_wait_ms={gateWaitMs}, physical_request_ms={responseMs}, parse_ms={parseMs}, total_ms={totalClock.ElapsedMilliseconds}."));
            throw;
        }
        catch (Exception ex)
        {
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Rag,
                $"Embedding request failed; endpoint={Base}, priority={(background ? "background" : "foreground")}, input_items={texts.Count}, input_chars={inputChars}, endpoint_gate_wait_ms={gateWaitMs}, physical_request_ms={responseMs}, parse_ms={parseMs}, total_ms={totalClock.ElapsedMilliseconds}, exception={ex.GetType().Name}."));
            throw;
        }
    }

    /// <summary>
    /// The zero-config fallback to the chat server queues embed calls behind
    /// generation on a single-slot llama-server (r9 01-send-path-latency.md
    /// 1.4). Kept, but surfaced once so it stops being a silent footgun.
    /// </summary>
    private void LogFallbackOnce()
    {
        if (_runtimeLogs is null) return;
        var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configured)) return;

        lock (_fallbackLogGate)
        {
            if (_fallbackLogged) return;
            _fallbackLogged = true;
        }

        var chatUrl = _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
        _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"Rag.EmbeddingBaseUrl is not set; embedding requests fall back to the chat server at {chatUrl}. " +
            "Configure a dedicated embeddings server to avoid queuing behind chat generation."));
    }

    private async Task<Exception> CreateEmbeddingEndpointExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "Unknown";
        var body = await response.Content.ReadAsStringAsync(ct);
        var baseMessage = $"Embedding request failed at {Base}/v1/embeddings with HTTP {status} ({reason}).";

        if (response.StatusCode is HttpStatusCode.NotImplemented or HttpStatusCode.NotFound)
        {
            var hint = "Start llama-server with --embeddings and point the RAG EmbeddingBaseUrl (older configs may refer to LlamaCppBaseUrl) to that embeddings-capable server.";
            if (string.IsNullOrWhiteSpace(body))
                return new InvalidOperationException($"{baseMessage} {hint}");

            return new InvalidOperationException($"{baseMessage} {hint} Server response: {body.Trim()}");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest
            && body.Contains("Pooling type 'none'", StringComparison.OrdinalIgnoreCase))
        {
            var hint = "Your llama.cpp model/server is not configured for OpenAI-compatible embeddings pooling. Use an embedding model and start the server with --embeddings --pooling mean (or cls), then retry.";
            return new InvalidOperationException($"{baseMessage} {hint} Server response: {body.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(body))
            return new HttpRequestException(baseMessage, null, response.StatusCode);

        return new HttpRequestException($"{baseMessage} Server response: {body.Trim()}", null, response.StatusCode);
    }

    public void Dispose()
    {
        // HttpClient is static and shared; do not dispose
    }

    private static int GetKnownDimensions(string? model) =>
        model?.Contains("qwen3-embedding-0.6b", StringComparison.OrdinalIgnoreCase) == true
            ? 1024
            : 768;

    private record EmbedResponse(
        [property: JsonPropertyName("data")] List<EmbedData> Data);

    private record EmbedData(
        [property: JsonPropertyName("index")]     int     Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);

    private readonly record struct EmbeddingRequestKey(string Endpoint, string Model, string Text, bool Background);

    private sealed class PriorityEmbeddingGate
    {
        private readonly object _gate = new();
        private readonly List<Waiter> _waiters = [];
        private bool _occupied;
        private long _sequence;

        public Task<IDisposable> EnterAsync(bool background, CancellationToken ct)
        {
            var waiter = new Waiter(background, Interlocked.Increment(ref _sequence));
            var grant = false;
            lock (_gate)
            {
                if (!_occupied)
                {
                    _occupied = true;
                    grant = true;
                }
                else
                {
                    _waiters.Add(waiter);
                }
            }

            if (grant)
                return Task.FromResult<IDisposable>(new GateLease(this));

            waiter.Registration = ct.Register(() => Cancel(waiter));
            if (ct.IsCancellationRequested)
                Cancel(waiter);
            return waiter.Completion.Task;
        }

        private void Cancel(Waiter waiter)
        {
            lock (_gate)
            {
                if (!_waiters.Remove(waiter))
                    return;
            }

            waiter.Completion.TrySetCanceled();
            waiter.Registration.Dispose();
        }

        private void Release()
        {
            Waiter? next = null;
            lock (_gate)
            {
                while (_waiters.Count > 0)
                {
                    next = _waiters
                        .OrderBy(waiter => waiter.Background)
                        .ThenBy(waiter => waiter.Sequence)
                        .First();
                    _waiters.Remove(next);
                    if (!next.Completion.Task.IsCanceled)
                        break;
                    next = null;
                }

                if (next is null)
                    _occupied = false;
            }

            if (next is not null)
            {
                next.Registration.Dispose();
                next.Completion.TrySetResult(new GateLease(this));
            }
        }

        private sealed class Waiter(bool background, long sequence)
        {
            public bool Background { get; } = background;
            public long Sequence { get; } = sequence;
            public TaskCompletionSource<IDisposable> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public CancellationTokenRegistration Registration { get; set; }
        }

        private sealed class GateLease(PriorityEmbeddingGate owner) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                    owner.Release();
            }
        }
    }

    private sealed class SharedEmbeddingRequest : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _requestCts = new();
        private int _waiters;
        private bool _completed;
        private bool _cancelled;

        public SharedEmbeddingRequest(Func<CancellationToken, Task<float[]>> factory) =>
            Completion = RunAsync(factory);

        public Task<float[]> Completion { get; }

        public async Task<float[]> WaitAsync(CancellationToken ct)
        {
            lock (_gate)
                _waiters++;

            try
            {
                return await Completion.WaitAsync(ct);
            }
            finally
            {
                var cancel = false;
                lock (_gate)
                {
                    _waiters--;
                    if (_waiters == 0 && !_completed && !_cancelled)
                    {
                        _cancelled = true;
                        cancel = true;
                    }
                }

                if (cancel)
                    _requestCts.Cancel();
            }
        }

        private async Task<float[]> RunAsync(Func<CancellationToken, Task<float[]>> factory)
        {
            try
            {
                return await factory(_requestCts.Token);
            }
            finally
            {
                lock (_gate)
                    _completed = true;
            }
        }

        public void Dispose() => _requestCts.Dispose();
    }
}
