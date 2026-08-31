# 08. R32 roadmap

Branch: `r32/round` from `b034182` on current `origin/main`. One round PR unless
the owner explicitly changes that workflow. Planning does not authorize a
commit, push, PR, merge, version, tag, release, or repository-setting change.

The owner controls every permanent Git and release action. Implementation
batches remain local until the owner authorizes the next action after reviewing
the evidence.

## 8.1 Dependency spine

```text
Current runtime facts + canonical launch/model identity
                         |
                         v
       Whole-workload inventory + snapshots
                         |
                         v
              plans + reservations
                 /               \
                v                 v
       adaptive launch       in-process AI
       + fit receipts        resource adapters
                \                 /
                 v               v
          evidence-backed recommendations
                 + explicit apply/undo

Evidence origins + current memory/RAG provenance
                         |
                         v
            assertion/source revisions
                         |
                         v
        current/as-of/history retrieval + review

Model inventory + pinned Hugging Face card identity
                         |
                         v
             bounded artwork cache/cards
```

The temporal track is independent of adaptive inference after the shared R31
provenance foundation. Do not block memory/RAG correctness on GPU work or join
the stores into one schema.

## 8.2 Bounded implementation batches

| Batch | Deliverable | Depends on | Classification | Landed |
| ---: | --- | --- | --- | --- |
| 0 | Verify installed Linux/Windows runtime help/version, exact argument spelling and effective-placement observability against the already-fixed doc 02 contract; inspect pinned reranker graph batch dimensions; capture current build/test/coverage baseline | planning pack | mandatory environment facts | Yes |
| CI | Separate non-required pre-PR branch checks from required PR merge-context checks; preserve main/fork coverage and trusted-push security boundaries; add authority-scoped superseded-run cancellation without changing repository settings | live ruleset re-read, doc 09 | mandatory operational efficiency | No |
| 1 | Typed CPU/Auto/All/Exact launch intent and exact legacy/tune-profile migration; remove implicit tune-profile-on-Start mutation; canonical managed launch specification, conflict policy for core `ExtraArgs`, one configured/planned/rendered/effective/observed projection and fingerprint, versioned compatibility tests | 0 | mandatory correctness foundation | Yes |
| 2 | Services-owned model inventory with bounded scan/GGUF/manifest cache and explicit invalidation; converge simple card fit onto the versioned prediction engine while preserving a labelled pre-download estimate | 0, 1 | mandatory shared foundation | Yes |
| 3 | Resource consumer registry, allocation owner/component/per-device model, immutable snapshots, local/remote/owned identity, authoritative per-device and Unknown observations, in-process adapters, bounded persistence | 1, 2 | mandatory resource spine | Yes |
| 4 | Whole-workload composition, headroom, priorities, reservations, concurrency, mandatory lease-bearing admission at every production start/restart/resume/lazy-load owner, System Overview/Services receipts | 3 | mandatory resource spine | Yes |
| 5 | Adaptive envelope, deterministic candidates, upstream fit target/min-context integration, effective-launch observation, bounded recovery, hysteresis, and no-settings-mutation behavior | 4 | mandatory adaptive inference | No |
| 6 | Capability-gated host cache/checkpoint/per-slot work and multi-device plans; reranker identity recovery and bounded batch experiment; ship individual controls only when their evidence gates pass | 3, 4, 5 | conditional measured optimization | No |
| 7 | Services-owned normalized recommendation tables in `experience.db`, rule registry, deterministic evidence compatibility/freshness, deduplication/dismissal, pending apply/reconcile/undo records, target projections | 1, 2, R31 experience | mandatory guidance spine | No |
| 8 | Recommendation review cards, stale-guarded Apply/Undo, adaptive-result proposal, model guidance annotations, and consistent Services/Models/Lab/Benchmarks/Doctor links | 5, 6 as available, 7 | mandatory explicit decision layer | No |
| 9 | Memory assertion/revision schema and sole production mutation authority, lazy legacy projection, correction/update/dispute/explicit-restore decisions, hard-delete dependency handling and plaintext/prior-copy truth, timeline UI, current/as-of/history retrieval | R31 provenance | mandatory temporal spine | No |
| 10 | Stable watched-root/source identity, staged source revisions and dataset generations, embedding cardinality/dimension validation, source revalidation, atomic RAG publication, exact-revision citations, crash/cancellation preservation, Dataset Manager history | 9 lineage contract, existing RAG | mandatory temporal correctness | No |
| 11 | Pinned Hugging Face thumbnail metadata, exact-host manual redirects, bounded pre-decode header inspection/cache service, selected repo/download/installed-card presentation, cache management | 2 | mandatory independent | No |
| 12 | Only the already-selected targeted hardening from doc 06 that was not naturally completed by its owning batch; cross-batch integration tests; authoritative feature/workflow/security/privacy docs; CHANGELOG only for landed behavior | 1-11 changed batches | mandatory close-out, not a catch-all | No |
| 13 | Full automated gates, canonical coverage, public diff/security/privacy audit, Linux/COSMIC and Windows live matrix, deferred ledger close-out | all landed batches | mandatory release-readiness evidence | No |

