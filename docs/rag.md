# RAG Workflow

## Overview

Hermaeus includes a local RAG: structure-aware chunking, query planning,
hybrid retrieval, ONNX reranking, budget-aware context packing, citations,
traces, versioned SQLite schema migrations, and native eval support.

## Getting Started

1. Start an embeddings runtime in **Services** (Doctor can auto-download if missing).
2. Open **RAG** and ingest a folder of `.txt` / `.md` / digital `.pdf` files.
3. Use **Dry run** to preview the ingest report before writing to SQLite, or
   choose a duplicate policy to skip unchanged sources, replace them, or just
   report what would happen. Use **Stop** during ingest to cancel long runs.
4. Ask questions against the dataset. The box empties on send and the question
   is shown above the answer it produced; a question that failed goes back in
   the box so it can be edited and retried. Answers are written by the model
   Chat has selected, falling back to Settings > LLM's default.
5. Inspect citations, source text, grounding score, query traces, planner
  variants, context packing summaries, and the last ingest report.
6. Run eval sets from the Eval Harness panel.

### Embedding Model Setup

- Hermaeus requires an embedding model to run the embedding server. If none is found, the
  **Doctor** panel can automatically download the recommended model (Qwen3-Embedding-0.6B-Q8_0)
  from a pinned Hugging Face commit and verify its SHA256 before configuring
  the embedding server.
- Embedding GGUF files live under the configured local AI assets root in
  `Models/embed`. Doctor installs the pinned Qwen embedding model there and
  moves a verified root-level copy into that folder when found.
- Existing Nomic installations are not replaced or deleted. Doctor keeps Nomic
  selected until the Qwen file has downloaded and verified, then changes the
  dedicated embedding server to Qwen. Reindex RAG datasets and re-embed any
  mismatched memories after that switch.
- The Settings embedding selector lists dedicated embedding models only. Chat
  and code GGUF files in the model root are not shown as embedding choices.
- Chat or code GGUF files are not treated as embedding models. Doctor will skip
  embedding backend health until a dedicated embedding model is installed or
  selected, avoiding connection-refused noise before setup is complete.
- The embedding model is critical for large ingests. **During ingest, Hermaeus automatically
  pauses other LLM servers and TTS services** to reduce memory pressure, starts
  the managed embedding server if needed, then restores suspended services after
  ingestion completes.
- Qwen3-Embedding-0.6B is a small 0.6B embedding model. The official GGUF
  release currently provides the verified Q8_0 file (about 640 MB), suitable
  for the dedicated embedding server alongside a chat model.

## Features

### Ingest

- RAG ingest for text/markdown and digital PDFs.
- Markdown ingest preserves heading paths so retrieval can boost structural
  matches.
- Code ingest groups symbols and carries namespace/class context through to
  stored chunks.
- PDF ingest records page markers from the extractor and keeps page metadata
  with each chunk.
- Log-like text is chunked by event boundaries and severity when the source
  looks like a log.
- Optional explicit web URLs (off by default).
- Reindex diffing and corpus health warnings.
- In-progress cancellation support.
- Directory ingest processes local files in bounded file batches, embedding and
  flushing each batch to SQLite before loading the next one. Large folders can
  resume with **Skip unchanged** after an interrupted run instead of rebuilding
  the whole corpus in memory.
- The ingest pipeline uses cancellable, batched embedding and storage steps to
  reduce DB lock contention and make long ingests abortable from the UI.
- Large ingests report separate overall progress and current-stage progress.
  Embedding progress identifies both the file batch and embedding batch, and
  oversized embedding inputs are retried with smaller clamps before failing the
  ingest. The embedding input budget is sized to fit a default-length chunk
  plus its metadata header in one call; a source path that would otherwise
  crowd out chunk content is truncated (keeping the distinctive tail) instead.
- A custom chunk size configured larger than the embedding input budget
  surfaces an ingest health warning naming both numbers, instead of silently
  truncating the end of every chunk.
