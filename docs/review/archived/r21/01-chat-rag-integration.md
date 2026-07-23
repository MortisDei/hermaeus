# 01. Chat-RAG integration

Goal: a conversation can have one RAG dataset attached; every send against
that conversation runs retrieval over the dataset and injects a bounded
context block into the prompt, with the retrieved chunks surfaced as the
individually clickable citation pills chat already knows how to render.

Chat stays chat. No grounded-answer refusal semantics, no RAG prompt
template replacing the chat prompt, no change to how the model streams.
Retrieval only adds context; it never blocks, rewrites, or vetoes a send.

## 1.1 Persist the attachment: `Conversation.RagDatasetId`

- Add `public string RagDatasetId { get; set; } = string.Empty;` to
  `Conversation` (src/Hermaeus.Core/Models/Conversation.cs). Empty string
  means "no dataset attached" and is the default for every existing and new
  conversation.
- `ConversationStore` (src/Hermaeus.Services/ConversationStore.cs): bump
  `SchemaVersion` from 1 to 2 and add a v2 migration through the existing
  `SqliteMigrationRunner` pattern that runs
  `EnsureColumnAsync(db, "rag_dataset_id", "TEXT NOT NULL DEFAULT ''", token)`.
  Follow the exact shape of the v1 migration at ConversationStore.cs:65-76.
  Wire the column into every read and write path in the store (the INSERT OR
  REPLACE, the row-to-model mapping, and any partial-update statements that
  rewrite whole rows). Do NOT add it to the FTS table; a dataset id is not
  searchable text.
- Verify with a round-trip test: save a conversation with a dataset id,
  reload, assert it survives; and load a conversation row created before the
  column existed (insert via raw SQL without the column list including it)
  and assert it reads back as empty string, not null.

## 1.2 Chat header dataset picker

Owner-facing name: **Knowledge**. UI copy uses "Knowledge" / "Knowledge
dataset"; code uses Rag* names.

- `ChatViewModel` gains:
  - `UiBoundCollection<RagDataset> AvailableRagDatasets` (or a small
    item VM if `RagDataset` binds poorly; prefer binding the model
    directly, it is already used by `RagViewModel`).
  - `RagDataset? SelectedRagDataset` observable. Setting it writes
    `RagDatasetId` onto the current conversation and saves through the same
    debounced/immediate save path conversation metadata already uses (match
    how `ModelId`/`SystemPrompt` changes persist; if those save immediately,
    save immediately).
  - A `RefreshRagDatasetsCommand` that calls
    `RagQueryService.GetDatasetsAsync()` and repopulates the collection.
    ChatViewModel therefore takes a `RagQueryService` constructor dependency
    (ViewModels already reference Hermaeus.Rag; `AgentViewModel` does exactly
    this at AgentViewModel.cs:339).
  - A "None" sentinel entry so the user can detach. Represent "None" however
    the existing model-picker handles its empty case; do not use null as a
    ComboBox item.
- View (`ChatView.axaml` header area, next to the model selector and the
  "T" sampling button): a compact button labeled with the attached dataset
  name (or "Knowledge" when none) opening a flyout with the dataset list.
  Refresh the list every time the flyout opens (this is the picker's only
  refresh mechanism; no event plumbing - see doc 03.1). Each entry shows
  name and chunk count. Selecting an entry closes the flyout.
- Switching conversations must load that conversation's `RagDatasetId` and
  resolve it against the available datasets; an id that no longer resolves
  is handled per doc 03.2.
- The picker is visible whenever the RAG subsystem is available in the nav
  (same gating as the RAG view itself). It is NOT gated on
  `RagSettings.Enabled` having any particular value beyond what the RAG
  panel already requires; attaching a dataset expresses intent by itself.

## 1.3 Per-send retrieval injection: `BuildRagInjectionAsync`

Mirror `BuildMemoryInjectionAsync` (ChatViewModel.cs:1177) exactly in
shape: private method on `ChatViewModel`, returns the context text, the
`SourceReference` list, and timing, and is called from the same place in
the send path where memory injection is composed into the system prompt.

Contract:

- Runs only when the current conversation has a non-empty `RagDatasetId`
  that resolves to a known dataset, and the user message text is non-blank.
- Calls `RagQueryService.RetrieveAsync(datasetId, question, opts, ct)` with
  `RagQueryOptions(TopK: 5, UseParentChild: dataset.Config.UseParentChild)`.
  Read `UseParentChild` from the dataset's own config
  (src/Hermaeus.Rag/Models/RagDataset.cs:32), not from a new setting; the
  dataset was chunked one way and must be queried that way.
- Weak-retrieval honesty: compute
  `RagQueryService.WouldRefuse(retrieval.SemanticCandidates, retrieval.Bm25Candidates, threshold)`
  (the list overload at RagQueryService.cs:658, default threshold 0.35f).
  If true, inject NOTHING for this turn: no context block, no pills. Record
  why in the chat trace (see 1.5). Chat must not parrot weakly-related
  chunks into every unrelated question ("thanks!", "write me a poem") just
  because a dataset is attached. This check is the entire reason attaching
  a dataset does not degrade normal conversation.
