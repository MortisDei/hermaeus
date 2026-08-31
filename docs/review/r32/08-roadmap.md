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
| 5 | Adaptive envelope, deterministic candidates, upstream fit target/min-context integration, effective-launch observation, bounded recovery, hysteresis, and no-settings-mutation behavior | 4 | mandatory adaptive inference | Yes |
| 6 | Capability-gated host cache/checkpoint/per-slot work and multi-device plans; reranker identity recovery and bounded batch experiment; ship individual controls only when their evidence gates pass | 3, 4, 5 | conditional measured optimization | Yes |
| 7 | Services-owned normalized recommendation tables in `experience.db`, rule registry, deterministic evidence compatibility/freshness, deduplication/dismissal, pending apply/reconcile/undo records, target projections | 1, 2, R31 experience | mandatory guidance spine | Yes |
| 8 | Recommendation review cards, stale-guarded Apply/Undo, adaptive-result proposal, model guidance annotations, and consistent Services/Models/Lab/Benchmarks/Doctor links | 5, 6 as available, 7 | mandatory explicit decision layer | Yes |
| 9 | Memory assertion/revision schema and sole production mutation authority, lazy legacy projection, correction/update/dispute/explicit-restore decisions, hard-delete dependency handling and plaintext/prior-copy truth, timeline UI, current/as-of/history retrieval | R31 provenance | mandatory temporal spine | Yes |
| 10 | Stable watched-root/source identity, staged source revisions and dataset generations, embedding cardinality/dimension validation, source revalidation, atomic RAG publication, exact-revision citations, crash/cancellation preservation, Dataset Manager history | 9 lineage contract, existing RAG | mandatory temporal correctness | Yes |
| 11 | Pinned Hugging Face thumbnail metadata, exact-host manual redirects, bounded pre-decode header inspection/cache service, selected repo/download/installed-card presentation, cache management | 2 | mandatory independent | Yes |
| 12 | Only the already-selected targeted hardening from doc 06 that was not naturally completed by its owning batch; cross-batch integration tests; authoritative feature/workflow/security/privacy docs; CHANGELOG only for landed behavior | 1-11 changed batches | mandatory close-out, not a catch-all | Yes |
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

### 8.2.7 Batch 5 evidence

Managed server configuration now persists the expanded adaptive envelope with
`Fixed` as the default. `Advise` only presents a plan. `AdaptAtLaunch` builds a
maximum-eight, deterministic single-axis candidate set from configured intent,
GGUF facts, runtime help, quality evidence, and the whole-workload plan. It
does not replace a model or provider, alter network exposure, remove a
projector, increase slots, or silently fall back from configured `Auto` to CPU.

Fit target is derived only from exactly one complete known device headroom.
`--fit-target` and `--fit-ctx` are rendered only when the selected runtime's
help proves the corresponding fit controls. Otherwise the control remains
Unavailable or Unknown. The reusable scalar `/props` parser identifies itself
as `llama-props-scalar-v1`, retains exact runtime identity, and treats missing,
malformed, non-object, or unrecognised effective values as Unknown. A healthy
endpoint is not placement proof.

Every adaptive attempt uses a fresh whole-workload reservation. Resource-only
failures can advance to the next candidate after owned teardown; configuration,
runtime, port, cancellation, and Unknown outcomes cannot. Changed candidates
are transient. A recent successful launch is preferred only when runtime,
model, complete hardware, base configuration, workload identity, and bounded
evidence age all match; the new snapshot and admission checks still run. More
elaborate hysteresis beyond preferring that last compatible success remains
deferred under the documented descope order.

Batch 5 verification: focused adaptive tests passed (22); the zero-warning
solution build passed; and the full sequential suite passed with 2,430 passed,
17 skipped, and 2,447 total tests. Test results were written outside the
checkout. Installed-runtime effective placement and fit reporting remains
Unknown in the reviewed environment, so the constrained Linux/COSMIC and
Windows live adaptive-start gates remain owner validation rather than claimed
automation.

### 8.2.8 Batch 6 evidence

Batch 6 closes its conditional investigations without promoting unsupported
optimizations to normal production behavior:

- Reranker asset recovery is keyed to the resolved model and vocabulary paths
  plus bounded file identity metadata. A missing or invalid asset set no longer
  makes every later asset set unavailable for the lifetime of the process. A
  changed set releases the old ONNX allocation before loading the replacement.
- The explicit reranker batch experiment is bounded to 20 candidate pairs, a
  batch size of 2-8, and a sequence length of 64-512. It requires dynamic batch
  dimensions on all three inputs and `[batch, 1]` logits, compares scores and
  stable order within a `1e-5` tolerance, checks cancellation between work,
  and reports the exact maximum input-tensor working-set cap. It is diagnostic
  evidence only; ordinary reranking remains one invocation per candidate.
