# 01 - GGUF metadata, KV-cache math, context fit, Auto Tune

Everything here hangs off one new capability: reading the GGUF header of
a local model file. Today the repo treats `.gguf` files as opaque blobs
(size + filename only); every fit/context judgment is a guess because of
that. Items 1.1 and 1.2 build the capability; 1.3-1.6 spend it.

## 1.1 GgufMetadataReader (new, `src/Aether.Services`)

A small internal parser for the GGUF header's metadata section only.
No tensor data is ever read; a well-formed header is a few KB to a few
hundred KB of the file.

Format facts (verify against a real local model during implementation):

- Layout: magic `GGUF` (4 bytes), `u32 version` (accept 2 and 3;
  reject 1 and anything else), `u64 tensor_count`,
  `u64 metadata_kv_count`, then `metadata_kv_count` key/value pairs.
- Each KV: string key (`u64` length + UTF-8 bytes), `u32 value_type`,
  value. Value types: 0 u8, 1 i8, 2 u16, 3 i16, 4 u32, 5 i32, 6 f32,
  7 bool, 8 string (`u64` len + bytes), 9 array (`u32` element type +
  `u64` count + elements), 10 u64, 11 i64, 12 f64. The reader must be
  able to *skip* any value type it does not care about, including
  nested arrays, or it cannot reach later keys.

Keys to extract into a `GgufModelInfo` record (arch = the value of
`general.architecture`):

| Key | Meaning | Notes |
| --- | --- | --- |
| `general.architecture` | arch prefix for all other keys | string |
| `general.file_type` | quantization enum | u32; map common values (0 F32, 1 F16, 2 Q4_0, 3 Q4_1, 7 Q8_0, 8 Q5_0, 9 Q5_1, 10 Q2_K, 12 Q3_K_M, 14 Q4_K_S, 15 Q4_K_M, 17 Q5_K_M, 18 Q6_K, 30 IQ4_XS, 32 BF16); unknown value N renders as `type N` |
| `{arch}.block_count` | transformer layer count | needed for KV math and offload fractions |
| `{arch}.context_length` | training context | cap and advisory input |
| `{arch}.embedding_length` | hidden size | fallback for head dims |
| `{arch}.attention.head_count` | attention heads | |
| `{arch}.attention.head_count_kv` | KV heads (GQA) | optional; fallback `head_count`; some models store a per-layer array - take the maximum |
| `{arch}.attention.key_length` | per-head K dim | optional; fallback `embedding_length / head_count` |
| `{arch}.attention.value_length` | per-head V dim | optional; same fallback |

Hardening (these files are downloaded from the internet; the parser is
a new attack surface and gets a security-review subsection, see doc 03):

- Hard caps before allocating: key/string length <= 64 KiB, array
  count <= 1,000,000, metadata_kv_count <= 100,000. Anything over =
  malformed, return null.
- Never trust declared lengths against the file: every read is bounds
  checked; truncated file = null, not an exception escaping.
- Public API is `GgufMetadataReader.TryRead(path)` returning
  `GgufModelInfo?`; any IO or parse failure returns null. Callers must
  behave exactly as today when it is null.
- Process-lifetime cache keyed on (full path, file size, mtime), same
  spirit as the r13 `HardwareProfile` cache; reads happen off the UI
  thread (callers below are responsible for that).

Tests: build tiny GGUF fixtures in-memory (write bytes to a temp file):
a valid v3 header with the llama keys, a v2 header, unknown-type values
that must be skipped, per-layer `head_count_kv` array, magic mismatch,
version 1, truncated at each structural boundary, oversized declared
string length, oversized kv count. No real model files in the repo.

## 1.2 KvCacheMath (new, pure, next to `ModelFitEstimator`)

Deterministic estimate, clearly labeled as an estimate everywhere it
surfaces:

```
kvBytesPerToken = block_count * head_count_kv * (key_length + value_length) * bytesPerElement
kvBytes(ctx)    = kvBytesPerToken * ctx
```

