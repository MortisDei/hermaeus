# R33 Lab and Benchmark runtime-authority audit

Audit date: 2026-09-13. Branch: `r33/planning`. Release target:
`v0.41.0-beta`.

This audit covers every shipped Lab recipe and the reusable Benchmark path after
the Linux owner-live evidence failures. The boundary is deliberately narrow:
requested configuration, resolved configuration, launched configuration,
effective runtime state, and observed telemetry are separate facts. A value
from an earlier boundary is never used as a proxy for a later one.

## Shared evidence contract

`RuntimeEvidenceEnvelope` in `src/Hermaeus.Core/Models/RuntimeEvidenceModels.cs`
is the shared persisted receipt. `RuntimeEvidenceEvaluator` in
`src/Hermaeus.Services/RuntimeEvidenceEvaluator.cs` marks it `Verified` only
when all of the following agree:

- requested, resolved, and launched configuration identities are present and
  stable, with requested equal to resolved and launched equal to resolved;
- the expected runtime and model identities are present;
- the managed process receipt contains PID, start time, executable path,
  executable hash where available, and exact launch arguments;
- the effective launch receipt is auditable, uses the expected runtime, and is
  associated with the same PID, start time, and executable;
- every recipe-specific effective field is proven by the receipt;
- proven effective values match the launched configuration. The envelope records
  the observed effective fields and only publishes an effective configuration
  stable id when those required values match;
- telemetry samples carry the same `PID:start-time` process-instance id.

Missing evidence produces `Inconclusive` or `Unverified`; conflicting evidence
produces `Mismatch`. Only `Verified` is eligible for Lab controlled comparison,
recommendation, Apply, Benchmark ranking, Insights, or Speed Check. A completed
workload and a verified runtime are intentionally separate outcomes.

The effective parser accepts the current b10930 nested `/props` shape for
context and slots, startup offload receipts for GPU placement, and optional
properties for KV cache, Flash Attention, CPU-MoE, and speculative settings.
Unknown optional properties remain unknown. The process telemetry source also
checks the live executable identity before accepting a sample.

## Lab recipe matrix

Every recipe requires the common effective fields `context`, `slots`, and
`gpu_layers`, plus the varied field below. The recipe runner freezes this list
into the immutable definition before execution. `Auto` GPU placement additionally
requires a proven fit result.

| Shipped recipe | Varied authority | Required effective fields | Controlled-result rule |
| --- | --- | --- | --- |
| Engine profile | `GpuLayers` / typed placement | `gpu_layers` plus common fields | Startup offload receipt must match the candidate, including CPU, partial, or all placement. |
| Context | `ContextSize` | `context` plus common fields | `/props` context must match the candidate. |
| KV cache | `KvCacheTypeK`, `KvCacheTypeV` | `kv_cache_type_k`, `kv_cache_type_v` plus common fields | Both effective K and V types must be present and match. |
| Flash Attention | `FlashAttention` | `flash_attention` plus common fields | Effective mode must be present and match `on` or `off`. |
| CPU-MoE placement | `CpuMoeLayers` | `cpu_moe_layers` plus common fields | Effective CPU expert placement must be proven, not inferred from the requested value. |
| External draft | speculative companion/mechanism | `speculative_mechanism` plus common fields | The runtime must report the active mechanism for the exact target process. |
| EAGLE-3 | speculative companion/mechanism | `speculative_mechanism` plus common fields | Companion identity and effective mechanism remain separate checks. |
| Speculative draft maximum | `SpeculativeNMax` | `speculative_mechanism`, `speculative_nmax` plus common fields | Mechanism and n-max must both be proven and match. |
| Speculative draft minimum | `SpeculativeNMin` | `speculative_mechanism`, `speculative_nmin` plus common fields | Mechanism and n-min must both be proven and match. |
| Speculative probability minimum | `SpeculativePMin` | `speculative_mechanism`, `speculative_pmin` plus common fields | Mechanism and p-min must both be proven and match. |
| Speculative draft GPU layers | `SpeculativeDraftGpuLayers` | `speculative_mechanism`, `speculative_draft_gpu_layers` plus common fields | Mechanism and draft GPU placement must both be proven and match. |
| Prompt-prefix reuse | `PromptCacheMode` | `prompt_cache` plus common fields | A timing improvement is not cache proof. Current b10930 exposes prompt-cache startup text but no parsed effective `prompt_cache` field, so this recipe remains Inconclusive until the runtime exposes a direct observation. |

The table is the complete `LabRecipeKind` enum. No recipe is allowed to receive
a headline delta or Apply review by relying only on its plan, configuration
fingerprint, command-line intent, endpoint health, or a requested value.

## Benchmark authority audit

`BenchmarkService` resolves the selected local GGUF model through the managed
runtime registry, uses the serving `ServerProcessManager` receipt, and samples
the process-scoped telemetry source. Normal workload completion is retained for
diagnosis when that chain cannot be proven, but the run is saved with
`EvidenceStatus = Unverified` or `Mismatch` and `ComparisonEligible = false`.
The same effective field comparison is used for context, slots, GPU placement,
Auto fit, KV cache, Flash Attention, CPU-MoE, threads, and any optional
speculative fields that are configured. A proven but different effective value
is `Mismatch`, not a successful observation.

The ranking and Insights paths filter on `ComparisonEligible`. An unverified
historical run remains visible with its evidence caveat and is not backfilled
from metadata, configuration defaults, or an old fingerprint. It must be rerun
after the effective receipt and telemetry binding are available. Markdown and
CSV exports include the envelope id, evidence status, reasons, and eligibility;
JSON retains the complete envelope.

