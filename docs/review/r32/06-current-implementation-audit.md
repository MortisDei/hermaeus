# 06. Current implementation audit

This audit deliberately ranges beyond the named R32 directions. Findings are
based on the `b034182` source and current upstream contracts checked on
2026-08-31. A finding lands in R32 only when it is a demonstrated correctness
risk or the planned work already crosses the same seam. Large-file aesthetics
and speculative rewrites do not qualify by themselves.

## 6.1 Findings that belong in R32

### A. Managed llama.cpp launch semantics have drifted

Evidence:

- `ServerConfig.GpuLayers` documents `0` as explicit CPU.
- `ServerProcessManager.BuildLaunchArguments` implements it by omitting
  `--n-gpu-layers` and retains a test requiring the omission.
- current upstream documentation says the option defaults to `auto` and
  `--fit` defaults on.

Risk: a saved explicit CPU choice may become automatic GPU offload on a newer
runtime, and upstream fit may adjust values Hermaeus currently treats as fixed.

Disposition: mandatory R32 doc 02 and Batch 1. The precedence and migration
contract is fixed in the plan; Batch 0 only verifies how each installed binary
spells and reports that contract. Update tests away from byte identity where
upstream defaults have changed, while retaining versioned compatibility
coverage.

### A2. Tune profiles silently mutate saved launch intent

Evidence:

- every ordinary `ServicesViewModel` start applies the selected tune profile,
  copies its GPU layers/threads/context/extras into `ServerConfig`, and saves it
  before launch;
- a tune profile stores the same legacy integer GPU-layer field and is neither
  a suggestion record nor a distinct effective-launch overlay.

Risk: the visible configured value is not durable user intent, and experience,
Hermaeus adaptation, upstream fit, and effective placement cannot be audited as
separate layers.

Disposition: mandatory doc 02 migration. Preserve legacy intent exactly,
remove implicit profile application from Start, treat profiles as evidence for
an explicit review/apply action, and record configured, planned, rendered,
runtime-effective, and observed placement separately.

### B. Extra arguments can override core fields without canonical identity

Evidence:

- typed context, port, host, threads, and GPU-layer arguments are appended
  before `ExtraArgs`.
- collision handling exists for several newer engine fields, but no equivalent
  guard/parser covers core `--ctx-size`, `--n-gpu-layers`, `--threads`, `--host`,
  or `--port` values.
- configuration identities are constructed independently in Benchmark, Lab,
  and Services and preserve only parsed known extras.

Risk: configured, fingerprinted, displayed, and effective launch values can
disagree. Adaptive fitting or recommendations built over that ambiguity would
learn from the wrong configuration. Duplicate host/port arguments also weaken
the loopback and process-identity contract if runtime precedence changes.

Disposition: mandatory R32. Add one canonical launch-spec builder/parser with
typed ownership of security and identity-sensitive fields. Refuse conflicting
security fields, project recognized expert extras into the effective identity,
and mark unknown extras Incomplete. Reuse the same projection in Services,
Lab, Benchmarks, telemetry, and adaptive receipts. Remove the duplicated
`MemoryLock` conditional while touching this block.

### C. Two GPU Fit models now disagree by construction

Evidence:

- Models, setup, and Hugging Face cards use `ModelFitEstimator`, which falls
  back to a 1.2 weight multiplier and one full-offload KV calculation.
- R31 added the richer `ModelFitPredictor`, with placement, K/V separation,
  companions, runtime overhead, explicit headroom, Unknown components, and
  fingerprints.
- the simpler estimator remains the source of public `Fits GPU`, `Partial
  offload`, and `Too large` chips.

Risk: the first recommendation a user sees can contradict the detailed GPU Fit
and ignores the exact companion/workload costs R32 is meant to model.

Disposition: mandatory R32. Keep a deliberately labelled pre-download estimate
projection, but derive it from the same versioned component model with Unknown
for unavailable configuration facts. Delete the duplicate formula only after
all three callers have equivalent UI behavior and regression coverage.

