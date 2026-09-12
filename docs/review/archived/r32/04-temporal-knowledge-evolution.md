# 04. Temporal and evidence-grounded knowledge evolution

R30 added typed relationships and one-hop retrieval. R32 turns memory updates
into reviewable assertion lineage with effective time and evidence. It does not
infer an all-knowing current state, build a general graph, or let a newer model
claim automatically erase older evidence.

## 4.1 Two times and three questions

Every temporal assertion must answer:

1. When did Hermaeus record this version?
2. When did the assertion claim to be effective in the world?
3. What evidence or user decision connects it to the version before it?

`RecordedAtUtc` is direct storage history. `EffectiveFromUtc` and optional
`EffectiveToUtc` describe claimed world time and carry their own evidence
origin. If effective time is absent, show Unknown. Do not copy `CreatedAt` into
effective time and call it established.

Recency is not correctness. A later contradicted assertion can remain disputed;
an older assertion can remain current when no replacement was accepted.

## 4.2 Assertion and revision identity

Keep `Memory.Id` readable for old rows, but add explicit lineage:

```text
KnowledgeAssertionRevision(
  AssertionId,
  RevisionId,
  PreviousRevisionId?,
  Content,
  Scope,
  Category,
  RecordedAtUtc,
  EffectiveFromUtc?,
  EffectiveToUtc?,
  TemporalOrigin,
  SourceReferences[],
  Status,
  Decision?)
```

`AssertionId` is stable across revisions. `RevisionId` identifies immutable
content and metadata. Existing memories migrate lazily as one current revision
whose assertion id is the legacy memory id. Do not rewrite the database merely
to populate synthetic history.

Initial status values are `Current`, `Superseded`, `Disputed`, and `Archived`.
Deleted is not a retained status. Explicit deletion removes content according
to section 4.8.

Use an additive SQLite migration through `SqliteMigrationRunner`. Prefer
normalized revision/source/decision tables rather than embedding an unbounded
history document in `typed_relationships_json`.

## 4.3 Update and correction semantics

Current Chat markers can update an injected memory. In R32, an accepted update
creates a successor revision in one transaction:

- insert the new immutable revision;
- close the prior revision's current status;
- add the explicit lineage relation;
- record the actor/origin and review decision;
- update search/current projections atomically.

No in-place content overwrite for a temporal update. Metadata-only edits such
as pinning or tags may remain mutable preferences if they do not claim the fact
changed. The UI must distinguish `Edit presentation` from `Revise fact`.

A correction can state that an earlier revision was wrong without fabricating
an effective interval. A temporal update can state that the old fact was true
until a named time. Both preserve source evidence and decision identity.

The authority boundary is structural, not a caller convention. Replace public
content-bearing `IMemoryStore.SaveAsync` use with commands on one
`IKnowledgeRevisionStore`: `CreateAssertion`, `ReviseAssertion`,
`CorrectAssertion`, `SetDispute`, `MutatePresentation`, `RestoreRevision`, and
`HardDelete`. Each command takes the expected current revision id and fails on
concurrent change. `IMemoryStore` becomes query/read projection; any legacy
upsert adapter is internal to migration/test fixtures and cannot be resolved by
production composition.

Audit and convert every real writer: Conversation memory extraction and marker
application, duplicate merge, Memories presentation/edit actions,
`WorkspaceMemoryStore`, project/workspace capture, Agent lesson/memory bridges,
and import/restore paths. A new assertion may be accepted under the existing
writer's authority, but changing content on an existing assertion always
creates a revision. Architecture tests reject new production calls to the
legacy generic write.

## 4.4 Contradiction detection and review

R32 supports proposals, not automatic truth resolution.

A contradiction proposal contains:

- the two exact revisions;
- why they appear incompatible;
- origin `ModelInference` unless a deterministic domain rule established it;
- source/effective-time comparison;
- proposed disposition: coexist, revise, supersede from time, mark disputed,
  or no relationship;
- missing evidence.

Only bounded inputs may create proposals:

- an explicit user edit/update;
- a memory extraction proposal involving an actually injected assertion;
- a new source revision linked to the prior source identity;
- an explicit review command over selected assertions.

No background scan asks a model to reconcile the whole database. Model output
cannot set Current/Superseded or effective intervals without explicit review.

Deterministic rules remain narrow. An accepted successor revision may close its
direct predecessor. Exact duplicate content may be merged. Expiration/archive
policy may hide an assertion. Text similarity, newer timestamp, higher model
score, or source popularity cannot establish truth.