## Native Linux receipt matrix

These probes used the exact installed runtime and model below, with no owner
settings or repository data writes:

```text
/mnt/Gaming/AI/llama-server/b10930/llama-server
-m /mnt/Gaming/AI/Models/llm/unsloth__gemma-4-E2B-it-qat-GGUF/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf
--host 127.0.0.1 --port <case-port> --ctx-size 4096 --threads 4 --parallel 1
--fit off --n-gpu-layers <placement> --props --metrics --log-verbosity 4 --no-webui
```

The executable reported build `10930` at commit `56381e407`. Each case served
`/health`, `/props`, `/metrics`, and one `/v1/chat/completions` request, then
received SIGINT. RSS is the `ps` value while the server was live. VRAM is the
`nvidia-smi` process value during the request. The response was HTTP 200 in all
cases; the capped eight-token prompt was not treated as semantic correctness
proof because the model returned a thinking fragment and `finish_reason`
`length`.

| Placement | Port and receipt | Effective placement | RSS | Process VRAM | Prompt / decode throughput | Cleanup |
| --- | --- | --- | ---: | ---: | ---: | --- |
| CPU | `39413`, `/tmp/hermaeus-native-r33-cpu-final-qOZnyP`, PID `98888` | `offloaded 0/36`; CPU model buffer `2483.90 MiB`; `/props` `n_ctx=4096`, `total_slots=1` | `2,873,912 KiB` | `129 MiB` | `10.16 / 9.76 tokens/sec` | HTTP 200, exit `0`, no server remained |
| Partial GPU | `39411`, `/tmp/hermaeus-native-r33-gpu17-YlITPJ`, PID `97903` | `offloaded 17/36`; Vulkan model buffer `776.75 MiB`; `/props` `n_ctx=4096`, `total_slots=1` | `2,525,288 KiB` | `926 MiB` | `13.21 / 2.78 tokens/sec` | HTTP 200, exit `0`, no server remained |
| All GPU | `39412`, `/tmp/hermaeus-native-r33-gpuall-sXV82A`, PID `98093` | `offloaded 36/36`; Vulkan model buffer `1223.90 MiB`; Vulkan KV buffers; `/props` `n_ctx=4096`, `total_slots=1` | `2,071,996 KiB` | `1376 MiB` | `159.56 / 72.42 tokens/sec` | HTTP 200, exit `0`, no server remained |

The runtime identified the device as an NVIDIA GeForce GTX 1660 SUPER with
6144 MiB total memory and driver `580.173.02`. The native receipts prove
runtime placement, loopback serving, process cleanup, and measured throughput.
They do not prove a visible Lab GUI walkthrough, semantic workload equivalence,
or owner acceptance of the editor fallback.

## Local continuation evidence, 2026-09-13

The repair continuation passed the Debug and Release solution builds with zero
warnings and zero errors. Each complete sequential suite passed `2,731`,
skipped `17` expected platform-gated tests, and failed `0`; the focused R33
repair set passed `119/119`. The final `scripts/coverage.sh` run passed the
repository's `60%` line-coverage ratchet, with its temporary report outside
the checkout.

The Release `R33Driver` returned `ok:true` on fresh `/tmp` settings, data, and
workspace paths. Its evidence included an Applied, changed,
readback-verified Agent receipt, persisted benchmark cancellation with zero
cases, one RAG generation/query, Chat retrieval context, voice completion, Lab
failure cleanup, Lab Apply/reopen, and driver shutdown. The driver uses named
scratch substitutions for nondeterministic external boundaries and is not
native-runtime or GUI proof.

The rebuilt `v0.41.0-beta` Linux archive passed its SHA256 check, launcher-link
and executable assertions, layout/no-PDB checks, and Release package assembly.
The published package apphost was also started from a dedicated `/tmp`
working directory. The current approved-host launch instead used the existing
owner data and configured Gemma/Qwen managed servers. `runtime.log` recorded
both loopback servers reaching `Running` and embedding warm-up completion, and
the COSMIC screenshot showed the loaded main shell, model selector, navigation
bar, conversation list, and Doctor warning banner. The native CUA surface
exposed only state inventory and no app/window interaction methods, so model
switching, Lab/Benchmark walkthroughs, editor fallback rendering, approval,
dialog placement, and clean window-close telemetry remain owner-live gates.
The fallback console interrupt stopped the app and managed children but left
the lifecycle journal `CleanExit:false`; the earlier tray-path clean evidence
is retained separately and is not replaced by this incomplete close attempt.

## Monaco and AvaloniaEdit fallback boundary

The workspace editor still follows the bounded local Monaco path first and
reattaches the current document to AvaloniaEdit when bundle, adapter, bridge,
resource, or WebView setup fails. The fallback carries document text and
display state only; it does not change Agent approval authority. Automated
source and view-model coverage proves the fallback contract. Native WebView
rendering, focus, keyboard/IME, DPI, large artifacts, teardown, and an owner
visible walkthrough remain owner-live gates because no desktop surface was
available to this audit.

## Evidence boundary

This audit proves the shared source-level and native process boundaries. It does
not convert native llama-server receipts into owner Lab GUI acceptance. The
remaining owner action is to run the selected model through each intended Lab
recipe and Benchmark flow, inspect the visible status and retained details, and
confirm that verified, inconclusive, and unverified states are presented as
documented.
