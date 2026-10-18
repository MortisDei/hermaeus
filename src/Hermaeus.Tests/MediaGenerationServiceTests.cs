using System.Net;
using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class MediaGenerationServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
    private static MediaGenerationRequest Request(MediaKind kind = MediaKind.Image) =>
        new(MediaGenerationProtocol.OpenAiCompatible, kind, "https://media.example.test/api/v1", "synthetic prompt", "exact-model", "secret:media");
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Binary(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };

    [Theory]
    [InlineData("", "v1/")]
    [InlineData("/", "v1/")]
    [InlineData("/v1", "v1/")]
    [InlineData("/v1/", "v1/")]
    [InlineData("/gateway", "gateway/v1/")]
    [InlineData("/gateway/", "gateway/v1/")]
    [InlineData("/gateway/v1", "gateway/v1/")]
    [InlineData("/gateway/v1/", "gateway/v1/")]
    public async Task OpenAI_routes_preserve_normalised_path_prefixes_for_all_media_kinds(string suffix, string expectedPrefix)
    {
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            var calls = 0;
            using var handler = new Handler((request, _) =>
            {
                calls++;
                var route = kind switch
                {
                    MediaKind.Image => "images/generations",
                    MediaKind.Audio => "chat/completions",
                    _ => calls == 1 ? "videos" : "videos/prefix_job/content"
                };
                Assert.Equal("https://media.example.test/" + expectedPrefix + route, request.RequestUri!.AbsoluteUri);
                Assert.Equal(calls == 1 ? HttpMethod.Post : HttpMethod.Get, request.Method);
                return Task.FromResult(kind switch
                {
                    MediaKind.Image => Json(new { data = new[] { new { b64_json = Convert.ToBase64String(Png) } } }),
                    MediaKind.Audio => Json(new { choices = new[] { new { message = new { audio = new { data = Convert.ToBase64String("RIFF0000WAVEsynthetic"u8.ToArray()) } } } } }),
                    _ => calls == 1 ? Json(new { id = "prefix_job", status = "completed" })
                        : Binary(new byte[] { 0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109 })
                });
            });
            using var http = new HttpClient(handler);
            var result = await new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(kind) with
                { Endpoint = "https://media.example.test" + suffix });
            Assert.Single(result.Files);
            Assert.Equal(kind == MediaKind.Video ? 2 : 1, calls);
        }
    }

    [Fact]
    public async Task Image_request_preserves_versioned_base_and_per_request_auth_and_returns_exact_bytes()
    {
        using var handler = new Handler(async (request, _) =>
        {
            Assert.Equal("https://media.example.test/api/v1/images/generations", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer synthetic-resolved", request.Headers.Authorization!.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("exact-model", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("synthetic prompt", body.RootElement.GetProperty("prompt").GetString());
            Assert.False(body.RootElement.TryGetProperty("response_format", out var ignoredFormat));
            return Json(new { data = new[] { new { b64_json = Convert.ToBase64String(Png) } } });
        });
        using var http = new HttpClient(handler);
        var result = await new MediaGenerationService(new Secrets(), http).GenerateAsync(Request());
        var output = Assert.Single(result.Files);
        Assert.Equal(Png, output.Data);
        Assert.Equal("image/png", output.MediaType);
        Assert.Equal("generated-1.png", output.SuggestedFileName);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task Audio_uses_modalities_and_voice_and_refuses_text_only_success()
    {
        using var handler = new Handler(async (request, _) =>
        {
            Assert.EndsWith("/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Contains("audio", body.RootElement.GetProperty("modalities").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal("alloy", body.RootElement.GetProperty("audio").GetProperty("voice").GetString());
            Assert.Equal("wav", body.RootElement.GetProperty("audio").GetProperty("format").GetString());
            return Json(new { choices = new[] { new { message = new { content = "text only" } } } });
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(MediaKind.Audio)));
    }

    [Fact]
    public async Task Audio_returns_provider_bytes_without_playback()
    {
        var wav = "RIFF0000WAVEsynthetic"u8.ToArray();
        using var handler = new Handler((_, _) => Task.FromResult(Json(new
            { choices = new[] { new { message = new { audio = new { data = Convert.ToBase64String(wav) } } } } })));
        using var http = new HttpClient(handler);
        var result = await new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(MediaKind.Audio));
        Assert.Equal(wav, Assert.Single(result.Files).Data);
    }

    [Fact]
    public async Task Video_submits_once_polls_its_own_job_and_downloads_only_after_completion()
    {
        var calls = 0;
        using var handler = new Handler(async (request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.IsType<MultipartFormDataContent>(request.Content);
                Assert.Contains("synthetic prompt", await request.Content!.ReadAsStringAsync());
                return Json(new { id = "video_123", status = "queued" });
            }
            if (calls == 2)
            {
                Assert.EndsWith("/videos/video_123", request.RequestUri!.AbsoluteUri);
                return Json(new { id = "video_123", status = "completed" });
            }
            Assert.EndsWith("/videos/video_123/content", request.RequestUri!.AbsoluteUri);
            // Protocol fixture, not a decodable video or real generation receipt.
            return Binary(new byte[] { 0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109 });
        });
        using var http = new HttpClient(handler);
        var progress = new ProgressCapture();
        var result = await new MediaGenerationService(new Secrets(), http, (_, _) => Task.CompletedTask)
            .GenerateAsync(Request(MediaKind.Video), progress);
        Assert.Equal(3, calls);
        Assert.Equal("video_123", result.JobId);
        Assert.Equal("video/mp4", Assert.Single(result.Files).MediaType);
        Assert.Contains(progress.Values, x => x.JobId == "video_123");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("unsupported")]
    public async Task Video_terminal_failure_never_downloads_or_resubmits(string status)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(new { id = "job_1", status })); });
        using var http = new HttpClient(handler);
        var operation = new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(MediaKind.Video));
        if (status == "failed") await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        else await Assert.ThrowsAsync<InvalidDataException>(() => operation);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Video_rejects_a_poll_for_a_different_job()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1
            ? Json(new { id = "job_1", status = "queued" }) : Json(new { id = "job_2", status = "completed" })));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http, (_, _) => Task.CompletedTask)
            .GenerateAsync(Request(MediaKind.Video)));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://other.example.test/image.png")]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("file:///private.png")]
    public async Task Returned_cross_origin_urls_are_not_requested(string url)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(new { data = new[] { new { url } } })); });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request()));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("file:///tmp/media")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test?key=secret")]
    [InlineData("https://example.test/#fragment")]
    public async Task Invalid_endpoint_is_rejected_before_resolving_credentials_or_network(string endpoint)
    {
        using var handler = new Handler((_, _) => throw new Xunit.Sdk.XunitException("No request expected."));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request() with { Endpoint = endpoint }));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Http_error_does_not_echo_sensitive_body_or_retry(int status)
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("synthetic-private-provider-body") });
        });
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request()));
        Assert.DoesNotContain("synthetic-private", failure.Message);
        Assert.Equal((HttpStatusCode)status, failure.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not base64")]
    [InlineData("aGVsbG8=")]
    [InlineData("")]
    public async Task Invalid_or_non_media_base64_is_refused(string encoded)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(new { data = new[] { new { b64_json = encoded } } })));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request()));
    }

    private const string Workflow = """{"6":{"class_type":"CLIPTextEncode","inputs":{"text":"{{prompt}}"}},"9":{"class_type":"SaveImage","inputs":{}}}""";

    [Fact]
    public async Task Comfy_binds_prompt_as_json_value_and_fetches_only_matching_job_output()
    {
        var calls = 0;
        using var handler = new Handler(async (request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.EndsWith("/prompt", request.RequestUri!.AbsoluteUri);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("quoted \"prompt\"", body.RootElement.GetProperty("prompt").GetProperty("6").GetProperty("inputs").GetProperty("text").GetString());
                return Json(new { prompt_id = "job-1" });
            }
            if (calls == 2) return Json(new { });
            if (calls == 3) return Json(new Dictionary<string, object> { ["job-1"] = new
                { status = new { completed = true, status_str = "success" }, outputs = new Dictionary<string, object>
                    { ["9"] = new { images = new[] { new { filename = "test.png", subfolder = "safe", type = "output" } } } } } });
            Assert.Equal("/view", request.RequestUri!.AbsolutePath);
            Assert.Contains("subfolder=safe", request.RequestUri.Query);
            return Binary(Png);
        });
        using var http = new HttpClient(handler);
        var result = await new MediaGenerationService(new Secrets(), http, (_, _) => Task.CompletedTask).GenerateAsync(Request() with
            { Protocol = MediaGenerationProtocol.ComfyUi, Endpoint = "http://localhost:8188", Prompt = "quoted \"prompt\"", WorkflowJson = Workflow });
        Assert.Equal(4, calls);
        Assert.Equal("job-1", result.JobId);
        Assert.Equal(Png, Assert.Single(result.Files).Data);
    }

    [Theory]
    [InlineData("../secret.png", "")]
    [InlineData("test.png", "../private")]
    [InlineData("C:\\secret.png", "")]
    [InlineData("test.png", "\\private")]
    public async Task Comfy_unsafe_output_paths_are_refused_before_download(string filename, string subfolder)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1 ? Json(new { prompt_id = "job-1" }) : Json(new Dictionary<string, object>
            { ["job-1"] = new { status = new { completed = true }, outputs = new { node = new { images = new[] { new { filename, subfolder, type = "output" } } } } } })));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request() with
            { Protocol = MediaGenerationProtocol.ComfyUi, WorkflowJson = Workflow }));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"nodes\":[]}")]
    [InlineData("{\"1\":{\"class_type\":\"Node\",\"inputs\":{\"text\":\"no binding\"}}}")]
    public async Task Comfy_wrong_workflow_is_refused_before_submission(string workflow)
    {
        using var handler = new Handler((_, _) => throw new Xunit.Sdk.XunitException("No request expected."));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request() with
            { Protocol = MediaGenerationProtocol.ComfyUi, WorkflowJson = workflow }));
    }

    [Fact]
    public async Task Cancellation_during_polling_does_not_interrupt_other_jobs_or_resubmit()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(new { id = "job_1", status = "queued" })); });
        using var http = new HttpClient(handler);
        var progress = new ProgressCapture();
        var service = new MediaGenerationService(new Secrets(), http, (_, token) =>
        { cts.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateAsync(Request(MediaKind.Video), progress, cts.Token));
        Assert.Equal(1, calls);
        Assert.Contains(progress.Values, x => x.JobId == "job_1");
    }

    [Fact]
    public async Task Oversized_declared_response_is_refused_before_body_read()
    {
        using var handler = new Handler((_, _) =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = long.MaxValue;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_download_budget_is_enforced_despite_missing_or_false_length(bool falseLength)
    {
        using var body = new NonSeekableStream(Png);
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            if (++calls == 1) return Task.FromResult(Json(new { data = new[] { new { url = "https://media.example.test/output.png" } } }));
            var content = new StreamContent(body);
            if (falseLength) content.Headers.ContentLength = 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http, Png.Length - 1).GenerateAsync(Request()));
        Assert.Equal(2, calls);
        Assert.True(body.WasDisposed);
    }

    [Fact]
    public async Task Exact_decoded_budget_succeeds_but_one_less_and_combined_overflow_refuse()
    {
        foreach (var count in new[] { 1, 2 })
        {
            using var handler = new Handler((_, _) => Task.FromResult(Json(new
                { data = Enumerable.Range(0, count).Select(_ => new { b64_json = Convert.ToBase64String(Png) }).ToArray() })));
            using var http = new HttpClient(handler);
            var exact = await new MediaGenerationService(new Secrets(), http, Png.Length * count).GenerateAsync(Request());
            Assert.Equal(count, exact.Files.Count);
            await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http, Png.Length * count - 1).GenerateAsync(Request()));
        }
    }

    [Fact]
    public async Task Too_many_outputs_are_refused_before_decoding()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(new
            { data = Enumerable.Range(0, 9).Select(_ => new { b64_json = Convert.ToBase64String(Png) }).ToArray() })));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request()));
    }

    [Fact]
    public async Task Workflow_failure_is_not_reported_as_success_or_followed_by_downloads()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1 ? Json(new { prompt_id = "job-1" }) : Json(new Dictionary<string, object>
            { ["job-1"] = new { status = new { completed = false, status_str = "error" }, private_error = "synthetic private details" } })));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request() with
            { Protocol = MediaGenerationProtocol.ComfyUi, WorkflowJson = Workflow }));
        Assert.DoesNotContain("private details", failure.Message);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Invalid_job_id_is_not_interpolated_into_a_request_path()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(new { id = "../private", status = "queued" })); });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(MediaKind.Video)));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(MediaKind.Audio, "audio", "out.wav")]
    [InlineData(MediaKind.Audio, "audio", "out.flac")]
    [InlineData(MediaKind.Audio, "audio", "out.opus")]
    [InlineData(MediaKind.Video, "images", "out.mp4")]
    [InlineData(MediaKind.Video, "gifs", "out.webm")]
    public async Task Comfy_retrieves_audio_and_video_from_their_actual_output_groups(MediaKind kind, string group, string filename)
    {
        // These short container headers test routing, not decoding or model inference.
        var bytes = Path.GetExtension(filename) switch
        {
            ".wav" => "RIFF0000WAVEsynthetic"u8.ToArray(),
            ".flac" => "fLaCsynthetic"u8.ToArray(),
            ".opus" => "OggSsynthetic"u8.ToArray(),
            ".webm" => new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 0 },
            _ => new byte[] { 0, 0, 0, 20, 102, 116, 121, 112, 105, 115, 111, 109 }
        };
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls switch
        {
            1 => Json(new { prompt_id = "job-1" }),
            2 => Json(new Dictionary<string, object> { ["job-1"] = new { status = new { completed = true }, outputs = new
                { node = new Dictionary<string, object> { [group] = new[] { new { filename, subfolder = "", type = "output" } } } } } }),
            _ => Binary(bytes)
        }));
        using var http = new HttpClient(handler);
        var result = await new MediaGenerationService(new Secrets(), http).GenerateAsync(Request(kind) with
            { Protocol = MediaGenerationProtocol.ComfyUi, WorkflowJson = Workflow });
        Assert.Equal(bytes, Assert.Single(result.Files).Data);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Client_deadline_cancels_the_active_http_request_without_retry()
    {
        var calls = 0;
        var cancelled = false;
        using var handler = new Handler(async (_, token) =>
        {
            calls++;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled = token.IsCancellationRequested; throw; }
            throw new InvalidOperationException("Unreachable.");
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<TimeoutException>(() => new MediaGenerationService(new Secrets(), http,
            MediaGenerationService.MaxOutputBytes, operationTimeout: TimeSpan.FromMilliseconds(100)).GenerateAsync(Request()));
        Assert.True(cancelled);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Repeated_prompt_expansion_is_bounded_before_sending_a_workflow()
    {
        var workflow = JsonSerializer.Serialize(new Dictionary<string, object> { ["1"] = new
            { class_type = "Node", inputs = Enumerable.Range(0, 300).ToDictionary(i => "input" + i, _ => "{{prompt}}") } });
        Assert.True(workflow.Length < 1_048_576);
        using var handler = new Handler((_, _) => throw new Xunit.Sdk.XunitException("No oversized workflow submission expected."));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new MediaGenerationService(new Secrets(), http).GenerateAsync(Request() with
            { Protocol = MediaGenerationProtocol.ComfyUi, WorkflowJson = workflow, Prompt = new string('x', 32_768) }));
        Assert.Contains("expanded workflow", failure.Message);
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _input = new(bytes);
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _input.ReadAsync(buffer, ct);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct);
    }
    private sealed class Secrets : ISecretStore
    {
        public Task<string> ResolveAsync(string value, CancellationToken ct = default) => Task.FromResult("synthetic-resolved");
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("synthetic");
        public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.Ordinal);
    }
    private sealed class ProgressCapture : IProgress<MediaGenerationProgress>
    {
        public List<MediaGenerationProgress> Values { get; } = [];
        public void Report(MediaGenerationProgress value) => Values.Add(value);
    }
}
