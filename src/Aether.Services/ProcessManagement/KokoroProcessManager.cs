using System.Diagnostics;
using System.Text;
using Aether.Core.Models;

namespace Aether.Services.ProcessManagement;

public sealed class KokoroProcessManager : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _healthCts;
    private string? _serverScriptPath;

    public bool IsRunning => _process is { HasExited: false };
    public string StatusLabel { get; private set; } = "Stopped";
    public event Action? StatusChanged;

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (IsRunning) return;

        StatusLabel = "Starting";
        StatusChanged?.Invoke();

        var baseUrl = new Uri(settings.Tts.ServiceUrl.TrimEnd('/'));
        var python = ResolvePython(settings);
        var defaultVoice = string.IsNullOrWhiteSpace(settings.Tts.Speaker) ? "af_heart" : settings.Tts.Speaker.Trim();
        var scriptPath = await EnsureServerScriptAsync(ct);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add(baseUrl.Host);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(baseUrl.Port.ToString());
        psi.ArgumentList.Add("--voice");
        psi.ArgumentList.Add(defaultVoice);
        psi.ArgumentList.Add("--speed");
        psi.ArgumentList.Add(Math.Clamp(settings.Tts.Speed, 0.5, 2.0).ToString("0.0################", System.Globalization.CultureInfo.InvariantCulture));

        if (settings.Tts.Device.Equals("cpu", StringComparison.OrdinalIgnoreCase))
            psi.Environment["CUDA_VISIBLE_DEVICES"] = string.Empty;
        else
            psi.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            StatusLabel = "Stopped";
            StatusChanged?.Invoke();
        };

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start Kokoro voice service.");

            _healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(settings.Tts.ServiceUrl, _healthCts.Token);
            StatusLabel = "Running";
            StatusChanged?.Invoke();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        _healthCts?.Cancel();
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        _process?.Dispose();
        _process = null;
        StatusLabel = "Stopped";
        StatusChanged?.Invoke();
    }

    private static async Task WaitForHealthAsync(string baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"{baseUrl.TrimEnd('/')}/health";
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(400, ct);
        }

        throw new TimeoutException("Kokoro voice service did not become healthy within 2 minutes.");
    }

    private static string ResolvePython(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Tts.PythonPath))
            return settings.Tts.PythonPath.Trim();

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private async Task<string> EnsureServerScriptAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_serverScriptPath) && File.Exists(_serverScriptPath))
            return _serverScriptPath;

        var path = Path.Combine(Path.GetTempPath(), "aether-kokoro-server.py");
        await File.WriteAllTextAsync(path, BuildScript(), Encoding.UTF8, ct);
        _serverScriptPath = path;
        return path;
    }

    private static string BuildScript() =>
        """
import argparse
import io
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
import soundfile as sf
from kokoro import KPipeline

DEFAULT_VOICES = [
    "af_heart",
    "af_bella",
    "af_sky",
    "af_nicole",
    "am_michael",
    "am_danny",
    "bf_isabella",
    "bm_george",
]

parser = argparse.ArgumentParser(description="Aether Kokoro voice server")
parser.add_argument("--host", default="127.0.0.1")
parser.add_argument("--port", type=int, default=8020)
parser.add_argument("--voice", default="af_heart")
parser.add_argument("--speed", type=float, default=1.0)
args = parser.parse_args()

_pipelines = {}
_pipeline_lock = threading.Lock()


def _lang_for_voice(voice: str) -> str:
    return voice[0].lower() if voice and voice[0].isalpha() else "a"


def _pipeline_for_voice(voice: str):
    lang = _lang_for_voice(voice)
    with _pipeline_lock:
        if lang not in _pipelines:
            _pipelines[lang] = KPipeline(lang_code=lang)
        return _pipelines[lang]


def _render_wav_bytes(text: str, voice: str, speed: float) -> bytes:
    pipeline = _pipeline_for_voice(voice)
    chunks = []
    for result in pipeline(text, voice=voice, speed=speed, split_pattern=r"\\n+"):
        audio = result.audio
        if audio is None:
            continue
        if hasattr(audio, "detach"):
            audio = audio.detach().cpu().numpy()
        else:
            audio = np.asarray(audio)
        chunks.append(audio.astype(np.float32))

    if not chunks:
        raise RuntimeError("No audio generated")

    audio = np.concatenate(chunks)
    buffer = io.BytesIO()
    sf.write(buffer, audio, 24000, format="WAV", subtype="PCM_16")
    return buffer.getvalue()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def _send_json(self, payload: dict, status: int = 200):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._send_json({"status": "ok"})
            return

        if self.path == "/voices":
            self._send_json({"voices": DEFAULT_VOICES})
            return

        self._send_json({"detail": "Not found"}, status=404)

    def do_POST(self):
        if self.path != "/v1/audio/speech":
            self._send_json({"detail": "Not found"}, status=404)
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            raw = self.rfile.read(length) if length > 0 else b"{}"
            data = json.loads(raw.decode("utf-8") or "{}")

            text = str(data.get("input") or "").strip()
            if not text:
                self._send_json({"detail": "input is required"}, status=400)
                return

            voice = str(data.get("speaker_wav") or args.voice or "af_heart").strip() or "af_heart"
            speed = float(data.get("speed") or args.speed or 1.0)
            speed = max(0.5, min(2.0, speed))

            wav = _render_wav_bytes(text, voice, speed)
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(wav)))
            self.end_headers()
            self.wfile.write(wav)
        except Exception as ex:
            self._send_json({"detail": str(ex)}, status=500)


if __name__ == "__main__":
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.serve_forever()
""";

    public void Dispose()
    {
        Stop();
        if (!string.IsNullOrWhiteSpace(_serverScriptPath))
        {
            try { File.Delete(_serverScriptPath); }
            catch { }
        }
    }
}