- The pinned graph's dynamic-batch shape was established in Batch 0, but the
  review host has no verified copy of the pinned ONNX asset, so this run's
  experiment result is `Unknown`. No production batch switch is claimed.
- The selected Linux help advertises host cache RAM, context checkpoints,
  per-slot cache, and related controls, but there is no repeated-prefix Lab
  benefit, restart/correctness, retention, or backup evidence. Those controls
  remain absent from the user-facing launch surface. Help advertisement is
  retained as runtime evidence only.
- Multi-device enumeration and split flags remain runtime facts. The adaptive
  planner still returns `Unknown` because no production overlay owns device
  correlation, per-device margins, and a real correctness/performance gate.
  No tensor split or other multi-device launch is emitted.

Batch 6 verification: focused reranker tests passed (3); the zero-warning
solution build passed; and the full sequential suite passed with 2,433 passed,
17 skipped, and 2,450 total tests. Test results were written outside the
checkout. The cache/checkpoint and multi-device owner hardware gates remain
open and are not represented as shipped controls.

### 8.2.9 Batch 7 evidence

Batch 7 adds the Services-owned recommendation foundation without adding an
automatic model, profile, workload, or restart decision:

- Core owns typed recommendation kind, eligibility, status, evidence,
  conditions, trade-offs, bounded path-free patches, decisions, and rollback
  records. Patch content is canonicalized and rejects secret field names and
  path values before persistence.
- Eligibility uses deterministic precedence: incomplete or missing targets are
  `Stale`, revoked evidence is `InsufficientEvidence`, contradiction is
  `Contradicted`, expired evidence is `Stale`, missing required facts are
  `InsufficientEvidence`, and only then can a proposal be `ReviewOnly` or
  `Actionable`. Required evidence state and age are evaluated at one explicit
  evidence time.
- The fixed rule registry names the seven R32 rule families and their
  derivation versions. Unknown or model-supplied rule ids are refused.
- `SqliteRecommendationStore` uses additive `experience.db` migrations with
  normalized recommendation, evidence, condition, trade-off, decision, and
  rollback tables. The uniqueness key deduplicates identical target/current/
  patch/derivation proposals, so dismissal is not recreated as a new row.
- Pending Apply, Dismiss, and Undo decisions plus bounded rollback pre-images
  survive restart as records. This batch does not apply them or reapply them
  during recovery; stale-guarded Settings ownership and UI review remain Batch
  8 work.

Batch 7 verification: focused recommendation tests passed (7); the
zero-warning solution build passed; and the full sequential suite passed with
2,440 passed, 17 skipped, and 2,457 total tests. Test results were written
outside the checkout. No owner-facing recommendation card or live Apply/Undo
flow is claimed until Batch 8.

### 8.2.10 Batch 8 evidence

Batch 8 adds the explicit decision layer over the Batch 7 recommendation
records:

- `ManagedServerRecommendationPatch` is a bounded typed adapter for persisted
  managed-server changes. It permits only reviewed scalar runtime fields and
  GPU placement intent, rejects unknown fields and paths, and never includes
  executable, model, projector, or draft-model paths.
- `RecommendationApplicationService` validates current target identity and
  Actionable eligibility before Apply, writes only a cloned `AppSettings`
  through `ISettingsService`, records pending and completed decisions, and
  leaves running processes untouched. Undo is another explicit transaction
  that refuses when the target identity changed after Apply.
- Startup reconciliation observes the current target identity against the
  pending operation's expected and post-Apply identities. It records whether
  the write landed, did not land, or became stale. It never reapplies a patch.
- Adaptive launches that produce a successful, auditable changed candidate now
  create a fresh runtime recommendation against the saved configuration.
  Planned or observed runtime values are not silently written to settings.
- Lab correctness-gated winner review uses the same recommendation path and
  exposes explicit Undo. Benchmark usage guidance creates a review-only,
  path-free model annotation whose only action is Dismiss or opening Models.
  Services, Benchmarks, Lab, and Doctor use the same review vocabulary and
  route details to the owning page.

Batch 8 verification: the focused recommendation/transaction filter passed
15 tests; the zero-warning solution build passed; and the full sequential suite
passed with 2,443 passed, 17 skipped, and 2,460 total tests. Test results were
written outside the checkout. Owner live gates remain one Lab-backed Apply and
Undo, one declined model annotation, one stale Apply refusal, and confirmation
that no recommendation restarts a server or switches a model.

### 8.2.11 Batch 9 evidence

Batch 9 moves memory content changes behind one revision authority while
retaining the existing `IMemoryStore` read projection:

- `memories.db` has an additive `knowledge-revisions` migration with normalized
  assertion, revision, source, decision, contradiction-proposal, and proposal-
  decision tables. Existing rows are assigned one `legacy:<memory-id>` current
  revision lazily, without synthetic history or a startup rewrite.
