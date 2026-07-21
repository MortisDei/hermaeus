# 04 - First-class llama-server engine options

Source: the owner-supplied "llama-server Optimisation & Tuning Guide"
(engine flags for 6/8/16 GB cards). Cross-checked against what Aether
already emits in `ServerProcessManager.BuildLaunchArguments`
(`ServerProcessManager.cs:447-530`): today we pass model/port/host/
ctx-size/threads/gpu-layers, default `--parallel` from `ServerConfig.Slots`
(r14 2.1), default `--cache-reuse 256` (r14 2.2), and the embeddings
batch pair; everything else must go through free-text `ExtraArgs`. The
KV-cost math (`KvCacheMath.BytesPerElementFromExtraArgs`,
`KvCacheMath.cs:28-40`) already understands `--cache-type-k/v` when the
user types it into ExtraArgs, so the estimation side is half-built.

**Governing principle (owner-stated, non-negotiable):** every option is
the user's choice. Defaults must match today's behavior exactly (f16 KV
cache, no forced quantization, no silently changed flags); the app may
*recommend*, never *impose*. A user who wants f16 KV at 64k context on
a 6 GB card gets exactly that plus an honest warning. This is the same
"warnings inform, the user decides" rule every round has carried.

## 4.0 Verified flag surface (llama.cpp master, checked 2026-07-21)

Verified against the upstream `tools/server/README.md` and
`common/speculative.cpp` on master. Aether pins tag **b10034**
(`LlamaServerSetupService.PinnedTag`, verified against the releases API
2026-07-16), which is within days of the check, so these should hold;
still run the bundled `llama-server --help` once during implementation
as a sanity check, because emitting a flag the binary rejects means
the server exits at startup and the Services card shows a dead process.

Confirmed facts, correcting the tuning guide where they differ:

- `-fa, --flash-attn` takes a value: `on | off | auto`, **default
  auto**. It is not a plain toggle. The right first-class shape is a
  tri-state defaulting to auto, and auto emits nothing.
- `--cache-type-k` / `--cache-type-v` (`-ctk`/`-ctv`) accept
  `f32, f16, bf16, q8_0, q4_0, q4_1, iq4_nl, q5_0, q5_1`; default f16.
- Context shift is spelled `--context-shift` / `--no-context-shift`
  (boolean pair, **default disabled**). The guide's `--ctx-shift`
  spelling is stale; confirm which spelling b10034 accepts in the
  `--help` sanity check and emit that one.
- Speculative decoding: `--spec-type` exists with values
  `none, draft-simple, draft-eagle3, draft-mtp, draft-dflash,
  ngram-simple, ngram-map-k, ngram-map-k4v, ngram-mod, ngram-cache`
  (verified in `common/speculative.cpp`), with tuning knobs
  `--spec-draft-n-max` (default 3), `--spec-draft-n-min`,
  `--spec-ngram-mod-n-min/-n-max`. The guide's zero-VRAM
  `ngram-mod` claim is a real mode.
- DRY sampling: `--dry-multiplier` (default 0.00 = disabled),
  `--dry-base` (1.75), `--dry-allowed-length` (2),
  `--dry-penalty-last-n` (-1).
- `--min-p` **already defaults to 0.05** server-side; the guide's
  `--min-p 0.05` recommendation is a no-op. Nothing to do.
- `--mlock` (off by default), `--mmap`/`--no-mmap` (mmap on by
  default), `--cache-reuse N` (default 0; Aether already emits 256),
  `-np/--parallel` (default -1 auto; Aether already emits from
  `Slots`), `--cont-batching` (**already enabled by default**; the
  guide recommending it is a no-op), `--rpc host:port,...`.
- Newly discovered, relevant to r17's sliding-window work:
  `--swa-full` (allocate the full-size SWA cache instead of the
  sliding-window-sized one) and `--ctx-checkpoints` (default 32,
  context checkpoints per slot for SWA models). See 4.6.
- `--kv-unified` / `--no-kv-unified` exists (unified KV buffer across
  slots); leave alone, the default tracks `--parallel` auto behavior.