Batch 0 may mark a runtime spelling/capability/observation unavailable or
Unknown. It does not choose a different launch precedence, settings migration,
admission owner, persistence store, or revision authority. If primary evidence
actually contradicts one of those fixed contracts, stop and amend the planning
pack explicitly before implementation.

### 8.2.1 Batch 0 evidence

Observed 2026-08-31 on the managed Linux assets available to the review host:

| Asset | Version identity | Server SHA256 | Help and launch result |
| --- | --- | --- | --- |
| Linux b10688 | `0.3.0-dev`, build `10688`, commit `c589f0ed1`, GNU 11.4.0, Linux x86_64 | `03bb2c08cac030cf8ed03df451fff94ee4424e910c362a8bf2a2371a84c8aa34` | 59,017-byte help; `0`, `auto`, `all`, and `17` accepted in the controlled server probe |
| Linux b10690 | `0.3.0-dev`, build `10690`, commit `bdf395515`, GNU 11.4.0, Linux x86_64 | `23ec6a3727260d051573d8fce9baa57829e47c19545bfd0abf5d90cf3c0b628b` | Byte-identical help to b10688; `0`, `auto`, `all`, and `17` accepted in the controlled server probe |
| Windows managed runtime | Not installed or mounted in the inspected environment | Unknown | Windows-specific facts remain Unknown, not inferred from Linux |

Both Linux server help responses advertise `--n-gpu-layers` with exact numeric,
`auto`, and `all` values; `--fit` defaults to on; `--fit-target`, `--fit-ctx`,
device listing, device selection, layer/row/tensor split syntax, tensor split,
main GPU, KV offload, host cache RAM, context checkpoints, checkpoint minimum
step, unified KV, per-slot unified KV, idle-slot cache, and slot-cache output.
The installed `llama-fit-params` companions print matching placement and fit
help, plus `--fit-print`, but lack the executable permission in both asset
trees, so they were inspected through the ELF loader rather than treated as
normal managed launch executables.
Both helper files are 15,968 bytes with mode `0664` and SHA256
`b2ad073a0c093706e5dfdfe2c9864d199ed921e49ea0383a7d0013e2ad8a81c8`; their
loader-invoked version output matches the corresponding server build.

The controlled probes used the pinned embedding GGUF with `--device none`.
Each placement value reached a healthy loopback server and clean shutdown on
both Linux builds. The b10690 Auto fit-on probe with a 512 MiB target and 256
minimum fit context also reached a healthy server, with a 1,024 context slot.
No fit adjustment was proven because the host reported no available devices.
`--list-devices` reported `(none)` for both builds. `/props` and `/slots`
reported build identity, context, and slot state, but did not expose effective
GPU-layer placement, fit changes, device ids, split mode, or tensor split.
Therefore syntax capabilities are Available, while effective placement and
fit-result reporting remain Unknown.

The pinned reranker asset has SHA256
`b232c2eeedd97a593edc177e3ce4cbd1d6c8f6d8f61a5c201cd0cdeb8134da18` and its
graph has dynamic `batch_size` dimensions for `input_ids`, `attention_mask`,
and `token_type_ids`, with `logits` shaped `[batch_size, 1]`. Genuine graph
batch support is proven. The current reranker still scores candidates one at
a time; batching remains the separate Batch 6 experiment and is not part of
Batch 0.

