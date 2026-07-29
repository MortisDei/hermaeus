using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Hermaeus.Voice;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r24 doc 05: speech recognition. Covers everything testable without a live
/// microphone or network - CTC decode/normalize, WAV parsing, recorder selection/argument
/// construction, remote-response parsing, provider registry, and file-transcription
/// validation. The pinned model download/hash itself is exercised only by asset-presence
/// checks (never downloads in a test).</summary>
public sealed class SpeechRecognitionTests
{
    // ── Wav2Vec2OnnxModel: CTC decode is pure and independently testable ──────

    [Fact]
    public void GreedyCtcDecode_collapses_repeats_drops_blank_and_maps_delimiter_to_space()
    {
        // Vocab: 0=<pad>(blank), 4="|"(space), 5="E", 6="T"
        var frames = new[] { 0, 5, 5, 0, 6, 4, 4, 5, 5, 5 };
        var text = InvokeGreedyCtcDecode(frames);
        Assert.Equal("ET E", text);
    }

    [Fact]
    public void GreedyCtcDecode_of_all_blank_frames_is_empty()
    {
        Assert.Equal(string.Empty, InvokeGreedyCtcDecode(new[] { 0, 0, 0 }));
    }

    private static string InvokeGreedyCtcDecode(int[] frameIds)
    {
        var method = typeof(Wav2Vec2OnnxModel).GetMethod("GreedyCtcDecode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [frameIds])!;
    }

    [Fact]
    public void NormalizeZeroMeanUnitVariance_produces_zero_mean_output()
    {
        var samples = new[] { 0.1f, 0.2f, -0.1f, 0.4f, -0.3f };
        var normalized = Wav2Vec2OnnxModel_NormalizeZeroMeanUnitVariance(samples);

        var mean = normalized.Average();
        Assert.True(Math.Abs(mean) < 1e-4, $"expected ~zero mean, got {mean}");
    }

    [Fact]
    public void NormalizeZeroMeanUnitVariance_of_empty_input_does_not_throw()
    {
        Assert.Empty(Wav2Vec2OnnxModel_NormalizeZeroMeanUnitVariance([]));
    }

    private static float[] Wav2Vec2OnnxModel_NormalizeZeroMeanUnitVariance(float[] samples)
    {
        var method = typeof(Wav2Vec2OnnxModel).GetMethod("NormalizeZeroMeanUnitVariance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (float[])method.Invoke(null, [samples])!;
    }

    [Fact]
    public void Pinned_model_hash_constant_is_a_well_formed_sha256_hex_string()
    {
        Assert.Matches("^[0-9a-f]{64}$", Wav2Vec2OnnxModel.ModelSha256);
    }

    // ── WavFile: read must round-trip Write and reject malformed input clearly ──

    [Fact]
    public void WavFile_round_trips_samples_written_by_Write()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("a.wav");
        var samples = new[] { 0f, 0.5f, -0.5f, 0.25f, -1f, 1f };
        WavFile.Write(path, samples, 16000);

        using var stream = File.OpenRead(path);
        var audio = WavFile.Read(stream);

        Assert.Equal(16000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.Equal(samples.Length, audio.Samples.Length);
        for (var i = 0; i < samples.Length; i++)
            Assert.True(Math.Abs(samples[i] - audio.Samples[i]) < 0.001f);
    }

    [Fact]
    public void WavFile_Read_rejects_a_stream_that_is_not_a_riff_file()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a wav file at all"));
        Assert.Throws<InvalidDataException>(() => WavFile.Read(stream));
    }

    [Fact]
    public void WavFile_Read_rejects_a_wav_with_no_data_chunk()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(16000);
            writer.Write(32000);
            writer.Write((short)2);
            writer.Write((short)16);
        }
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => WavFile.Read(stream));
    }

    // ── LinuxAudioCapture: pure selection/argument-construction seams ─────────

    [Fact]
    public void SelectRecorderCommand_prefers_parecord_then_arecord_then_ffmpeg()
    {
        Assert.Equal("parecord", LinuxAudioCapture_SelectRecorderCommand(c => true));
        Assert.Equal("arecord", LinuxAudioCapture_SelectRecorderCommand(c => c != "parecord"));
        Assert.Equal("ffmpeg", LinuxAudioCapture_SelectRecorderCommand(c => c == "ffmpeg"));
        Assert.Null(LinuxAudioCapture_SelectRecorderCommand(_ => false));
    }

    private static string? LinuxAudioCapture_SelectRecorderCommand(Func<string, bool> isOnPath)
    {
        var method = typeof(LinuxAudioCapture).GetMethod("SelectRecorderCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string?)method.Invoke(null, [isOnPath]);
    }

    [Theory]
    [InlineData("parecord")]
    [InlineData("arecord")]
    [InlineData("ffmpeg")]
    public void BuildStartInfo_requests_16kHz_mono_via_ArgumentList_not_a_shell_string(string command)
    {
        var method = typeof(LinuxAudioCapture).GetMethod("BuildStartInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var psi = (System.Diagnostics.ProcessStartInfo)method.Invoke(null, [command, "/tmp/out.wav"])!;

        Assert.False(psi.UseShellExecute);
        Assert.Contains(psi.ArgumentList, a => a.Contains("16000"));
        Assert.Contains("/tmp/out.wav", psi.ArgumentList);
    }

    // ── OpenAiSpeechRecognitionProvider: pure response parsing ─────────────────

    [Theory]
    [InlineData("{\"text\": \"hello world\"}", "hello world")]
    [InlineData("{\"text\": \"\"}", "")]
    [InlineData("{}", "")]
    [InlineData("not json at all", "")]
    public void ParseTranscriptText_handles_normal_empty_and_malformed_bodies(string body, string expected)
    {
        Assert.Equal(expected, OpenAiSpeechRecognitionProvider_ParseTranscriptText(body));
    }

    private static string OpenAiSpeechRecognitionProvider_ParseTranscriptText(string body)
    {
        var method = typeof(OpenAiSpeechRecognitionProvider).GetMethod("ParseTranscriptText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [body])!;
    }

    // ── NativeSpeechRecognitionProvider: no-model-installed paths ──────────────

    [Fact]
    public void NativeSpeechRecognitionProvider_IsAvailable_is_false_with_no_model_installed()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        var provider = new NativeSpeechRecognitionProvider(settings);

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public async Task NativeSpeechRecognitionProvider_TranscribeAsync_with_no_model_installed_returns_a_clear_error()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        var provider = new NativeSpeechRecognitionProvider(settings);

        var wavPath = temp.PathFor("a.wav");
        WavFile.Write(wavPath, [0.1f, 0.2f, 0.1f], 16000);
        await using var stream = File.OpenRead(wavPath);

        var result = await provider.TranscribeAsync(stream, new SpeechTranscribeOptions());

        Assert.Equal(string.Empty, result.Text);
        Assert.NotNull(result.Error);
        Assert.Contains("not installed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── SpeechRecognitionProviderRegistry ──────────────────────────────────────

    [Fact]
    public async Task Registry_defaults_to_native_and_persists_a_provider_switch()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var native = new NativeSpeechRecognitionProvider(settings);
        var openAi = new OpenAiSpeechRecognitionProvider(settings, new FakeSecretStoreForStt());
        var registry = new SpeechRecognitionProviderRegistry(settings, native, openAi);

        Assert.Equal(SttProvider.OnnxNative, registry.GetActiveProvider());
        Assert.Same(native, registry.GetActiveService());

        await registry.SetActiveProviderAsync(SttProvider.OpenAi);

        Assert.Equal(SttProvider.OpenAi, registry.GetActiveProvider());
        Assert.Same(openAi, registry.GetActiveService());
        Assert.Equal("OpenAi", settings.Settings.Stt.Provider);
    }

    // ── SttSettingsViewModel: file-transcription validation (5.3) ──────────────

    private static SttSettingsViewModel NewSttSettingsViewModel(TempDir temp, out ISettingsService settings)
    {
        settings = NewSettings(temp);
        var native = new NativeSpeechRecognitionProvider(settings);
        var openAi = new OpenAiSpeechRecognitionProvider(settings, new FakeSecretStoreForStt());
        var registry = new SpeechRecognitionProviderRegistry(settings, native, openAi);
        return new SttSettingsViewModel(settings, registry, new FakeToasts());
    }

    [Fact]
    public async Task TranscribeFile_rejects_a_non_wav_extension_without_reading_the_file()
    {
        using var temp = new TempDir();
        var vm = NewSttSettingsViewModel(temp, out _);
        var path = temp.PathFor("notes.txt");
        await File.WriteAllTextAsync(path, "hello");
        vm.RequestAudioFilePicker = () => Task.FromResult<string?>(path);

        await vm.TranscribeFileCommand.ExecuteAsync(null);

        Assert.Contains(".wav", vm.TranscribeStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, vm.TranscribedText);
    }

    [Fact]
    public async Task TranscribeFile_reports_a_missing_file_clearly()
    {
        using var temp = new TempDir();
        var vm = NewSttSettingsViewModel(temp, out _);
        vm.RequestAudioFilePicker = () => Task.FromResult<string?>(temp.PathFor("does-not-exist.wav"));

        await vm.TranscribeFileCommand.ExecuteAsync(null);

        Assert.Contains("not found", vm.TranscribeStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscribeFile_rejects_a_file_over_the_size_cap()
    {
        using var temp = new TempDir();
        var vm = NewSttSettingsViewModel(temp, out _);
        var path = temp.PathFor("huge.wav");
        // Sparse-ish large file: seek then write one byte, avoiding actually
        // allocating 200+MB of real content for the test.
        using (var fs = new FileStream(path, FileMode.Create))
        {
            fs.Seek(210L * 1024 * 1024, SeekOrigin.Begin);
            fs.WriteByte(0);
        }
        vm.RequestAudioFilePicker = () => Task.FromResult<string?>(path);

        await vm.TranscribeFileCommand.ExecuteAsync(null);

        Assert.Contains("too large", vm.TranscribeStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscribeFile_with_no_picker_wired_does_nothing()
    {
        using var temp = new TempDir();
        var vm = NewSttSettingsViewModel(temp, out _);

        await vm.TranscribeFileCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.TranscribeStatus);
    }

    private sealed class FakeSecretStoreForStt : ISecretStore
    {
        public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult($"secret:{name}");
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Fake");
    }
}
