# 02. Recall and the palette

This is the round. Everything else is scaffolding around it.

## The problem

Hermaeus stores four kinds of knowledge about your work and gives you four
unrelated ways to look for them:

| Store | What it holds | How you search it today |
| --- | --- | --- |
| `ConversationStore` | Every message you have ever sent or received | FTS5 over the whole conversation blob (`ConversationStore.cs:56-62`), so you find the conversation, never the message |
| `MemoryStore` | Extracted facts and preferences | Proper hybrid FTS plus cosine (`MemoryStore.cs:336-398`), the best search in the app |
| Agent task state | Goals, summaries, final answers, reservations, `report.md` | Nothing. A task index exists for rendering the queue, not for search |
| `SqliteRagStore` | Ingested documents | Excellent hybrid retrieval, but only ever within one dataset you name up front |

So "what did I decide about the KV cache settings a few weeks ago" is
currently unanswerable, even though the answer is definitely on this
machine, in one of those four stores, and the app has an embedding model
loaded. That is the gap.

And separately, the owner cannot always remember what the app can do. Both
problems have the same solution shape: **one keystroke, one box, ranked
answers.** So they get one surface.

## Design decisions, settled

**Recall is federated, not a new master index.** Two of the four stores
already have first-class semantic search. Duplicating memories and RAG
chunks into a second index would double the storage, double the embedding
cost, and guarantee the two copies drift. Recall fans out to four sources
and fuses the results. A new index is built **only** for the two stores
that have no semantic search today: conversation messages and agent tasks.

**Fusion is reciprocal rank fusion, reusing what exists.** The app already
does RRF in `src/Hermaeus.Rag/Retrieval/HybridRetriever.cs`, parameterised
by `RagDatasetConfig.HybridRrfK` (`RagDataset.cs`, default 60). Recall
fuses its four sources with the same function. Do not write a second
ranking scheme; a divergence between "how RAG ranks" and "how Recall ranks"
is the same class of bug r23 called out for glob matching.

**Recall never runs on the send path.** Indexing is a background pass
modelled directly on `MemoryStore`'s embedding backfill
(`MemoryStore.cs:515-545`): it runs shortly after startup and after writes,
processes a bounded batch, and gives up on rows that fail repeatedly so a
misconfigured embedding endpoint cannot tax every write forever. Copy that
shape, including the bounded `LIMIT 200` batch and the failure accounting.

**Recall degrades to keyword-only, honestly.** With no embedding model
configured or reachable, Recall still works as an FTS-plus-BM25 search and
says so in one line, exactly as RAG query already does
(`docs/features.md:554-559`). It never silently returns worse results
without explanation.

**Recall is read-only and local.** It reads local stores, ranks, and
navigates. It never writes to the stores it indexes, never sends anything
anywhere, and adds no outbound destination to the Privacy Audit.

## 2.0 What Recall stores, and the user's control over it

**Read this before 2.1. It is a requirement, not a caveat.**

The index in 2.1 holds a copy of the text of every indexed message and
task. That is a second copy of the user's own words, in a new file, that
lands in every backup. In an app whose entire pitch is that the user is in
charge of their data, a store like that may not be invisible and may not
be permanent-by-default. Building 2.1 through 2.5 without this item is not
an acceptable partial delivery.

Context that shapes the right answer rather than excusing it: the app
already keeps a second searchable copy of every message.
`ConversationStore.RebuildFtsAsync` (`ConversationStore.cs:98-113`) copies
the whole `messages_json` blob into the `conversations_fts` table, and that
table is already in the data root and already in every backup. Recall is a
better index of the same material, not a new category of collection. So the
default is **on**, because a flagship search feature that ships off is a
flagship search feature nobody ever finds. But it is on and **visible**,
not on and silent.

Required, all of them:

- **A visible switch.** Settings > Memory, next to the existing recall and
  lesson toggles: "Index my history for Recall", default on, with one line
  of honest copy stating in plain words that this keeps a searchable copy
  of message and task text in the data root and that it is included in
  backups. Turning it off stops indexing immediately and disables the
  recall half of the palette; the command half keeps working.
- **A destructive control that actually destroys.** "Clear Recall index",
  behind the standard `ConfirmActionDialog`, deletes every row and vacuums
  the file. It must be genuinely gone, not soft-deleted, and the
  confirmation must say plainly that it removes the index only and does not
  touch a single conversation, memory or task. Same copy discipline as the
  project-delete dialog in doc 01 1.4, and for the same reason: every other
  destructive action in this app destroys real data, so a user will
  reasonably assume this one does too.
- **Per-conversation exclusion.** An "Exclude from Recall" toggle on the
  conversation context menu, alongside pin and archive. Setting it removes
  that conversation's existing entries immediately, not at the next sweep.
  Some conversations are nobody's business, including the search box's.