### D. Model inventory and GGUF parsing are duplicated and uncached

Evidence:

- `LocalAiAssetLocator.FindGgufModels` recursively scans the model tree.
- Model Management, Services, Benchmarks, Doctor, setup, tune-profile logic,
  and server switching call equivalent scans.
- GGUF metadata is re-read independently by Models, Services, Benchmark, Lab,
  capability probing, speculative validation, and identity construction.

Risk: large model libraries repeatedly traverse storage and parse the same
headers, while different callers can observe different snapshots during file
movement/update. R32 artwork, resource plans, and recommendations would add
more consumers if this remains implicit.

Disposition: include a small Services-owned `IModelInventory` in R32. Cache
canonical path, size, mtime, manifest identity, role, and parsed GGUF facts;
invalidate on explicit refresh/download/update/move/delete and verify cheap
file identity on read. Do not add a filesystem watcher in the first batch.
Callers retain domain-specific projections.

### E. RAG replacement is not atomic and has no source revision

Evidence:

- `RagPipeline.StoreChunksAsync` saves dataset metadata, deletes every changed
  source in one store call, then writes parent and child chunks in separate
  batch transactions.
- cancellation or failure after deletion can leave the source absent or
  partially repopulated.
- citations carry source path/hash on chunks but there is no published source
  revision whose completeness can be switched atomically.

Risk: a failed reindex can destroy the last usable indexed version and citations
cannot distinguish historical indexed content from the current file.

Disposition: mandatory in the temporal R32 track. Stage a complete source
revision, validate counts/embeddings/FTS, then atomically publish it as current.
Cancellation/failure discards staging and preserves the prior complete revision.

### F. A deleted successor can leave its predecessor permanently hidden

Evidence:

- `KnowledgeRelationshipSemantics.IsSuperseded` returns true for any non-empty
  `Supersedes` memory target id.
- ordinary recall filters that predecessor before proving the target still
  exists or is current.
- `MemoryStore.DeleteAsync` deletes the selected row and FTS entry but does not
  repair incoming typed relationships.

Risk: deleting the replacement memory can make both the replacement and the
older assertion disappear from current recall. Cross-entity relationship
targets can also dangle without visible integrity state.

Disposition: mandatory R32 temporal migration and deletion semantics. Current
projection depends on valid revision lineage, and hard deletion must repair or
remove dependent content in one confirmed transaction.

### G. In-process AI resources lack a shared lifecycle/resource contract

Evidence:

- reranker, Whisper, and native Kokoro create persistent ONNX sessions inside
  singleton services.
- they expose domain-specific load/dispose behavior but no shared resource
  identity, approximate/observed memory, active/idle state, or cooperative
  release contract.
- `OnnxCrossEncoderReranker` previously made `_unavailable` sticky after missing
  assets or a load failure; the R32 Batch 6 repair now keys that state to the
  resolved model/vocabulary asset identity and releases the old allocation on
  replacement.

Risk: whole-workload fit cannot distinguish resident in-process consumers, and
a repaired or changed reranker configuration can remain unavailable for the
rest of the process.

Disposition: R32 doc 01 consumer adapters include lifecycle state and optional
explicit cooperative release. Batch 6 fixes reranker invalidation keyed to
asset identity while retaining the adapter. Automatic idle eviction remains
out until cold-start and memory evidence justify a user-controlled policy.

### H. Reranking performs one ONNX invocation per candidate

Evidence: ordinary `OnnxCrossEncoderReranker.RerankAsync` still loops over up
to 100 candidates and `ScorePair` runs an input shaped `[1, maxLength]` for
each. Batch 6 adds a separate bounded diagnostic that accepts at most 20
candidates and batch size 8.

Risk: repeated session invocation and allocation can dominate query latency and
CPU use. The current default cap is 20, so ordinary queries may pay 20 separate
runs.

