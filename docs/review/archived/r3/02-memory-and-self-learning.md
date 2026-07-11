# r3-02: Memory Improvements and the Lesson Store (Self-Learning)

Two halves. The first upgrades the memory that already exists (chat
memories, workspace memory). The second is new: a self-learning system
that records what worked and what failed, with evidence, and feeds it
back into agent context. They share machinery deliberately.

## Part 1 - Memory improvements

The r2 pass wired injection and provenance; the plumbing is sound. The
remaining weaknesses are retrieval quality, selection quality, and
lifecycle.

### M1. Hybrid recall: add embeddings next to FTS

`MemoryStore.SearchAsync` is FTS5-with-LIKE-fallback
([MemoryStore.cs:270-301](../../src/Aether.Services/MemoryStore.cs#L270-L301)).
Lexical match misses paraphrases ("what runtime does the user prefer"
vs a memory saying "likes llama.cpp"). Aether already ships an
embedding stack in `Aether.Rag`.

- Additive migration v4: `embedding BLOB` column (plus
  `embedding_model TEXT`). Embed on save when an embedding service is
  available; backfill lazily on first hybrid search (memory counts are
  small, hundreds not millions, so in-process cosine over all
  non-archived rows is fine - no vector index needed).
- Hybrid score: normalised FTS rank blended with cosine similarity;
  keep pure-FTS behaviour when no embedding model is configured so the
  default local-first install degrades gracefully.
- Constraint: ONNX/embedding types must not leak out of `Aether.Rag`;
  consume `IEmbeddingService` (already in Core) from `Aether.Services`.

### M2. Relevance-aware injection selection

`SelectMemoriesForInjectionAsync` re-sorts search candidates purely by
pinned/importance/recency ([MemoryInjectionService.cs:76-81](../../src/Aether.Services/MemoryInjectionService.cs#L76-L81)),
discarding the search relevance that produced them, under a hardcoded
500-token budget. Fixes:

- Carry the retrieval score into selection (pinned first, then a
  relevance-weighted score, not recency-dominant).
- Budget becomes `MemorySettings.InjectionTokenBudget` (default 500).
- Drop the `Task.Run` wrapper (pure sync CPU-trivial code, same in
  `MemoryExtractionService`); make both methods synchronous.

### M3. Lifecycle: recall tracking, decay, forget/update markers

Schema already has `frequency_count`, `last_merge_time`,
`expiration_date`, `is_archived`; merge-on-duplicate exists in
`ConversationMemoryService.MergeAndSaveAsync`. Missing: any feedback
from *use*.

- Migration v4 also adds `last_recalled_at TEXT`, `recall_count
  INTEGER DEFAULT 0`; bump them when a memory is actually injected.
- Effective importance = stored importance decayed by time since last
  recall (computed at read, not a background rewrite job). Archived
  automatically below a floor only after N months unrecalled; never
  silently deleted; the Memories panel shows recall stats and sorts by
  effective importance.
- Extend the marker protocol: `[MEMORY_UPDATE: <id> | <new content>]`
  and `[MEMORY_FORGET: <id>]`, valid only for ids that were injected
  into the current prompt (the injection block must therefore print
  each memory's short id). Guard rails: forget/update outside the
  injected set is ignored and logged. This closes the loop where the
  model notices a stored fact is stale but has no way to say so.

### M4. LLM-structured extraction pass (quality)

`MemoryExtractionService` categorises by keyword lists and scores
importance by string length heuristics
([MemoryExtractionService.cs:71-104](../../src/Aether.Services/MemoryExtractionService.cs#L71-L104)).
Keep the `[MEMORY: ...]` marker path as the universal fallback, but
when auto-summary already spends an LLM call
(`ConversationMemoryService.GenerateSummaryOutputAsync`), ask that call
for structured JSON (content, category, importance, tags) instead of
prose-with-markers, and parse it with the same tolerant extraction the
agent uses. One call, strictly better metadata.

### M5. Unify the two workspace memory stores

`WorkspaceMemoryStore` (SQLite via `IMemoryStore`, scope=Workspace, the
live implementation) and `FileAgentWorkspaceMemoryStore` (the legacy
per-workspace `memory.json` it replaced) both implement
`IAgentWorkspaceMemoryStore`, and tests still construct the legacy one
(e.g. `AgentWorkspaceMemoryPersistsNotesPerWorkspace`). Migrate the
tests to the SQLite store, keep only the import-from-legacy code, and
delete the legacy class. One implementation, one behaviour.

## Part 2 - The Lesson Store (agent self-learning)

The gist requested: track what works and what does not, and why;
entries can be updated, adjusted, removed. This is absolutely
buildable, and mostly from signals the agent already emits. The design
principle: **lessons are deterministic observations first, model
prose second**. Confidence comes from evidence counts, not vibes.

### Data model

New `Aether.Agent` service + store: `ILessonStore` /
`SqliteLessonStore`, SQLite db at `agent/lessons.db` under the data
root (rebuild-safe like `task_index.db`; migrations via the existing
`SqliteMigrationRunner`).

```
lesson {
  id            TEXT PK
  scope         'global' | 'workspace'
  scope_id      workspace root hash for workspace scope, '' for global
  kind          'command' | 'patch' | 'approval' | 'task' | 'stated'
  signature     TEXT     -- dedupe key, e.g. "command:dotnet test:exit!=0:CS0246"
  claim         TEXT     -- one sentence: what happened / what holds
  guidance      TEXT     -- what to do about it next time
  outcome       'worked' | 'failed' | 'user_rejected' | 'observation'
  confidence    REAL     -- 0..1
  evidence_count INTEGER
  created_at / updated_at / last_confirmed_at TEXT
  status        'active' | 'retired'
  source_json   TEXT     -- SourceReference(s): task id, trace locator
}
```

### Signal sources (all deterministic, all already flowing)

1. **Command outcomes.** Every `run_command` result has an exit code
   and output (richer once 01-F lands). Non-zero exit with a stable
   error signature (first error code / first stderr line, normalised)
   creates or reinforces a `command` lesson: claim "dotnet test fails
   in this workspace with CS0246 unless X", guidance filled initially
   with the raw signature, refined later (see below). A subsequent
   success for the same signature context records the resolution.
2. **Patch outcomes.** `apply_draft_patch` success, `baseHash` block,
   rejection with reason: `patch` lessons ("edits to src/Gen/*.cs are
   always rejected: generated code").
3. **Approval decisions.** A user rejection of a tool call or review
   item is a first-class "the user does not want this here" lesson
   (workspace scope), created from `ApprovalHistory` entries.
4. **Task terminal states.** `failed`/`blocked` with blockers snapshot
   a `task` lesson; `complete` after prior failures confirms whichever
   lessons were injected into that task (evidence up).
5. **Stated lessons.** A `[LESSON: ...]` marker in agent responses
   (exact same extraction pattern as `[MEMORY: ...]`), kind `stated`,
   starting at low confidence. This is the only model-authored source
   and is labelled as such.

### Update rules (the learning part)

- Same `signature` again: `evidence_count += 1`, confidence rises on a
  bounded curve (e.g. `1 - 1/(1+n)` capped at 0.95), `last_confirmed_at`
  updated. Never insert duplicates; the signature is the dedupe key.
- Contradiction (a `worked` lesson's command now fails, or vice versa):
  confidence drops sharply; below a floor (e.g. 0.2) status flips to
  `retired`. Retired lessons are kept for audit and can be revived by
  new evidence or the user.
- User edits win: the Lessons UI (a panel in the Agent workbench, like
  Workspace Memory) supports edit, pin (locks confidence), retire,
  delete. Every automatic mutation writes a trace row so "why does the
  agent believe this" is always answerable.
- An optional LLM refinement pass (only when the agent model is idle,
  never blocking a step) may rewrite `guidance` prose for high-evidence
  lessons; the deterministic fields (signature, counts, outcome) are
  never model-writable.

### Injection

- New `AgentContextPack.Lessons` section, budgeted through the existing
  `ContextPackBuilder` (own budget constant ~1500 tokens, same pattern
  as memory/RAG sections in `AgentContextBuilder`).
- Relevance = scope match (workspace lessons for this root + globals),
  then overlap between lesson signature/claim terms and the current
  goal + recently used tools, pinned and high-confidence first. Print
  each lesson's id, confidence, and evidence count so the model can
  weigh and reference them.
- Chat can optionally consume `global` + `stated` lessons through the
  existing memory injection path (they map naturally onto the existing
  `learned_behaviors` category), behind a Memory settings toggle,
  default off until the store has mileage.

### Guard rails

- Lessons **influence, never authorise**. The safety gate does not read
  the lesson store; no lesson can promote a disposition, pre-approve a
  command, or widen a template. (The one approval convenience,
  remembered per-task approvals, lives in 01-F and expires with the
  task; it is not lesson-driven.)
- Lesson text originates partly from model output and workspace
  content: treat it as untrusted context in prompts (same posture as
  RAG snippets), and redact via `RedactionService` before persisting
  anything that might carry secrets from command output.
- Store is per-machine, local, inside the data root, covered by the
  existing backup/restore flow.

### Why this design and not "let the model journal freely"

A freeform model journal degrades into unfalsifiable prose. Keying
lessons on deterministic signatures with evidence counts means the
store self-corrects: a wrong lesson meets contradiction and retires
itself, which is exactly the "can be updated, removed, adjusted"
requirement, automated. The user retains a manual override at every
point.

### Acceptance (store level)

- Failing command twice yields one lesson with `evidence_count = 2`,
  not two lessons.
- A contradicted lesson's confidence drops and eventually retires; new
  confirming evidence revives it.
- Injection respects budget, scope, and pinning; a retired lesson is
  never injected.
- User delete removes it; user pin freezes confidence; both traced.
- Backup/restore round-trips `lessons.db`.