### 8.2.2 Batch 1 evidence

Batch 1 landed the versioned `GpuPlacementIntent` shape with `Cpu`, `Auto`,
`All`, and `Exact(N)` kinds. Legacy `GpuLayers` values map as `0 -> Cpu`,
`-1 -> All`, and positive values to `Exact(N)`. Values below `-1`, malformed
typed intents, and unsupported intent schema versions remain invalid and
refuse launch with a repair error. A missing typed field therefore remains
CPU, preserving the old deserialization meaning rather than becoming Auto.

Settings load materializes the typed intent in memory without rewriting the
file. The next ordinary save writes `GpuPlacement` and omits legacy
`GpuLayers`. Tune profiles receive the same typed mapping and remain evidence;
ordinary Start and model selection no longer copy profile values into the
editor or saved server configuration.

The managed argument builder now consumes the single typed intent. CPU emits
explicit fit-off plus zero layers, Auto emits fit-on with placement unset, and
All or Exact emit fit-off plus the proven explicit placement form. Core
`ExtraArgs` aliases for host, port, context, threads, slots, fit, and placement
are removed when they agree and refuse launch when they conflict. Loopback
host ownership is therefore not bypassable through the escape hatch.

`ConfigurationIdentityFactory` is shared by managed telemetry, Lab, and
benchmark projections. It includes the placement schema/value, includes
recognized non-core extras as hashed identity inputs, and retains bounded
unknown extra hashes while marking identity `Incomplete`. The installed Linux
runtime facts from Batch 0 prove the syntax needed by these forms, but the
host still reports no devices and `/props` exposes no effective placement or
fit result. Effective runtime placement and fit outcome therefore remain
Unknown for the Batch 1 live gate. Windows remains Unknown because no managed
Windows runtime is installed or mounted in the review environment.

Batch 1 verification: focused launch, migration, profile, and identity tests
passed (37); the full sequential suite passed with 2,367 passed and 17
skipped; the zero-warning solution build passed. Test results were written
outside the checkout.

The first pushed Batch 1 run exposed four Windows-only lifecycle failures:
the new fail-closed runtime preflight correctly rejected the tests' deliberate
`where.exe` fake runtime. The production probe remains the default; the
Windows fixtures now inject explicit test capability facts so they exercise
process lifecycle behavior without claiming that `where.exe` is llama-server.

### 8.2.3 Batch 2 evidence

`ModelInventoryService` now owns the model-page local inventory. Its bounded
scan retains at most 2,048 validated chat GGUF paths, keeps a deterministic
lexical top set plus an overflow flag, rejects reparse-point paths, and reports
the cap in the Models status text. It re-enumerates file identity (canonical
path, size, and UTC mtime) so add, delete, move, and replacement changes are
seen without a filesystem watcher. GGUF metadata is cached by that identity;
manifest attachment is cached with the snapshot. Explicit invalidation is
used by model link, update, delete, companion, and refresh paths, while
unchanged identities reuse the bounded snapshot.

The Models card now uses the versioned `ModelFitPredictor` for local GGUFs
with readable shape metadata, including its component and Unknown handling.
Remote/provider and metadata-unavailable cards use the same predictor's
clearly labelled `Pre-download estimate` projection. `ModelFitEstimator`
remains only as a compatibility facade, and its size-only calculation delegates
to that predictor. The setup wizard and Hugging Face file cards use the same
label for their pre-download projection, so a rough estimate is not presented
as a detailed prediction.

Batch 2 verification: bounded inventory, invalidation, manifest refresh,
fit-facade delegation, and model-page regression tests passed (47 focused);
the full sequential suite passed with 2,372 passed and 17 skipped out of
2,389 tests; the zero-warning solution build passed; and the pushed Batch 2
run `33358704564` passed on Ubuntu and Windows. Test results were written
outside the checkout. Batch 2 is Landed.

### 8.2.4 CI batch evidence