- `bytesPerElement` defaults to 2.0 (f16 cache). If the server config's
  `ExtraArgs` contain `--cache-type-k`/`--cache-type-v`, map `q8_0` to
  1.0 and `q4_0`/`q4_1` to 0.5625 for that half of the cache; any other
  value keeps 2.0. Parse via the existing `ExtraArgsParser.Split`.
- Projected VRAM at full offload:
  `fileSizeBytes * 1.05 + kvBytes(ctx) + GpuHeadroomBytes` (the
  existing 1.5 GiB headroom constant now covers compute buffers and
  display overhead; the old 1.2 weights multiplier was implicitly
  covering KV and is replaced by this split, see 1.3).
- Partial offload with `layers` of `block_count` on GPU: scale *both*
  the weights term and the KV term by `layers / block_count`
  (llama.cpp keeps the KV of offloaded layers on the GPU).
  `GpuLayers < 0` or `>= block_count` means full offload; `0` means
  nothing on GPU.
- Known overestimate: sliding-window-attention models (gemma family)
  need less than this. Acceptable for a warning threshold; the doc
  comment must say so.

Tests: exact arithmetic against hand-computed values for a known shape
(e.g. block_count 32, kv heads 8, key/value 128: 2 MiB per 1024 tokens
at f16 per layer math), cache-type overrides, offload fractions, and
the degenerate zero/missing-field cases returning null.

## 1.3 Context-aware ModelFitEstimator

`ModelFitEstimator.Estimate` (`src/Aether.Services/ModelFitEstimator.cs:20`)
gains an overload taking `GgufModelInfo?` and a context size. When info
is null, behavior is byte-identical to today (existing tests must not
change). When present:

- `FitsGpu` iff projected VRAM (1.2 formula, full offload) fits
  `MaxGpuVramBytes`.
- `FitsPartial` iff weights + KV fit RAM (+ existing RAM headroom),
  with the Reason string now stating the split, e.g.
  `~4.1 GB weights + ~1.3 GB KV cache at 16,384 context vs 8.0 GB VRAM: needs partial CPU offload.`
- `TooLarge` otherwise, message includes the KV number so users see
  *why* a small file can still be too large at huge context.

Call sites to upgrade (each reads the local file, so each must fetch
`GgufModelInfo` off the UI thread and only for local paths):

- Models page cards and Tune-all staleness display:
  `ModelManagementViewModel.cs:188` and `:760`. Context to assume: the
  model's `LlamaTuneProfile.ContextSize` when one exists, else the
  managed Chat server's `ContextSize`, else 4096.
- Setup wizard starter tier (`SetupWizardViewModel.cs:156`) and the HF
  browser file list stay size-based: no local file exists yet for
  either. Explicitly out of scope (doc 03 rejections).

Acceptance: a 4 GB model on an 8 GB card reports FitsGpu at 8k context
and FitsPartial or TooLarge at 65k context, with both numbers visible
in the Reason.

## 1.4 Real oversized-context warning on the Services card

`ServerProcessViewModel` (`src/Aether.ViewModels/ServicesViewModel.cs:102-107`):
replace the flat `LargeContextSizeThreshold = 16384` rule with a
hardware-aware assessment.

- New state: `ContextFitNote` (string) + `HasContextFitWarning` (bool),
  replacing `HasOversizedContext`/`OversizedContextNote` and their
  bindings in `ServicesView.axaml`.
- Inputs: `ModelPath` (must resolve to an existing local `.gguf`),
  `ContextSize`, `GpuLayers`, cached `GgufModelInfo`, and a
  `HardwareProfile`. The parent `ServicesViewModel` fetches the profile
  once via `ISystemInfoService.GetHardwareProfileAsync` (already
  process-cached, `SystemInfoService.cs:82`) and hands it to each child
  VM; add it as an optional constructor parameter so existing test call
  sites keep compiling (r7 lesson).
