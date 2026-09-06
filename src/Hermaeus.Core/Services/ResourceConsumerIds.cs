namespace Hermaeus.Core.Services;

/// <summary>Stable logical ids for local-AI workloads.</summary>
public static class ResourceConsumerIds
{
    /// <summary>Kokoro (native) only. The Python provider uses tts.kokoro-process.</summary>
    public const string NativeKokoro = "tts.kokoro-native";
}