One known engine constraint to encode: llama.cpp historically requires
flash attention enabled to use a **quantized V cache** (`-ctv` other
than f16/bf16). Verify whether b10034 still enforces this; if so, when
the user picks a quantized V type while flash attention is `off`, show
an inline warning ("quantized V cache needs flash attention on or
auto") - inform, never auto-change either field.

## 4.1 `ServerConfig` additions (additive JSON, defaults = today)

Add to `ServerConfig` (`src/Aether.Core/Models/ServerConfig.cs`),
each surfaced as a control on the Services card's server editor next
to the existing Context Size / GPU Layers / Threads / Slots fields:

| Field | Type / default | Emitted as | Notes |
| --- | --- | --- | --- |
| `KvCacheTypeK` | string, `"f16"` | `--cache-type-k <v>` only when not f16 | Dropdown options: f16, bf16, q8_0, q5_1, q5_0, q4_1, q4_0, iq4_nl (the verified accepted set minus f32, which only wastes VRAM). Primary recommendation surface is f16/q8_0/q4_0. |
| `KvCacheTypeV` | string, `"f16"` | `--cache-type-v <v>` only when not f16 | Same options. Single "KV cache" dropdown setting both, plus an "advanced: split K/V" affordance, is acceptable if simpler. Inline warning when quantized V is combined with flash attention off (4.0). |
| `FlashAttention` | string tri-state, `"auto"` | `--flash-attn <on|off>` only when not auto | Verified value form; auto emits nothing (server default). |
| `ContextShift` | bool, `false` | `--context-shift` | Rolling context for long agent loops instead of a hard out-of-context error. Server default is disabled, matching our default. |
| `MemoryLock` | bool, `false` | `--mlock` | Advanced section. |
| `NoMemoryMap` | bool, `false` | `--no-mmap` | Advanced section. |

Rules, all following the existing pattern in `BuildLaunchArguments`:

- Default values emit nothing: a config saved by an older version
  deserializes to today's exact command line (additive-JSON rule).
- `ExtraArgs` always wins: extend the existing `HasArg` guard
  (`ServerProcessManager.cs:483`) so a flag typed in ExtraArgs
  suppresses the first-class emission, exactly like `--parallel` and
  `--cache-reuse` today. A user's existing ExtraArgs setup must not
  double-emit or conflict.
- No new NuGet packages, no shell strings; everything goes through
  `ArgumentList` as today.

## 4.2 Wire KV cache type into the fit math

`KvCacheMath` currently derives bytes-per-element only from ExtraArgs.
Once `KvCacheTypeK/V` are first-class, the fit estimator, the Services
context-fit warning, and `SuggestContextSize` must read the first-class
fields first and fall back to ExtraArgs parsing. Extend the
bytes-per-element map to the full verified value set, derived from
each format's bits-per-weight over 8: f32 4.0, f16/bf16 2.0, q8_0
1.0625 (8.5 bits, slightly refining r17's 1.0), q5_0/q5_1 0.6875,
q4_0/q4_1/iq4_nl 0.5625; unknown strings keep 2.0 as today. This matters directly
for doc 01's 1.3 decision: q8_0 KV halves the cache cost and q4_0
roughly quarters it, so the "largest context that fits" answer is a
function of the cache type - the guide's own numbers (16k+ context
viable on 8 GB with q8_0) only work because of this. Acceptance: with
the same model and VRAM, switching the KV cache dropdown from f16 to
q8_0 visibly raises the suggested/fitting context in the UI.

The context-fit warning must also respect the owner constraint from
doc 01: if the user picks a context larger than what fits (or larger
than training context), the app warns with the arithmetic and lets
them run anyway. No clamping.

## 4.3 Recommended-preset helper, not forced defaults

The guide's per-tier cheat sheet (6 GB: 8k ctx + q4_0 KV; 8 GB: 16k +
q8_0; 16 GB: 32k+ + q8_0) becomes a single **"Suggest engine
settings"** button on the server editor, next to Auto Tune:

- Reads the cached `HardwareProfile` (VRAM tier) and, when available,
  the model's `GgufModelInfo` (training context cap).
- Fills the editor fields (context, KV cache type, flash attention)
  with the tier recommendation - *in the editable form only*. Nothing
  is saved or applied until the user clicks Save, same contract as
  Auto Tune results.
- The button's flyout/tooltip states what it suggests and why in one
  sentence per field, e.g. "q8_0 KV cache: halves context memory,
  near-lossless. You can keep f16 if you prefer."
