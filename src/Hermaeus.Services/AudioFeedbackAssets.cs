using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// Small source-controlled PCM assets. They are generated as bounded WAV data
/// in memory so no downloaded pack or binary dependency enters the product.
/// </summary>
internal static class AudioFeedbackAssets
{
    public static byte[] CreateWav(AudioFeedbackEventKind kind, int volume)
    {
        var sampleRate = 8000;
        var sampleCount = kind == AudioFeedbackEventKind.TaskFailed ? 1600 : 900;
        var frequency = kind switch
        {
            AudioFeedbackEventKind.TaskFailed or AudioFeedbackEventKind.ManagedRuntimeFailed => 330,
            AudioFeedbackEventKind.TaskNeedsApproval => 660,
            _ => 520
        };
        var amplitude = (short)(Math.Clamp(volume, 0, 100) * 300);
        var dataLength = sampleCount * sizeof(short);
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        for (var i = 0; i < sampleCount; i++)
        {
            var envelope = i < 40 ? i / 40d : (sampleCount - i < 80 ? (sampleCount - i) / 80d : 1d);
            writer.Write((short)(Math.Sin(i * 2 * Math.PI * frequency / sampleRate) * amplitude * envelope));
        }
        return stream.ToArray();
    }
}