- **Honest size reporting.** The index's row count and on-disk size shown
  wherever the switch lives, and counted in the System Overview storage
  figures like every other database. A store the user cannot see the cost
  of is a store they cannot make a decision about.
- **Deletion propagates, always.** Deleting a conversation, a task or a
  project deletes the corresponding entries in the same operation, not on a
  later pass. A record that survives its own source is a bug of the highest
  severity in this doc.

Acceptance:
- A test asserts the switch off means no rows are written by any indexing
  path, including the startup backfill.
- A test asserts "Clear Recall index" leaves zero rows and leaves the
  conversation, memory, task and dataset stores byte-identical.
- A test asserts per-conversation exclusion removes existing rows
  synchronously and blocks re-indexing of that conversation.
- A test asserts deleting a conversation removes its entries in the same
  operation.

## 2.1 The recall index

New `src/Hermaeus.Services/Recall/RecallIndexStore.cs` over
`{DataRoot}/recall.db`, `SqliteMigrationRunner` scope `"recall"`,
`SchemaVersion = 1`. Register it in `DataRootManifest`.

One table, because both indexed kinds share a shape:

```
recall_entries(
  id            TEXT PRIMARY KEY,   -- deterministic: kind + source key
  kind          TEXT NOT NULL,      -- 'message' | 'task'
  source_id     TEXT NOT NULL,      -- conversation id | task id
  sub_id        TEXT NOT NULL,      -- message index | '' for tasks
  project_id    TEXT NOT NULL DEFAULT '',
  title         TEXT NOT NULL,      -- conversation title | task goal
  body          TEXT NOT NULL,
  created_at    TEXT NOT NULL,
  indexed_at    TEXT NOT NULL,
  embedding     BLOB,
  embedding_dim INTEGER
)
```

Plus an FTS5 virtual table over `title, body` with `id UNINDEXED`, mirroring
`conversations_fts` (`ConversationStore.cs:56-62`) and rebuilt on schema
change the same way (`ConversationStore.cs:79-80`).

The id is deterministic so re-indexing is an upsert, not a duplicate. The
`project_id` is denormalized onto the row deliberately: scoped recall must
not need a join across three databases.

**Dimension drift.** When the embedding model changes, stored vectors
become meaningless. `MemoryStore` already solved this and the Memories
panel already surfaces it with a "Re-embed memories" button
(`docs/features.md:157-162`). Recall follows the identical rule: a vector
whose `embedding_dim` differs from the current model's is skipped for the
semantic half rather than scored as garbage (`MemoryStore.cs:453-458`), and
the recall surface offers the same explicit re-embed action. Never re-embed
automatically on a model switch.

## 2.2 Indexing conversation messages

The existing conversations FTS indexes `messages_json` as one blob, so a
hit tells you which conversation, not which message. Recall indexes one
entry per message.

- Kind `message`, `source_id` = conversation id, `sub_id` = message index,
  `title` = conversation title, `body` = message content.
- Index user and assistant messages. Skip system messages, skip empty
  bodies, and skip messages below a small floor (a bare "thanks" is noise
  that will otherwise dominate short-query results).
- Store the attachment summary text but not attachment file bodies; the
  attachment content was never persisted (`docs/features.md:68-74`) and
  Recall must not become the thing that starts persisting it.
- Incremental: on conversation save, upsert that conversation's messages.
  Deleting a conversation deletes its entries. Archiving does not delete
  them, but archived conversations are excluded from results by default
  with a toggle to include them, matching how memory search treats archived
  rows (`docs/features.md:129-131`).

## 2.3 Indexing agent tasks

Index the durable, meaningful parts of a task, not the raw transcript. A
full step-by-step transcript is mostly tool output and would swamp every
other source.

Per task, one entry whose body is the concatenation of: goal, summary,
final answer, reservations (`AgentModels.cs:209`), the plan step
descriptions, and the orchestration `report.md` when one exists
(`docs/features.md:320-325`). `title` = goal. `source_id` = task id.

- A child task carries its parent's id in `sub_id` so a hit can show "sub
  task of: <parent goal>", which the review queue already does
  (`docs/features.md:311-315`).
- Re-index on task state transitions to a terminal state, and on report
  write. Do not re-index on every step; that is per-step I/O on the agent's
  hot loop for no benefit.

## 2.4 The four sources and the fused result

`IRecallSource` in `Hermaeus.Core`, four implementations:

| Source | Implementation |
| --- | --- |
| Conversations | Query `recall_entries` where kind = 'message': FTS rank plus cosine, the `MemoryStore.HybridRerankAsync` shape (`MemoryStore.cs:407-460`) |
| Agent tasks | Same, kind = 'task' |
| Memories | Wrap `MemoryStore.SearchAsync` (`MemoryStore.cs:336`). It is already hybrid. Do not reimplement it |
| Documents | Wrap the existing retriever across datasets in scope. Respects `IsParent` exclusion and parent-upgrade exactly as RAG does |

