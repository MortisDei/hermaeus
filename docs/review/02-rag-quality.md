# 02 - RAG quality and cost

Nothing here crashes; everything here caps how good retrieval can get
or how big a corpus stays usable. Measure-then-fix applies: 2.1 and
2.5 change ranking behaviour, so add the eval fixtures (2.7) first and
run them before/after.

## 2.1 Embeddings see only a ~192-token prefix of each chunk

`MaxEmbeddingInputTokens = 192` with retry clamps 128/64
(`src/Aether.Rag/Pipeline/RagPipeline.cs:37-38`), enforced by
`ClampEmbeddingInput` (`:639-657`). Default chunks are
`TargetChunkChars = 1600` (~400 estimated tokens,
`src/Aether.Rag/Models/RagDataset.cs:17`), and the metadata header
(`BuildEmbeddingText`, `:306-339`: Title, Source path, Heading,
Symbol, Url lines) is prepended INSIDE the same budget. Net effect:
roughly the second half of every default-sized chunk, and more when
the source path is long, is invisible to semantic search. The
nomic-embed model in use supports 2048 tokens.

Fix:

- Raise the primary clamp so a default chunk plus header fits:
  512 tokens as the first attempt, keeping the retry ladder
  (512 -> 256 -> 128) for servers with small physical batches
  (`--ubatch-size`). Keep `EstimateTokens` (chars/4) as the estimator.
- Stop letting metadata crowd out content: cap the header portion
  (title + path + heading lines) at 48 estimated tokens; truncate the
  PATH (keep the tail, it is the distinctive part), never the content,
  to fit.
- Chunk-size guard: if `TargetChunkChars / 4` exceeds the primary
  clamp, log one ingest health warning naming both numbers, so an
  oversized custom chunk config is visible instead of silent.

Acceptance criteria:

- Test: a 1600-char chunk with a long source path embeds with its
  final sentence present in the embedding input (assert via a fake
  embedding service that records inputs).
- Test: retry ladder still reduces on "too large" errors.
- Eval fixture (2.7) recall does not regress; expect improvement on
  the long-chunk fixture.

## 2.2 Refusal preflight measures the wrong thing, and one grounding mode is dead

`StreamQueryAsync` refuses when
`ComputeGroundingScore(question, context, mode) < RefusalThreshold`
(`src/Aether.Rag/RagQueryService.cs:246-247`): that is token overlap
between the QUESTION and the packed context. A question phrased in
different vocabulary than the corpus ("how do I make it talk?" vs
docs saying "voice output") gets refused even when semantic retrieval
found the right chunks with high cosine scores. Separately,
`ComputeGroundingScore` (`:573-576`) branches on
`RagGroundingMode.SemanticPlaceholder` and calls `ScoreTokenOverlap`
on BOTH branches; the enum is dead weight.

Fix:

- Base the preflight on retrieval strength, not question/context
  token overlap: refuse when the best semantic candidate's cosine
  score AND the best BM25 candidate's normalized score are both below
  thresholds (constants; start at cosine < 0.35 and no BM25 term
  match at all), i.e. "nothing matched either way". Keep the
  token-overlap check only as a secondary signal if desired, but the
  primary gate must use scores retrieval already computed.
- Post-answer grounding (`:317`) stays token overlap (answer vs
  context is a legitimate overlap check).
- Delete `RagGroundingMode.SemanticPlaceholder` handling: collapse
  `ComputeGroundingScore` to one path. Keep the enum value itself only
  if it is serialized in stored traces; map it to TokenOverlap on read
  and note that in the code.
- Refusal UX: the refusal event currently yields no sources
  (`:249-285` yields the refusal token and trace only), so the user
  sees a bare sentence. Emit the sources event before the refusal
  text and extend the refusal message to say the closest sources are
  shown and why they were not trusted (reuse `RefusalReason`).

Acceptance criteria:

- Test: strong cosine match + zero token overlap between question and
  context does NOT refuse.
- Test: empty/garbage dataset still refuses, sources event emitted,
  reason recorded in the trace.
- All existing refusal tests updated intentionally, not deleted.

## 2.3 Boost scales are inconsistent between BM25 and fusion

The same 0.008-0.020 boost constants appear in two places with
opposite effect:

- Inside `Bm25Scorer.ComputeMetadataBoost`
  (`src/Aether.Rag/Retrieval/Bm25Scorer.cs:102-123`) they are added to
  raw BM25 scores that are typically 1-10 per matched term: the boosts
  are noise, effectively no-ops.
