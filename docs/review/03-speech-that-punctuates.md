# 03. Speech that punctuates

## Was in-process ONNX the right call?

Yes for the architecture. No for the model. Those are separable and this
round keeps the first and replaces the second.

**The architecture call was right.** r24's spec said managed
`whisper-server`; the owner overrode it mid-round to in-process ONNX. That
override avoided a second managed process with its own pinned release tag,
its own download-extract-verify path, its own port to defend, its own
leftover-process banner and its own job-object membership. It matches
`NativeKokoroVoiceProvider`, which already proves the pattern on the output
side. Speech recognition now costs one model file and no lifecycle. Keep
it.

**The model choice is the problem.** `Wav2Vec2OnnxModel.Vocab`
(`Wav2Vec2OnnxModel.cs:38-42`) is thirty-two symbols:

```
<pad> <s> </s> <unk> | E T A O N I H S R D L U M W C F G Y P B V K ' X J Q Z
```

Twenty-six uppercase letters, an apostrophe, a word delimiter and four
special tokens. There is no lowercase and no punctuation in the
vocabulary, so no post-processing can recover them: the information was
never produced. Every transcript this app can emit looks like

```
HELLO CAN YOU CHECK WHETHER THE BUILD PASSED
```

For 5.4's dictation-anywhere, which inserts at the cursor for editing, that
is a transcript you must rewrite before you can use it. It is also
English-only, and `TranscribeAsync` hardcodes `"en"` into every
`SpeechTranscript` it returns (`NativeSpeechRecognitionProvider.cs:98-125`)
so nothing downstream can tell the truth about that either.

Whisper produces punctuation, casing and language detection because it was
trained on transcripts that have them. It is not a better decoder over the
same output; it is a different output.

## A real defect to fix regardless

`Wav2Vec2OnnxModel.Transcribe` builds one tensor over the entire waveform:

```
var input = new DenseTensor<float>(normalized, new[] { 1, normalized.Length });
```

(`Wav2Vec2OnnxModel.cs:140-141`). wav2vec2 is full self-attention, so cost
grows quadratically with audio length. Meanwhile the file-transcription
path caps input at `MaxFileBytes = 200L * 1024 * 1024`
(`SttSettingsViewModel.cs:17`), which at 16 kHz mono PCM16 is about
6,250 seconds of audio, roughly 1.7 hours, roughly 312,000 encoder frames.

That is not a slow transcription. It is an out-of-memory kill of the whole
application, reachable by choosing an ordinary podcast file in a picker the
app itself offered. The microphone path is safe only by accident, because
`MaxUtteranceSeconds` defaults to 60 (`SttSettings.cs:48`).

Whisper fixes this structurally: it decodes fixed 30 second windows, so
memory is constant in file length. That is the right fix. A smaller byte
cap is the fallback if this doc is cut (see doc 06).

## 3.1 Assets

- Pin a Whisper ONNX export by repository, revision and file name. **Verify
  the SHA256 by downloading and hashing at implementation time**, and
  record the verification date in a comment exactly as
  `Wav2Vec2OnnxModel.cs:25-32` and `KokoroOnnxModel` do. Do not copy a hash
  from a listing, a README or this document.
- Whisper exports as separate graphs: an encoder, and a decoder that takes
  and returns `past_key_values` (some exports merge the with-past and
  without-past decoders into one graph with a flag). Either shape is fine;
  pick one and state which in the class comment.
- **The KV cache is graph IO, not something to implement.** ONNX Runtime
  returns `present.*` outputs which are fed back as `past_key_values.*`
  inputs on the next step. r24's rejection reasoned about "KV cache
  management" as if it had to be written by hand
  (`docs/review/archived/r24/06-roadmap.md:176-179`); with an exported
  decoder graph it is bookkeeping over named tensors. That is the fact that
  makes this doc affordable, and it is worth stating in the class comment
  so r26 does not re-litigate it either.
- Ship one default, sized like the current one rather than larger. Do not
  auto-download: the existing approval-gated install plan
  (`NativeSpeechRecognitionProvider.cs:47-66`) already shows target path,
  install steps, risk level and download size, and it stays.
- **Retire `Wav2Vec2OnnxModel` from the code**, but never delete a model
  the user already downloaded. Two local STT engines is support surface for
  no benefit. Doctor reports any leftover `wav2vec2-stt` asset directory as
  no longer used, with its size and an explicit remove action. Silently
  deleting several hundred megabytes the user chose to download is exactly
  the kind of thing this app does not do.

## 3.2 The log-Mel front end

Whisper needs 80-bin log-Mel spectrogram input, not raw samples:

