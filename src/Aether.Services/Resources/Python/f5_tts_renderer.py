import argparse
from pathlib import Path

import soundfile as sf
from f5_tts.api import F5TTS

parser = argparse.ArgumentParser(description="Aether F5-TTS renderer")
parser.add_argument("--text", required=True)
parser.add_argument("--ref-audio", required=True)
parser.add_argument("--ref-text", default="")
parser.add_argument("--output", required=True)
parser.add_argument("--device", default="cpu")
parser.add_argument("--model", default="F5TTS_v1_Base")
parser.add_argument("--remove-silence", action="store_true")
args = parser.parse_args()

tts = F5TTS(model=args.model, device=args.device)
ref_text = args.ref_text.strip() if args.ref_text else tts.transcribe(args.ref_audio)
if not ref_text:
    raise RuntimeError("Could not determine reference text for F5-TTS voice cloning.")

wav, sr, _ = tts.infer(
    ref_file=args.ref_audio,
    ref_text=ref_text,
    gen_text=args.text,
    file_wave=args.output,
    remove_silence=args.remove_silence,
)

if not Path(args.output).exists():
    sf.write(args.output, wav, sr)
