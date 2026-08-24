# llama.cpp: what Hermaeus uses, and what it does not

A full-surface survey of `llama-server` against what Hermaeus emitted, carried
out at **build b10215** (2026-08-01). Read this before adding a flag.

This is the last complete baseline, not a claim that every later change was
verified against that exact build. Later changes use the selected executable's
own `--help` at discovery and immediately before launch. R31 Batch 0 directly
observed the selected Windows CUDA runtime at build b10590. Linux runtime facts
remain unverified until the published branch is checked on the owner's COSMIC
machine, so this document does not infer Linux support from Windows evidence.

Managed Linux releases are extracted with their archive link relationships
validated against the install root and materialized as regular files. This
preserves versioned companion-library SONAMEs without creating archive-directed
filesystem links. Doctor then executes `--version`; a path that exists but
cannot load its companion libraries is an error, not a usable installation.
Build identifiers from different schemes are reported as not comparable rather
than being ordered by a misleading numeric fallback.

## The rule

**Only flags the installed binary lists in its own `--help` may be emitted.**

r27 learned this the hard way: `--draft-max`, `--draft-min`, `--draft-n` and
`--spec-ngram-size-n` had been removed upstream and now print "the argument has
been removed" and do nothing. Emitting one looks like it worked and changes
nothing measurable, which is worse than not supporting the feature.

To re-run this survey:

```bash
"<install>/llama-server.exe" --help > /tmp/llama-help.txt
```

then diff the flags in `ServerProcessManager.BuildLaunchArguments` against it.

## Verified at b10215

Every flag Hermaeus can emit still exists in b10215:

`-m`, `--port`, `--host`, `--ctx-size`, `--threads`, `--n-gpu-layers`,
`--parallel`, `--cache-reuse`, `--embeddings`, `--pooling`, `-b`, `-ub`,
`--cache-type-k`, `--cache-type-v`, `--flash-attn`, `--context-shift`,
`--mlock`, `--no-mmap`, `--mmproj`, `--spec-type`, `--spec-draft-model`,
`-ngld`, `--spec-draft-n-max`, `--spec-draft-n-min`, `--spec-draft-p-min`,
`--n-cpu-moe`, `--cpu-moe`, `--reasoning-format`, `--reasoning-preserve`,
`--no-reasoning-preserve`.

## Adopted in 0.38.0-alpha

### Extensible capability evidence and portable identity

Capability observations are now data records keyed by stable dotted ids such
as `runtime.prompt-threads`, `speculative.draft.eagle3`, and
`reasoning.preserve-template`. Each record retains `Available`, `Unavailable`,
or `Unknown`, an evidence code and explanation, the exact runtime identity, an
optional model identity, bounded parsed parameters, and observation time.
Unknown future ids survive cache and JSON round trips without adding settings
or model properties. Raw help text is never stored in the parameter map.

The runtime identity includes the executable SHA256, size, modification time,
parsed version/build/compiler/backend facts when available, and managed asset
identity. Its portable stable id excludes the executable path. Model identity
uses an existing verified hash or manifest when available, otherwise records a
clearly weaker file size/mtime fallback. Benchmark evidence composes these with
hardware and configuration identities in a v2 fingerprint. Historical v1
fingerprints keep their original stable-id meaning and load as incomplete.

Capability-cache v2 replacement compares runtime and model stable ids. Older
path/size/mtime entries remain readable and migrate when a successful new probe
replaces them.

### Model-specific MTP evidence

GGUF NextN metadata proves that relevant weights or metadata exist. Generic
`draft-mtp` help proves that the runtime contains a mechanism. Neither proves
that the selected runtime graph engages it for the selected model. Hermaeus now
keeps that pair `Unknown` until a model-specific capability response or direct
positive draft count is observed. A successful authoritative runtime probe
that lacks `draft-mtp` can still report `Unavailable`; a failed probe reports
`Unknown`.

## Adopted in 0.37.0-alpha

### Capability evidence, runtime discovery, and reasoning transport

Hermaeus reads bounded GGUF metadata, including the architecture-suffixed
`nextn_predict_layers` scalar and the presence of `tokenizer.chat_template`.
It combines that evidence with the selected executable's `--help` flags and a
healthy managed server's `/props` response. Embedded MTP, separate reasoning
output, template reasoning preservation, and modalities are each reported as
Available, Unavailable, or Unknown with evidence. Results are cached by model
and runtime identity using an atomic state file.

The same help probe now discovers the speculative type names printed beside
`--spec-type`, `--threads-batch`, and `--perf` when present. A type is not made
configurable merely because it was discovered. Hermaeus currently has complete
semantics for self-drafting n-gram modes and the MTP-head path; unfamiliar
types remain runtime facts until their drafter, model compatibility, memory,
and launch semantics are understood. At launch the help probe is repeated:
saved speculative settings or prompt threads are refused when the selected
runtime cannot prove their flag, rather than emitted optimistically.