The required `build-and-test (ubuntu-latest)` and
`build-and-test (windows-latest)` jobs now exist only in `ci.yml` for `main`
pushes and `main` pull requests. Development pushes matching `r*/round` use
the separate `branch-ci.yml`: a read-only GitHub API gate checks for an open
same-repository pull request with the exact branch and `main` base, and only
when none exists does the distinct `branch-build-and-test` matrix run. The
trusted Windows Defender exclusion remains limited to push workflows.

Branch, pull-request, and main authorities have separate concurrency groups.
Branch runs cancel superseded pushes on the same ref; pull-request runs cancel
superseded updates for that PR; main runs are never cancelled by unrelated
main activity. The branch matrix never emits either required check name, so a
skipped non-required branch job cannot satisfy protection accidentally.

Static workflow guards pass (5 tests), and the live ruleset was re-read before
editing: the required check names and pull-request requirement remain as
documented. The owner-authorized branch-without-PR exercise `33359135717`
passed its lookup gate and Linux and Windows branch matrices. A required-check
attachment exercise against a draft pull request was not performed because
this round is not authorized to open or modify a pull request. The CI row
therefore remains `No` pending that owner-only live gate, even though the
workflow implementation and branch-push exercise passed.

The pre-change baseline was a zero-warning solution build, 2,354 passed and
17 skipped out of 2,371 sequential tests, and 38,550 of 60,422 covered lines
(63.80%). Raw runtime, test, and coverage outputs were kept outside the
checkout.

The CI batch re-reads the live ruleset but does not modify it. A skipped job
must never carry either required check name. Branch and PR concurrency groups
are separate so a non-authoritative push cannot cancel an authoritative PR
merge-context run.

### 8.2.5 Batch 3 evidence

The Services-owned resource registry now keeps immutable consumer descriptors
separate from mutable allocation state. Owners are explicitly classified as
Hermaeus-owned processes, Hermaeus in-process components, configured external
endpoints, or unrelated/unknown device use. Consumer ids cannot be reused for
a different owner, allocations require registration, component ids are unique,
and lifecycle updates preserve the allocation owner and refuse terminal-state
resurrection.

Snapshots defensively copy bounded consumers, allocations, observations,
Unknowns, and device totals. Consumer and allocation observations are selected
by the fixed trust order without summing corroborating evidence; whole-device
totals remain device-scoped and cannot be attributed to a consumer. The
reranker, Whisper, and native Kokoro adapters report only resident sessions,
and loaded in-process sessions retain explicit Unknown byte usage until a
trustworthy measurement exists.

`SqliteResourceSnapshotStore` persists only an explicit, path-free projection
of a snapshot in `experience.db`, with stable identity references and a
32-snapshot retention bound. It does not record continuous sampling telemetry.
The `adaptive-launch` empirical domain and codec are registered for the later
launch-result writer; Batch 3 does not add admission, reservations, planning,
or automatic eviction.

Batch 3 verification: resource model, registry, adapters, observation authority,
lifecycle, persistence projection, cancellation, and SQLite retention tests
passed (23 focused); the full sequential suite passed with 2,400 passed and 17
skipped out of 2,417 tests; and the zero-warning solution build passed. Test
results were written outside the checkout.

### 8.2.6 Batch 4 evidence

`ResourceCoordinator` now captures a fresh immutable snapshot for each plan or
acquire decision, applies explicit device and system headroom policy, retains
negative headroom as `DoesNotFit`, and reports missing attribution or capacity
as `Unknown`. Short-lived reservations are serialized, expire, honor
cancellation, and release on owner failure or completion. They do not stop,
unload, kill, or rewrite another consumer.

Production composition passes admission through the managed server owner,
the injected Lab managed-runtime factory, local XTTS and Python Kokoro process
owners, in-process reranker, Whisper, and native Kokoro ONNX owners, and the
Memory and Recall embedding backfill owners. The Services and System Overview
receipts show feasibility, headroom, active allocations, device totals, and
Unknown reasons without persisting raw paths or attributing whole-device totals
to a process. Adapter discovery and lifecycle publication use one allocation
identity, so a concurrent adapter read is not false-positive duplicate state.

Batch 4 verification: focused admission and registry tests passed (31); the
full sequential suite passed with 2,408 passed and 17 skipped out of 2,425
tests; the zero-warning solution build passed; and version bump CI run
`33360884985` passed on Ubuntu and Windows. Required PR-check attachment and
the Linux/COSMIC and Windows owner live resource-pressure gates remain
operational follow-up, not simulated evidence.

