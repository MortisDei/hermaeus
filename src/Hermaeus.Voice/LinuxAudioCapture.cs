using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Voice;

/// <summary>
/// r24 doc 05 5.2: Linux microphone capture via the same subprocess fallback
/// chain <see cref="AudioPlayback"/> already uses for output - try
/// <c>parecord</c>, then <c>arecord</c>, then <c>ffmpeg</c>, first one found on
/// PATH wins, launched with ArgumentList (no shell string). If none of the
/// three are found, <see cref="UnavailableReason"/> names which ones it looked
/// for, since that is a fixable problem the user can only fix if told.
/// </summary>
public sealed class LinuxAudioCapture : IAudioCapture
{
    private const int SampleRate = 16000;

    public bool IsAvailable => SelectRecorderCommand(IsOnPath) is not null;

    public string? UnavailableReason => IsAvailable
        ? null
        : "No microphone recorder found on PATH (looked for parecord, arecord, ffmpeg). Install one of these to enable dictation.";

    public IReadOnlyList<AudioInputDevice> EnumerateDevices() =>
        // Device enumeration varies per backend (PulseAudio/ALSA/ffmpeg) and none
        // of the three recorder tools expose a portable programmatic listing; "default"
        // is what every one of them captures from when no device is specified, and is
        // the only choice this round offers on Linux.
        IsAvailable ? [new AudioInputDevice("default", "System default")] : [];

    public Task<ICaptureSession> StartAsync(string? deviceId, CancellationToken ct = default)
    {
        var command = SelectRecorderCommand(IsOnPath)
            ?? throw new InvalidOperationException(UnavailableReason ?? "No microphone recorder found.");

        var path = Path.Combine(Path.GetTempPath(), $"hermaeus-capture-{Guid.NewGuid():N}.wav");
        var psi = BuildStartInfo(command, path);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start '{command}' to capture audio.");

        return Task.FromResult<ICaptureSession>(new Session(process, path));
    }

    /// <summary>Selection-logic seam for tests, mirroring <see cref="AudioPlayback.SelectPlayerCommand"/>.</summary>
    internal static string? SelectRecorderCommand(Func<string, bool> isOnPath)
    {
        if (isOnPath("parecord")) return "parecord";
        if (isOnPath("arecord")) return "arecord";
        if (isOnPath("ffmpeg")) return "ffmpeg";
        return null;
    }

    /// <summary>Argument construction seam for tests: verifies each recorder is invoked
    /// with 16kHz mono PCM16 output and no shell string.</summary>
    internal static ProcessStartInfo BuildStartInfo(string command, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        IReadOnlyList<string> args = command switch
        {
            "parecord" => ["--rate=16000", "--channels=1", "--format=s16le", "--file-format=wav", outputPath],
            "arecord" => ["-f", "S16_LE", "-r", "16000", "-c", "1", "-t", "wav", outputPath],
            "ffmpeg" => ["-y", "-f", "pulse", "-i", "default", "-ar", "16000", "-ac", "1", outputPath],
            _ => throw new ArgumentException($"Unknown recorder command: {command}")
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    private static bool IsOnPath(string command)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return pathVar.Split(Path.PathSeparator).Any(dir =>
        {
            try { return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, command)); }
            catch { return false; }
        });
    }

    private sealed class Session : ICaptureSession
    {
        private readonly Process _process;
        private readonly string _outputPath;
        private readonly Timer _levelTimer;
        private long _lastReadPosition;
        private bool _stopped;
        private bool _disposed;

        public event Action<float>? PeakLevelChanged;

        public Session(Process process, string outputPath)
        {
            _process = process;
            _outputPath = outputPath;
            // Best-effort level meter: the recorder is a subprocess writing the WAV
            // directly, so there is no in-process buffer callback to hook the way
            // WindowsAudioCapture does. Poll the growing file instead.
            _levelTimer = new Timer(_ => SampleLevel(), null, 150, 150);
        }

        private void SampleLevel()
        {
            try
            {
                using var stream = new FileStream(_outputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length <= _lastReadPosition) return;

                stream.Position = _lastReadPosition;
                var toRead = (int)Math.Min(stream.Length - _lastReadPosition, 32000);
                var buffer = new byte[toRead];
                var read = stream.Read(buffer, 0, toRead);
                _lastReadPosition += read;

                short peak = 0;
                for (var i = 0; i + 1 < read; i += 2)
                {
                    var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                    var abs = Math.Abs((int)sample);
                    if (abs > peak) peak = (short)abs;
                }
                PeakLevelChanged?.Invoke(peak / (float)short.MaxValue);
            }
            catch
            {
                // File not created yet, or mid-write; try again on the next tick.
            }
        }

        public string? Stop()
        {
            if (_stopped)
                return File.Exists(_outputPath) ? _outputPath : null;
            _stopped = true;

            _levelTimer.Change(Timeout.Infinite, Timeout.Infinite);
            RequestGracefulStop();

            return File.Exists(_outputPath) && new FileInfo(_outputPath).Length > 0 ? _outputPath : null;
        }

        /// <summary>Sends SIGINT so the recorder finalizes the WAV header on the way out,
        /// falling back to a hard kill if it does not exit promptly.</summary>
        private void RequestGracefulStop()
        {
            try
            {
                if (_process.HasExited) return;

                using var interrupt = Process.Start(new ProcessStartInfo
                {
                    FileName = "kill",
                    ArgumentList = { "-INT", _process.Id.ToString() },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                interrupt?.WaitForExit(1000);

                if (!_process.WaitForExit(2000) && !_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_stopped)
                Stop();

            _levelTimer.Dispose();
            try { _process.Dispose(); } catch { }
        }
    }
}