- Create, revise, correct, dispute, presentation mutation, restore-as-new,
  and hard-delete commands compare the expected current revision inside their
  transaction. Content revisions retain source and decision identity, while
  presentation changes do not create content history. Hard delete removes
  revision content, sources, decisions, proposals, FTS, embeddings, and the
  current projection without promoting an older revision.
- Current, AsOf, and History retrieval are explicit. Unknown effective time is
  not treated as a precise as-of interval. Current memory projections carry the
  exact revision identity into context-source locators; superseded and disputed
  content is excluded from ordinary injection. Contradiction proposals retain
  two exact revisions and their evidence comparison for review only. Rejecting
  a proposal records a decision and changes neither revision.
- Memories now shows a linear timeline with adjacent bounded diffs, separate
  recorded/effective times, sources, decisions, revise/correct/dispute/restore
  actions, and explicit contradiction proposal review. Export history writes
  bounded redacted versioned JSON; the existing CSV remains
  current-projection-only. Memory content remains plaintext, and prior exports,
  backups, snapshots, and physical storage remanence remain explicit deletion
  limits.

Batch 9 verification: the focused revision and Memories filters passed 50
tests; the zero-warning solution build passed; the full sequential suite passed
with 2,489 passed, 17 skipped, and 2,506 total tests; and the canonical 60%
line-coverage gate passed. Test and coverage results were written outside the
checkout. Owner live gates remain a real memory revise/timeline/current/as-of
flow, rejection of one contradiction proposal, and forget/delete followed by
restart and export verification.

### 8.2.12 Batch 10 evidence

Batch 10 publishes RAG content through source revisions and complete dataset
generations:

- Watched roots have stable platform-native identity where available, and
  source identity is dataset, watch-root, and normalized relative locator. Root
  disappearance, replacement, reparse/symlink traversal, and unknown identity
  block a missing-source plan rather than risking a destructive false positive.
- Ingest stages chunks, embeddings, full-text search, and BM25 statistics under
  a new generation. One transaction advances the query-visible current pointer
  only after exact embedding cardinality, finite non-empty vectors, consistent
  dimensions, source identity, and content-hash revalidation pass. Failure or
  cancellation leaves the prior current generation intact; stale staged rows are
  scavenged at startup.
- Revisions retain source evidence, content hash, embedding identity, and
  predecessor identity. Retrieval, chat receipts, and Dataset Manager history
  expose generation, source, revision, and content identity without storing an
  absolute owner path as the identity. Missing-source removal also publishes a
  replacement generation, so the previous query-visible set is not deleted
  first. Generation-aware cache entries cannot survive a current-pointer
  change.
- The Dataset Manager shows generation state, embedding identity, chunk count,
  and publication history. RAG citations use an exact dataset/generation/source/
  revision/content locator, and the RAG documentation describes the retained
  deletion and owner-validation limits.

Batch 10 verification: the focused RAG, watcher, lineage, chat, and stream
filters passed 94 tests; the zero-warning solution build passed with 0 warnings
and 0 errors; the full sequential suite passed with 2,496 passed, 17 skipped,
and 2,513 total tests. Test results were written outside the checkout. The
native Windows root-identity path was compiled but not executed in this Linux
run, so its runtime behavior remains an owner Windows live gate. Owner live
gates remain a changed watched source, exact old/new citation revision, and a
cancelled or failed reindex that preserves the prior complete version.

### 8.2.13 Batch 11 evidence

Batch 11 keeps Hugging Face artwork as optional decoration with independent
provenance and failure handling:

- The existing selected model-card request reads only the bounded string
  `cardData.thumbnail`. Relative repository paths and canonical resolve URLs
  must exist in the exact immutable tree revision. External hosts, unsafe
  authorities, traversal, encoded separators/dot segments, declared queries,
  and missing or malformed revisions do not start an artwork request.
- Artwork fetches use an anonymous HTTPS client with automatic redirects
  disabled, at most five manual hops, and the exact source-controlled Hub
  delivery-host set. Same-Hub redirects stay on `huggingface.co`; a delivery
  host cannot change origin. Response bodies are header-streamed and bounded at
  2 MiB, with declared MIME, magic, PNG/JPEG/WebP header, animation,
  dimension, pixel, decoded-byte, and post-Avalonia dimension checks.
- Cache entries are atomically written under the resolved Data Root cache,
  keyed by normalized repository, exact revision, source identity, and content
  hash. Cache hits are offline; metadata records MIME, size, dimensions, ETag,
  fetched time, and last access. The cache has byte/count LRU limits, a
  confirmed Data Management Clear action, and is excluded from Data Root
  backups. Installed-card reuse requires a size-consistent manifest with
  verified repo, exact revision, and SHA256 provenance. Custom avatars remain
  separate and take precedence.
