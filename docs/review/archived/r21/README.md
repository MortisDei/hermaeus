# Review round 21: RAG meets chat

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 04 is the roadmap and sequencing contract.

## Why this round exists

Hermaeus is pitched as a local-first AI workstation with "local RAG" in the
first sentence of its identity, and the owner daily-drives the app through
the Chat view. Yet those two facts have never met: **normal chat cannot use
RAG at all.** Retrieval Q&A exists only inside the RAG panel's one-shot query
box (`RagViewModel.QueryAsync`), which has no conversation history, no
sampling controls, no memory, no attachments, and a bare answer pane. RAG has
not had a dedicated round since r10 (~11 rounds ago).

The infrastructure gap is almost embarrassing in how ready it is:

- `RagQueryService.RetrieveAsync` (RagQueryService.cs:197) is a complete
  retrieval-only seam: planner, hybrid retrieval, rerank, parent upgrade,
  no answer generation.
- `MessageViewModel.Sources` (MessageViewModel.cs:52) documents itself as
  holding "Memories (and, in future, RAG/agent citations)"; the
  `CitationSources` split for non-memory pills already exists and
  `MessageControl.axaml:82` already renders it. Nothing ever feeds it.
- The chat trace record has a `RagContextItems` field (ChatViewModel.cs:51)
  that is declared, never assigned, and never read. Dead since it was added.
- Chat already has the exact injection pattern to mirror:
  `BuildMemoryInjectionAsync` (ChatViewModel.cs:1177) with best-effort
  failure, timing capture, and source pills.

This round wires a RAG dataset into the daily chat flow as a first-class,
per-conversation attachment, and fixes the robustness gaps that surface the
moment retrieval runs on a path where failure must never block a send.

## Documents

| Doc | Theme |
| --- | --- |
| `01-chat-rag-integration.md` | Attach a dataset to a conversation; per-turn retrieval injection with citation pills, trace timing, Context Inspector visibility |
| `02-retrieval-robustness.md` | Embedding-failure BM25 fallback in the pipeline; best-effort contract on the chat path |
| `03-lifecycle-and-glue.md` | Dataset picker lifecycle, deleted/missing dataset honesty, "Open in chat" handoff from the Dataset Manager, Privacy Audit disclosure |
| `04-roadmap.md` | Ships as 0.27.0-alpha; sequencing, test budget, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. File:line references were exact at spec time
  (tree at 67e9b01, v0.26.1-alpha); re-verify before editing.
- No em dashes anywhere. Zero-warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`.
- Schema changes are additive only, through `SqliteMigrationRunner`.
- Chat send must never fail because of anything this round adds. Every new
  step on the send path is best-effort with a logged warning, exactly like
  memory injection.
- The r10 rejections stand: no vector DB or search-library dependency, no
  persisted ANN index, no LLM-based query rewriting on the query path, no
  auto-reindexing on model change.
- Update `docs/features.md`, `docs/rag.md`, `CHANGELOG.md`, and
  `docs/security-review.md`. Do not document planned behaviour as existing.
