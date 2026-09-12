# 05. Runtime, fit and supplementary evidence

## 5.1 Preserve R32 and close the remaining launch path

**RF:** R32 has typed GPU placement, canonical configuration/runtime/model
identities, whole-workload resource planning, reservations/admission, adaptive
envelopes, effective/observed distinctions and isolated Lab hosts. See
`src/Hermaeus.Services/ProcessManagement/ServerProcessManager.cs`,
`ResourceCoordinator.cs`, `IsolatedLabRuntimeHost.cs`, `docs/llama-cpp-features.md`
and the R32 closure. None of these are proposed as new R33 architecture.

**R33-R, mandatory:** reconcile model changes and Auto-tune with those owners.
`ServerProcessManager.AutoTuneAsync:356` copies only part of ServerConfig and
`TryProbeAsync:476` launches directly. KV/Flash Attention/MoE/companions and other
newer fields are not all copied. A probe can therefore measure a configuration
other than the one shown or later launched. It tries one analytically reduced
context, then retries layer descent at the original infeasible context.
Models single/bulk tuning calls the same method without the hardware/GGUF inputs
used by Services (`ModelManagementViewModel.cs:930,1007`).

**Outcome:** ordinary start, Auto-tune, Models tuning, Lab, resume and recovery
share canonical launch construction, admission and ownership. Tuning changes
only declared dimensions. Every candidate names its full configuration and
expected workload, preserves failed evidence and releases its allocation.

**Boundary:** a Services-owned tune operation using existing runtime factory,
configuration cloning/identity, coordinator and evidence stores. Retire the
independent reduced-config launch path; do not create a second optimizer.
Expose cancellation to Services and Models through the same operation. Prevent
concurrent tunes across those pages from allocating independently.

Use an explicit bounded candidate set across context and placement when an
infeasible baseline needs recovery. Reuse the approved context ladder and whole-
workload prediction; do not repeatedly reset to a known-infeasible context after
one reduced candidate fails. Missing model shape or device evidence remains
Unknown and calls for controlled probes or explicit owner choice, not a guessed
fit. Keep minimum usable context and accelerated-backend intent in the decision.
No automatic CPU downgrade or model substitution.

**OO/U:** the reported Auto-tune application crash is not yet tied to a stack.
Services and Models already catch ordinary awaited exceptions. Reproduce with
isolated assets, provider/process boundaries and callbacks; distinguish managed
exception, dispatcher exception, native crash and OS OOM termination. A bounded
retry policy cannot by itself prevent an OS kill if admission is bypassed.
Do not claim the crash fixed until its mechanism and representative siblings are
verified. Preserve evidence before clearing UI logs.

**Verification:** canonical field preservation; overlarge carried context;
missing metadata; lower-context success/failure; all candidates fail; user cancel
in identity/preflight/start/health/cleanup; wrong-port process; no child/lease
leak; result not silently applied; successful health != performance/quality.
**Dependencies:** application operation owner, production launch identity and
configuration reconciliation in doc 06.
**Non-goals:** runtime hot mutation, global machine optimizer, killing unrelated
GPU processes, new model-specific exceptions.

## 5.2 Model switch and backend hierarchy

**RF:** Services only replaces context on model-path change when a model card
has a default (`ApplyModelDefaultsIfPathActuallyChanged`). Model card defaults
are not capacity measurements. Existing editor/tune/global/service values have
different authorities; R33 must not silently promote any one into observed fit.

**P:** show current value and source: explicit service edit, reviewed model
profile/default, or carried prior configuration. On model change, invalidate
old fit/tune compatibility, preserve configured intent, and identify what is
unverified or infeasible. Offer an explicit review of a viable candidate or a
controlled tune. Persist only after the existing owner save/review flow.

Backend explanation must follow actual selection:

```text
installation preference (Auto or explicit runtime variant)
 -> installed selected variant / executable identity
 -> service executable and placement/engine configuration
 -> capability and device observations
 -> effective backend/placement, with reason or override
```

This is not a single scalar precedence chain: CPU placement may be selected
inside a Vulkan-capable binary. CUDA/Vulkan executable choice and GPU layer
placement are different dimensions. `LocalAiSetupService` stores preferred and
installed variants separately. On the inspected host both were Vulkan; the
runtime executable was b10821. That snapshot does not prove the earlier Auto
behavior. Explain stale/unavailable paths, explicit overrides and incomplete
observations without rewriting preferences. Audit Services creation/rebuild,
Setup/update, Models launch and existing service configs, not only detection.

Verification includes Auto installation fallback with configured Auto retained,
explicit backend strictness, service using an older/manual executable, CPU
placement on accelerated binary, changed model with no card default, dirty
editor against a new persisted configuration, and unknown observed backend.

## 5.3 Current upstream facts and selected experiments

Checked 2026-09-07. Inspected upstream commit
`2092353c8b828105f3f47e40e094ed3153ce0531` (2026-09-07 UTC).
Local executable reports `0.4.0-dev`, b10821, commit `971595d66`, SHA256
`c583d3a45d750672e77f4034e30d5f4c05d1747162b1ee50db9cb15c46149a82`.
Only version/help were executed locally; no model, fitting or throughput probe.
Windows installed executable identity remains Unknown.