- Decision:
  - GPU present and `GpuLayers != 0`: warn when projected VRAM (1.2,
    with the offload fraction from `GpuLayers` vs `block_count`)
    exceeds `MaxGpuVramBytes`. Message states the arithmetic:
    `At 32,768 context this model needs ~13.2 GB (weights ~7.5 GB + KV cache ~5.7 GB); this GPU has 8.0 GB. Prompt processing will spill to system RAM.`
  - `GpuLayers == 0`: compare weights + KV against `TotalRamBytes`
    minus the RAM headroom; warn with a RAM-phrased message.
  - Metadata or hardware unavailable: fall back to the existing flat
    16384 rule and today's generic wording, so the warning never
    silently disappears on machines where we cannot do better.
- Recompute on `ModelPath`/`ContextSize`/`GpuLayers` changes. The file
  read goes through the 1.1 cache on a background task with a
  generation counter, marshaled back via `RunOnUi` (r12 pattern); no
  file IO on the UI thread, no stale overwrite after a rapid second
  edit.

Acceptance: with a mocked profile of 8 GB VRAM and a fixture-backed
`GgufModelInfo`, a context of 8192 produces no warning and 65536
produces one carrying both GB figures; with metadata unavailable the
old 16384 behavior is asserted unchanged.

## 1.5 Auto Tune learns about context

`ServerProcessManager.AutoTuneAsync`
(`src/Aether.Services/ProcessManagement/ServerProcessManager.cs:161-231`)
today only descends GPU layer candidates
(`BuildGpuLayerCandidates`, `:301`) and passes `ContextSize` through
untouched. Owner ask: when the configured context cannot fit, tune it
down to something that does instead of only shedding layers.

Design (bounded, at most one extra probe):

- New pure helper `SuggestContextSize(GgufModelInfo info, long vramBytes,
  int configuredContext)`: the largest value from the ladder
  `{2048, 4096, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072}`
  that is `<= min(configuredContext, info.TrainingContextLength)` and
  whose full-offload projection (1.2) fits `vramBytes`. Returns null
  when the configured context already fits, when metadata/VRAM are
  unavailable, or when nothing on the ladder fits.
- `AutoTuneAsync` gains optional `GgufModelInfo?` and `HardwareProfile?`
  parameters (defaulting to null keeps every existing call site and
  test compiling; the VM resolves and passes them).
- Flow: if `SuggestContextSize` returns a value, probe
  (all layers, suggested context) *before* the existing layer descent.
  If that probe reaches /health, return immediately with the suggested
  context recorded. If it fails, fall through to today's layer descent
  at the configured context, unchanged.
- `ServerTuneResult` gains `int? TunedContextSize` (null = context
  untouched). In `ServicesViewModel.AutoTuneAsync`
  (`ServicesViewModel.cs:327-366`), a non-null value assigns
  `ContextSize` alongside the existing `GpuLayers`/`Threads`
  assignments, flows into `PersistTuneProfileAsync` (the profile
  already stores `ContextSize`, `LlamaTuneProfile.cs:11`), and the
  status line states it explicitly:
  `Auto-tune verified all 32 GPU layers at 16,384 context (configured 65,536 does not fit in 8.0 GB VRAM with this model).`
- CPU-only machines (no VRAM in the profile): suggestion is always
  null; Auto Tune behaves exactly as today.
- Auto Tune is an explicit user click and its result is visible in the
  editable fields before Save, so no extra confirmation dialog. Nothing
  outside this command ever changes `ContextSize`.

Tests: `SuggestContextSize` pure-math cases (fits, capped by training
context, capped by configured, nothing fits, null metadata); probe
ordering via the candidate/suggestion helpers (do not spawn real
processes; the existing `ServerProcessManagerTests` pattern already
avoids that).

## 1.6 Training-context advisory

With `info.TrainingContextLength` available: when
`ContextSize > TrainingContextLength`, append one sentence to
`ContextFitNote` (1.4), independent of the VRAM verdict:
`This model was trained at 32,768 context; running beyond that can degrade quality.`
Advisory only, never blocks Start, never edits the value. Also cap the
1.5 ladder at the training context (already specified there).
