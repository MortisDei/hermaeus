# llama.cpp: what Hermaeus uses, and what it does not

A survey of `llama-server`'s current surface against what Hermaeus emits,
carried out at **build b10215** (2026-08-01). Read this before adding a flag.

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

## Adopted in 0.37.0-alpha

### Capability evidence and reasoning transport

Hermaeus reads bounded GGUF metadata, including the architecture-suffixed
`nextn_predict_layers` scalar and the presence of `tokenizer.chat_template`.
It combines that evidence with the selected executable's `--help` flags and a
healthy managed server's `/props` response. Embedded MTP, separate reasoning
output, template reasoning preservation, and modalities are each reported as
Available, Unavailable, or Unknown with evidence. Results are cached by model
and executable path, size, and modification time using an atomic state file.

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
| `--reasoning-format`, `-rea/--reasoning` | Extracts a model's reasoning into a separate `reasoning_content` field instead of leaving it in `message.content` | Shipped in 0.37.0-alpha through the separate stream, message, persistence, export, and evidence-gated history paths. |
| `-cram`, `--cache-ram N` | Caps the RAM used to keep KV caches of idle slots warm | Already defaults to 8192 MiB, which is sensible. A knob with no observed problem behind it is a knob nobody sets correctly. |
| `--swa-full` | Full-size sliding-window-attention cache | Trades memory for a narrow correctness/perf case on SWA models. No observed need. |
| `-kvu/--kv-unified` | One KV buffer shared across sequences | Hermaeus runs `--parallel 1` by design (r14 2.1), so there is one sequence and nothing to unify. |
| `-ot`, `--override-tensor` | Per-tensor buffer placement | `--n-cpu-moe` is the ergonomic form of the case that matters. The general form belongs in ExtraArgs, where it already works. |
| `--no-op-offload`, `--no-host` | Low-level backend placement | Debugging switches, not user-facing settings. ExtraArgs covers them. |
| `--jinja` / `--no-jinja` | Jinja chat templating | Already enabled by default upstream. |
| `--lora`, `--lora-scaled` | LoRA adapters at load time | A real feature request would come with a workflow (where adapters live, how they are picked per conversation). Nobody has asked. |

## Also fixed here

The project moved from `ggerganov/llama.cpp` to the `ggml-org` organisation.
Hermaeus still had the old slug in three places: the release-download base URL,
the Doctor update check's API URL, and the releases page link. GitHub redirects
the old path, so this kept working, but a redirect is not a guarantee and an
API call that silently depends on one is a latent break.
