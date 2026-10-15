# 07. Performance baselines and measurement

**Status:** protocol only. No R34 performance numbers have been collected.
Historical R33 Speed Check numbers are workload-specific, not baselines for
startup, Recall or another GPU. B0 must capture an R33 control on each actual
host before changed implementations can claim improvement.

## Host matrix and fingerprints

| Host | Required baseline | Evidence limit |
| --- | --- | --- |
| GTX 1660 Super 6 GB, Pop!_OS 24.04/COSMIC/Wayland session | Released R33 control versus candidate on this same system | Record actual X11/XWayland/native Wayland backend, compositor, kernel, NVIDIA/Vulkan/CUDA drivers and physical display; do not infer backend from session |
| RTX 4060 laptop 8 GB, Windows 11 | Independent R33 control versus candidate on this laptop | Record laptop power/AC mode, hybrid graphics, Windows build, driver, throttling and trustworthy process counters |
| CPU fallback | Same CPU/runtime/model/config before/after; GPU placement proven off | No pooling CPU/GPU samples or extrapolating from one host |
| macOS | Best-effort only | Unknown without native testing; do not block Windows/Linux by inventing a Mac pass |

Every receipt carries app commit/build/configuration/RID, SDK/runtime, process
identity, exact executable SHA/version/backend/help identity, model/companion
hash and quantisation, dataset/query/suite revision, requested/resolved/launched/
effective configuration, parser/evaluator version, hardware, available memory,
thread/affinity settings, placement, context/slots/KV/FA, thermal/power state,
background workload and measurement source/unit/time. Capture absolute paths
only in private raw evidence where needed; portable fingerprints/exports follow
existing redaction. A version string alone does not identify a build.

## Repeatable method

Use existing `StartupTimingService`, traces, Activity, runtime telemetry,
Lab/Benchmark/eval records and R33 driver seams. Raw OS/profiler traces can be
attachments referenced by operation/run ID outside the checkout. They do not
become a second result database or admission authority. Instrument only missing
phase boundaries. First record observer overhead with sampling off/on.

1. Prepare isolated scratch settings/data/workspace, a known corpus and fixed
   workload definition. Never use the owner's real settings to reset caches.
2. Record startup and background completion separately: visible first usable
   window, ready-to-send, model warmup, Recall/backfill/watch completion. Define
   cold process versus cold filesystem cache; do not claim disk-cold without
   controlling OS cache. Reboot-based cold samples require owner scheduling.
3. Run one practice iteration, then initially five independent samples per
   candidate and host; alternate/counterbalance baseline/candidate order. Use
   existing recipe repetition limits where applicable. Expand only if observed
   variability prevents a decision. Never silently discard failed/OOM/slow runs.
4. Record raw values, elapsed wall/CPU, allocation bytes/GC, working set/private
   bytes and samples with units, timestamps and provenance. Report median,
   min/max, sample count, failures and missing reasons. Tail percentiles need
   enough samples and a stated method; five runs are not a robust P95.
5. Use the same output/quality rules and effective-evidence requirements on both
   sides. Warm/cold, model change, driver change or different corpus needs a
   separate comparison, not a pooled average.
6. Explain uncontrolled deltas and rerun them before a headline. Set practical
   regression tolerances from B0 noise plus owner workflow impact **before**
   evaluating candidates; no arbitrary speedup targets or universal score.

## Domain protocols and owners

| ID | Domain / owner | Workload and measurements | Decision rule |
| --- | --- | --- | --- |
| PERF-01 | Startup / lifecycle | Fresh/warm launch with fixed data, stopped versus ready servers, setup-complete versus wizard; monotonic startup phases and time to usable UI | No hidden work moved after the stopwatch; startup and eventual idle cost both reported |
| PERF-02 | Idle/background / Services + shell | After quiescence, five-minute no-input capture, then telemetry open/close, Recall pending/exhausted retries and watch schedule states | Attribute wakeups/polls to owners, no more than required registered loops. Idle without models and model-resident idle are distinct |
| PERF-03 | Navigation / Desktop | Fixed route sequence across all major surfaces, 20 cycles, loaded/reused/recreated view paths | UI latency, allocations, callback counts, live view counts, retained memory; no monotonic obsolete-view growth in covered cases |
| PERF-04 | Markdown / Desktop | Fixed short/long/code/table transcript, known streaming delta schedule, selection/copy, scroll pin and navigate away/back | Parse CPU/allocation versus layout/render/UI stalls, reused/rebuilt blocks. Streaming/focus/copy correctness gates any scheduling change |
| PERF-05 | RAG / Rag | Fixed small and over-cache-budget corpora, ingest/reindex/watch/clear/reopen, retrieval/rerank on/off | Phase timings, rows/bytes, peak allocations, publication correctness and eval relevance; memory budget is not merely cache retention |
| PERF-06 | Recall/Memory / Services | 1k/10k/50k/100k protocol in [06](06-retrieval-and-storage-efficiency.md) | Embedding versus SQLite/decode/cosine/select/hydrate/fuse and relevance preserved |
| PERF-07 | Load/unload/switch / Services | Same model stop/start; alternate two models; cancel during each phase; sleep only if supported | Load/wake latency, actual released memory, process ownership and next admission, source restore outcome |
| PERF-08 | KV/FA/SFA/speculation/cache / Lab | Existing bounded single-axis recipes, fixed inference suite and quality corpus | RuntimeEvidenceEnvelope Verified, field/kernel proof and quality gates; no throughput-only Apply |
| PERF-09 | Placement/admission / resource coordinator | CPU/partial/all where supported; embedding/voice co-residency, simultaneous start requests | Fresh reservations, observed peaks versus prediction, Unknown preserved, no silent eviction/model switch |
| PERF-10 | Lab/Benchmark observer overhead / Services | Same workload with/without trace/sampling at existing cadence | Separate harness/preparation/cleanup and inference; instrumentation must not dominate reported gain |
| PERF-11 | Agent / Agent | Scratch task with fixed model-boundary responses and separate real-model run; prepare/approve/readback/restart | Task state, receipt and recovery latency/bytes, output bounds, no lost state or weakened approval |
| PERF-12 | Shutdown / shared lifecycle | Quiescent and active Chat/Agent/embedding/Lab/voice, close/tray/restart; failure-injected owner | Deadline and per-owner result, no owned child left after proven clean drain. An incomplete drain is recorded honestly and fails clean-shutdown acceptance |

## Raw evidence and result eligibility

Keep scratch/run outputs outside the repository, with a manifest mapping raw
files and SHA256 to operation IDs, host fingerprints and protocol revision.
Use the existing verification scratch ownership helper for shell drivers;
Windows tests use the documented external results directory. Store synthetic
versus owner-private provenance explicitly. Commit only sanitised summaries
and redistributable fixtures after separate review.

Runtime comparisons use the existing evidence envelope, not a new performance
truth. Missing process VRAM stays Unknown; whole-device observations stay
labelled as device totals even in a controlled quiet-host experiment. Buffered
Lab requests cannot manufacture TTFT; use an instrumented streaming path or
leave it missing. Report measured release, reload cost and model readiness
separately. A warm HTTP `/health` response is not proof a model is resident.

Accept an optimisation only when repeatable useful benefit exceeds the chosen
noise/impact band and the behavioural/security/quality gates pass. If evidence
is inconclusive, retain the simple implementation and record Defer. Net code
deletion is welcome; neither line-count targets nor favourable synthetic-only
results can override correctness.
