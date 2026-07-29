using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>r24 doc 05 5.2: microphone capture, 16kHz mono PCM16 (what the STT
/// providers want, avoiding a resampler). No device, no silent failure: when
/// <see cref="IsAvailable"/> is false, <see cref="UnavailableReason"/> names the
/// actual reason ("no input device found", "microphone access denied by the
/// system", or on Linux, which recorder binaries were looked for and not found).</summary>
public interface IAudioCapture
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }

    IReadOnlyList<AudioInputDevice> EnumerateDevices();

    /// <summary>Starts capturing. The returned session must be disposed
    /// deterministically by the caller; a leaked session holding the microphone
    /// open is a defect of the same severity as a leaked server process.</summary>
    Task<ICaptureSession> StartAsync(string? deviceId, CancellationToken ct = default);
}

/// <summary>A single capture in progress. A visible recording indicator must cover
/// its entire duration (enforced by the caller, not this type). The temp WAV is
/// deleted by the caller after transcription, on every path including failure and
/// cancellation.</summary>
public interface ICaptureSession : IDisposable
{
    /// <summary>Fired roughly on each capture buffer with a 0..1 peak amplitude, for a UI level meter.</summary>
    event Action<float>? PeakLevelChanged;

    /// <summary>Stops capture and returns the temp WAV file path, or null if nothing was captured yet.</summary>
    string? Stop();
}