- Selected repository artwork is published to its file cards through the same
  cancellable selection generation. A publication race regression replays
  already-available artwork into rows created afterward, and stale selections
  cannot replace the current repository. Search and download behavior remains
  usable when artwork fails.

Batch 11 verification: the focused artwork, client, and model-management
filters passed 80 tests, including 38 artwork and cache/integration cases; the
zero-warning solution build passed with 0 warnings and 0 errors; and the full
sequential suite passed with 2,534 passed, 17 skipped, and 2,551 total tests.
Test results were written outside the checkout. Owner live gates remain valid,
missing, blocked, and malformed thumbnail repositories, slow/offline artwork
while metadata and downloads remain usable, restart/cache-clear behavior,
high-DPI and narrow-window layout, keyboard selection, and screen-reader/
tooltip text on Linux/COSMIC and Windows.

### 8.2.14 Batch 12 evidence

Batch 12 closes only the selected audit and integration work that was not
already completed by its owning batch:

- The existing settings lifecycle guard remains the boundary for the reported
  data-root reset class. Ordinary autosave does not migrate an unconfirmed
  root, and reload/direct-save/stale-completion cases remain covered. A new
  cross-batch regression joins the configured Data Root, model manifest,
  Services-owned model inventory, and verified offline artwork cache. It proves
  an installed model can reuse its exact-revision artwork without a network
  request after inventory refresh.
- The authoritative feature, user-guide, RAG, llama.cpp, security/privacy,
  testing, workflow, and CHANGELOG documentation was reviewed against the
  landed behavior. The deferred ledger records landed R32 items as closed,
  keeps conditional cache/checkpoint and multi-device execution explicitly
  selected or Unknown, and retains the owner-only required-PR CI attachment
  gate.
- No new subsystem, dependency, settings authority, migration runner, or
  release claim was added. Batch 12 leaves platform artwork/layout,
  effective-runtime, hardware, required-PR, and final public release gates to
  the documented Batch 13 boundary.

Batch 12 verification: the focused artwork, client, and model-management
filters passed 81 tests, including 39 artwork and cache/integration cases; the
canonical coverage gate passed at the configured 60% line floor with results
outside the checkout; the zero-warning solution build passed with 0 warnings
and 0 errors; and the full sequential suite passed with 2,535 passed, 17
skipped, and 2,552 total tests. Batch 11 branch CI run `33391762243` passed on
Ubuntu and Windows.

### 8.2.15 Batch 13 readiness audit

The automated R32 release-readiness audit is complete for `0.39.0-beta`:

- The solution build passed with 0 warnings and 0 errors. The complete
  sequential suite passed with 2,535 passed, 17 skipped, and 2,552 total
  tests. The canonical 60% line-coverage gate passed with all generated
  results outside the checkout.
- The Linux `linux-x64` tarball and cross-built Windows `win-x64` ZIP both
  built as `0.39.0-beta`; each checksum verified, each archive passed its
  integrity test, required launcher/application/package files were present,
  and no PDB files were included. The Windows result proves packaging only,
  not Windows runtime or GUI behavior.
- The complete committed R32 range was checked for `git diff --check`,
  tracked test/coverage/model artifacts, credentials, private paths, raw
  owner data, em dashes in added lines, unsafe process-launch changes, and
  accidental workflow or repository-setting changes. No new violation was
  found. The Batch 12 branch CI run `33393611503` passed on Ubuntu and
  Windows.
- An isolated Linux/COSMIC `0.39.0-beta` package smoke using temporary HOME
  and XDG data reached a real Hermaeus window and initialized its stores. The
  temporary XDG root also moved `SingleInstanceGuard`, so this run deliberately
  created an unsupported second Hermaeus process beside the owner's already
  running instance. The tray-enabled temporary process then recorded
  unhandled Avalonia/Tmds.DBus failures during menu-layout or D-Bus disconnect
  handling, while the owner process remained stable. A tray-disabled retry
  initialized and recorded `CleanExit: true`. This is useful dual-instance
  harness evidence, not proof that the single-instance production package fails,
  and it does not satisfy the required artwork, high-DPI, narrow-window,
  keyboard/accessibility, hardware, or owner-data validation. It is not evidence
  of a path reset, and no production tray behavior is silently declared fixed.

Batch 13 remains `No` in the table because automated evidence cannot replace
the named owner gates. The reviewed host has no managed Windows runtime, so
Windows runtime identity, placement, fit, and GUI behavior remain Unknown.
Linux/COSMIC artwork layout, high-DPI and narrow-window behavior, keyboard and
screen-reader behavior, constrained whole-workload placement and effective
fit, compatible hardware pressure/recovery, and the required same-repository
PR merge-context check have not been owner-validated in this round. No release
tag, PR, merge, workflow dispatch, or repository-setting change was made.

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
