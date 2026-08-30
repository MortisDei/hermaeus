# 03. Lab

Lab is the first-class workspace for controlled local experiments:

`choose model/profile -> define controlled experiment -> run -> observe -> compare -> preserve evidence -> optionally apply`

It owns experiments, not model catalog metadata, service lifecycle management,
or general capability benchmarking.

## 3.1 Ownership boundaries

| Surface | Owns |
| --- | --- |
| Models | model discovery, provenance, display metadata, and per-model defaults |
| Services | configured runtime processes, start/stop/update, and persistent server configuration |
| Benchmarks | model capability/behavior suites and historical capability comparison |
| Lab | temporary controlled runtime configurations, experiment protocols, observations, correctness checks, comparisons, and explicit apply |

Lab is a new navigation panel with `LabViewModel` in ViewModels and views in
Desktop. Experiment execution and persistence live in Services. ViewModels do
not launch processes or parse runtime logs. MainWindow owns only navigation and
cross-panel delegates; do not add experiment logic to `MainWindowViewModel`.

Lab may reuse Benchmark measurement primitives and suites through narrow
services. It must not make `BenchmarkService` understand configuration sweeps or
make `ServicesViewModel` an experiment runner.

## 3.2 Experiment definition and isolation

One immutable definition contains:

- protocol id and version;
- target model/profile and exact v2 fingerprints;
- baseline configuration;
- one or more candidate configurations;
- controlled workload/suite identity, prompts or benchmark references, and
  seeds/sampling policy;
- warm-up policy, repetitions, order/randomization policy, timeout, and stop
  conditions;
- required metrics and correctness checks;
- requested capability ids and evidence required before running;
- explicit expected deterministic-equivalence rule where applicable.

The editor presents the exact baseline and every candidate before Start. The
run freezes that definition; changing the draft in the editor creates a new
definition/version.

Lab runs against a cloned, temporary configuration. It never saves a candidate
to `settings.json`, never rewrites a model profile, and never changes an
ordinary managed server behind Chat. Prefer a dedicated loopback process and
port owned by the Lab run. If the current process layer cannot isolate this
safely, implement the lifecycle seam before the experiment, not a stop/edit/run
/restore sequence over the owner's active server.

Cancellation stops at safe request/process boundaries, records partial
observations, and normalizes the run as Cancelled or PartiallySucceeded. Cleanup
is ownership-scoped. Never broadly kill `llama-server`, `dotnet`, or unrelated
processes.

## 3.3 Observation contract

Each observation records value, unit, source, origin, trust state, timestamp,
runtime/model/hardware/config fingerprints, repetition/case identity, and any
missing reason.

Lab supports these metrics where trustworthy:

- decode tokens/sec;
- prompt tokens/sec;
- TTFT;
- prompt/completion/tokens served;
- process RAM current/peak;
- VRAM current/peak;
- predicted and observed KV footprint/type;
- runtime/compute overhead;
- speculative drafted/accepted counts and acceptance ratio;
- deterministic equivalence;
- quality/benchmark score and delta;
- Flash Attention state and measured effect;
- observation source.

Missing values stay missing. `0` is displayed only when a counter explicitly
reported zero. Timing differences do not become reused-token counts. Process
memory does not become VRAM. A loaded model does not become quality equivalence.

## 3.4 Comparison and preservation

Comparisons require the same target model, protocol, workload, sampling policy,
and compatible hardware/runtime scope. The UI lists every fingerprint
difference, not only the setting the user intended to change. If uncontrolled
differences remain, refuse a headline delta while still allowing side-by-side
inspection.

Show median and observed range plus repetition count for timing metrics. Do not
claim confidence intervals or statistical significance without a deliberately
specified method and enough samples. Correctness failures are first-class and
cannot be hidden behind a speed median.

Persist definition, immutable run, observations, outputs/hashes required for
correctness, failures, and provenance in `experience.db` Lab domain records or
a normalized Lab child schema linked to them. Do not duplicate benchmark
history. A Lab run that invokes a benchmark references its existing run id and
evidence.

## 3.5 Empirical engine-profile optimisation

Provide bounded recipes, not an exhaustive flag combinator.

Initial dimensions may include GPU layers/placement, context, KV type, Flash
Attention, prompt threads, batch/ubatch where already safely representable,
and supported speculative settings. Each recipe:

- declares a small candidate set and maximum run count;
- changes one dimension at a time unless it explicitly tests an interaction;
- keeps the baseline in every comparison;
- checks fit before launch but treats fit as prediction;
- records launch refusal/OOM/failure as evidence rather than silently dropping
  that candidate;
- measures correctness as well as speed;
- never applies the winner automatically.

The result is a trade-off table. "Apply to Services" opens a review showing
the exact persisted fields that will change, validates the selected server/model
identity is still current, then routes through the single settings save flow.
Applying does not delete the prior configuration or experiment evidence.

