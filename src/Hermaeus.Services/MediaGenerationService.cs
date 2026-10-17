using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>Explicit media API client. It does not launch engines, discover capabilities or manage server queues.</summary>
public sealed class MediaGenerationService : IMediaGenerationService
{
    public const int MaxOutputBytes = 128 * 1024 * 1024;
    public const int MaxOutputFiles = 8;
    private const int MaxControlBytes = 2 * 1024 * 1024;
    private const int MaxEncodedResponseBytes = 48 * 1024 * 1024;
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _outputBudget = MaxOutputBytes;
    private readonly TimeSpan _operationTimeout = TimeSpan.FromMinutes(30);

    public MediaGenerationService(ISecretStore secrets, HttpClient? http = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _secrets = secrets;
        _http = http ?? SharedHttp;
        _delay = delay ?? Task.Delay;
    }

    internal MediaGenerationService(ISecretStore secrets, HttpClient http, int outputBudget,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? operationTimeout = null) : this(secrets, http, delay)
    {
        if (outputBudget is < 1 or > MaxOutputBytes) throw new ArgumentOutOfRangeException(nameof(outputBudget));
        _outputBudget = outputBudget;
        if (operationTimeout is { } timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(30)) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            _operationTimeout = timeout;
        }
    }

    public async Task<MediaGenerationResult> GenerateAsync(MediaGenerationRequest request,
        IProgress<MediaGenerationProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Protocol))
            throw new ArgumentException("Select a supported media kind and protocol.");
        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 32_768)
            throw new ArgumentException("Enter a prompt of at most 32,768 characters.");
        var endpoint = ValidateEndpoint(request.Endpoint);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_operationTimeout);
        try
        {
            var token = deadline.Token;
            var key = string.IsNullOrWhiteSpace(request.SecretReference) ? ""
                : await _secrets.ResolveAsync(request.SecretReference, token);
            if (!string.IsNullOrWhiteSpace(request.SecretReference) && string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("The configured media credential could not be resolved.");
            var result = request.Protocol == MediaGenerationProtocol.ComfyUi
                ? await GenerateComfyAsync(request, endpoint, key, progress, token)
                : await GenerateOpenAiAsync(request, endpoint, key, progress, token);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException("Media generation reached its client deadline. An accepted server job may still be running.");
        }
    }

    private async Task<MediaGenerationResult> GenerateOpenAiAsync(MediaGenerationRequest request,
        Uri endpoint, string key, IProgress<MediaGenerationProgress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Model) || request.Model.Length > 512)
            throw new ArgumentException("Enter the exact generation model ID supported by this server.");
        var api = endpoint.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.Ordinal)
            ? endpoint : new Uri(endpoint, "v1/");
        if (request.Kind != MediaKind.Audio && !string.IsNullOrEmpty(request.Size)
            && (!System.Text.RegularExpressions.Regex.IsMatch(request.Size, @"^\d{2,4}x\d{2,4}$")
                || request.Size.Length > 9))
            throw new ArgumentException("Size must be empty for the provider default, or WIDTHxHEIGHT.");
        progress?.Report(new("Submitting generation"));
        if (request.Kind == MediaKind.Video)
        {
            if (request.VideoSeconds is < 1 or > 60)
                throw new ArgumentException("Video duration must be between 1 and 60 seconds; the provider may support fewer values.");
            using var body = new MultipartFormDataContent();
            body.Add(new StringContent(request.Model), "model");
            body.Add(new StringContent(request.Prompt), "prompt");
            body.Add(new StringContent(request.VideoSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)), "seconds");
            if (request.Size.Length > 0) body.Add(new StringContent(request.Size), "size");
            using var created = await JsonAsync(HttpMethod.Post, new Uri(api, "videos"), key, body, MaxControlBytes, ct);
            var id = RequiredJobId(created.RootElement, "id");
            var status = RequiredString(created.RootElement, "status");
            for (var poll = 0; ; poll++)
            {
                progress?.Report(new(status, id));
                if (status == "completed") break;
                if (status == "failed") throw new InvalidOperationException("The video generation job failed. Check the provider's job details.");
                if (status is not ("queued" or "in_progress"))
                    throw new InvalidDataException("The video provider returned an unsupported job status.");
                if (poll >= 900) throw new TimeoutException("Video polling limit reached. The server job may still be running.");
                await _delay(TimeSpan.FromSeconds(2), ct);
                using var current = await JsonAsync(HttpMethod.Get, new Uri(api, "videos/" + id), key, null, MaxControlBytes, ct);
                if (RequiredJobId(current.RootElement, "id") != id)
                    throw new InvalidDataException("The video provider returned a different job identity.");
                status = RequiredString(current.RootElement, "status");
            }
            var data = await DownloadAsync(new Uri(api, "videos/" + id + "/content"), key, _outputBudget, ct);
            return new([MakeFile(data, request.Kind, 1)], id);
        }

        object payload;
        string route;
        if (request.Kind == MediaKind.Image)
        {
            var fields = new Dictionary<string, object> { ["model"] = request.Model, ["prompt"] = request.Prompt, ["n"] = 1 };
            if (request.Size.Length > 0) fields["size"] = request.Size;
            // Do not send response_format: GPT Image returns base64 by default,
            // whereas legacy/compatible models have differing option contracts.
            payload = fields;
            route = "images/generations";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Voice) || request.Voice.Length > 128)
                throw new ArgumentException("Enter a voice supported by this audio model.");
            payload = new { model = request.Model, modalities = new[] { "text", "audio" },
                audio = new { voice = request.Voice, format = "wav" },
                messages = new[] { new { role = "user", content = request.Prompt } }, stream = false };
            route = "chat/completions";
        }
        using var response = await JsonAsync(HttpMethod.Post, new Uri(api, route), key,
            JsonContent.Create(payload), MaxEncodedResponseBytes, ct);
        var files = new List<GeneratedMedia>();
        if (request.Kind == MediaKind.Audio)
        {
            var root = response.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1
                || !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("audio", out var audio))
                throw new InvalidDataException("The selected endpoint/model did not return generated audio.");
            files.Add(MakeFile(Decode(RequiredString(audio, "data"), _outputBudget), request.Kind, 1));
        }
        else
        {
            if (!response.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() is < 1 or > MaxOutputFiles)
                throw new InvalidDataException("The image provider returned no usable output or too many files.");
            var remaining = _outputBudget;
            foreach (var item in data.EnumerateArray())
            {
                byte[] bytes;
                if (item.TryGetProperty("b64_json", out var encoded) && encoded.ValueKind == JsonValueKind.String)
                    bytes = Decode(encoded.GetString()!, remaining);
                else if (item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var target) && SameOrigin(endpoint, target)
                    && target.UserInfo.Length == 0 && target.Fragment.Length == 0)
                    bytes = await DownloadAsync(target, key, remaining, ct);
                else
                    throw new InvalidDataException("The image provider must return base64 or a same-origin output URL. Cross-origin downloads are refused.");
                remaining -= bytes.Length;
                files.Add(MakeFile(bytes, request.Kind, files.Count + 1));
            }
        }
        return new(files.AsReadOnly());
    }

    private async Task<MediaGenerationResult> GenerateComfyAsync(MediaGenerationRequest request,
        Uri endpoint, string key, IProgress<MediaGenerationProgress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowJson) || request.WorkflowJson.Length > 1_048_576)
            throw new ArgumentException("Supply a trusted ComfyUI API-format workflow of at most 1 Mi characters.");
        JsonObject workflow;
        try { workflow = JsonNode.Parse(request.WorkflowJson, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new InvalidDataException("Use a ComfyUI API-format node object."); }
        catch (JsonException) { throw new InvalidDataException("The workflow is not valid JSON."); }
        if (workflow.Count is < 1 or > 1024) throw new InvalidDataException("The workflow must contain between 1 and 1,024 nodes.");
        // Account escaped UTF-8 before repeating a prompt across many inputs.
        // Serialising the original bounded workflow cannot expand without limit.
        var expandedBytes = (long)JsonSerializer.SerializeToUtf8Bytes(workflow).Length;
        var replacementDelta = JsonEncodedText.Encode(request.Prompt).EncodedUtf8Bytes.Length
            - JsonEncodedText.Encode("{{prompt}}").EncodedUtf8Bytes.Length;
        var replacements = 0;
        foreach (var node in workflow)
        {
            if (node.Value is not JsonObject definition || definition["class_type"] is not JsonValue type
                || !type.TryGetValue<string>(out var className) || string.IsNullOrWhiteSpace(className)
                || definition["inputs"] is not JsonObject inputs)
                throw new InvalidDataException("Use API-format nodes with class_type and inputs, not the editor's workflow format.");
            foreach (var input in inputs.ToArray())
                if (input.Value is JsonValue value && value.TryGetValue<string>(out var text) && text == "{{prompt}}")
                {
                    expandedBytes += replacementDelta;
                    if (expandedBytes > 8 * 1024 * 1024 - 512)
                        throw new InvalidDataException("The expanded workflow exceeds the 8 MiB request budget.");
                    inputs[input.Key] = request.Prompt;
                    replacements++;
                }
        }
        if (replacements == 0) throw new InvalidDataException("Set a workflow string input to exactly {{prompt}} to bind the generation prompt.");
        progress?.Report(new("Submitting trusted workflow"));
        using var submitted = await JsonAsync(HttpMethod.Post, new Uri(endpoint, "prompt"), key,
            JsonContent.Create(new { prompt = workflow, client_id = Guid.NewGuid().ToString() }), MaxControlBytes, ct);
        var id = RequiredJobId(submitted.RootElement, "prompt_id");
        for (var poll = 0; poll < 900; poll++)
        {
            progress?.Report(new("Waiting for workflow output", id));
            using var history = await JsonAsync(HttpMethod.Get, new Uri(endpoint, "history/" + id), key, null, MaxControlBytes, ct);
            if (!history.RootElement.TryGetProperty(id, out var job))
            {
                await _delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            if (!job.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The workflow history has no terminal status.");
            if (status.TryGetProperty("status_str", out var state) && state.GetString() == "error")
                throw new InvalidOperationException("The workflow failed. Check this job in ComfyUI for details.");
            if (!status.TryGetProperty("completed", out var completed) || completed.ValueKind != JsonValueKind.True)
            {
                await _delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            if (!job.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The workflow completed without saved media outputs.");
            var files = new List<GeneratedMedia>();
            var remaining = _outputBudget;
            foreach (var node in outputs.EnumerateObject())
            {
                if (node.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var group in node.Value.EnumerateObject())
                {
                    if (group.Name is not ("images" or "audio" or "videos" or "video" or "gifs") || group.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in group.Value.EnumerateArray())
                    {
                        var name = RequiredString(item, "filename");
                        if (!ExtensionMatchesKind(name, request.Kind)) continue;
                        if (files.Count >= MaxOutputFiles) throw new InvalidDataException("The workflow exceeds the eight-file output limit.");
                        var folder = item.TryGetProperty("subfolder", out var subfolder) ? subfolder.GetString() ?? "" : "";
                        ValidateRemotePath(name, false);
                        ValidateRemotePath(folder, true);
                        if (RequiredString(item, "type") != "output")
                            throw new InvalidDataException("Only saved ComfyUI output files can be retrieved.");
                        var target = new Uri(endpoint, "view?filename=" + Uri.EscapeDataString(name)
                            + "&subfolder=" + Uri.EscapeDataString(folder) + "&type=output");
                        var bytes = await DownloadAsync(target, key, remaining, ct);
                        remaining -= bytes.Length;
                        files.Add(MakeFile(bytes, request.Kind, files.Count + 1));
                    }
                }
            }
            if (files.Count == 0) throw new InvalidDataException("The workflow produced no saved media of the selected kind.");
            return new(files.AsReadOnly(), id);
        }
        throw new TimeoutException("Workflow polling limit reached. The server job may still be running.");
    }

    private async Task<JsonDocument> JsonAsync(HttpMethod method, Uri uri, string key, HttpContent? body, int limit, CancellationToken ct)
    {
        using var request = NewRequest(method, uri, key, body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        CheckResponse(response, uri);
        var data = await ReadBoundedAsync(response.Content, limit, ct);
        try { return JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { throw new InvalidDataException("The media provider returned invalid JSON."); }
    }

    private async Task<byte[]> DownloadAsync(Uri uri, string key, int limit, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Get, uri, key);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        CheckResponse(response, uri);
        return await ReadBoundedAsync(response.Content, limit, ct);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, Uri uri, string key, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = body };
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static void CheckResponse(HttpResponseMessage response, Uri expected)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Media provider returned HTTP {(int)response.StatusCode}. Check endpoint, model, access and quota.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri is { } actual && actual != expected)
            throw new InvalidDataException("Media request redirection is not supported.");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (limit <= 0 || content.Headers.ContentLength > limit)
            throw new InvalidDataException("Media response exceeds the client byte budget.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - (int)output.Length + 1)), ct);
            if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("Media response exceeds the client byte budget.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private static byte[] Decode(string value, int remaining)
    {
        // Whitespace is deliberately not accepted as hidden unaccounted payload.
        if (value.Length == 0 || value.Any(char.IsWhiteSpace) || ((long)value.Length + 3) / 4 * 3 > (long)remaining + 2)
            throw new InvalidDataException("Encoded media is empty or exceeds the client byte budget.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidDataException("The provider returned invalid base64 media."); }
        if (bytes.Length == 0 || bytes.Length > remaining) throw new InvalidDataException("Decoded media exceeds the client byte budget.");
        return bytes;
    }

    public static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTP(S) endpoint without embedded credentials, query or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    private static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme && left.IdnHost == right.IdnHost && left.Port == right.Port;

    private static string RequiredString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("The media provider omitted a required response field.");
        return value.GetString()!;
    }

    private static string RequiredJobId(JsonElement element, string property)
    {
        var value = RequiredString(element, property);
        if (value.Length > 256 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("The media provider returned an invalid job ID.");
        return value;
    }

    private static void ValidateRemotePath(string value, bool folder)
    {
        if ((!folder && string.IsNullOrWhiteSpace(value)) || value.Length > 1024 || value.Contains('\\') || value.Contains(':')
            || value.StartsWith('/') || value.Any(char.IsControl)
            || value.Split('/').Any(part => part is "." or "..") || (!folder && value.Contains('/')))
            throw new InvalidDataException("The workflow returned an unsafe output path.");
    }

    private static bool ExtensionMatchesKind(string name, MediaKind kind) => (kind, Path.GetExtension(name).ToLowerInvariant()) switch
    {
        (MediaKind.Image, ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif") => true,
        (MediaKind.Audio, ".wav" or ".mp3" or ".flac" or ".ogg" or ".opus") => true,
        (MediaKind.Video, ".mp4" or ".webm") => true,
        _ => false
    };

    private static GeneratedMedia MakeFile(byte[] bytes, MediaKind kind, int index)
    {
        var (type, extension, actualKind) = DetectFormat(bytes);
        if (actualKind != kind) throw new InvalidDataException("The output bytes do not match the requested media kind.");
        return new($"generated-{index}{extension}", type, bytes);
    }

    private static (string Type, string Extension, MediaKind Kind) DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", ".png", MediaKind.Image);
        if (data.StartsWith(new byte[] { 255, 216, 255 })) return ("image/jpeg", ".jpg", MediaKind.Image);
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return ("image/gif", ".gif", MediaKind.Image);
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp", MediaKind.Image);
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8)) return ("audio/wav", ".wav", MediaKind.Audio);
        if (data.StartsWith("fLaC"u8)) return ("audio/flac", ".flac", MediaKind.Audio);
        if (data.StartsWith("OggS"u8)) return ("audio/ogg", ".ogg", MediaKind.Audio);
        if (data.StartsWith("ID3"u8) || (data.Length >= 2 && data[0] == 255 && (data[1] & 0xE0) == 0xE0)) return ("audio/mpeg", ".mp3", MediaKind.Audio);
        if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8)) return ("video/mp4", ".mp4", MediaKind.Video);
        if (data.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return ("video/webm", ".webm", MediaKind.Video);
        throw new InvalidDataException("The provider returned an unsupported or unrecognised media format.");
    }
}