- 16 kHz, `n_fft` 400, hop 160, Hann window, 80 mel filters, log10, clamped
  to 8 dB below the maximum, scaled to roughly [-1, 1].
- Pad or trim to exactly 30 seconds, which is 3000 frames.

This needs an FFT. There is none in the base class library and **no new
NuGet package**. An iterative radix-2 complex FFT is about sixty lines and
is completely pure, which makes it one of the most testable things in the
codebase: check it against a hand-computed DFT for small inputs, check
Parseval's identity on random input, check a pure tone lands in the
expected bin.

Compute the mel filterbank from the standard formula at load time rather
than embedding it as an opaque blob, so its shape, monotonically increasing
centre frequencies and row normalization are all assertable.

Everything in this section is `float[]` in, `float[,]` out, with no
session and no IO. Give it the same treatment
`NormalizeZeroMeanUnitVariance` and `GreedyCtcDecode` already get
(`Wav2Vec2OnnxModel.cs:165-201`): `internal static`, pure, fully covered.

## 3.3 The decode loop

Greedy decode with Whisper's forced prefix:

```
<|startoftranscript|> <|lang|> <|transcribe|> <|notimestamps|>
```

- Stop on `<|endoftext|>` **or a hard maximum token count per window**. The
  maximum is not optional. Whisper's well-known failure mode is looping
  forever on silence or music, and an unbounded decode loop inside a
  desktop app is a hang with no cancel.
- Suppress the standard non-speech token set, and suppress the blank and
  space tokens on the first decode step, per Whisper's own decoding rules.
- **Detokenize only.** Whisper uses GPT-2 byte-level BPE, and greedy decode
  from a fixed prefix never needs to encode text, so the tokenizer reduces
  to an id-to-bytes table plus the byte decoder. There is no merge logic to
  implement and no tokenizer library to add.
- Map the compression-ratio and no-speech signals onto the existing
  `SpeechTranscript.IsLowConfidence`. Today that field means only "the text
  came back empty" (`NativeSpeechRecognitionProvider.cs:124-125`), which is
  why r24's hands-free mode cannot currently refuse a hallucinated
  transcript. Giving it real meaning is what makes 5.5's "never auto-send a
  low-confidence transcript" rule actually hold.

## 3.4 Long audio, bounded

- Decode 30 second windows sequentially with a small overlap, then
  concatenate. Memory is constant in file length because every window is
  the same fixed-size tensor.
- Keep a file cap, but an honest one, and **state it as a duration**, not
  as megabytes. The user is holding a recording, not a byte count.
- Report progress per window and allow cancellation between windows. A
  forty minute file must be stoppable, and the temp-file deletion rules
  from r24 5.2 apply to the cancelled path exactly as to the failed one.

## 3.5 Language, told truthfully

- `SpeechTranscript.Language` comes from the detected language token, not
  from a hardcoded `"en"`.
- A language preference: Auto (default) or a forced language. Settings >
  Voice, preference-only, per the r22 placement rule that r24 5.7 followed.
- If the shipped default model is English-only, say English-only in the UI
  and do not show the picker. Never offer a language list the installed
  model cannot honour.

## 3.6 Migration and honesty

- `SttSettings.Provider` stays `"OnnxNative"` (`SttSettings.cs:20`) and now
  means Whisper. No settings migration, no renamed value.
- Doctor's STT check (`DoctorService.Stt.cs`) reports an installed
  wav2vec2 model as superseded, offers the new install through the existing
  approval-gated action, and downloads nothing on launch.
- `docs/voice.md` must state what the local model actually is, which
  languages it handles, and that punctuation and casing come from the model
  rather than from post-processing. Delete anything that says otherwise.
  That file was written as output-only before r24 and its input half is the
  newest, least-reviewed prose in the docs directory.

## Testing

Roughly 20 to 24, none needing a microphone, a network call or a GPU. If a
test appears to need one, the abstraction is wrong; the per-platform asset
selection in `LlamaServerSetupService` remains the model to copy.

FFT against a reference DFT for small inputs, and Parseval on random input.
Mel filterbank row count, monotone centres, and row normalization. Log-Mel
of a synthetic tone putting energy in the expected bin. Padding and
trimming both producing exactly 3000 frames. Windowing a 95 second buffer
into the expected window count with the expected overlap. Decode stopping
at `<|endoftext|>`. Decode stopping at the token cap and reporting that
rather than hanging. Suppressed tokens never emitted. Detokenizing known
ids to known strings, including one multi-byte UTF-8 case. Low-confidence
classification from a synthetic repetitive token sequence. Language token
mapping to a language code. Cancellation honoured between windows. Pin
constants present and well-formed. The install plan naming the real target
path. The file cap expressed as a duration and rejecting an over-long file
with that duration in the message.
