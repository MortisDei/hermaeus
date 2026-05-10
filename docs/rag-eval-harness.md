# RAG Eval Harness Plan

Aether should inherit Oghma Infinium's habit of proving retrieval quality with
repeatable question sets, then make that native and visible in the app.

## Eval Artifacts

- `gold.json`: direct answer questions with expected source titles and answer
  keywords.
- `stress.json`: ambiguity, aliases, long-tail names, and "not enough context"
  cases.
- `aliases.json`: canonical term and alternate names used during query
  expansion.
- `run.jsonl`: one row per query with model, dataset, retrieved chunks, latency,
  grounding score, and pass/fail notes.

## Metrics

- Retrieval hit rate: expected source appears in top K.
- Citation precision: cited chunks actually support the answer.
- Grounding overlap: answer terms are present in retrieved context.
- Refusal accuracy: app says insufficient context when it should.
- Latency: embed, retrieve, prompt-build, first-token, full-response.

## Native UI

- Dataset eval tab with selectable gold/stress files.
- Run history table.
- Per-question inspector with query expansion, top chunks, scores, and generated
  answer.
- Export to JSONL/Markdown for debugging and regression tracking.

## First Implementation Slice

1. Add eval file loader and result model.
2. Run retrieval-only eval without generating answers.
3. Add full answer eval with grounding score.
4. Add dashboard cards and per-case source inspector.
