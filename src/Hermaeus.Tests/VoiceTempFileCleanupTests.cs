using System.Net;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class VoiceTempFileCleanupTests
{
    /// <summary>
    /// r11 4.3: GenerateSpeechAsync synthesized to a %TEMP% file whenever the
    /// caller (VoiceOrchestrator.PlayAsync, always) did not request a
    /// persisted OutputPath, and never deleted it after playback - every
    /// spoken chat reply, notification, and agent narration left a wav on
    /// disk. Playback is injected so this remains a deterministic cleanup test.
    /// </summary>
    [Fact]
    public async Task GenerateSpeechAsync_deletes_the_temp_wav_after_a_fake_synthesis_and_playback_cycle()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";

        var handler = new FakeSpeechHandler();
        using var http = new HttpClient(handler);
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http,
            static (_, _) => Task.CompletedTask);

        var result = await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", OutputPath: null, PlayAudio: true));

        Assert.False(string.IsNullOrWhiteSpace(result.OutputPath));

        Assert.False(File.Exists(result.OutputPath), $"temp wav {result.OutputPath} should be deleted after playback");
    }

    /// <summary>An explicitly requested OutputPath is the caller asking to keep the file; it must survive.</summary>
    [Fact]
    public async Task GenerateSpeechAsync_keeps_an_explicitly_requested_output_path()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        var explicitPath = temp.PathFor("keep-me.wav");

        var handler = new FakeSpeechHandler();
        using var http = new HttpClient(handler);
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http,
            static (_, _) => Task.CompletedTask);

        await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", OutputPath: explicitPath, PlayAudio: false));

        Assert.True(File.Exists(explicitPath), "an explicitly requested OutputPath must not be deleted");
    }

    [Fact]
    public async Task OpenAi_playback_uses_injected_shared_seam_without_default_association()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        var handler = new FakeSpeechHandler();
        using var http = new HttpClient(handler);
        string? playedPath = null;
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http,
            (path, _) =>
            {
                playedPath = path;
                return Task.CompletedTask;
            });

        var result = await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", PlayAudio: true));

        Assert.True(result.Success);
        Assert.Equal(result.OutputPath, playedPath);
        Assert.Contains("hermaeus-openai-", playedPath, StringComparison.Ordinal);
        Assert.False(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task OpenAi_preview_uses_the_shared_non_associating_playback_path()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        var handler = new FakeSpeechHandler();
        using var http = new HttpClient(handler);
        string? playedPath = null;
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http,
            (path, _) =>
            {
                playedPath = path;
                return Task.CompletedTask;
            });

        await provider.PreviewVoiceAsync("alloy", "preview");

        Assert.NotNull(playedPath);
        Assert.Contains("hermaeus-openai-", playedPath, StringComparison.Ordinal);
        Assert.False(File.Exists(playedPath));
    }

    private sealed class PassthroughSecretStore : ISecretStore
    {
        public bool IsReference(string value) => false;
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("passthrough");
    }

    private sealed class FakeSpeechHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFFfakeWAVE"))
            });
    }
}