Batch 6 is not one all-or-nothing umbrella. Cache/checkpoints, multi-device,
and reranker batching each require their own acceptance result. An honest
Unknown or measured no-benefit result closes the investigation but does not
claim the control/optimization shipped.

Batch 12 cannot absorb newly discovered subsystems or unfinished contracts.
Failure, cancellation, migration, and rollback behavior lands with the owning
batch. A new finding is fixed there when it is the same touched defect class,
recorded in `deferred.md` when worthwhile but unrelated, or explicitly rejected
when unsupported.

## 8.3 Commit and review boundaries

No commit is authorized by this planning task. If the owner later authorizes
implementation and commits:

- planning docs are one coherent documentation commit;
- the CI workflow batch stands alone and preserves required check names;
- Batch 1 stands alone because launch/security semantics need a narrow review;
- Batch 2 may split model inventory from fit convergence if each remains
  buildable and behavior/docs are truthful;
- Batches 3-5 split registry/snapshot, planner/reservation, and adaptive launch;
- Batch 6 splits by cache, multi-device, and reranker evidence family;
- Batches 7-8 split recommendation contract/derivation from UI/apply behavior;
- Batches 9-10 split memory revision migration from RAG revision publication;
- Batch 11 keeps fetch/cache security separate from card layout if useful;
- no push-sized batch mixes resource planning, temporal migration, and artwork
  merely because all are R32.

Before any authorized push-sized batch, inspect its complete diff, run focused
tests, zero-warning build, the required full-suite boundary, documentation
review, and secret/PII/local-path scan. Do not use `git add -A`.

Each batch is an atomic continuation unit: schema/migration, production writer
cutover, failure cleanup, focused tests, and truthful docs land together. Do
not mark a row Landed when only a schema, UI shell, or happy-path service exists.
If interrupted, leave the row No, record the exact incomplete boundary in the
working handoff, and resume that batch before dependent work.

## 8.4 Automated test budget

Expected new or materially expanded distinct tests: **180-235**, chosen for
behavior and risk rather than to hit a count. The area allocations below total
235-300 because one scenario often protects several contracts and is counted
in each affected area for planning.

| Area | Expected tests |
| --- | ---: |
| Launch semantics, canonical arguments, effective identity | 20-25 |
| Model inventory and fit convergence | 15-20 |
| Consumer registry, observations, snapshots | 20-25 |
| Workload composition, reservations, admission | 25-30 |
| Adaptive envelope, fit, recovery, hysteresis | 30-35 |
| Cache/multi-device/reranker conditional work | 15-25 |
| Recommendation record, rules, review/apply/undo | 30-40 |
| Memory assertion revisions and temporal retrieval | 30-35 |
| RAG source revision publication/citations | 20-25 |
| Hugging Face artwork security/cache/UI | 20-25 |
| Guards, redaction, docs, and targeted audit regressions | 10-15 |
| CI event/name/gate/concurrency contract | 8-12 |

Ranges overlap where one test protects several contracts. Do not manufacture
redundant cases to reach the sum.

Focused tests run during each batch. Run the full sequential suite and
zero-warning solution build after CI, Batches 2, 5, 8, 10, 11, and every push-sized
boundary:

```bash
dotnet build Hermaeus.sln
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj \
  --results-directory "${TMPDIR:-/tmp}/hermaeus-r32-tests"
```

Use the documented host environment when restore/audit metadata or VSTest IPC
requires it. Do not perform a known-doomed restricted-runner attempt. Keep the
suite sequential, register harness cases, and use `[WindowsOnlyFact]` rather
than a fake early-return pass.

Run `./scripts/coverage.sh` after Batch 12 with results outside the checkout.
Use the report to find missing changed/error paths; do not add coverage padding.

## 8.5 Live-verification schedule

Record exact runtime/model/hardware/effective configuration identities and
whether evidence is automated, live, or Unknown.