Primary sources:
[server options](https://github.com/ggml-org/llama.cpp/blob/2092353c8b828105f3f47e40e094ed3153ce0531/tools/server/README.md),
[argument parser](https://github.com/ggml-org/llama.cpp/blob/2092353c8b828105f3f47e40e094ed3153ce0531/common/arg.cpp),
[fit implementation](https://github.com/ggml-org/llama.cpp/blob/2092353c8b828105f3f47e40e094ed3153ce0531/common/fit.cpp),
[Vulkan implementation](https://github.com/ggml-org/llama.cpp/blob/2092353c8b828105f3f47e40e094ed3153ce0531/ggml/src/ggml-vulkan/ggml-vulkan.cpp).

| Capability | UF / installed evidence | R33 decision |
| --- | --- | --- |
| Fit and selective placement | Auto/all/exact, device/split/tensor controls; fit adjusts unset values. Source retains explicitly set context and rejects user-fixed incompatible placement/overrides during fitting. | Preserve explicit fit ownership; test recovery through current contract, not adding contradictory flags. |
| CPU/GPU MoE | `--cpu-moe`, `--n-cpu-moe`; dense first-N FFN placement has separate `--n-cpu-ffn`. Installed help advertises these. | Existing CPU-MoE recipe stays. Conditional dense-FFN selective placement experiment only for a compatible dense model and bounded evidence. |
| Expert caching/prefetch | [RFC #28248](https://github.com/ggml-org/llama.cpp/discussions/28248) describes proposed cache work and closed predecessors. [#25859](https://github.com/ggml-org/llama.cpp/issues/25859) reports offload profiling/fork prefetch work. These are author reports, not merged support guarantees. | Research/watch. No expert-cache/prefetch setting or claimed speedup. No such option established in inspected help/parser. |
| KV/session/slots | Context checkpoints, host cache RAM, unified KV and per-slot limits, idle-slot caching are documented and largely already gated in Hermaeus. | Reuse existing controls. Measure host-RAM/context consequences and clear/restore behavior; do not turn session state into task persistence. |
| Speculation | Current and installed help list draft-simple, EAGLE3, MTP, DFlash, DSpark and n-gram variants. | Existing external/EAGLE plans stay. DFlash/DSpark asset-pair experiment conditional on exact binary and verified compatible companion; syntax alone does not establish engagement. |
| Vulkan allocation behavior | Source includes allocation/block-size and host-memory environment controls. [Issue #27734](https://github.com/ggml-org/llama.cpp/issues/27734) reports a large-context throughput cliff on a particular build/device. | Do not generalize its claimed workaround to a 6 GB NVIDIA GPU. Reproduce only if relevant symptoms occur; record driver/build/context/workload. No permanent environment slider. |
| Structured logging | Upstream `--log-jsonl` landed in [b10823](https://github.com/ggml-org/llama.cpp/releases/tag/b10823). Installed b10821 help lacks it. | Capability-gated supplementary evidence adapter, next section. No forced runtime update. |

Experimental arbitrary Extra Args remain explicit, conflict-checked and outside
normal defaults. Environment-only Vulkan experiments cannot be represented as
CLI flags; use a reviewed isolated process environment if an experiment earns
selection, without globally changing the owner's environment. No fork download,
model install or runtime update is part of this planning pass.

## 5.4 Supplementary JSONL, canonical native diagnostics

**UF:** [log.cpp at inspected revision](https://github.com/ggml-org/llama.cpp/blob/2092353c8b828105f3f47e40e094ed3153ce0531/common/log.cpp)
emits a JSON object with `type`, `time`, `level`, `msg`; arg.cpp advertises
`--log-jsonl`/`--no-log-jsonl`, stdout and disabled colors. This is a structured
log envelope containing message text, not a versioned typed allocation/fit API.
Do not infer stable model placement or token counters merely from JSON syntax.

**P outcome:** native Hermaeus diagnostics remain useful with llama.cpp absent.
Attach supplementary observations to existing operation/runtime identities:
Hermaeus application evidence, llama.cpp runtime evidence, provider evidence,
and OS/process observations remain separate origins. Preserve collection time,
source time semantics, binary/parser version, stream, bounded payload and trust.
Do not replace `IRuntimeLogService` or redefine all evidence as llama.cpp logs.

**Boundary:** capability observation for the selected binary, optional parser
adapter at managed process logging, redaction before persistence, bounded line
size/rate/retention, schema validation, malformed/unknown-line fallback and
provider-neutral projection. Plaintext parsers must continue to work when JSONL
is absent; when enabled, unwrap `msg` only through a build-compatible adapter.
No prompt-logging/debug flag is enabled to obtain evidence. Raw model content
or secrets must not leak into the supplemental store.

**Verification:** b10821 unavailable, supported fixture available, mixed/plain
streams, unknown fields/levels, partial/oversized line, log flooding, parser
failure, stderr process crash, missing counters, redaction, restart retention,
and a non-llama provider whose application diagnostics remain complete.
**Dependencies:** operation/evidence identities and existing runtime logging.
**Non-goals:** replacing native logs, universally trusting message-text metrics,
turning upstream experimental schemas into permanent public settings.