## 3.6 General external speculative draft models

The current argument/config layer already represents a draft path and
`draft-simple`; the current validator checks containment/symlinks and vocabulary
size. R31 completes the workflow only when the selected runtime advertises
`speculative.draft.simple`.

Required validation:

- target and draft are distinct readable model assets with recoverable
  provenance;
- exact runtime capability and launch flags are observed;
- vocabulary compatibility is checked and its evidence recorded;
- target/draft model family, tokenizer identity, draft size, placement, and
  memory budget are displayed;
- unsupported or unproven compatibility is a refusal/Unknown, not a warning
  that allows a normal launch;
- a controlled baseline and candidate run capture acceptance, TTFT,
  prompt/decode throughput, memory, outputs, and correctness.

Vocabulary equality remains necessary, never sufficient. Lab can show a poor
pair honestly. It does not maintain a global "good drafter" list from one
machine.

## 3.7 EAGLE-3

Treat `draft-eagle3` as a target-specific external drafter:

- expose it only when the selected runtime observes the exact capability;
- require the EAGLE model's target identity/metadata and reject an unbound or
  mismatched target;
- keep its companion identity and GPU/RAM overhead in GPU Fit and every Lab
  fingerprint;
- use the same controlled baseline, correctness, acceptance, timing, and memory
  evidence as general drafting;
- never infer EAGLE support from `eagle`, `eagle3`, or model filenames.

Current upstream documents support, but the R30 pinned b10034 runtime predates
that documentation. The feature is conditional, not promised on every managed
install. Live acceptance requires an installed runtime and a known target/draft
pair that prove it.

## 3.8 Speculative tuning recipes

Add bounded sweeps for runtime-advertised parameters, initially
`spec-draft-n-max`, `spec-draft-n-min`, `spec-draft-p-min`, and draft GPU layers.
Do not assume a current llama.cpp default is optimal or stable.

- Parameter ranges come from Hermaeus-reviewed recipe definitions and observed
  runtime bounds, not arbitrary unbounded text input.
- Recipes specify whether one-at-a-time or interaction sweeps are intended.
- Stop early on repeated OOM/start failure, deterministic mismatch, or user
  cancellation, preserving every completed/failed candidate.
- Show speed, acceptance, TTFT, prompt throughput, memory, correctness, and
  output equivalence together.
- Do not crown the highest tok/s candidate if required correctness failed.

## 3.9 Prompt/shared-prefix reuse

Provide two evidence levels:

1. **Direct counter:** consume a stable machine-readable runtime counter only
   when capability discovery proves its schema and runtime identity.
2. **Controlled effect:** run identical-prefix workloads with cache enabled and
   disabled, report prompt timing/throughput and output correctness, and label
   the result as a timing effect. Do not report reused-token counts.

Assess llama-server prompt-diff diagnostics only as optional Lab/debug
instrumentation. A build-scoped parser must be opt-in, store bounded derived
events rather than raw logs, and never add flags to normal Chat launches.

Agent-child shared-prefix experiments use reconstructed, hash-recorded prompts
from a controlled fixture. They do not expose private workspace text in exports.
The experiment may establish a speed effect; it does not change Agent prompt
construction in the same batch.

## 3.10 KV/context memory and low-bit experiments

Use doc 02's analytical breakdown as the prediction side. Lab varies context,
K/V representation, Flash Attention, and supported cache placement under an
exact protocol.

For every candidate show:

- expected weights/KV/overhead/companion/headroom allocation;
- observed memory and source;
- prompt/decode throughput and TTFT;
- actual context accepted/served;
- deterministic equivalence when promised;
- quality/benchmark delta for lossy/low-bit representations.

The selected runtime must advertise a cache type before Lab offers it. Loading
success proves only loadability. A future format enters through the capability
registry plus a reviewed byte-cost adapter and quality protocol, not an enum
rewrite.

## 3.11 MoE expert caching experiments

Separate existing CPU expert placement (`--n-cpu-moe`/`--cpu-moe`) from an
actual expert-cache mechanism.

- CPU placement can be tested immediately as a bounded placement dimension,
  with exact weight placement, RAM/VRAM, throughput, and correctness.
- Selective caching is exposed only when the selected runtime advertises a
  stable mechanism and parameters. ExtraArgs is not capability evidence.
- Candidate sets remain small and record cache hit/miss evidence only if the
  runtime reports it directly.
- No assumption that caching helps. Thrash, extra transfers, and slower output
  are valid outcomes.

If no selected runtime proves expert caching, R31 ships CPU-MoE experiments and
the documented Unknown state, not a simulated cache control.

## 3.12 Deterministic equivalence and correctness

For deterministic configurations where speculation is expected to preserve
baseline output:

- baseline and candidate use the same prompt bytes, system/template, sampler,
  seed, max tokens, stop conditions, runtime/model pair, and run order policy;
