# RAG Workflow

## Overview

Aether includes a local RAG: hybrid retrieval, ONNX reranking,
parent-child context, citations, traces, and native eval support.

## Getting Started

1. Start an embeddings runtime in **Services**.
2. Open **RAG** and ingest a folder of `.txt` / `.md` / digital `.pdf` files.
3. Use **Dry run** to preview the ingest report before writing to SQLite, or
   choose a duplicate policy to skip unchanged sources, replace them, or just
   report what would happen. Use **Stop** during ingest to cancel long runs.
4. Ask questions against the dataset.
5. Inspect citations, source text, grounding score, query traces, and the last
   ingest report.
6. Run eval sets from the Eval Harness panel.

## Features

### Ingest

- RAG ingest for text/markdown and digital PDFs.
- Optional explicit web URLs (off by default).
- Reindex diffing and corpus health warnings.
- In-progress cancellation support.
- The ingest pipeline now uses cancellable, batched embedding and storage steps
  to reduce DB lock contention and make long ingests abortable from the UI.
- Large ingests report progress per embedding and storage batch so the UI can
  surface better feedback and cancel if needed.

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
- RAG query traces, ONNX cross-encoder reranking, and native eval harness.
- RAG query service provides hybrid retrieval with optional reranking for
  improved relevance.

### Reranker

- Reranker install: The ONNX cross-encoder reranker assets are not downloaded
  automatically during query time.
- Use the **Doctor** panel to install the reranker model and vocabulary.
- Doctor shows progress messages during the download and loading steps.
- This prevents heavy network activity during queries and makes reranker
  installation an explicit, observable action.

## Eval Harness

Eval files can be either an array of cases or an object with `cases` /
`questions`. Cases support the following fields:

- `question` - the retrieval query
- `expected_sources` - list of expected source identifiers
- `answer_keywords` - list of keywords the answer should contain
- `should_refuse` - whether the query should be refused or answered

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
