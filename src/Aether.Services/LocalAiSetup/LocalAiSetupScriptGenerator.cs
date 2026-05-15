namespace Aether.Services;

internal static class LocalAiSetupScriptGenerator
{
    internal static string BuildXttsApiScript(string? modelDirectory = null, string? outputDirectory = null)
    {
        var modelDefault = string.IsNullOrWhiteSpace(modelDirectory) ? "None" : $"r'''{modelDirectory.Trim()}'''";
        var outputDefault = string.IsNullOrWhiteSpace(outputDirectory) ? "None" : $"r'''{outputDirectory.Trim()}'''";
        return $$"""
#!/usr/bin/env python3
import argparse
import os
import time
import uuid
from pathlib import Path
from typing import Optional

import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel


DEFAULT_MODEL_DIR = {{modelDefault}}
DEFAULT_OUTPUT_DIR = {{outputDefault}}
app = FastAPI(title="Aether XTTS v2 API")
tts_engine = None
settings = None


class SpeechRequest(BaseModel):
    input: Optional[str] = None
    text: Optional[str] = None
    voice: Optional[str] = None
    speaker: Optional[str] = None
    speaker_wav: Optional[str] = None
    language: str = "en"
    response_format: str = "wav"


def find_xtts_model(script_dir: Path) -> Optional[Path]:
    candidates = [
        script_dir / "multi-dataset--xtts_v2",
        script_dir.parent / "multi-dataset--xtts_v2",
        script_dir.parent / "TTS" / "multi-dataset--xtts_v2",
    ]
    for candidate in candidates:
        if (candidate / "config.json").exists() and ((candidate / "model.pth").exists() or (candidate / "model.safetensors").exists()):
            return candidate
    return None


def load_model():
    global tts_engine
    if tts_engine is not None:
        return tts_engine
    try:
        from TTS.api import TTS
    except Exception as exc:
        raise RuntimeError(f"Python package TTS is not installed in this environment: {exc}") from exc

    model_dir = Path(settings.model_dir) if settings.model_dir else find_xtts_model(Path(__file__).resolve().parent)
    if model_dir is None:
        model_name = f"tts_models/multilingual/multi-dataset/xtts_v{settings.model_version}"
        tts_engine = TTS(model_name=model_name)
    else:
        config_path = model_dir / "config.json"
        tts_engine = TTS(model_path=str(model_dir), config_path=str(config_path))
    if hasattr(tts_engine, "to"):
        tts_engine = tts_engine.to(settings.device)
    return tts_engine


def voice_candidates() -> list[str]:
    voice_dir = Path(settings.voice_dir) if settings.voice_dir else Path(settings.output_dir).parent / "voices"
    if not voice_dir.exists():
        return []
    return [str(path) for path in voice_dir.glob("*") if path.suffix.lower() in {".wav", ".mp3", ".flac"}]


@app.get("/health")
def health():
    return {"ok": True, "model_loaded": tts_engine is not None}


@app.get("/voices")
def voices():
    return {"voices": voice_candidates()}


@app.get("/v1/audio/voices")
def openai_voices():
    return {"data": [{"id": path, "name": Path(path).stem} for path in voice_candidates()]}


@app.post("/v1/audio/speech")
def speech(request: SpeechRequest):
    text = request.input or request.text
    if not text:
        raise HTTPException(status_code=400, detail="input or text is required")
    speaker_wav = request.speaker_wav or request.voice or request.speaker
    if not speaker_wav:
        voices = voice_candidates()
        speaker_wav = voices[0] if voices else None
    if not speaker_wav:
        raise HTTPException(status_code=400, detail="speaker_wav or a voice sample is required")

    engine = load_model()
    output_dir = Path(settings.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / f"aether-{int(time.time())}-{uuid.uuid4().hex}.wav"
    engine.tts_to_file(text=text, speaker_wav=speaker_wav, language=request.language, file_path=str(output_path))
    return FileResponse(str(output_path), media_type="audio/wav", filename=output_path.name)


def parse_args():
    parser = argparse.ArgumentParser(description="Aether XTTS v2 API server")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8020)
    parser.add_argument("--model-dir", default=DEFAULT_MODEL_DIR)
    parser.add_argument("--output-dir", default=DEFAULT_OUTPUT_DIR or str(Path.cwd() / "output"))
    parser.add_argument("--voice-dir", default=None)
    parser.add_argument("--model-version", default="2.0.3")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--preload", action="store_true")
    return parser.parse_args()


if __name__ == "__main__":
    settings = parse_args()
    if settings.preload:
        load_model()
    uvicorn.run(app, host=settings.host, port=settings.port)
""";
    }
}