| Boundary | Linux/COSMIC and/or Windows evidence |
| --- | --- |
| After CI | owner-authorized branch-without-PR, same-repository PR, fork/external PR where available, and main event show the intended distinct names/authority; superseded same-ref run cancels without cross-authority cancellation |
| After 1 | current managed CUDA/Vulkan/Windows binaries: explicit CPU, Auto accelerated selection, all/exact layers, core ExtraArgs conflict refusal, loopback/CORS preservation |
| After 2 | large local model tree refresh, update/move/delete invalidation, card estimate versus detailed prediction consistency |
| After 4 | Chat plus GPU embeddings plus one in-process consumer, external GPU pressure, cancellation/failure reservation cleanup; Windows process VRAM stays Unknown if appropriate |
| After 5 | constrained-memory adaptive start with configured/planned/effective/observed receipt, one bounded recovery, no orphan, no settings mutation |
| After 6 | repeated-prefix cache/checkpoint run if shipped; real multi-device placement only where hardware exists; reranker batch equivalence/latency/memory if supported |
| After 8 | one Lab-backed runtime recommendation Apply/Undo, one declined model recommendation, one stale proposal refusal, no implicit restart/model switch |
| After 9 | memory revision/diff/current/as-of flow, rejected contradiction, hard delete after restart/export |
| After 10 | changed watched source, exact old/new citation revision, cancelled/failed reindex preserving prior complete version |
| After 11 | valid/missing/blocked/malformed artwork, offline cache, clear, high DPI, narrow window, keyboard/accessibility |

The owner's older Z97/DDR3/GTX 1660 Super Linux host is a valuable constrained
whole-workload target. It is not evidence for multi-device behavior. If no
appropriate two-device target is available, multi-device execution remains
Unknown rather than simulated success.

## 8.6 Descope order

If R32 exceeds a sensible round, move work in this order and update this pack
plus `deferred.md`. Nothing silently disappears.

1. reranker batching if the pinned graph/measurement has not earned it;
2. multi-device execution UI beyond capability/inventory/plan representation;
3. context checkpoint/cache controls beyond accounting and Lab investigation;
4. installed-card artwork reuse; keep selected repository/download-card art;
5. model recommendation annotations in Agent child selectors; keep explicit
   model recommendation review elsewhere;
6. adaptive hysteresis beyond preferring the last compatible successful launch;
7. AsOf injection into ordinary Chat; keep Memory timeline and explicit AsOf
   inspection query;
8. automatic cache LRU sophistication beyond hard byte/count bounds and Clear.

--- do not descope below this line ---

9. explicit CPU and effective-launch semantic correctness;
10. canonical security/identity-sensitive launch arguments;
11. one shared model/fit truth for R32 consumers;
12. resource inventory, Unknown handling, and reservation race prevention;
13. adaptive envelope, bounded recovery, and configured/effective receipts;
14. recommendation evidence/staleness and explicit Apply authority;
15. atomic memory/RAG revision publication and hard-delete integrity;
16. artwork host/content/cache security if any artwork is displayed;
17. automated, privacy, public-diff, and owner live gates.

## 8.7 Architectural collision resolutions

- **Old card estimator versus R31 fit predictor:** one calculation foundation,
  different labelled projections. No second workload formula.
- **Server config versus ExtraArgs:** typed security/identity fields own their
  arguments. Known expert extras are canonicalized; unknowns make identity
  incomplete.
- **Several identity constructors:** one effective-configuration factory in
  Services, consumed by Lab/Benchmarks/ViewModels through Core shapes.
- **Resource coordinator versus subsystem ownership:** subsystems register
  descriptors/providers and retain lifecycle control. Coordinator plans and
  reserves; it does not become another Doctor-style god service.
- **In-process ONNX resources versus cross-project references:** Core contracts
  plus adapters in Rag/Voice/Composition. Do not pull ONNX types into Services
  or Core.
- **Upstream fit versus whole workload:** Hermaeus determines the available
  envelope; runtime fit solves backend placement inside it. Keep both receipts.
- **Experience versus recommendations:** evidence stays in its authoritative
  store; recommendation rows reference it and never change safety authority.
- **Memory history versus RAG history:** share lineage vocabulary, not a database
  or generic entity graph.
- **RAG replacement versus cancellation:** stage complete revision, then one
  atomic current-pointer publication. Never delete current first.
