# RAG Workflow

## Overview

Aether includes a local RAG: structure-aware chunking, query planning,
hybrid retrieval, ONNX reranking, budget-aware context packing, citations,
traces, versioned SQLite schema migrations, and native eval support.

## Getting Started

1. Start an embeddings runtime in **Services** (Doctor can auto-download if missing).
2. Open **RAG** and ingest a folder of `.txt` / `.md` / digital `.pdf` files.
3. Use **Dry run** to preview the ingest report before writing to SQLite, or
   choose a duplicate policy to skip unchanged sources, replace them, or just
   report what would happen. Use **Stop** during ingest to cancel long runs.
4. Ask questions against the dataset.
5. Inspect citations, source text, grounding score, query traces, planner
  variants, context packing summaries, and the last ingest report.
6. Run eval sets from the Eval Harness panel.

### Embedding Model Setup

- Aether requires an embedding model to run the embedding server. If none is found, the
  **Doctor** panel can automatically download the recommended model (nomic-embed-text-v1.5-Q4_K_M)
  from a pinned Hugging Face commit and verify its SHA256 before configuring
  the embedding server.
- Embedding GGUF files live under the configured local AI assets root in
  `Models/embed`. Doctor installs the pinned nomic embedding model there and
  moves a verified root-level copy into that folder when found.
- The Settings embedding selector lists dedicated embedding models only. Chat
  and code GGUF files in the model root are not shown as embedding choices.
- Chat or code GGUF files are not treated as embedding models. Doctor will skip
  embedding backend health until a dedicated embedding model is installed or
  selected, avoiding connection-refused noise before setup is complete.
- The embedding model is critical for large ingests. **During ingest, Aether automatically
  pauses other LLM servers and TTS services** to reduce memory pressure, starts
  the managed embedding server if needed, then restores suspended services after
  ingestion completes.
- For 6 GB VRAM systems, nomic-embed-text-v1.5-Q4_K_M (Q4 quantization) is recommended.

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
  ingest.

### Web Loading

- The optional web loader is off by default.
- When enabled for a dataset, Aether fetches only the HTTP(S) pages explicitly
  listed in the ingest panel.
- Aether does not crawl links or use a remote scraping service.

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
- Queries can refuse early when the retrieved context is too weak to answer
  reliably, instead of forcing a speculative response.

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

## Model Sources

Aether recommends using the Hugging Face Model Hub to obtain ONNX and tokenizer
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
