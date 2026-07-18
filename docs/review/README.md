# Review Round 14 (r14)

Theme: **Fast by default**. Origin: owner field report (2026-07-18
runtime.log) of two chat sends that "took fucking forever": the second
send carried a 9,744-token prompt (two attached files) that the server
read at 51 tokens/sec for 191 seconds and answered at 8.6 tokens/sec
for another 283 seconds, roughly 8 minutes end to end. Every number in
that log is CPU-class, and the code guarantees it: Aether installs the
CPU-only llama.cpp build for every Windows user and launches it with
zero GPU offload. r13 taught the app what hardware it runs on; r14
makes the inference stack actually use it, and makes the latency story
honest while it runs.

Headline findings, all verified in code against the log:

- **Every Windows install is CPU-only.** `LlamaServerSetupService`
  pins `-bin-win-cpu-x64.zip` and the latest-release selector matches
  the same suffix, so install and update both land a binary with no
  GPU backend. On top of that `GpuLayers` defaults to 0 and the flag
  is omitted at 0, so even a GPU build would idle the GPU.
- **The configured context is silently quartered.** No `--parallel`
  is passed and current llama-server defaults to 4 slots: the log
  shows `n_ctx_slot = 16128` against a much larger configured
  `--ctx-size`, and the second send landed on a cold slot and
  reprocessed all 9,744 tokens despite a warm 4,045-token cache in a
  sibling slot.
- **Updates nest and never apply.** The update installer resolves the
  install directory from the current exe, so each update nests one
  tag deeper (`b10064\b10066\llama-server.EXE` in the log), old
  versions are never pruned, and running servers keep the old binary
  with no restart prompt.
- **"Before first token" hides 111 s of active decoding.** The
  orchestrator only stamps first-token on visible content, so the
  non-content stream prefix (reasoning/tool deltas or buffering, to
  be confirmed by trace) is misattributed and the user stares at a
  blank bubble with no phase feedback.
- **Log noise buries the signal**: 60+ "Failed to fetch models"
  errors for a server the app itself knows is stopped, and triple
  Stopping/Stopped pairs per shutdown.

## Documents

- `01-gpu-runtime.md` - variant-aware llama.cpp builds (CUDA/Vulkan/
  CPU by detected GPU), companion CUDA runtime, offload defaults,
  Doctor advisory.
- `02-serving-defaults.md` - `--parallel 1`, explicit prompt cache +
  `--cache-reuse`, per-slot context truth, embeddings batch clamp.
- `03-update-hygiene.md` - install-root fix (no more nested tags),
  prune superseded versions, restart-to-apply, variant passthrough.
- `04-latency-truth.md` - first-event vs first-token accounting, live
  phase feedback, models-fetch log gating, idempotent stop logging,
  slow-send bottleneck hint.
- `05-roadmap.md` - version, sequencing, tests, security touch,
  explicit rejections.

## How to work this pack

Same conventions as r1-r13 (see `docs/review/archived/`): every item
has acceptance criteria; check archived rounds before re-proposing
anything explicitly rejected; zero-warning builds
(`TreatWarningsAsErrors` solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments,
or docs; the approval-gated agent security posture is non-negotiable.
The only destructive surface in this pack is 3.2 version pruning:
confirm-gated, tag-pattern directories under the install root only,
never user models or data.