- Dry-run ingest never creates or updates a dataset row; only a real ingest
  does, so a zero-chunk dataset never appears in the picker.
- Adding documents to an existing dataset that was embedded with a different
  model than the one currently configured is blocked with a message naming
  both models. Use **Reindex** (below) first.

### Dataset Manager

Each dataset card in the manager shows chunk/source counts, missing and stale
file counts, and duplicate-source counts, with these actions:

- **Add** - ingest more documents into the dataset.
- **Reindex** - shown only when the dataset's recorded embedding model differs
  from the currently configured one. Re-embeds every stored chunk with the
  current model in batches, from stored content only (the original source
  files are not required), then rebuilds BM25 stats and clears the query
  cache. Progress reports through the same ingest progress UI.
- **Remove missing** - shown only when one or more source files no longer
  exist on disk. Lists the missing paths and, after explicit confirmation,
  deletes their chunks, rebuilds BM25 stats, and refreshes health. Never runs
  automatically: a temporarily unmounted drive must not silently shred a
  dataset.
- **Delete** - deletes the dataset and all of its chunks and BM25 stats
  (explicit deletes, not a database cascade), after confirmation.

Re-ingesting into an already-queried dataset, reindexing, and removing missing
sources all clear the in-memory query cache for that dataset, so a query
immediately after any of these actions sees the new state without needing an
app restart.

### Watched Sources

A dataset can watch zero or more folders so it stops being a photograph of
those folders taken once at ingest time.

- **Add a watched folder** from the Dataset Manager card. Exclude globs ship
  non-empty by default (`.git`, `node_modules`, `bin`, `obj`, `.venv`,
  `__pycache__`, `dist`, `target`) so pointing a watched source at a repo
  root does not flood the dataset with build output. Globs reuse the exact
  same engine (`GlobMatcher`) as the Agent's `glob_files` tool, so what a
  watched source matches never diverges from what the rest of Hermaeus means
  by a glob.
- **Scan** walks every watched root under its globs and classifies drift
  against the dataset's stored source rows - new, changed, missing, or
  unchanged - without changing anything. Change detection prefers a stored
  content hash; it falls back to modification time (with a one-second
  tolerance) only when no hash was recorded. The scan is cancellable.
- **Refresh now** runs the scan, shows the plan, and applies it after
  confirmation. New and changed files are ingested through the normal
  pipeline (`IngestDuplicatePolicy.Replace`) - the same chunking, embedding,
  BM25 rebuild, and cache invalidation as a manual ingest. Missing files are
  listed and, only if you confirm that part separately, their chunks are
  removed - the same two-step contract as **Remove missing** above. A
  refresh that would remove more than half a dataset's sources warns
  prominently before that second confirmation: that shape is almost always
  an unmounted drive or a bad glob, not an intended purge. One refresh runs
  per dataset at a time; a second request while one is in flight is refused
  with a clear reason, not queued.
- **Automatic refresh** (Settings > RAG, off by default): refresh watched
  sources on app start and/or every N hours. Automatic refresh only ever
  ingests new and changed files - it never deletes, under any configuration
  - runs well after startup and never on the chat send path, and its
  outcome is recorded as an Activity event rather than a toast you could
  miss. A dataset whose embedding model has drifted from the current setting
  is skipped, the same guard a manual ingest already enforces.
- The Dataset Manager card shows watched root count, last-refresh time, and
  a live drift summary; "up to date, checked 20 minutes ago" when there is
  nothing to do.

There is no filesystem watcher (no `FileSystemWatcher`): watching is a
deterministic, on-demand or scheduled scan, not an always-open OS handle.
Watchers differ meaningfully between Windows and Linux, drop events under
load, and fire multiple times for one logical save; a scan gives the same
outcome without holding anything open for the life of the process.

### Web Loading

- The optional web loader is off by default.
- When enabled for a dataset, Hermaeus fetches only the HTTP(S) pages explicitly
  listed in the ingest panel.