- If the user has already set any of these fields away from default,
  the preset must not silently overwrite them without showing the
  before/after (a simple confirmation listing the changed fields is
  enough).

## 4.4 Speculative decoding and sampling flags: scoped small

- **Draft-model speculative** (`--spec-type draft-simple` family plus
  a draft model): real speedup but requires managing a second model
  file and its VRAM share - meaningful new surface (model picker for
  the draft, VRAM math for two models). Defer to a future round;
  record here, do not build.
- **N-gram speculative** (`--spec-type ngram-mod`, verified real in
  4.0): zero additional VRAM, drafts from the prompt/history itself.
  Expose as one advanced checkbox ("N-gram speculative decoding
  (experimental)"), default off, emitting `--spec-type ngram-mod`
  only when checked; leave `--spec-draft-n-max` and the
  `--spec-ngram-mod-*` knobs at server defaults (ExtraArgs territory
  for anyone who wants to tune them).
- **Sampling defaults**: `--min-p 0.05` is already the server default
  (4.0), so the guide's recommendation is a no-op - do nothing. DRY
  (`--dry-multiplier`/`--dry-base`/`--dry-allowed-length`) is disabled
  by default and shapes generation behavior, not memory; do not add
  first-class fields this round. The preset helper (4.3) may *offer*
  the guide's DRY line (`--dry-multiplier 0.8 --dry-base 1.75
  --dry-allowed-length 2`) as an optional "agent-loop hardening"
  ExtraArgs snippet the user can apply or ignore, shown as plain text
  before it lands in the field.

## 4.5 Explicitly not adopted from the guide

- `--host 0.0.0.0` (the guide's master command): Aether pins
  `--host 127.0.0.1` (`ServerProcessManager.cs:459-460`) as a security
  invariant (localhost binding for managed servers, `CLAUDE.md`). Not
  negotiable; a user who wants LAN exposure can use ExtraArgs and owns
  that choice, but no Aether UI or preset ever suggests it.
- `--rpc` VRAM pooling across machines: interesting, but it is a
  distributed-systems feature (remote worker discovery, trust of a
  network endpoint that receives model compute, failure modes when the
  peer drops). Too much surface for this round and it has security
  review implications; record as a future-round candidate in the
  roadmap, do not implement.
- `--embedding false` from the master command: not a real flag shape
  in our build (embeddings are opt-in via `--embeddings`, which Aether
  already handles per `EmbeddingsMode`); nothing to do.
- The guide's model-name recommendations (per-tier model picks) are
  content, not engine config; they do not belong in Aether's UI.

## 4.6 Sliding-window models: `--swa-full` and the fit math

Not in the tuning guide, found during 4.0 verification: for
sliding-window-attention models (the gemma family), llama-server
allocates the KV cache at the *sliding-window* size by default and
only allocates the full-context-sized cache under `--swa-full`.
This is the engine-side counterpart of r17's "sliding-window models
are a known overestimate" caveat and of commit 876cc10 (sliding-window
KV context estimates): our per-layer SWA-aware estimate models the
default behavior, and a user who adds `--swa-full` via ExtraArgs gets
the full-size cache our non-SWA formula describes. Two small actions:

- The fit math should detect `--swa-full` in ExtraArgs and, when
  present, skip the sliding-window discount for those layers (use the
  full-context KV cost). No new UI; this is correctness for an
  existing escape hatch.
- Do not surface `--swa-full` or `--ctx-checkpoints` as first-class
  options this round; they are niche and the defaults are right for
  chat use.

## Acceptance

- A fresh default server config produces a byte-identical command line
  to v0.22.0-alpha.
- Setting KV cache q8_0 in the dropdown emits `--cache-type-k q8_0
  --cache-type-v q8_0`, the fit warning/suggestion math reflects the
  cheaper cache, and switching back to f16 removes the flags.
- Flash attention auto emits nothing; on/off emit the value form; a
  quantized V type with flash attention off shows the inline warning
  and still launches with exactly what the user chose.
- A flag present in ExtraArgs is never emitted twice.
- "Suggest engine settings" fills but never saves; user-modified
  fields are only changed after an explicit listing of what changes.
- `docs/features.md` documents each new option with its default and
  the fact that nothing is ever forced.