- Context block: build from `retrieval.Selected` via the shared packer (see
  1.4), bounded by the new `RagSettings.ChatInjectionTokenBudget` (see 1.6).
  Prepend a header the model can use, in the same markdown-block style as
  `MemoryInjectionService.BuildMemoryContext`:

  ```
  ---
  ## Knowledge Context (dataset: <name>)
  The following excerpts were retrieved from the user's local documents
  because they appear relevant to the message. Treat them as reference
  material; if they do not answer the question, say so rather than
  guessing. Cite excerpts as [1], [2], ... when you rely on them.

  [1] <source file name> - <heading/page if present>
  <chunk content>

  [2] ...
  ```

- Sources: one `SourceReference(ProvenanceKind.Rag, title, Locator: <source path>, Snippet: <chunk content>, Timestamp: null)`
  per packed chunk, numbered in pack order so pill order matches the [n]
  markers. Title should be the source file name plus heading/page marker
  when the chunk metadata carries one (match whatever
  `RagViewModel.ApplySources` uses for display strings today). These land
  on `MessageViewModel.Sources` of the assistant reply exactly like memory
  sources do; the existing `CitationSources` split renders them
  individually (MessageControl.axaml:82). Verify a mixed turn (memories +
  RAG) renders both: collapsed memory pill plus individual citation pills.
- Best-effort: the entire method body after the guard clauses sits in
  try/catch; any exception logs one `RuntimeLogLevel.Warning` entry
  (category Rag) and returns empty. A send must complete identically to
  today when the embedding server is down, the dataset row is gone, or the
  DB is locked. See doc 02.2 for the test matrix.
- Ordering/latency: retrieval runs after memory recall in the pre-stream
  phase (sequential is fine and keeps the trace breakdown legible). Note
  the first query against a dataset warms the chunk cache
  (`WarmCacheAsync`), which loads all chunks and embeddings; that cost
  lands in this turn's RagMs and is visible in the trace, which is the
  honest place for it. No preloading on conversation open.

## 1.4 Shared context packer seam

`StreamQueryAsync` builds its context via private `BuildContext(fused, opts)`
(RagQueryService.cs:298). Chat must reuse the same packing (budget-aware,
per-source caps, packing summary), not reimplement it.

- Expose a public method on `RagQueryService`:
  `public RagContextPack BuildContextPack(IReadOnlyList<ScoredChunk> selected, RagQueryOptions opts)`
  that wraps the existing private logic (rename/delegate, do not duplicate).
  If `BuildContext` returns an internal type, promote what chat needs
  (text + summary + the per-chunk list actually packed, so pills match what
  was truly injected after budget cuts, not the pre-pack TopK).
- Chat passes `ContextTokenBudget: settings.Rag.ChatInjectionTokenBudget`
  and otherwise default packing options. Pills are created only for chunks
  that survived packing.

## 1.5 Trace and Context Inspector truth

- The chat trace record (ChatViewModel.cs:51 area): populate the existing
  dead `RagContextItems` with the packed-chunk count, and add `RagMs` (long)
  to the pre-stream timing breakdown alongside RecallMs/SelectMs/LessonMs,
  wired through wherever those are displayed in the Chat Trace Viewer. Add
  a `RagNote` string carrying: the planner notes from retrieval (embedding
  mismatch, BM25-only fallback per doc 02), or the weak-retrieval skip
  reason ("retrieval below confidence threshold; nothing injected"), or
  empty. Show it in the trace detail view when non-empty.
- Context Inspector (`BuildContextSnapshot`, ChatViewModel.cs:1273): when a
  RAG block would be injected for the current draft, it must appear as a
  `ContextPart("Knowledge", "Dataset: <name>", <block text>)`. If the
  inspector only builds parts from static state (not per-send retrieval),
  run the same `BuildRagInjectionAsync` on inspector open with a short
  timeout and show either the block or a one-line "retrieval skipped/failed:
  <reason>" part. The inspector's claim to show "the exact context pack
  before send" is the feature; do not let it silently omit the RAG block.
- The send-path token estimate (context usage bar) should include the
  injected block's tokens the same way memory injection's block is counted
  today. If memory injection is currently NOT counted in the estimate,
  leave both uncounted and note that in the final response rather than
  half-fixing it here.

## 1.6 Settings

Add to `RagSettings` (src/Hermaeus.Core/Models/RagSettings.cs), additive
JSON with safe defaults:

- `public int ChatInjectionTokenBudget { get; set; } = 2000;`

That is the only new setting. TopK stays at the pipeline default (5); the
refusal threshold stays the pipeline default (0.35f). Do not add a settings
UI row for this in this round unless one line fits naturally in the
existing Settings > RAG section; if added, label it "Chat knowledge context
budget (tokens)".

## Acceptance criteria

1. Attach a dataset, ask a question the corpus answers: the reply carries
   citation pills whose snippets are the packed chunks, the system prompt
   contained the Knowledge Context block (verifiable via Context Inspector
   and the trace), and `RagContextItems`/`RagMs` are non-zero in the trace.
2. Same conversation, send "thanks!": no pills, no block, trace shows the
   weak-retrieval skip note.
3. Detach (None): sends are byte-identical to pre-round behaviour.
4. Embedding server stopped: send still completes; one warning in runtime
   logs; trace notes the degradation (doc 02 decides BM25-only vs skip).
5. Old conversations (pre-column) load and behave as "no dataset".
6. A conversation with a dataset attached keeps it across app restarts.
