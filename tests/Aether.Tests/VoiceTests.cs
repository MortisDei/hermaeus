using Aether.Core.Models;
using Aether.Voice;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

internal static class VoiceTests
{
    public static Task PhonemizerDictionaryWordsProduceOnlyVocabSymbols()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("hello world");
        True(phonemes.Length > 0, "Dictionary words should phonemize to a non-empty string.");
        AssertAllSymbolsAreInVocab(phonemes);
        return Task.CompletedTask;
    }

    public static Task PhonemizerFallbackHandlesOutOfDictionaryWords()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("xylophone zephyr");
        True(phonemes.Length > 0, "Out-of-dictionary words should still produce phonemes via the fallback rules.");
        AssertAllSymbolsAreInVocab(phonemes);
        return Task.CompletedTask;
    }

    public static Task PhonemizerIsDeterministic()
    {
        var first = KokoroPhonemizer.ToPhonemes("The quick brown fox.");
        var second = KokoroPhonemizer.ToPhonemes("The quick brown fox.");
        Equal(first, second, "Phonemization of the same input must be deterministic.");
        return Task.CompletedTask;
    }

    public static Task TokenizerWrapsEachChunkWithPadTokens()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("hello world");
        var chunks = KokoroTokenizer.Encode(phonemes);
        True(chunks.Count > 0, "Non-empty phonemes should produce at least one chunk.");
        foreach (var chunk in chunks)
        {
            Equal(KokoroVocab.PadTokenId, chunk[0], "Each chunk must start with the pad token.");
            Equal(KokoroVocab.PadTokenId, chunk[^1], "Each chunk must end with the pad token.");
            True(chunk.Length <= KokoroVocab.MaxSequenceTokens + 2, "Chunk length must respect Kokoro's context window plus the two pad tokens.");
        }

        return Task.CompletedTask;
    }

    public static Task TokenizerSplitsLongInputIntoMultipleChunks()
    {
        var longPhonemes = string.Concat(Enumerable.Repeat(KokoroVocab.Ash, KokoroVocab.MaxSequenceTokens * 2 + 5));
        var chunks = KokoroTokenizer.Encode(longPhonemes);
        True(chunks.Count >= 3, $"Expected at least 3 chunks for {longPhonemes.Length} phonemes, got {chunks.Count}.");
        return Task.CompletedTask;
    }

    public static Task TokenizerReturnsEmptyForBlankInput()
    {
        var chunks = KokoroTokenizer.Encode(string.Empty);
        Equal(0, chunks.Count, "Blank phoneme input should produce no chunks.");
        return Task.CompletedTask;
    }

    public static async Task OnnxModelRefusesToLoadWhenAssetsAreMissing()
    {
        using var temp = new TempDir();
        var model = new KokoroOnnxModel(() => temp.PathFor("kokoro-assets"));
        var loaded = await model.EnsureLoadedAsync("af_heart", CancellationToken.None);
        False(loaded, "Model must not report loaded when its ONNX file has not been downloaded yet.");
        False(model.AssetsPresent("af_heart"), "AssetsPresent must be false when the model directory is empty.");
    }

    public static async Task OnnxModelHashVerificationRejectsTamperedFile()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("model.onnx");
        await File.WriteAllTextAsync(path, "not the real model");
        False(await KokoroOnnxModel.VerifySha256Async(path, KokoroOnnxModel.ModelSha256), "A tampered/incomplete file must fail SHA256 verification against the pinned hash.");
    }

    public static Task NativeProviderReportsNotInstalledWithoutAssets()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai-assets");

        using var provider = new NativeKokoroVoiceProvider(settings);
        False(provider.IsInstalled, "Native Kokoro provider must not report installed before assets are downloaded.");

        var detection = provider.Detect();
        False(detection.IsAvailable, "Detect() must report unavailable when the ONNX model file is missing.");
        return Task.CompletedTask;
    }

    public static Task NativeProviderReResolvesAssetsRootAfterSettingsChange()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai-assets-a");

        using var provider = new NativeKokoroVoiceProvider(settings);
        False(provider.Detect().IsAvailable, "Must report unavailable before any assets root has a model.");

        // Point LocalAiAssetsRoot somewhere else and drop a model file there,
        // without recreating the provider (it is a DI singleton in the real
        // app and is never reconstructed just because settings changed).
        var newRoot = temp.PathFor("ai-assets-b");
        settings.Settings.DataManagement.LocalAiAssetsRoot = newRoot;
        var kokoroDir = Path.Combine(newRoot, "Models", "voice", "kokoro-native");
        Directory.CreateDirectory(kokoroDir);
        File.WriteAllText(Path.Combine(kokoroDir, "model_q8f16.onnx"), "placeholder");

        True(provider.Detect().IsAvailable,
            "The provider must re-resolve LocalAiAssetsRoot from current settings rather than caching the value from construction time.");
        return Task.CompletedTask;
    }

    public static Task NativeProviderRequiresNoPythonVersion()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        using var provider = new NativeKokoroVoiceProvider(settings);
        Equal(null, provider.RequiredPythonVersion, "The native provider must not require a Python interpreter.");
        return Task.CompletedTask;
    }

    private static void AssertAllSymbolsAreInVocab(string phonemes)
    {
        foreach (var c in phonemes)
        {
            True(KokoroVocab.SymbolToId.Keys.Any(k => k.Length == 1 && k[0] == c),
                $"Phoneme character '{c}' (U+{(int)c:X4}) is not present in Kokoro's vocabulary.");
        }
    }
}
