using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// Small source-controlled PCM assets. They are generated as bounded WAV data
/// in memory so no downloaded pack or binary dependency enters the product.
/// </summary>
internal static class AudioFeedbackAssets
{
    internal sealed record Cue(string Id, IReadOnlyList<int> Frequencies, int ToneMilliseconds = 110, int GapMilliseconds = 45);

    public static Cue Resolve(AudioFeedbackEventKind kind) => kind switch
    {
        AudioFeedbackEventKind.TaskNeedsApproval => new("task-needs-approval-ascending", [660, 880]),
        AudioFeedbackEventKind.TaskCompleted => new("task-completed-triad", [523, 659, 784]),
        AudioFeedbackEventKind.TaskFailed => new("task-failed-descending", [440, 330], 180, 70),
        AudioFeedbackEventKind.ManagedRuntimeReady => new("runtime-ready-ascending", [440, 660, 880]),
        AudioFeedbackEventKind.ManagedRuntimeFailed => new("runtime-failed-descending", [440, 220], 180, 70),
        AudioFeedbackEventKind.LongOperationCompleted => new("long-operation-completed-triad", [392, 523, 659]),
        AudioFeedbackEventKind.RecordingStarted => new("recording-started-short", [880], 90, 20),
        AudioFeedbackEventKind.RecordingStopped => new("recording-stopped-short", [660], 90, 20),
        _ => new("generic-notification", [520])
    };

    public static byte[] CreateWav(AudioFeedbackEventKind kind, int volume)
    {
        var cue = Resolve(kind);
        const int sampleRate = 16000;
        var toneSamples = Math.Max(1, sampleRate * cue.ToneMilliseconds / 1000);
        var gapSamples = Math.Max(0, sampleRate * cue.GapMilliseconds / 1000);
        var sampleCount = cue.Frequencies.Count * toneSamples + Math.Max(0, cue.Frequencies.Count - 1) * gapSamples;
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
            var cuePosition = toneSamples + gapSamples;
            var frequencyIndex = Math.Min(cue.Frequencies.Count - 1, i / cuePosition);
            var tonePosition = i % cuePosition;
            if (tonePosition >= toneSamples)
            {
                writer.Write((short)0);
                continue;
            }

            var fadeIn = Math.Min(1d, tonePosition / (sampleRate * 0.008d));
            var fadeOut = Math.Min(1d, (toneSamples - tonePosition) / (sampleRate * 0.012d));
            var envelope = Math.Min(fadeIn, fadeOut);
            var frequency = cue.Frequencies[frequencyIndex];
            writer.Write((short)(Math.Sin(tonePosition * 2 * Math.PI * frequency / sampleRate) * amplitude * envelope));
        }
        return stream.ToArray();
    }
}
