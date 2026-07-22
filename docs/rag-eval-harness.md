# RAG Eval Harness Plan

Hermaeus should inherit Oghma Infinium's habit of proving retrieval quality with
repeatable question sets, then make that native and visible in the app.

## Eval Artifacts

- `gold.json`: direct answer questions with expected source titles and answer
  keywords.
- `stress.json`: ambiguity, aliases, long-tail names, and "not enough context"
  cases.
- `aliases.json`: canonical term and alternate names used during query
  expansion.
- `run.jsonl`: one row per query with model, dataset, retrieved chunks, latency,
  grounding score, planner variants, packing summary, and pass/fail notes.

## Metrics

- Recall@K: fraction of expected sources retrieved in the top K.
- Mean reciprocal rank: inverse rank of the first expected source.
- Citation hit rate: answer text cites or references the expected source.
- Unsupported answer rate: answer was produced without enough grounded support.
- Refusal accuracy: app says insufficient context when it should.
- Reranker rank delta: how much the cross-encoder moved the expected source.
- Latency: embed, retrieve, prompt-build, first-token, full-response.

## Native UI

- Dataset eval tab with selectable gold/stress files.
- Run history table.
- Per-question inspector with query variants, top chunks, scores, packing
  summary, and generated answer.
- Export to JSONL/Markdown for debugging and regression tracking.

## First Implementation Slice

1. Add eval file loader and result model.
2. Run retrieval-only eval without generating answers.
3. Add full answer eval with grounding score.
4. Add dashboard cards and per-case source inspector.