Each returns a ranked list; `RecallService` fuses with RRF and returns:

```
RecallHit(
  Kind,          // Message | Task | Memory | Document
  Title,
  Snippet,       // query-term-centred excerpt, bounded
  Timestamp,
  ProjectId,
  Score,         // fused, for ordering and diagnostics only
  Target)        // where Enter goes
```

`Target` is a typed navigation instruction, not a string. Message hits open
that conversation scrolled to that message. Task hits open that task in the
workbench. Memory hits jump to it in Memories, which
`RequestNavigateToMemory` already does (`MainWindowViewModel.cs:124-126`).
Document hits open the citation the same way RAG's existing citation pills
do. Every hit must land somewhere real; a hit that cannot navigate is a bug,
not an acceptable degradation.

Sources run concurrently with a bounded per-source timeout (use the same
3-second query-embedding timeout memory search uses, `MemoryStore.cs:414`).
A source that times out is omitted and **named in the result footer**. A
partial result that lies about being complete is worse than a slow one.

## 2.5 The palette

`Ctrl+K` from anywhere. One box, two halves, chosen by what you type.

**Empty query: the capability index.** This is the direct answer to "what
can it even do". Opening the palette with nothing typed lists every command
the app has, grouped by area (Chat, Agent, Models, Services, RAG, Memory,
Voice, Doctor, System, Settings), scrollable, each with its keyboard
shortcut where one exists. The user asked what the app can do; this is the
app answering in full, in one keystroke, without them having to visit
twelve panels to find out.

**A word or short phrase: commands first, then recall.** Command matches
are exact-ish and instant; recall results stream in underneath as the
sources return.

**A question, or Enter on the "Search everything" row: recall only.**

Rules:
- The command list comes from the shared registry in doc 04 4.1. The
  palette does not own its own list of commands. One registry, two
  surfaces, so they cannot drift.
- Navigation commands set `ActivePanel` (`MainWindowViewModel.cs:43`,
  switch at :65-79) and nothing else. The palette must not become a second
  navigation source of truth.
- A scope chip toggles All / current project, defaulting to the current
  project when one is active (doc 01 1.6).
- Kind chips on each hit (Message, Task, Memory, Document) with the
  project colour dot. Result rows show a timestamp; "when" is most of how
  people actually locate their own past work.
- Recall queries debounce; a keystroke must not fire four store queries and
  an embedding call.
- Escape closes and restores focus to wherever it came from. The palette
  never leaves the user somewhere they did not ask to be.
- Empty state uses `MossEmptyState`. Icon-only controls get tooltips; the
  guard test will fail otherwise.

## 2.6 Recall in chat

Opt-in, off by default, in Settings > Memory alongside the existing
agent-lessons toggle (`docs/features.md:168-174`), which is the closest
precedent: consuming one subsystem's knowledge inside another, read-only.

When on, a chat send may retrieve from Recall and inject a bounded block,
under exactly the constraints Knowledge injection already lives by
(`docs/features.md:38-51`):

- Bounded by its own configurable budget, separate from the Knowledge
  budget.
- Weak retrieval injects nothing rather than parroting unrelated history
  into every message.
- **Every injected item renders as a visible, clickable citation pill**,
  reusing the existing pill UI. Recall must never silently put the user's
  own past into a prompt with no indication. If the pills are not there,
  the feature is not shippable.
- Retrieval only adds context. It never blocks, rewrites or refuses a send.
- The chat trace records what Recall injected and why, alongside the
  existing memory-recall and Knowledge timing entries
  (`docs/features.md:21-37`).

Injected recall content is **untrusted text**. It is data the model reads,
never instruction the app acts on. The r23 stated-lesson gate-claim filter
exists because content that reaches the model can try to claim authority;
recall content gets the same treatment. It cannot approve anything, cannot
widen the safety gate, and cannot carry a memory id tag that
`[MEMORY_UPDATE]` or `[MEMORY_FORGET]` could target.

## Testing

Roughly 26 to 32 tests, including the four 2.0 acceptance tests, which are
the ones to write first: index upsert determinism and no-duplicate
re-indexing; message skip rules (system, empty, below floor); archived
exclusion and the include toggle; conversation delete cascades; task entry
composition including parent linkage and report body; re-index on terminal
transition only; dimension-drift skip and the re-embed action; RRF fusion
ordering with hand-built source results; per-source timeout producing a
partial result that names the omitted source; keyword-only degradation when
no embedding service is configured; every `RecallHit.Target` kind
navigating to a real destination; palette command list matching the doc 04
registry exactly; scope chip filtering by project; debounce; recall
injection producing pills and producing nothing on weak retrieval; recall
content unable to satisfy a gate claim.