Disposition: Batch 6 includes the bounded investigation. The diagnostic
requires dynamic input batch dimensions and `[batch, 1]` logits, proves
score/order equivalence, checks cancellation between bounded work units, and
reports its tensor working-set cap. It does not change the sequential
production path. The current review host has no verified pinned asset, so the
experiment remains Unknown and no optimization is claimed.

### I. Recommendation machinery is fragmented

Evidence: `EngineOptionPresets`, Auto Tune, Lab recommendation strings,
Benchmark `UsageInsight`, Doctor benchmark advice, tune-profile stale flags,
and GPU Fit comparisons each implement their own eligibility and presentation.

Risk: evidence age, compatibility, missing correctness, Apply, dismissal, and
rollback semantics differ by page. Static VRAM presets can appear as equally
credible as controlled Lab evidence.

Disposition: core R32 doc 03. Converge the receipt and rule registry, not the
underlying evidence stores or every UI into one giant service.

### J. Remote artwork needs a stricter fetch boundary than ordinary web ingest

Evidence: Hugging Face currently has fixed-host metadata/file requests, while
model-card `thumbnail` may be publisher-controlled. The RAG web loader accepts
explicit user-entered HTTP/HTTPS URLs and follows the normal HTTP client policy.

Risk: reusing a generic URL loader for decoration could contact arbitrary hosts
without an explicit URL action and expose the user's address or decoder to
hostile content.

Disposition: mandatory doc 05 allowlist, redirect, MIME/magic, byte/pixel, log,
and cache policy. Do not generalize the RAG web loader as part of this change.

### K. Resource admission is bypassable at real allocation owners

Evidence: Services and the isolated Lab host directly construct and start
`ServerProcessManager`; restart/resume paths also allocate managed processes,
while reranker, Whisper, and Kokoro construct persistent in-process sessions.

Risk: a coordinator used only by the primary Start command looks correct while
Lab, recovery, resume, or lazy-load paths allocate outside its reservation.

Disposition: mandatory docs 01-02. Put admission at each production allocation
owner, make a lease/attempt mandatory in the callable start/load contract, and
guard against public unleased entry points. UI commands are not the boundary.

### L. Generic memory writes bypass temporal revision authority

Evidence: `IMemoryStore.SaveAsync(Memory)` is a public generic upsert used by
conversation capture, workspace persistence, Memories, and Project flows.

Risk: adding revision tables beside this API would allow existing writers to
silently replace content without lineage, concurrency, or current-pointer
rules.

Disposition: mandatory doc 04. Route every production content mutation through
one expected-current revision command authority; retain generic row writes only
as internal migration/test plumbing.

### M. Current encryption and deletion surfaces cannot support stronger claims

Evidence: `Memory.IsEncrypted` and `is_encrypted` are markers, not content
encryption. Backups copy database files, and memory CSV export emits current
content. SQLite deletion does not by itself prove physical media erasure.

Risk: a temporal-history feature could imply encrypted storage, physical
scrubbing, or revocation of prior exports/backups that the product does not
provide.

Disposition: mandatory truthfulness in doc 04 and user/security documentation.
R32 provides transactional logical deletion from the active store and says the
content remains plaintext at rest. It does not add a misleading encryption
toggle or claim to revoke prior copies. Physical scrub is claimable only after
the actual SQLite/WAL/filesystem procedure is implemented and verified.

### N. Watched RAG identity and embedding publication are under-specified

Evidence: watched sources are keyed by absolute path with case-insensitive
comparison on every platform; containment uses a case-insensitive prefix; file
content is read before its timestamp and is not revalidated after embedding;
embedding batches are indexed without a cardinality/finite/same-dimension
contract.

Risk: roots with a shared string prefix can alias, Linux-distinct paths can
collide, a file changed mid-run can publish stale content as current, and a
short or inconsistent embedding response can fail late or mix an index.

