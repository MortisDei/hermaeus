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
    "af_alloy",
    "af_aoede",
    "af_bella",
    "af_jessica",
    "af_kore",
    "af_nicole",
    "af_nova",
    "af_river",
    "af_sarah",
    "af_sky",
    "am_adam",
    "am_echo",
    "am_eric",
    "am_fenrir",
    "am_liam",
    "am_michael",
    "am_onyx",
    "am_puck",
    "am_santa",
    "bf_alice",
    "bf_emma",
    "bf_isabella",
    "bf_lily",
    "bm_daniel",
    "bm_fable",
    "bm_george",
    "bm_lewis",
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