- Inside `HybridRetriever.ComputeBoost`
  (`src/Aether.Rag/Retrieval/HybridRetriever.cs:83-116`) they are added
  to RRF scores where rank 1 contributes only `0.7 / 61 = 0.0115`: a
  single phrase-match boost (0.015) outranks being the top semantic
  hit. The boosts dominate the fusion instead of nudging it.

Fix: make boosts proportional, not absolute. In the fusion, compute
the boost as a multiplier on the fused score (e.g. `score * (1 +
boostFactor)` with boostFactor capped at 0.5), so structural matches
break ties and lift near-ties without letting a title substring beat a
clear semantic winner. Delete the dead-weight metadata boost inside
`Bm25Scorer` entirely (BM25 already rewards term frequency; the
structural signal belongs in one place, the fusion). Document the
intended magnitudes in the code where the constants live.

Acceptance criteria:

- Test: rank-1 semantic candidate with no metadata match still beats a
  rank-8 candidate with every metadata boost firing.
- Test: between two candidates with near-equal fused scores, the one
  with a heading match wins.
- Eval fixtures show no recall regression.

## 2.4 BM25 re-tokenizes the whole corpus on every query

`Bm25Scorer.Score` computes per-document TF by tokenizing every
candidate chunk's full content (`Bm25Scorer.cs:30-51`), and
`ScoreQueryVariants` runs it once per query variant, up to 3
(`RagQueryService.cs:454-476`). Every RAG query therefore tokenizes
the entire dataset text three times. At 10k chunks this is the
dominant retrieval cost and it grows linearly.

Fix: tokenize each chunk once per query, not once per variant: hoist
`ComputeTf` out of the per-variant loop (compute a
`Dictionary<chunkId, tf>` once, pass it to `Score`). Do NOT build a
new persisted index structure for this round (rejected in 04); the
one-pass change removes the 3x factor and the repeated regex cost is
then bounded by corpus size once per query, which the trace's
`RetrievalLatencyMs` already surfaces.

Acceptance criteria:

- Test: a counting tokenizer seam (or a scorer overload taking
  precomputed TF) proves each chunk's content is tokenized at most
  once per query regardless of variant count.
- Scores identical to the old path on a fixture corpus.

## 2.5 Dataset health loads every chunk body on every refresh

`RefreshDatasetManagerAsync` calls `GetChunksForDatasetAsync` per
dataset and feeds full chunk content into
`RagDatasetHealthService.Compute`
(`src/Aether.ViewModels/RagViewModel.cs:550-577`), which only needs
source paths, chunk indexes, and modified dates. Loading all content
(and it runs after every ingest, delete, and app load) makes the RAG
tab open slowly for big corpora.

Fix: add a store method returning only the columns health needs
(`SELECT source_path, chunk_index, source_modified_utc ...`), and a
`RagDatasetHealthService.Compute` overload over that projection. Keep
the old overload for tests.

Acceptance criteria:

- Test: health results identical on both overloads for a fixture.
- No call site of the health refresh loads embeddings or content.

## 2.6 Eval harness gaps

- Retrieval-only mode can never pass a `should_refuse` case:
  `RefusalCorrect = !test.ShouldRefuse` and `Passed = hitCount > 0 &&
  !test.ShouldRefuse` (`src/Aether.Rag/Eval/RagEvalService.cs:105-106`).
  Fix: in retrieval-only mode, a `should_refuse` case passes when the
  preflight gate (2.2) would refuse, i.e. evaluate the gate on the
  retrieval result instead of hard-failing; mark it in Notes.
- Evals are uncancellable from the UI: `RunEvalAsync` passes
  `CancellationToken.None` (`RagViewModel.cs:517`). Wire a CTS and a
  Stop button like ingest already has.

Acceptance criteria:

- Retrieval-mode eval with a `should_refuse` case over an empty
  dataset passes; over a dataset that answers it, fails.
- Cancelling a running eval stops between cases and reports
  "cancelled" status, no partial export corruption (export only on
  completion).

## 2.7 Eval fixtures for this round

To make 2.1/2.2/2.3 verifiable, add a small built-in fixture corpus +
eval set under `src/Aether.Tests` assets (not shipped to users): ~12
short docs with known-answer questions, including (a) a long-chunk
case whose answer sits in the final third of a 1600-char chunk, (b) a
vocabulary-mismatch case (question words absent from the answer
chunk), (c) a `should_refuse` case. Tests run retrieval with fake
embeddings where determinism is needed and assert recall@5 = 1.0 for
(a) and (b) after this round's changes.