Disposition: mandatory doc 04 watched-root identity, platform-aware normalized
relative source id, descendant symlink/containment validation, complete staged
generation validation, source revalidation, and one atomic publication.

### O. CI duplicates authoritative work once a same-repository PR exists

Evidence: `ci.yml` runs the same named Linux/Windows matrix on pushes to
`r*/round` and pull requests to `main`. PR #9 shows push and pull-request runs
for the same head commit. The active `main` ruleset requires those two exact
check names strictly through GitHub Actions.

Risk: every PR update pays for two equivalent matrices, yet a naive skipped job
with a required name reports success and can mask the real authority.

Disposition: mandatory R32 CI batch in doc 09. Keep distinct non-required
pre-PR branch checks; once a same-repository PR exists, run the required PR
merge-context checks only. Preserve fork PR and main coverage, and cancel only
superseded runs within the same authority/ref group.

## 6.2 Relevant opportunities that need evidence gates

### Cooperative release of dormant ONNX sessions

An explicit `Release local AI memory` action and per-consumer `CanRelease`
contract fit the resource registry. Automatic idle unload could trade memory
for severe speech/rerank cold starts and is not justified yet. Measure session
load time and memory first; ship only explicit release or an opt-in timeout with
clear trade-offs if the evidence earns it.

### Host prompt-cache and checkpoint budgeting

Current upstream exposes cache RAM, context checkpoints, idle-slot caching, and
per-slot KV controls. They can improve repeated-prefix work but consume system
RAM and add retention/privacy questions. Pull them into R32 only through Lab,
resource accounting, and capability gates as doc 02 specifies.

### Resource-aware background work

Embedding warm-up/backfill is deliberately off the startup path and serialized
inside `MemoryStore`, but it starts whenever the managed embedding service
becomes available without a shared foreground/background resource decision.
R32 can register it as Background and defer acquisition while a foreground
Lab/start action holds a conflicting reservation. Do not cancel an in-flight
database write or suppress eventual backfill.

### One effective-configuration projection

The duplicate `ConfigurationIdentityV2` construction is a strong opportunity
to make launch receipts, telemetry, Lab, Benchmarks, GPU Fit, and recommendations
agree. This is a focused abstraction earned by five existing consumers, not an
architecture-for-architecture's-sake rewrite.

## 6.3 Demonstrated debt recorded outside R32

### Large orchestration classes

`AgentService`, `AgentViewModel`, `ChatViewModel`, `ServicesViewModel`,
`ModelManagementViewModel`, `RagViewModel`, `LabViewModel`, and several test
files are very large. This increases collision and review cost, but line count
alone is not a defect. R32 must extract only the resource inventory, launch
specification, model inventory, recommendation, and temporal services it needs.
A broad ViewModel/service split belongs in the existing architecture-debt
direction with scenario coverage and its own round.

### Doctor remains a cross-subsystem aggregation hotspot

`DoctorService` partials import RAG and Voice implementation namespaces and the
Services project references both projects. Do not add adaptive planner or
temporal engine knowledge to Doctor. R32 exposes small health/recommendation
provider contracts and lets Doctor aggregate their result. A complete Doctor
modularization is useful but unrelated to delivering R32 safely.

### SQLite migration runners are duplicated across dependency boundaries

Agent, Rag, and Services each carry a near-equivalent internal migration
runner. Consolidating them into Core would either leak SQLite into Core or
require a new shared infrastructure project. The duplication is small and
stable, so no R32 action. New R32 schemas use the runner in their owning project.

### Clock-dependent tests remain operational debt

The 150 ms startup debounce assertion and MCP 5000 ms bound still exist. They
have not demonstrated flakiness and are unrelated to R32 behavior. Keep them in
the operational watch section of `deferred.md`; new R32 time behavior must use
`TimeProvider` or explicit signals from the start.

