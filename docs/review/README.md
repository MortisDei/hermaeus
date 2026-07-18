# Review round 17: Hardware-aware context fit and benchmark truth

Written 2026-07-19 against v0.21.0-alpha (r16 implemented at ebed9fd).
Prior rounds live in `docs/review/archived/`; check each round's roadmap
"Explicit rejections" before proposing anything adjacent.

## Why this round

One theme: **numbers the app shows about models and hardware must be
measured or derived, not guessed.** Two fronts, and they share a new
component (a GGUF metadata reader) that makes both honest.

1. **Context fit and tuning (doc 01).** The "large context" warning on
   the Services page is a flat `ContextSize > 16384` check
   (`ServicesViewModel.cs:102-107`); it fires identically on a 6 GB card
   and a 24 GB card because nothing in the app can compute what a KV
   cache actually costs. `ModelFitEstimator` only weighs the model file
   against VRAM and ignores context entirely. Auto Tune searches GPU
   layer candidates but passes `ContextSize` straight through, so the
   one knob that most often decides whether a model fits is never tuned.
   All three become real by parsing the GGUF header (layer count, KV
   head count, head dims, training context, quantization), which the
   repo currently never reads.
2. **Benchmark truth (doc 02).** The benchmark's tokens/sec is
   `chars / 4` over the *total* case duration including prompt
   processing, while the llama-server timings object (parsed since r10
   for chat, `LlamaCppService.cs:325-338`) is thrown away because the
   benchmark uses the text-only stream. `ResourceScore` measures the
   Aether process's own RSS delta, not the server doing the work.
   Run metadata stamps `Quantization = ""` and `RuntimeKind = "dotnet"`
   on every run, which is also the Insights grouping key. The "Cold"
   phase claim has been false since r14 made `cache_prompt: true`
   unconditional. And selecting a model in the Benchmarks dropdown
   restarts the live chat server before Run is ever clicked.

All findings were verified in code (file:line refs throughout); the two
items marked "reproduce first" in doc 02 state exactly what to confirm
before changing code.

## Reading order

- `01-gguf-context-and-tuning.md` - GGUF reader, KV math, fit, Auto Tune
- `02-benchmark-truth.md` - real timings, honest scores, honest metadata
- `03-roadmap.md` - ship shape, sequencing, test expectations, rejections

## Ground rules (unchanged from prior rounds)

- Zero-warning build; all tests green before finishing.
- No em dashes anywhere (a test scans for them).
- No new NuGet packages: the GGUF reader in doc 01 is a small internal
  header parser, not a dependency.
- Additive JSON/schema only; stored runs and profiles must keep loading.
- Warnings inform, the user decides: nothing in this round changes a
  server config, context size, or model selection automatically outside
  the explicitly user-clicked Auto Tune action.
