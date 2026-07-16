# 04 - Roadmap

## Version

Implementing this pack ships **0.15.0-alpha**. Version bump lives only
in `Directory.Build.props`, per r8 convention.

## Sequencing

1. **3.1 (shutdown crash)** first: five-line fix plus a guard test,
   removes the one remaining unhandled exception in daily use.
2. **3.3 (voice)** next: self-contained in Aether.Voice, high
   perceived-quality payoff, golden tests make it safe.
3. **3.2 step 1-3 (send-lag measurement + timings + warning)**: land
   the observability before any tuning; step 4 only if the numbers
   demand it.
4. **Doc 01 (RAG correctness)** in item order: 1.1 and 1.2 change
   storage and must land before quality work re-ranks anything;
   1.3-1.7 are independent after that.
5. **2.7 (eval fixtures)** before 2.1/2.2/2.3 so ranking changes are
   measured, then **doc 02** in item order.

## Test expectations

Rough guide, not a quota: 1.1 (3, incl. migration), 1.2 (2), 1.3 (1),
1.4 (4), 1.5 (2), 1.6 (1), 1.7 (1), 2.1 (3), 2.2 (3), 2.3 (3),
2.4 (2), 2.5 (1), 2.6 (2), 2.7 fixtures feeding the above, 3.1 (1-2),
3.2 (2), 3.3 (6-8 golden). Expect roughly 35-45 new tests, from 517.
All tests run without a live llama-server, embedding server, or
network: embedding and LLM boundaries use the existing fake seams;
SQLite tests use per-test temp data roots (tests stay sequential).

## Docs touch

`docs/rag.md` gains: reindex action, remove-missing-sources action,
mismatch guard behaviour, refusal-with-sources behaviour.
`docs/voice.md`: typographic normalization coverage. `docs/features.md`
and `CHANGELOG.md` per the standing rule. `docs/security-review.md`
gains an r10 subsection (below).

## Security review touch

- 1.5 remove-missing-sources and 1.4 reindex are the only
  data-destructive/data-rewriting additions: both are explicit,
  user-confirmed, and operate only on the app's own database (no
  filesystem deletes). Document the confirm gate.
- 3.1 changes shutdown disposal only; the 5 s bound means a hung MCP
  child is abandoned to the r9 job object rather than blocking exit.
- No new network surface anywhere in the pack.

## Explicit rejections

Checked against archived rounds and rejected for r10; do not
re-propose without new evidence:

- **A vector database or search-library dependency** (sqlite-vec,
  FAISS, Lucene). Corpus sizes in field use do not justify a
  dependency; the r10 fixes keep brute-force scan + BM25 fast enough,
  and the no-new-NuGet rule stands.
- **A persisted inverted index / ANN index for BM25 or cosine.** Same
  reasoning; 2.4's one-pass tokenization removes the observed cost.
  Revisit only with a trace showing retrieval latency dominating on a
  real corpus.
- **Auto-reindexing datasets on embedding-model change.** Reindex is
  minutes of GPU work the user did not ask for; it is a button (1.4),
  never a side effect.
- **Auto-removing missing-source chunks during ingest.** An unmounted
  drive must not shred a dataset silently (1.5); removal is
  user-clicked, like r9's orphan Stop.
- **LLM-based query expansion or rewriting on the query path.** Adds
  a generation round-trip before retrieval; the alias file + variant
  mechanism covers it deterministically.
- **A semantic grounding scorer to replace token overlap.** The dead
  `SemanticPlaceholder` mode is deleted, not implemented: post-answer
  overlap is adequate, and the refusal gate now uses retrieval scores
  (2.2) which are already semantic.
- **Auto-changing llama-server flags from lag findings (3.2).**
  Advisory only, quoting observed evidence; r9's rejection of
  speculative context tuning stands.
- **Async-over-sync shutdown rework (making app exit fully async).**
  Avalonia's Exit event is synchronous; a bounded blocking wait at
  process end is the honest version.
- **Stripping em dashes app-wide or in chat rendering (3.3).** The
  repo's no-em-dash rule is about authored text; user/model content
  is normalized only at the speech boundary.