- Hermaeus does not crawl links or use a remote scraping service.

### PDF Support

- PDF ingest uses managed PdfPig text extraction for digital PDFs.
- Scanned or image-only PDFs are skipped with health warnings.
- OCR remains a later release gate.

### Querying

- RAG citations with `[1] [2] [3] +N`, source inspector, copy source/path.
- RAG query traces now include query variants, planner notes, packing summaries,
  and refusal reasons.
- Malformed source or trace metadata markers are logged as warnings instead of
  being silently ignored.
- The query planner emits multiple variants so lexical retrieval can score the
  original query, alias-expanded form, and keyword-focused variants together.
- RAG query service provides hybrid retrieval with structural boosts, optional
  reranking, and budget-aware context packing for improved relevance.
  Structural boosts (title/heading/symbol matches, freshness) are a bounded
  proportional multiplier on the fused score, so they can only break ties and
  lift near-ties, never let a low-ranked candidate outrank a clear top result.
- Queries can refuse early when retrieval itself found nothing (the best
  semantic score is below the confidence threshold and no BM25 term matched
  at all), instead of scoring how much the question's own wording overlaps
  the context. A question phrased differently than the corpus vocabulary no
  longer gets refused when retrieval actually found the right chunks. A
  refusal still shows the closest sources it considered and explains why they
  were not trusted.
- Parent-child retrieval (chunking with a smaller child size for embedding
  and a larger parent size for context) correctly returns child matches
  upgraded to their parent's content; parent bodies are excluded from
  retrieval candidates.
- A dataset embedded with one model, queried under a different currently
  configured model, skips semantic search and falls back to BM25-only
  retrieval with a planner note, instead of a raw exception or silently
  wrong rankings. Reindex the dataset to re-enable semantic search.
- An unreachable or stopped embedding server falls back to the same BM25-only
  path with a planner note ("semantic search unavailable: ...") instead of a
  raw exception, both from the RAG panel and from a chat send with a
  Knowledge dataset attached. The fallback is not cached - the next query
  probes the embedding server again and is fully semantic once it is back.

### Using a dataset in Chat

- A conversation can have one RAG dataset attached via the chat header's
  "Knowledge" picker (owner-facing name; settings and code keep the `Rag*`
  names). Selecting "None" detaches it. The picker refreshes its dataset list
  only when its flyout opens - there is no live update if a dataset is
  created or deleted elsewhere while it is closed.
