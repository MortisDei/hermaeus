# llama.cpp: what Hermaeus uses, and what it does not

This is the current reference for Hermaeus's managed `llama-server` surface.
It is a compatibility contract, not a claim that every installed runtime has
every feature. The selected executable's successful `--help` probe is the gate
for flags and capability-specific controls, and the check is repeated before
launch.

Runtime and hardware facts are installation evidence, not a permanent promise
for every managed asset. The R32 Batch 0 runtime snapshot, including exact
build identity, is recorded in the [R32 roadmap evidence](review/r32/08-roadmap.md#821-batch-0-evidence).
Other environments and untested runtime or hardware combinations remain
governed by the selected executable's live capability probe rather than being
inferred from a build number, filename, or upstream documentation.

## The compatibility rule

Only flags the installed executable lists in its own `--help` may be emitted.
Unknown, missing, or failed probe evidence remains `Unknown`; Hermaeus does not
turn a failed probe into `Unavailable` and does not infer support from a model
filename, build number, or generic metadata.

To inspect a runtime manually:

```bash
"<install>/llama-server" --help > /tmp/llama-help.txt
```

The launch surface is implemented in
`ServerProcessManager.BuildLaunchArguments`. Managed Linux installs also
validate archive links against the install root and materialize safe companion
libraries before `--version` is checked. A path that exists but cannot load its
companion libraries is not Ready.

## Current managed surface

The current managed server may emit these options when the selected runtime
proves them:

`-m`, `--port`, `--host`, `--ctx-size`, `--threads`, `--n-gpu-layers`,
`--parallel`, `--cache-reuse`, `--embeddings`, `--pooling`, `-b`, `-ub`,
`--cache-type-k`, `--cache-type-v`, `--flash-attn`, `--context-shift`,
`--mlock`, `--no-mmap`, `--mmproj`, `--spec-type`, `--spec-draft-model`,
`-ngld`, `--spec-draft-n-max`, `--spec-draft-n-min`, `--spec-draft-p-min`,
`--n-cpu-moe`, `--cpu-moe`, `--reasoning-format`, `--reasoning-preserve`, and
`--no-reasoning-preserve`.

Newer runtimes may advertise `--load-mode` and `--cors-origins`. Managed
launch maps MemoryLock to `--load-mode mlock` and NoMemoryMap to
`--load-mode none` only when the selected runtime proves the option. Managed
loopback servers receive the localhost-only CORS origins when that option is
available. Older runtimes retain their supported forms, and external server
profiles are not rewritten.

The R32 launch contract distinguishes typed intent from runtime evidence:

| Intent | Contracted launch form | Fit ownership |
| --- | --- | --- |
| CPU | explicit zero GPU layers using the selected runtime's proven form | fit off |
| Auto | placement fields left unset | fit may own placement only |
| All | explicit `all` or a proven equivalent | fit off |
| Exact(N) | explicit numeric GPU-layer count | fit off |

Batch 0 established whether the selected executable accepts the relevant
syntax and whether effective values can be observed. Batch 1 stores the typed
intent and renders one conflict-free launch vector. A runtime capability probe
still gates launch: a requested mode with no Available evidence is refused,
and a successful syntax probe without effective-placement reporting remains
Unknown rather than being presented as effective.

The bounded R32 launch capability ids include `runtime.gpu-placement.cpu`,
`runtime.gpu-placement.auto`, `runtime.gpu-placement.all`,
`runtime.gpu-placement.exact`, `runtime.fit`, `runtime.fit.target`,
`runtime.fit.minimum-context`, `runtime.fit.report.effective`,
`runtime.device.list`, the reviewed device and split controls, KV offload,
host cache, checkpoints, unified KV, idle slots, and slot-cache output. Help
parsing uses exact option boundaries, so a similarly named draft or future
flag cannot create a false positive. Effective-placement evidence is accepted
only from bounded structured fields in the running server response.

## Capability evidence and identity

Capability observations use stable dotted ids such as
`runtime.prompt-threads`, `runtime.fit.report.effective`,
`speculative.draft.eagle3`, and `reasoning.preserve-template`. Each record
retains `Available`, `Unavailable`, or `Unknown`, an evidence code and
explanation, the exact runtime identity, an optional model identity, bounded
parsed parameters, and observation time. Unknown ids survive cache and JSON
round trips without adding a new setting. Raw help text is not stored in the
parameter map. Changes in the bounded R32 launch matrix are reported as
capability drift, not as a raw help-text diff.

The runtime identity includes executable hash, size, modification time, parsed
version/build/compiler/backend facts when available, and managed asset identity.
Its portable stable id excludes the executable path. Model identity uses a
verified hash or manifest when available, otherwise a clearly weaker
size/mtime fallback. Benchmark evidence combines runtime, model, hardware, and
configuration identity into a v2 fingerprint. Historical v1 fingerprints remain
readable as incomplete records.

Capability cache replacement compares runtime and model stable ids. Older
path/size/mtime entries remain readable and are replaced by a successful probe
with the newer identity.

## Reasoning and MTP

When supported, llama.cpp reasoning uses `--reasoning-format deepseek` and
streams `reasoning_content` separately from answer content. The optional
preservation flags are emitted only after the paired runtime capability is
proven. Stored reasoning is replayed only when the matching template reports
`supports_preserve_reasoning=true` and the saved server setting is enabled.

GGUF NextN metadata proves that relevant weights or metadata exist. Generic
`draft-mtp` help proves that the runtime has a mechanism. Neither proves that
the selected model engages it. The model/runtime pair remains `Unknown` until
a model-specific capability response or a direct positive draft count is
observed. An authoritative probe that lacks `draft-mtp` may report
`Unavailable`; a failed probe reports `Unknown`.

## Speculative decoding

Hermaeus exposes a composable `--spec-type` configuration for mechanisms with a
complete compatibility workflow. The runtime's advertised types are retained
as evidence, but discovery alone does not create a user control.

- `ngram-mod` drafts from prompt and history without another model file.
- `draft-mtp` uses a selected MTP head when a trusted companion relationship is
  proven.
- `draft-simple` and `draft-eagle3` are available to Lab only when exact
  runtime support, target and companion identity, tokenizer/vocabulary
  compatibility, and the EAGLE target binding where applicable are proven.
- Draft maximum, minimum, minimum probability, and draft GPU-layer controls
  require the exact parameter flag and an explicit saved baseline.

Draft and accepted token counts are direct observations. Acceptance is derived
only when the drafted count is positive. Zero drafted is a measured zero;
missing counters remain missing. Prompt timing is never converted into a
reused-token count.

The Services launch path validates draft paths for containment, links, and GGUF
vocabulary compatibility. A mismatch refuses launch. A missing or stale draft
path is not silently replaced. See [Lab](lab.md) for isolated speculative
experiments and [user workflows](user-guide.md#models-and-services) for
companion review.

## Mixture-of-Experts CPU placement

**MoE experts on CPU** is exposed under Services > Advanced engine options.
Blank or 0 emits nothing, a number emits `--n-cpu-moe`, and `all` emits
`--cpu-moe`. It has no effect on dense models. This is expert placement, not an
expert-cache, prefetch, or NVMe-streaming feature. GPU Fit keeps the analytical
placement Unknown when the available metadata cannot prove the tensor split.

## Deliberately not user-facing

These options remain available only through explicit extra arguments or are not
exposed because there is no bounded, evidence-backed single-user workflow:

| Area | Decision |
| --- | --- |
| `-cram`, `--cache-ram` | Not exposed while the upstream default is adequate and no observed user problem justifies another knob. |
| `--swa-full` | Not exposed without a reproducible correctness or performance case. |
| `-kvu`, `--kv-unified` | Not exposed for the single-slot managed-server design. |
| `-ot`, `--override-tensor` | General placement remains an explicit advanced argument; the reviewed MoE case has a dedicated control. |
| `--no-op-offload`, `--no-host` | Debugging and low-level placement switches, not user-facing settings. |
| `--ctx-checkpoints`, `--checkpoint-min-step` | Not exposed without measured repeated-prefix benefit and restart/correctness evidence. |
| `--cache-idle-slots`, `--slot-save-path` | Not exposed without explicit retention, privacy, backup, and restart semantics. |
| `--device`, split modes, `--tensor-split`, `--main-gpu` | Not exposed as multi-device launch controls without correlated per-device observations and a real correctness/performance gate. |
| `--jinja`, `--no-jinja` | Upstream default is sufficient. |
| `--lora`, `--lora-scaled` | No complete adapter storage, selection, and per-conversation workflow exists. |

## Watch and research boundary

The following are retained as research or upstream watch items, not shipped
Hermaeus capabilities:

- DSpark, DFlash, and other unfamiliar speculative types need a stable
  mechanism-specific asset, launch contract, correctness evidence, and measured
  benefit before a control is added.
- Prompt-cache effectiveness may be measured by Lab's controlled timing
  protocol. A direct reuse counter requires a reviewed machine-readable field
  from the exact runtime.
- Runtime reconfiguration, a public speculative C API, MoE prefetch/streaming,
  and reconstructable KV have no stable managed-server contract here.
- Backend sampling, context checkpoints, cache RAM, and multi-device placement
  need a reproducible local workload and bounded failure behavior before they
  become settings.

The dated upstream decisions and their evidence are in
[`review/archived/r31/evidence/r31-batch-15.md`](review/archived/r31/evidence/r31-batch-15.md),
[`review/archived/r31/evidence/r31-batch-17.md`](review/archived/r31/evidence/r31-batch-17.md), and
[`review/archived/r31/evidence/r31-batch-18.md`](review/archived/r31/evidence/r31-batch-18.md). They are
engineering records, not current feature authority.