## 4.5 Current and as-of retrieval

Ordinary Chat and Memory injection retrieve current accepted revisions only,
plus directly relevant disputed evidence when the query is about the dispute.
They continue to use bounded lexical/vector retrieval and at most one recorded
relationship hop.

Add an explicit retrieval mode:

```text
KnowledgeTimeQuery(
  Mode: Current | AsOf | History,
  AsOfUtc?,
  IncludeDisputed,
  Scope,
  Limit)
```

- `Current` uses accepted current revisions.
- `AsOf` includes revisions whose established effective interval contains the
  requested time. Revisions with Unknown effective time are labelled and do
  not become precise historical facts.
- `History` is an inspection timeline, not prompt injection by default.

Context receipts name assertion/revision ids, effective interval, status,
source, and any relationship that admitted the result. The model-facing text
states uncertainty and disputes directly. It never silently flattens several
versions into one sentence.

Re-embedding follows revision identity. Superseded revision embeddings may be
retained for History/AsOf inspection but are excluded from Current search.
Embedding-model mismatch and backfill behavior remain visible.

## 4.6 Source-version lineage for RAG

Do not merge RAG documents and memories into one store. Add a small shared
lineage contract that each subsystem can project.

RAG uses explicit source and publication identities:

```text
RagSource(SourceId, DatasetId, WatchRootId?, RelativeLocator, Kind)
RagSourceRevision(RevisionId, SourceId, ContentHash, SourceEvidence,
                  EmbeddingIdentity, State, CreatedAtUtc)
RagDatasetGeneration(GenerationId, DatasetId, EmbeddingIdentity,
                     EmbeddingDimensions, State, CreatedAtUtc)
```

A watched `SourceId` is dataset id plus stable `WatchRootId` plus normalized
relative path, not an absolute path or filename. `RagWatchedSource` gains a
stable id and last-confirmed root identity. On Linux use case-sensitive path
comparison; on Windows use the platform-appropriate comparison. Every scanned
file is rechecked for containment and symlink/reparse ancestors. A missing,
replaced, or identity-Unknown mounted root produces a scan error and cannot
create a missing-source removal plan. Moving a confirmed watched root preserves
source identity; ambiguous legacy absolute paths stay legacy/Unknown until the
user confirms the mapping.

Ingest stages content, parent/child chunks, embeddings, and FTS rows under a
non-current generation/revision. Require exactly one non-empty finite embedding
per requested non-parent chunk, identical dimensions, the selected embedding
identity, complete parent references, and expected chunk counts. A short/long
batch result or mixed dimension fails staging; it is never indexed by position
or silently reduced to the majority dimension.

Immediately before publication, revalidate source identity and content hash so
a file changed during chunk/embed cannot be published under the earlier hash.
Then one SQLite transaction marks the complete source revision current, closes
its predecessor, writes the dataset generation/count/BM25 projection, and
advances the query-visible generation. Queries join only the published current
generation. Cache keys contain generation id and are invalidated after commit.

Cancellation/failure before commit deletes staging where possible and leaves
the prior pointer untouched. A bounded startup scavenger removes abandoned
staging rows after a grace period. A crash before commit exposes nothing; a
crash after commit exposes the complete new generation. Dataset-wide embedding
model/dimension reindex stages and publishes one complete dataset generation,
so mixed old/new vectors are never current. Per-source content refresh may
publish one source revision while retaining the dataset embedding identity.

The earlier revision remains inspectable until the user removes it under the
dataset retention policy.

Important boundaries:

- a changed file is not automatically a contradiction;
- filename/path alone is not portable source identity;
- a failed or cancelled reindex does not replace the last complete revision;
- deleted source handling follows the existing watched-source rule and user
  confirmation. Refresh still never invents deletion;
- citations identify the exact source revision/content hash that supplied the
  chunk;
- no cross-dataset graph, document community detection, or arbitrary traversal.

This closes the evidence gap where a citation could point to today's file while
the answer came from an older indexed body.

## 4.7 Review and inspection surfaces

Memory detail gains a timeline:

- current revision first;
- effective and recorded times shown separately;
- content diff between adjacent revisions;
- sources and evidence origins;
- accepted decision and actor;
- contradiction/dispute cards;
- actions to revise, correct, mark disputed, restore as a new revision, archive,
  or delete.