- Every send against a conversation with a dataset attached retrieves from it
  (top 5, parent/child per the dataset's own ingest config) and, when
  retrieval clears the confidence threshold, injects a bounded "Knowledge
  Context" block into the system prompt with the retrieved chunks. Chunks
  that survive packing render as individually clickable citation pills on
  the reply, the same pills the RAG panel's own query pane uses.
- A weak or unrelated match (e.g. "thanks!") injects nothing - chat does not
  parrot weakly-related chunks into every message just because a dataset
  happens to be attached. Unlike the RAG panel, chat never refuses to answer;
  weak retrieval simply means the model answers from its own knowledge, as it
  always does when no context is injected.
- The injection budget is `RagSettings.ChatInjectionTokenBudget` (default
  2000 tokens; Settings > RAG > "Chat knowledge context budget"), separate
  from the RAG panel's own query budget. TopK and the refusal threshold use
  the pipeline defaults; there is no per-conversation override in this round.
- If the attached dataset id no longer resolves (deleted, or a temporarily
  unmounted data root), the picker shows "Knowledge: missing" and the send
  proceeds with nothing injected. The stored id is never silently cleared -
  only picking another dataset or "None" changes it - matching how missing
  RAG sources are never auto-removed elsewhere in the app.
- **Open in chat** on a Dataset Manager card starts a new conversation with
  that dataset pre-attached.

### Reranker

- Reranker install: The ONNX cross-encoder reranker assets are not downloaded
  automatically during query time.
- Use the **Doctor** panel to install the reranker model and vocabulary.
- Settings discovers installed reranker folders under the configured local AI
  assets root at `Models/rerank/*` and lets you choose from the available
  rerankers instead of typing the path manually.
- Doctor shows progress messages during the download and loading steps.
- Downloads are pinned to a specific Hugging Face commit and verified with
  SHA256 before the ONNX session or tokenizer loads.
- This prevents heavy network activity during queries and makes reranker
  installation an explicit, observable action.
- The in-memory query cache is bounded by dataset count and an approximate
  byte ceiling. A single dataset that exceeds the byte ceiling is queried but
  not retained in cache, so very large embedding sets cannot grow memory use
  without limit.

## Scale and the memory budget

Retrieval keeps a per-dataset **scan index** in memory: the chunk ids and one
contiguous block of embeddings. Content, paths, titles and heading paths stay in
SQLite and are read only for the handful of chunks that survive ranking.

That index has a budget of 128 MiB across all cached datasets, with an
eight-dataset LRU. Because the index holds embeddings rather than documents, its
size is exact arithmetic over chunk count and embedding dimension rather than a
sum over strings, so it no longer varies with how long your documents happen to
be. Each dataset's index size is shown against the budget on its Dataset Manager
card.

**Above the budget, a dataset is still queried.** It is scanned from storage
instead of from memory, which is slower, and the retrieval result's planner
notes say so. Before r27 an over-budget dataset was dropped by the cache without
being cached, and every subsequent query read that empty entry back, scored
nothing, and returned no results and no error while re-reading every chunk and
every embedding out of SQLite. Raising the budget was rejected as the fix: a
bigger number moves the cliff rather than removing it.

## Keyword candidates

BM25 scoring is unchanged, including its stats and its tuning. What changed in
r27 is where its candidates come from: an FTS5 index over chunk content, rather
than tokenising every chunk in the dataset once per query variant. FTS5 finds a
few hundred candidates and `Bm25Scorer` ranks them exactly as it always has. The
only chunks that stop being scored are ones that share no query term at all and
therefore scored essentially zero; a regression test asserts that scoring the
candidate set produces the same ranked ids, in the same order, as scoring the
whole corpus.

The index is maintained inside the same transaction as the chunk rows it
mirrors, and is backfilled once, lazily, on the first search of an existing
dataset. An install that never opens the RAG panel never pays for the backfill.
Malformed search syntax falls back to a LIKE scan rather than surfacing as an
error.

## Eval Harness

Eval files can be either an array of cases or an object with `cases` /
`questions`. Cases support the following fields:

- `question` - the retrieval query
- `expected_sources` - list of expected source identifiers
- `answer_keywords` - list of keywords the answer should contain
- `should_refuse` - whether the query should be refused or answered

The native eval harness now reports retrieval metrics in addition to pass/fail:

- Recall@K
- Mean reciprocal rank
- Citation hit rate
- Unsupported answer rate
- Refusal accuracy
- Latency and reranker rank delta

Retrieval-only mode evaluates `should_refuse` cases against the same
retrieval-strength preflight gate the live query path uses, so a `should_refuse`
case can pass (it no longer hard-fails every refusal case in this mode). Evals
are cancellable from the UI; cancelling stops between cases and reports
"cancelled" without writing a partial export (export only happens once a run
completes).

## Model Sources

Hermaeus recommends using the Hugging Face Model Hub to obtain ONNX and tokenizer
files for optional components like the reranker and small local LLMs.

Hugging Face provides a model hub to browse and download model artifacts; many
files are downloadable directly from their site, and they also offer the
Hugging Face Inference API (requires an API key).

The Inference API has a free tier with rate limits. For local deployments,
prefer downloading model files and installing them via the **Doctor** panel.

Example small instruction-tuned models for local LLM:
- `microsoft/Phi-4-multimodal-instruct-onnx` (Phi-4)
- Other similarly sized models that provide ONNX artifacts

Always check the model license and hosting requirements before downloading.
