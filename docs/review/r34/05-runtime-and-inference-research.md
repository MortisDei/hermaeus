# 05. Runtime and inference research

## Actual baseline

**RF:** setup's source-controlled fallback is llama.cpp **b10034** (S32).
The R33 authority audit tested **b10930**; the later release receipt records
an owner Linux session using **b11491**. These are different baselines. No
current installed runtime, model, GPU, help output, listener or memory release
was inspected in this planning pass. Never apply b10930 evidence to b10034 or
infer today's installed build from the historical b11491 receipt.

Hermaeus already has typed launch configuration, capability probes, bounded
resource admission, isolated Lab recipes, effective-launch parsing, Speed Check
and immutable empirical evidence. P3 extends those only where a measured
question requires it. It does not add a runtime manager or provider-specific
policy to Core. Source locations S11-S14/S31/S32 are in [01](01-current-state-and-evidence.md).

## Upstream delta, checked 2026-10-16

UF sources R03-R06 in [10](10-technology-decisions.md) distinguish the released
`v0.6.0` commit `d81235049384534c167caea52b85a694f6103d14` from inspected
master `2f729cfa4ca6f55b8bacf67bb2ba92cb9548e291`. New master syntax is not
automatically in that release or a b-numbered binary.

The upstream server documentation advertises router model loading/unloading,
idle sleep, KV formats, prompt caching, speculative mechanisms and compatible
HTTP endpoints. That is advertisement, not effective behaviour on the target
machines. Its internal `/tools` API is explicitly unsuitable for downstream
applications. Do not route Hermaeus tools/MCP through it or inherit a second
permission mechanism. Anthropic-compatible transport does not authorise
another provider adapter by itself.

The developer documentation describes router children communicating state and
shutdown over process streams. Sleep destroys inference contexts and reloads
on a new inference request; health/properties/metrics can remain responsive.
Therefore a healthy or sleeping label is insufficient proof of reclaimed GPU
memory or a successful resource release. Native execution is required.

The `v0.6.0` release notes include Vulkan sparse Flash Attention for quantised
K/V. This is backend/model/kernel-specific support, not evidence that every
Vulkan GPU, dense model or KV format gains speed. Keep sparse attention distinct
from generic Flash Attention and SWA/context-window controls.

## Experiments and adoption gates

Use [07](07-performance-baselines.md) for environmental identity, raw samples,
repetition and comparison rules. Every row is unexecuted. P3 may end with a
documented Defer if the runtime cannot prove a required field.

| Question | Controlled experiment through existing authority | Acceptance / decision |
| --- | --- | --- |
| Is release discovery compatible with upstream versioning? | S32's `ParseBuildNumber` accepts b-number tags. Feed saved b-tag and semantic-release asset metadata to existing setup/version tests; inspect archive wrapper naming, executable identity, pruning and source digest | P1 P3 compatibility audit. UF semantic release exists; source filters b tags. Do not call every install broken. Extend selection only with exact supported archive/version proof and downgrade tests; never lexically compare b and semver or drop hash checks |
| Does single-model idle sleep save usable resources? | Isolated baseline stop/start versus supported idle sleep, then wake; same model/context/KV, idle time and request. Capture pre-idle, sleeping, post-release and awake samples, reload latency and failures | First candidate before router adoption. Require actual released memory, updated resource residency/admission and correct restart/cancel. Without trustworthy process counters retain Unknown; controlled whole-device deltas remain device observations, not model-owned VRAM |
| Would router mode simplify switching? | Disposable approved spike with two known local models, bounded one-resident policy, no downloads; capture router and child PIDs/start/hash, launch argv, effective model identity, admission on autoload, unload/cancel/crash | **Defer adoption** until one Services owner can account for all children and prevent uncontrolled allocations. Router-owned model switching cannot bypass Hermaeus admission or create independent saved presets. Compare against existing cold restart; faster switching alone is insufficient |
| KV quantisation trade-off | Existing Lab f16/q8_0/q4_0 recipes with exact supported K/V and explicit FA where needed; same model, prompts, context and placement. Pair with quality suite and long-context retrieval cases | Require verified effective fields, valid output and quality evidence. Existing missing `quality.score` gate remains. Do not assume KV memory ratio equals total VRAM saving |
| Flash Attention and Vulkan SFA | Exact runtime/backend/model eligibility first; single-axis FA off/on or proven sparse-kernel variation. Compare each host against its own baseline; log effective kernel/fields and fallback | Unknown kernel engagement means no SFA headline. GTX 1660 Super and RTX 4060 are separate experiments. Do not force unsupported quants or use a hardware flag as execution proof |
| Prompt cache/shared prefix | Existing isolated recipe with identical reconstructed prompts and request-cache state; cold/warm ordering counterbalanced, fixed hashes/output rule | Preserve `ControlledTimingEffect` when reused-token counters are absent. A timing delta is not a token-reuse count; cache RAM/residency/clear/privacy must remain bounded |
| Speculative decoding | Existing exact-pair/target/tokeniser/hash gates; controlled no-spec baseline and mechanism/parameter candidate; acceptance counters, TTFT and throughput, companion residency and output correctness | No draft counters means Unknown engagement; zero is distinct. Preserve provider-neutral tool behaviour. A short exact output match cannot establish broad quality/distribution equivalence |
| Capability/effective evidence drift | Saved help/props/startup fixtures for exact old/new binary identities plus real installed execution. Include renamed/removed options, negative capability, conflicting fields and missing PID association | Add fields through existing capability cache/evaluator, never another probe database. Failed/unknown evidence must continue to block ranking/recommendation/Apply |
| Quantisation, including PTQ1_0 | Read publisher format/backend contract before downloading. If approved, compare exact model/format/runtime pair against a fit-compatible quality baseline, full allocations and load cost | **Research/defer** private ternary fork adoption. R04 says Bonsai 2 transforms/formats need the publisher fork; a recognised GGUF type or smaller file is insufficient. Do not silently swap the official runtime, auto-convert weights or claim a 27B model fits a 6 GB GPU |

## Explicit exclusions and reversible experiments

No runtime install, model download, new endpoint, normal-launch flag change,
automatic model routing or saved preset change occurs here. An experiment must
restore the unchanged source configuration and retain a failed/blocked restore
receipt. Reject uncontrolled partial cleanup or a winner derived from
Unverified/Mismatch evidence. Preserve original raw evidence when parsers evolve.

PAIR, vLLM persistent weights and third-party orchestration are dispositions in
[10](10-technology-decisions.md), not implicit P3 integrations. Research any
additional model/backend only after the existing feature's benefit and safety
can be measured on the relevant host. macOS remains best-effort/Unknown until
native testing exists.
