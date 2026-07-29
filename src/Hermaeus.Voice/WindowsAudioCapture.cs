using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Voice;

/// <summary>
/// r24 doc 05 5.2: Windows microphone capture via the winmm waveIn API
/// (no NuGet package - a well-trodden native API). Double-buffered with
/// CALLBACK_EVENT so buffer-ready notifications are picked up by a plain
/// background thread rather than a native callback delegate invoked on an
/// arbitrary OS thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioCapture : IAudioCapture
{
    private const int SampleRate = 16000;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BufferMs = 100;
    private const int BufferCount = 4;

    /// <summary>Hard safety ceiling enforced by the capture engine itself, independent
    /// of any caller-side timer - the hot-mic rules require this in code, not by
    /// convention. The configurable, user-visible default (SttSettings.MaxUtteranceSeconds,
    /// 60s) is enforced by the caller; this is the backstop if that caller has a bug.</summary>
    private static readonly TimeSpan HardMaxDuration = TimeSpan.FromMinutes(5);

    public bool IsAvailable => WinMm.waveInGetNumDevs() > 0;
    public string? UnavailableReason => IsAvailable ? null : "No input device found.";

    public IReadOnlyList<AudioInputDevice> EnumerateDevices()
    {
        var count = WinMm.waveInGetNumDevs();
        var devices = new List<AudioInputDevice>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (WinMm.waveInGetDevCaps((nuint)i, out var caps, (uint)Marshal.SizeOf<WinMm.WAVEINCAPS>()) == 0)
                devices.Add(new AudioInputDevice(i.ToString(), caps.szPname));
        }
        return devices;
    }

    public Task<ICaptureSession> StartAsync(string? deviceId, CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(UnavailableReason ?? "No input device found.");

        var deviceIndex = 0u;
        if (!string.IsNullOrWhiteSpace(deviceId) && uint.TryParse(deviceId, out var parsed))
            deviceIndex = parsed;

        var session = new Session(deviceIndex, SampleRate, Channels, BitsPerSample, BufferMs, BufferCount);
        session.Start();
        return Task.FromResult<ICaptureSession>(session);
    }

    private sealed class Session : ICaptureSession
    {
        private readonly int _sampleRate;
        private readonly int _bufferBytes;
        private readonly GCHandle[] _headerPins;
        private readonly GCHandle[] _dataPins;
        private readonly byte[][] _buffers;
        private readonly List<byte> _captured = new(SampleRate * 2 * 30);
        private readonly AutoResetEvent _bufferEvent = new(false);
        private readonly object _lock = new();
        private nint _waveIn;
        private Thread? _pumpThread;
        private volatile bool _running;
        private bool _disposed;

        public event Action<float>? PeakLevelChanged;

        public Session(uint deviceIndex, int sampleRate, short channels, short bitsPerSample, int bufferMs, int bufferCount)
        {
            _sampleRate = sampleRate;
            _bufferBytes = sampleRate * channels * (bitsPerSample / 8) * bufferMs / 1000;

            var format = new WinMm.WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = channels,
                nSamplesPerSec = sampleRate,
                wBitsPerSample = bitsPerSample,
                nBlockAlign = (short)(channels * bitsPerSample / 8),
                nAvgBytesPerSec = sampleRate * channels * bitsPerSample / 8,
                cbSize = 0
            };

            var result = WinMm.waveInOpen(out _waveIn, deviceIndex, ref format, _bufferEvent.SafeWaitHandle.DangerousGetHandle(), 0, WinMm.CALLBACK_EVENT);
            if (result != 0)
                throw new InvalidOperationException($"Could not open the microphone (winmm error {result}). It may be in use by another application, or access may be denied by the system.");

            _headerPins = new GCHandle[bufferCount];
            _dataPins = new GCHandle[bufferCount];
            _buffers = new byte[bufferCount][];
            for (var i = 0; i < bufferCount; i++)
            {
                _buffers[i] = new byte[_bufferBytes];
                _dataPins[i] = GCHandle.Alloc(_buffers[i], GCHandleType.Pinned);

                // Boxed explicitly (not a plain array element): the pinned handle
                // must address this exact object, and native code mutates it in
                // place through that same address on every subsequent call.
                object header = new WinMm.WAVEHDR
                {
                    lpData = _dataPins[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)_bufferBytes
                };
                _headerPins[i] = GCHandle.Alloc(header, GCHandleType.Pinned);
            }
        }

        public void Start()
        {
            for (var i = 0; i < _headerPins.Length; i++)
            {
                var addr = _headerPins[i].AddrOfPinnedObject();
                WinMm.waveInPrepareHeader(_waveIn, addr, Marshal.SizeOf<WinMm.WAVEHDR>());
                WinMm.waveInAddBuffer(_waveIn, addr, Marshal.SizeOf<WinMm.WAVEHDR>());
            }

            var openResult = WinMm.waveInStart(_waveIn);
            if (openResult != 0)
                throw new InvalidOperationException($"Could not start microphone capture (winmm error {openResult}).");

            _running = true;
            _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "hermaeus-mic-capture" };
            _pumpThread.Start();
        }

        private void PumpLoop()
        {
            var started = DateTime.UtcNow;
            while (_running)
            {
                if (DateTime.UtcNow - started >= HardMaxDuration)
                {
                    Stop();
                    break;
                }

                if (!_bufferEvent.WaitOne(200))
                    continue;

                for (var i = 0; i < _headerPins.Length; i++)
                {
                    if (!_running) break;

                    var addr = _headerPins[i].AddrOfPinnedObject();
                    var header = Marshal.PtrToStructure<WinMm.WAVEHDR>(addr);
                    if ((header.dwFlags & WinMm.WHDR_DONE) == 0)
                        continue;

                    var bytesReady = (int)header.dwBytesRecorded;
                    if (bytesReady > 0)
                    {
                        lock (_lock)
                        {
                            for (var b = 0; b < bytesReady; b++)
                                _captured.Add(_buffers[i][b]);
                        }
                        PeakLevelChanged?.Invoke(ComputePeak(_buffers[i], bytesReady));
                    }

                    if (_running)
                    {
                        WinMm.waveInUnprepareHeader(_waveIn, addr, Marshal.SizeOf<WinMm.WAVEHDR>());
                        WinMm.waveInPrepareHeader(_waveIn, addr, Marshal.SizeOf<WinMm.WAVEHDR>());
                        WinMm.waveInAddBuffer(_waveIn, addr, Marshal.SizeOf<WinMm.WAVEHDR>());
                    }
                }
            }
        }

        private static float ComputePeak(byte[] buffer, int byteCount)
        {
            short peak = 0;
            for (var i = 0; i + 1 < byteCount; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                var abs = Math.Abs((int)sample);
                if (abs > peak) peak = (short)abs;
            }
            return peak / (float)short.MaxValue;
        }

        private bool _finalized;
        private string? _finalizedPath;

        /// <summary>Stops capture and returns the WAV path. Idempotent, and safe to call
        /// from the pump thread itself (the hard-duration safety net does exactly that) -
        /// a thread never joins itself, and finalization only runs once regardless of
        /// which caller wins the race.</summary>
        public string? Stop()
        {
            if (!_running)
                return _finalizedPath;

            _running = false;
            _bufferEvent.Set(); // wake the pump thread so it observes _running=false promptly
            if (Thread.CurrentThread != _pumpThread)
                _pumpThread?.Join(TimeSpan.FromSeconds(2));

            return FinalizeCapture();
        }

        private string? FinalizeCapture()
        {
            lock (_lock)
            {
                if (_finalized) return _finalizedPath;
                _finalized = true;
            }

            WinMm.waveInStop(_waveIn);
            WinMm.waveInReset(_waveIn);
            for (var i = 0; i < _headerPins.Length; i++)
                WinMm.waveInUnprepareHeader(_waveIn, _headerPins[i].AddrOfPinnedObject(), Marshal.SizeOf<WinMm.WAVEHDR>());

            byte[] pcm;
            lock (_lock)
                pcm = _captured.ToArray();

            if (pcm.Length == 0)
                return null;

            var samples = new float[pcm.Length / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = value / (float)short.MaxValue;
            }

            var path = Path.Combine(Path.GetTempPath(), $"hermaeus-capture-{Guid.NewGuid():N}.wav");
            WavFile.Write(path, samples, _sampleRate);
            _finalizedPath = path;
            return path;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_running)
                Stop();

            if (_waveIn != 0)
            {
                WinMm.waveInClose(_waveIn);
                _waveIn = 0;
            }

            foreach (var pin in _headerPins)
                if (pin.IsAllocated) pin.Free();
            foreach (var pin in _dataPins)
                if (pin.IsAllocated) pin.Free();

            _bufferEvent.Dispose();
        }
    }

    private static class WinMm
    {
        public const int CALLBACK_EVENT = 0x00050000;
        public const uint WHDR_DONE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public nint lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public nint dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public nint lpNext;
            public nint reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS
        {
            public short wMid;
            public short wPid;
            public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public short wChannels;
            public short wReserved1;
        }

        [DllImport("winmm.dll")]
        public static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        public static extern int waveInGetDevCaps(nuint uDeviceID, out WAVEINCAPS caps, uint cbwic);

        [DllImport("winmm.dll")]
        public static extern int waveInOpen(out nint hwi, uint uDeviceID, ref WAVEFORMATEX format, nint dwCallback, nint dwInstance, int fdwOpen);

        [DllImport("winmm.dll")]
        public static extern int waveInPrepareHeader(nint hwi, nint pwh, int cbwh);

        [DllImport("winmm.dll")]
        public static extern int waveInUnprepareHeader(nint hwi, nint pwh, int cbwh);

        [DllImport("winmm.dll")]
        public static extern int waveInAddBuffer(nint hwi, nint pwh, int cbwh);

        [DllImport("winmm.dll")]
        public static extern int waveInStart(nint hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInStop(nint hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInReset(nint hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInClose(nint hwi);
    }
}
