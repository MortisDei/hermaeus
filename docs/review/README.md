# Review Round 10 (r10)

Theme: **RAG deep-dive**, plus the field follow-ups from first real use
of 0.14.0-alpha. RAG is the one major subsystem that has never had a
dedicated round; this audit read every file in `Aether.Rag` plus
`RagViewModel` and found real correctness bugs (one retrieval mode is
broken end to end, dataset deletion leaks rows forever, re-ingest
serves stale results until restart) alongside quality problems that
silently cap retrieval relevance (embeddings computed from a ~192-token
prefix of chunks sized at ~400 tokens).

Field report on 0.14.0-alpha (2026-07-16, owner): no more crashes,
Start works first click, sends are much faster, speech is much better.
Three residual issues, all specced in doc 03:

- The app throws an unhandled `InvalidOperationException` on shutdown:
  the DI container is disposed synchronously from the window Closed
  path but now contains the async-only `McpToolBridge`.
- Around 20-30 seconds of silence after send before the GPU starts
  prompt processing. The r9 `ChatSendTiming` instrumentation exists
  precisely for this; doc 03 mandates reading it and adds the missing
  server-side breakdown.
- Voice: capitalized/marked-up words fall through the dictionary and
  gain a spoken trailing "e" ("Joke" as "Jok-e"); typographic
  punctuation (U+2014 dashes, curly quotes) confuses the phonemizer.

## Documents

- `01-rag-correctness.md` - parent-child retrieval broken, dataset
  delete orphans rows, stale query cache after re-ingest, embedding
  model/dimension mismatch guard and reindex, deleted sources never
  leave a dataset, dry-run writes, lost `LastIngestPath`.
- `02-rag-quality.md` - embedding input clamp vs chunk size, refusal
  preflight scoring, dead grounding mode, boost scale inconsistency,
  BM25 query-time cost, dataset health cost, eval harness gaps.
- `03-field-follow-ups.md` - shutdown disposal crash, send-lag
  diagnosis with llama-server timings, voice pronunciation fixes.
- `04-roadmap.md` - version, sequencing, test expectations, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r9 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
explicitly rejected; zero-warning builds (`TreatWarningsAsErrors`
solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs (write U+2014 when a spec must name the character); the
approval-gated agent security posture is non-negotiable. Anything that
deletes user data (01 items 1.2 and 1.5) must be explicit,
user-confirmed, and reported afterwards.