### MCP HTTP/SSE and Agent Local API execution remain prerequisite-bound

Neither becomes more correct because R32 adds experience or resource state.
They stay parked behind demonstrated transport demand and a single serialized
Agent mutation owner respectively.

## 6.4 Security and privacy audit notes

- Existing managed `llama-server` launches bind loopback and add narrowed CORS
  only when the selected runtime advertises it. Canonical launch parsing must
  prevent `ExtraArgs` from overriding those fields.
- Process launches inspected in the touched paths use `ArgumentList`; R32 must
  retain that rule for fit/device/cache flags.
- Whole-device external usage must not persist unrelated process names.
- Temporal history can multiply sensitive content. Plaintext-at-rest disclosure,
  active-store hard deletion, export/backup limits, and source-snippet cleanup
  are mandatory, not close-out polish. Encryption is not an existing capability
  and is not smuggled into R32 through a documentation claim.
- Artwork must never receive secrets or contact arbitrary publisher hosts.
- No evidence/recommendation/resource state enters Agent approval, workspace
  policy, API authorization, or destructive-action gates.

No broader credential exposure, shell-string launch, path-traversal, or unsafe
settings-write defect was established by this planning audit. That statement is
not a substitute for the per-batch public diff and privacy scans in doc 08.

## 6.5 UX audit notes

R32 should resolve these rough edges because its surfaces already change:

- replace conflicting `Fits GPU` labels with `Model fit estimate` versus
  `Whole workload plan` language;
- show configured and effective launch values together, never only the adapted
  value;
- give Unknown components equal visual weight to positive fit chips;
- use one recommendation review vocabulary instead of page-specific prose;
- keep model artwork bounded and secondary so download facts remain scannable;
- distinguish temporal `Revise fact` from presentation edits and hard delete;
- show RAG source revision and last complete index instead of letting a failed
  refresh look like an empty source;
- make resource-conflict remedies explicit actions rather than generic start
  errors.

Do not redesign navigation, themes, all cards, or all large ViewModels under
the cover of these changes.

## 6.6 Test-gap audit

R32 must add representative coverage for:

- explicit CPU semantics against current and older help contracts;
- conflicting core ExtraArgs and effective identity;
- agreement among pre-download estimate, detailed fit, and workload plan;
- model inventory invalidation during update/move/delete;
- atomic RAG source replacement under failure and cancellation;
- delete/restore behavior for superseded memory lineage;
- reranker asset-path recovery and, if implemented, batch equivalence;
- resource reservations across simultaneous foreground/background requests;
- recommendation staleness, contradictions, dismissal, Apply, and Undo;
- artwork redirect/content/decode/cache boundaries;
- temporal plaintext disclosure, deletion, as-of retrieval, and exact source
  citations;
- every allocation/restart/resume/lazy-load owner refusing an absent or stale
  admission lease;
- watched-root identity, source mutation during indexing, embedding cardinality
  and generation consistency;
- CI trigger/ruleset guards that distinguish branch and required PR checks.

The current suite has strong regression breadth and guard tests, but no test can
prove real CUDA/Vulkan/WDDM allocation, installed-runtime fit output, ONNX graph
batch behavior, desktop image layout, or OS-level memory attribution. Doc 08
keeps those as explicit owner live gates rather than faking platform passes.

## 6.7 Rejected audit-driven expansion

- no blanket refactor of every large file;
- no new infrastructure project solely to share a 70-line migration runner;
- no generic process-manager hierarchy across llama.cpp, TTS, MCP, and Local API;
- no universal resource optimizer or opaque composite score;
- no automatic ONNX eviction before measured cold-start/resource evidence;
- no general SSRF rewrite of explicitly user-entered RAG web ingestion inside
  the artwork batch;
- no GraphRAG, source-code graph, or whole-workspace entity unification;
- no test rewrites solely to improve coverage percentage;
- no opportunistic package upgrades unrelated to a demonstrated R32 need.
