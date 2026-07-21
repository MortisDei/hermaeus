using System.Net;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class VoiceTempFileCleanupTests
{
    /// <summary>
    /// r11 4.3: GenerateSpeechAsync synthesized to a %TEMP% file whenever the
    /// caller (VoiceOrchestrator.PlayAsync, always) did not request a
    /// persisted OutputPath, and never deleted it after playback - every
    /// spoken chat reply, notification, and agent narration left a wav on
    /// disk. PlayAudio: true is required to exercise the fix, so this
    /// genuinely spawns the OS player against a fake (non-audio) body; the
    /// assertion only cares that the temp file is gone afterward, not that
    /// playback actually produced sound.
    /// </summary>
    [Fact]
    public async Task GenerateSpeechAsync_deletes_the_temp_wav_after_a_fake_synthesis_and_playback_cycle()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";

        var handler = new FakeSpeechHandler();
        using var http = new HttpClient(handler);
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http);

        var result = await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", OutputPath: null, PlayAudio: true));

        Assert.False(string.IsNullOrWhiteSpace(result.OutputPath));

        // Poll rather than assert immediately: GenerateSpeechAsync's own delete happens
        // synchronously after PlayWavFileAsync returns, but a transient OS-level file lock
        // (antivirus/indexer scanning a file moments after it was written) can delay the
        // delete becoming visible to this process's own File.Exists check.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (File.Exists(result.OutputPath) && DateTime.UtcNow < deadline)
            await Task.Delay(200);
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
        using var provider = new OpenAiVoiceProvider(settings, new PassthroughSecretStore(), http);

        await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", OutputPath: explicitPath, PlayAudio: false));

        Assert.True(File.Exists(explicitPath), "an explicitly requested OutputPath must not be deleted");
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
