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
| 0 | Verify installed Linux/Windows runtime help/version, exact argument spelling and effective-placement observability against the already-fixed doc 02 contract; inspect pinned reranker graph batch dimensions; capture current build/test/coverage baseline | planning pack | mandatory environment facts | No |
| CI | Separate non-required pre-PR branch checks from required PR merge-context checks; preserve main/fork coverage and trusted-push security boundaries; add authority-scoped superseded-run cancellation without changing repository settings | live ruleset re-read, doc 09 | mandatory operational efficiency | No |
| 1 | Typed CPU/Auto/All/Exact launch intent and exact legacy/tune-profile migration; remove implicit tune-profile-on-Start mutation; canonical managed launch specification, conflict policy for core `ExtraArgs`, one configured/planned/rendered/effective/observed projection and fingerprint, versioned compatibility tests | 0 | mandatory correctness foundation | No |
| 2 | Services-owned model inventory with bounded scan/GGUF/manifest cache and explicit invalidation; converge simple card fit onto the versioned prediction engine while preserving a labelled pre-download estimate | 0, 1 | mandatory shared foundation | No |
| 3 | Resource consumer registry, allocation owner/component/per-device model, immutable snapshots, local/remote/owned identity, authoritative per-device and Unknown observations, in-process adapters, bounded persistence | 1, 2 | mandatory resource spine | No |
| 4 | Whole-workload composition, headroom, priorities, reservations, concurrency, mandatory lease-bearing admission at every production start/restart/resume/lazy-load owner, System Overview/Services receipts | 3 | mandatory resource spine | No |
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

The CI batch re-reads the live ruleset but does not modify it. A skipped job
must never carry either required check name. Branch and PR concurrency groups
are separate so a non-authoritative push cannot cancel an authoritative PR
merge-context run.

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