- **Temporal history versus privacy deletion:** lineage retains evidence only
  until explicit hard deletion, which removes content-bearing dependents.
- **Artwork versus generic web loading:** a strict decoration-specific fetcher,
  not the RAG web loader or unrestricted `ModelDownloadService`.
- **Model inventory versus filesystem watchers:** explicit invalidation plus
  cheap identity verification first. No watcher complexity without evidence.

## 8.8 Public-repository security/privacy gate

Before any authorized push-sized batch:

1. inspect `git status --untracked-files=all`, the full staged diff, and commit
   range;
2. scan for credentials, tokens, private/local paths, usernames/hostnames,
   logs, TRX/coverage, model data, images fetched during tests, dumps, and temp
   artifacts;
3. verify process launches use `ArgumentList`, loopback/CORS cannot be
   overridden, and owned cleanup never becomes a broad process kill;
4. verify snapshots/experience omit unrelated process names and raw command or
   environment data;
5. verify plaintext-at-rest disclosure, active-store hard deletion, prior
   export/backup limits, and source-snippet removal across every new table; do
   not claim encryption or physical erasure that is not implemented;
6. verify artwork host/redirect/content/decode/cache limits and absence of
   credentials;
7. verify SQLite changes are additive, transactional, parameterized, and data-
   root aware;
8. verify approval, workspace policy, risk classification, token scopes,
   fingerprints, SHA256 checks, secret storage, and destructive confirmations
   are unchanged or stricter;
9. run focused tests, zero-warning build, required full suite, `git diff --check`,
   and applicable live checklist;
10. update authoritative docs only for behavior that actually landed.

## 8.9 Explicit rejections

- no opaque automatic model/profile selection or specialist routing;
- no model replacement, provider switch, remote fallback, or accelerated-Auto
  to CPU downgrade inside an adaptive envelope;
- no automatic process kill, model unload, or ONNX eviction without a separate
  evidence-backed user policy;
- no adaptive mutation of saved settings or a loaded server;
- no infinite retry, brute-force Cartesian tuning, or startup benchmark suite;
- no universal performance/quality/resource score or numeric confidence;
- no recommendation input to Agent safety, approval, workspace, API scope, or
  destructive confirmation;
- no autonomous fine-tuning, LoRA/adapters, or continuous training;
- no automatic contradiction resolution, newer-means-true rule, or model-
  authored current-state decision;
- no GraphRAG, arbitrary multi-hop traversal, graph UI, AST graph, or whole-
  workspace knowledge graph;
- no raw temporal/recommendation JSON editor;
- no retention of explicitly deleted knowledge content in hidden history;
- no arbitrary external thumbnail URL, SVG, animation, startup artwork fetch,
  or secret-bearing media request;
- no generic ViewModel/service rewrite, process hierarchy, migration project,
  package upgrade, or coverage padding under R32;
- no new NuGet dependency without written proof that existing BCL/Avalonia/
  SQLite/ONNX facilities are inadequate;
- no test parallelization, fake platform pass, test artifact in the checkout,
  broad process kill, permanent development branch, unauthorized commit/push,
  tag, release, merge, or repository-setting change.

## 8.10 Documentation obligations

As behavior lands, update at minimum:

- `docs/features.md` and `docs/user-guide.md` for all visible workflows;
- `docs/llama-cpp-features.md` for launch semantics, fit, cache, devices, and
  capability state;
- `docs/benchmarks.md` and the Lab workflow docs for recommendation evidence and
  any measured cache/reranker work;
- `docs/rag.md` for atomic source revisions, citations, and Dataset Manager;
- the memory/Recall sections of `docs/features.md` and `docs/user-guide.md` for
  revision timeline/current/as-of behavior;
- `docs/security-review.md` plus the relevant feature/workflow documentation
  for resource observations, temporal retention/deletion, and artwork
  network/cache policy;
- `docs/testing.md` for any new platform/live fixture and timing seams;
- `CHANGELOG.md` only for behavior that has landed;
- `docs/review/deferred.md` at close-out with exact selected/Unknown/closed
  dispositions.

Do not edit archived review plans to pretend they predicted R32, and do not
document planned behavior as current product behavior.