Do not present raw relationship JSON. Do not visualize a graph in R32. A linear
revision timeline plus direct relationship cards is the honest surface.

RAG Dataset Manager shows source revision, content hash/modified evidence,
index status, predecessor, and exact citation target. Chat's context receipt
uses the same identifiers in a compact projection.

## 4.8 Privacy, deletion, export, and backup

- Deleting an assertion removes all active-store revisions, FTS rows,
  embeddings, copied source snippets, and content-bearing decision/proposal
  rows in one confirmed transaction. A non-content Activity event may retain
  opaque id, action, and timestamp.
- Deleting one revision repairs lineage in the same transaction. If it deletes
  the current/successor revision, `CurrentRevisionId` becomes null and the
  assertion has no Current projection. It never promotes an older revision.
  Restoration is an explicit reviewed operation that creates a new revision
  from the selected historical content and records that decision.
- Forget markers keep their current bounded authority and become hard deletion,
  not a hidden temporal tombstone.
- A new versioned memory export includes assertion/revision/effective-time/
  source and decision structure and is bounded/redacted. Existing CSV export is
  explicitly current-projection-only until upgraded. Files exported before a
  deletion are user-owned copies and cannot be revoked; exports created after
  deletion exclude the content.
- The new tables live in the existing `memories.db` and RAG database under the
  data root, so `DataRootManifest` and SQLite online backup include them without
  a new file allowlist. Restore is still the existing whole-file backup/restore
  behavior; R32 does not claim transactional restore across several databases.
- Hermaeus does not currently encrypt memory content. `Memory.IsEncrypted` and
  `is_encrypted` are legacy dead metadata, not a security property. R32 stores
  revision content under the same plaintext-at-rest contract and updates the
  security/privacy documentation truthfully. Actual memory encryption needs a
  separately authorized key-management and migration design.
- Hard deletion guarantees removal from active logical stores and derived
  indexes. Enable and verify SQLite secure-delete/checkpoint behavior or offer
  a confirmed compaction action before claiming best-effort physical scrubbing.
  Never promise erasure from prior backups, exports, filesystem snapshots, or
  storage-device remanence.

## 4.9 Acceptance criteria

- Existing memory rows load as one current legacy revision without invented
  effective time or destructive migration.
- Revising a fact preserves the prior immutable revision and atomically changes
  the current projection.
- Every production content mutation uses revision commands; generic legacy
  upsert cannot bypass lineage.
- Correction, temporal succession, contradiction, and presentation-only edits
  remain distinct operations.
- A newer/model-authored assertion cannot automatically become Current or
  supersede accepted evidence.
- Current, AsOf, and History modes return deterministic, inspectable results;
  Unknown effective time never becomes a precise interval.
- Current Chat injection excludes superseded revisions and labels disputes.
- RAG citations bind to the exact indexed source revision; failed reindex keeps
  the last complete revision current.
- Hard deletion removes content across revisions, embeddings, sources,
  proposals, and current generated projections, without implicitly restoring
  an older revision. Prior user-created exports/backups remain explicit limits.
- Retrieval remains bounded and one-hop. No GraphRAG or whole-workspace graph
  is introduced.

## 4.10 Test and live-verification budget

Expected automated coverage: 35-45 tests.

- lazy legacy mapping and additive migration rollback;
- atomic revision, correction, dispute, archive, and hard-delete transactions;
- recorded/effective time validation, open intervals, Unknown, and timezone
  normalization;
- current/as-of/history retrieval, disputed inclusion, one-hop boundary, and
  context receipts;
- embedding backfill/mismatch and current-projection FTS behavior;
- watched-root/relative source identity, platform path comparison, containment,
  root replacement, source changes during embedding, and legacy ambiguity;
- embedding batch cardinality, finite values, mixed dimensions, and
  dataset-generation atomicity;
- contradiction proposal authority, model-inference isolation, and review;
- RAG source revision success/failure/cancel behavior and exact citations;
- plaintext-at-rest disclosure, logical/secure-delete limits, export redaction,
  backup, and data-root switching.

Owner live gates:

- revise a real memory, inspect its timeline/diff, and query current and as-of;
- review and reject one contradiction proposal, confirming nothing is hidden;
- forget a multi-revision assertion and confirm content is absent after restart
  and export;
- modify and reindex a watched source, then verify old and new citations point
  to their exact revisions;
- cancel one reindex and confirm the prior complete revision remains current.