Capability snapshots are compared across executable identities. Only meaningful
state changes are retained: core capabilities changing state and speculative
types appearing or disappearing. The detailed record is an Activity event.
Moss raises one concise heads-up per newly observed snapshot, with a warning
when a removed capability can affect a configured server. Raw help text is not
stored or presented as a diff.

llama.cpp reasoning uses `--reasoning-format deepseek` when supported. Stream
events carry `reasoning_content` separately from answer content. The optional
`--reasoning-preserve` and `--no-reasoning-preserve` flags are emitted only after
the paired runtime capability is proven. Stored reasoning is replayed only when
the matching template reports `supports_preserve_reasoning=true` and the saved
server setting is enabled.

## Adopted in 0.36.0-alpha

### Mixture-of-Experts CPU offload (`--n-cpu-moe`, `--cpu-moe`)

The one that mattered. On a MoE model the expert weights are most of the file
but only a few experts are active per token, so the useful trade is "attention
on the GPU, experts in RAM". Hermaeus previously had no way to express that:
the only VRAM knob was `--n-gpu-layers`, and turning it down to make a MoE
model fit gives up attention offload, which is the part that actually wants the
GPU.

Exposed as **MoE experts on CPU** under Services > Advanced engine options.
Blank or 0 is off and emits nothing (so an existing config launches exactly as
it did before), a number N emits `--n-cpu-moe N`, and `all` emits `--cpu-moe`.
It has no effect on a dense model.

## Considered and deliberately not adopted

Recorded so the next round starts from a survey rather than repeating it.

| Flag | What it does | Why not now |
| --- | --- | --- |
| `-cram`, `--cache-ram N` | Caps the RAM used to keep KV caches of idle slots warm | Already defaults to 8192 MiB, which is sensible. A knob with no observed problem behind it is a knob nobody sets correctly. |
| `--swa-full` | Full-size sliding-window-attention cache | Trades memory for a narrow correctness/perf case on SWA models. No observed need. |
| `-kvu/--kv-unified` | One KV buffer shared across sequences | Hermaeus runs `--parallel 1` by design (r14 2.1), so there is one sequence and nothing to unify. |
| `-ot`, `--override-tensor` | Per-tensor buffer placement | `--n-cpu-moe` is the ergonomic form of the case that matters. The general form belongs in ExtraArgs, where it already works. |
| `--no-op-offload`, `--no-host` | Low-level backend placement | Debugging switches, not user-facing settings. ExtraArgs covers them. |
| `--jinja` / `--no-jinja` | Jinja chat templating | Already enabled by default upstream. |
| `--lora`, `--lora-scaled` | LoRA adapters at load time | A real feature request would come with a workflow (where adapters live, how they are picked per conversation). Nobody has asked. |

## Watchlist and evidence gates

| Area | Current r30 position | Revisit only when |
| --- | --- | --- |
| DSpark, DFlash, EAGLE-3 and other speculative types | Runtime discovery reports them when installed help names them, but does not expose a control. Vocabulary equality alone does not establish drafter compatibility. | A supported runtime and documented end-to-end drafter workflow can be validated with model, vocabulary, GPU-layer, and Speed Check evidence. |
| General external draft model | `draft-mtp` keeps the bounded MTP-head workflow. The generic picker remains incomplete. | Hermaeus can prove more than vocabulary equality, preserve detailed failure evidence, and measure acceptance, TTFT, throughput, and overhead for a known pair. |
| Prompt cache and prefill effectiveness | Benchmarks distinguish cold and warm attempts and record prompt throughput and TTFT, but llama-server does not provide a stable reuse-token counter in this integration. | A stable machine-readable runtime counter or an explicit controlled shared-prefix workload can be added without fabricating reuse counts. |
| Backend sampling | Not configured. No accessible runtime evidence in Batch #3 established a useful single-slot contract. | A selected runtime advertises a stable option and a Speed Check can isolate its effect for `--parallel 1`. |
| Internal performance instrumentation | `--perf` is detected as a fact only. It is not enabled by normal launches or parsed as a benchmark contract. | Its diagnostic output is stable and machine-readable enough to explain a measured difference without coupling to log wording. |
| Context checkpoints, cache RAM, multi-device placement | Not exposed. Their memory and placement behavior has no demonstrated single-user need. | A reproducible local workload and bounded failure behavior justify a user-facing workflow. |

## Also fixed here

The project moved from `ggerganov/llama.cpp` to the `ggml-org` organisation.
Hermaeus still had the old slug in three places: the release-download base URL,
the Doctor update check's API URL, and the releases page link. GitHub redirects
the old path, so this kept working, but a redirect is not a guarantee and an
API call that silently depends on one is a latent break.