- compare token ids if the runtime exposes them, otherwise compare exact decoded
  UTF-8 output and label that weaker level;
- retain hashes plus bounded inspectable diff; private prompt/output text is not
  included in public export by default;
- report `Equivalent`, `Different`, or `Unknown`, with reason;
- a Different result is a failed correctness requirement even if speed improves.

Greedy equivalence is the reference where backend/CPU sampling can differ due
to floating point behavior. Stochastic results use quality/behavior comparison,
not exact equivalence, unless the runtime contract explicitly promises it.

Every Lab recipe declares its correctness gate. A speed-only recipe is allowed
only when it is explicitly named speed-only and cannot produce an Apply
recommendation.

## 3.13 DFlash boundary

The capability registry and external-drafter model must represent
`draft-dflash` without architectural changes. Current upstream documentation
describes a target-specific block-diffusion drafter, but R31 keeps production
enablement in Research/Watch until all of these are demonstrated on the selected
runtime:

- stable capability/launch contract and target-binding metadata;
- converted asset provenance and compatibility validation;
- trustworthy block/draft acceptance counters;
- memory accounting and deterministic/correctness behavior;
- at least one repeatable local benefit on supported hardware.

If those gates become true during R31, DFlash may use the same external-drafter
Lab adapter in an independent commit. It must not be special-cased into Services
settings merely to close the row.

## 3.14 Security and privacy invariants

- Lab binds temporary servers to loopback and never removes Local API or Agent
  gates.
- Every executable/model/companion path goes through existing containment,
  symlink, provenance, and trust checks.
- Downloads remain explicit, SHA256-verified, and outside the synthesis/run
  path. Lab does not fetch a drafter because a recipe names one.
- Experiment exports omit local paths, host/user identity, tokens, raw headers,
  environment variables, and prompt/output bodies by default.
- Temporary processes, files, ports, and samples are owned by one run and
  cleaned on success, failure, cancellation, and restart recovery. A missing
  ownership manifest is known empty; a present manifest that is malformed,
  truncated, unreadable, or otherwise indeterminate is `Unknown`, remains
  untouched, and blocks ownership mutation or process cleanup until it can be
  read safely. Failure to read evidence is never treated as absence.
- Apply revalidates identity and uses normal settings persistence. It never
  writes `settings.json` directly.

## 3.15 Acceptance criteria

- Lab is a first-class panel with an immutable definition, explicit baseline,
  bounded candidates, progress/cancel, observations, comparison, evidence, and
  explicit Apply flow.
- A run cannot mutate the owner's active Chat service or persistent config.
- Every displayed metric names its source; missing and zero remain distinct.
- Comparisons refuse uncontrolled model/protocol/fingerprint differences.
- Engine-profile, external draft, EAGLE-3, speculative tuning, prefix, KV, and
  CPU-MoE recipes each remain visible capabilities, even when unavailable.
- External/EAGLE/MoE/low-bit controls appear only from observed runtime
  capabilities.
- Exact or declared deterministic equivalence gates speed results and Apply.
- DFlash is representable and remains Research/Watch until its listed gate is
  met.
- Apply is explicit, stale-identity guarded, and auditable.
- Restart recovery and ownership add/remove operations fail closed when the
  existing ownership evidence is `Unknown`; they do not rewrite the manifest
  or terminate a process on that basis.

## 3.16 Test and live-verification budget

Expected automated coverage: 45-60 tests.

- definition validation, canonicalization, bounds, immutable run snapshots,
  cancellation, cleanup, restart recovery, and loopback isolation;
- observation missing/zero/source behavior and comparison refusals;
- temporary-config no-save and stale Apply refusal;
- recipe bounds and single-variable/declared-interaction guards;
- external/EAGLE target/vocabulary/runtime capability validation;
- speculative counters, sweep early stop, equivalence/difference/Unknown;
- prefix direct-counter versus timing-effect labels;
- KV/MoE capability gating, memory/quality fields, and no loaded-equals-good
  shortcut;
- export redaction and evidence-store linkage.

Linux/COSMIC live gates are staged:

1. **Lab shell:** run/cancel a baseline on an isolated loopback server while
   Chat's configured server remains usable.
2. **GPU/KV:** compare two context/cache candidates and inspect prediction,
   observation, speed, and correctness.
3. **Speculation:** on a runtime-proven mechanism and known compatible asset,
   run baseline/candidate, inspect draft acceptance and equivalence. If no such
   asset/runtime exists, verify the feature is Unknown/unavailable and record
   the missing evidence instead of claiming it passed.
4. **Apply:** apply one disposable candidate after review, verify Services shows
   the exact fields, then restore through the same explicit flow.
5. **Privacy:** export a run from a workspace under the owner's home directory
   and confirm the export contains no home path, user/host identity, token, or
   raw private prompt/output.
