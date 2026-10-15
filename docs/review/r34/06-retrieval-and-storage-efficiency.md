# 06. Retrieval and storage efficiency

## Source-grounded baseline

**RF, S16:** `RecallIndexStore.SearchAsync` handles stored **message/task**
rows. It obtains up to 100 ranked FTS hits, requests an embedding with a
three-second budget, then reads every eligible same-kind stored embedding
under project scope. It checks dimensions, decodes float32 BLOBs, computes
cosine, collects non-FTS scores, sorts to take 100, builds a full score map and
hydrates those rows. It then applies the existing calibrated relevance rules.
It is a linear semantic scan, with additional O(N) temporary scores and sorting.
This establishes algorithm shape, **not observed latency at 100k vectors**.

Semantic SQL excludes archived rows even when `includeArchived` permits them
in FTS. P2 must characterise and preserve current behaviour or raise a separate
explicit behaviour decision, not silently change it during optimisation.
`FromBlob` allocates per vector, uses `Buffer.BlockCopy`, and scalar cosine
accumulates into doubles. Corrupt byte length, non-finite values, zero vectors
and mismatched dimensions need representative robustness tests before adopting
another primitive. A SIMD float calculation can alter ties/thresholds.

**RF, S17/S18:** RAG already has contiguous `RagScanIndex`, SIMD scan/top-k,
eight-dataset LRU, 128 MiB nominal cache budget, generation/model identity and
candidate-only content hydration. An over-budget index is not cached, but
`LoadScanIndexAsync` still builds the full temporary index. This is a cache
retention bound, not proof of a streaming peak-memory bound. Message/task Recall
and document Recall do not use identical storage paths; do not describe all
four federated sources as this one SQL scan.

`RagScanIndex.ByteSizeFor` calculates float payload plus a fixed 128-byte
per-id allowance. Float payload arithmetic is exact; managed string/object
overhead is an allowance, not measured process memory. B0/P1 retain that
distinction when comparing cache budget with allocations/working set.

**RF:** RAG publication retains the old generation through failure/cancellation;
source/revision/content hashes bind citations. Memory revisions are durable
facts; Recall is a clearable projection. These ownership boundaries stay intact.

## P1/P2 Recall protocol

Use deterministic, **synthetic and labelled** scratch corpora at **1k, 10k,
50k and 100k eligible vectors** for each kind, plus a labelled relevance corpus
from redistributable project/help/code material. Record the seed, generator
revision, query set/hash, source count, vector dimensions, model/tokeniser/hash,
SQLite schema, archive/project mix, cache state and hardware. No owner corpus
or private prompts may enter committed fixtures or public raw reports.

For each size, run semantic-only, lexical-only and mixed queries, rare terms
beyond the FTS cap, scope restrictions, archived cases, missing/failed embedding,
dimension drift, zero/malformed vectors and cancellation. Cold process/index,
warm SQLite page cache and warmed embedding residency are separate states.

Measure separately through the existing operation/log/eval evidence:

| Stage | Direct measurement |
| --- | --- |
| Query embedding | Queue/admission wait, request time, token/input size and residency |
| SQLite read | Command/reader time, rows and BLOB bytes actually read; record whether decoding is inside reader interval |
| Decode | BLOB-to-vector time and allocated bytes |
| Similarity | Cosine loop time, eligible/scored rows and dimensions |
| Candidate selection | Top-k/sort time, temporary bytes, tie/threshold handling |
| Hydration | Rows/content bytes and SQL time |
| Complete result | Wall clock, allocations/GC/peak memory and source fusion time |

Record inclusive and exclusive timings explicitly so sums do not double-count
overlapped work. Today's total-only Recall log does not already provide all
these phases; add narrowly scoped diagnostics in P1 if needed, not another
performance store. Use a fixed query vector for the scan microbenchmark and
real embedding requests for end-to-end runs. Warm-query speed cannot hide cold
load, index build, backfill or model contention cost.

## Ordered candidate ladder

| Candidate | Decision now | Required evidence before selecting implementation |
| --- | --- | --- |
| Existing float32 exact scan | **Retain reference** | Record both semantic/FTS results and ordering, source IDs and relevance; this is the correctness oracle |
| Bounded top-k, avoid full score dictionary/sort | **Simplify if measured**, DEL-06 | Same winner IDs/score semantics including ties and lexical-only dense misses; lower measured allocation/cost; cancellation and source scope unchanged |
| Reuse existing SIMD/span scan primitive | **Conditional** | Compare double/float accumulation near 0.40 threshold and duplicate/zero vectors; no cross-layer reference against dependency direction; do not create a vector subsystem for one loop |
| Compact vectors | **Experiment only** | Compare float16/int8 or model-supported reduced dimensions with float32. Record scales/normalisation, cosine/rank error, relevance/refusal metrics, file/memory bytes, decode cost and reindex time. Distinguish storage quantisation from changing embedding model/dimension |
| Candidate pruning/two-stage retrieval | **Conditional** | Preserve semantic hits without lexical overlap. A lexical-only prefilter cannot replace the existing semantic scan. Evaluate full labelled recall/negative/scope corpus before accepting approximate loss |
| SQLite vector extension | **Defer pending measured need and owner approval** | Maintainer sqlite-vec docs verify an available in-SQLite option (E04 in [10](10-technology-decisions.md)); platform native assets, licence, patched SQLite loading, backup/schema/rebuild and query quality still need proof. It is not a free managed optimisation |
| New vector database | **Reject for R34** | No measured case justifies another persistence authority/dependency/backup lifecycle |

For representation changes use additive schema/versioned vector metadata,
include dimension/model/normalisation identity, preserve old data until rebuilt
and verified, and make reopening/clear/delete/retry/backup/restoration testable.
An old float32 database must remain readable. Do not reinterpret old BLOB bytes
under a new codec, silently mix embeddings, or mutate existing migrations.
Rollback selects the verified old representation or rebuilds the derived index
from source, without losing source data or Memory revision history.

## RAG, Memory and SQLite audit

P1 measures ingest loading/chunking/embedding/publication, query planning/FTS/
scan/hydration/rerank/packing separately. S2 addresses true input limits first.
Audit sibling Memory semantic loops and document Recall for the same measured
allocation class; reuse successful primitives only after their semantics match.
Keep reranker sequential by default and its native lifetime gate intact. Its
bounded batch diagnostic already exists; dynamic ONNX dimensions alone do not
justify enabling production batching.

Use `EXPLAIN QUERY PLAN`, actual row/byte counts, schema/index definitions,
transaction duration, lock/busy observations and WAL/database size under
controlled read/write/backup/reopen workloads. Inspect current PRAGMAs and
connection pooling before proposing changes. Do not globally switch journal
mode, synchronous durability, indexing policy or vacuum cadence for a synthetic
win. Publish one complete generation, keep FTS/content in the same transaction,
invalidate by generation/model, preserve clear/delete and bounded retries.

## Embedding alternatives, availability and quality

Source S33 pins Qwen3-Embedding-0.6B Q8_0 at revision
`370f27d7550e0def9b39c1f16d3fbaa13aa67728` with SHA256 in Doctor. Existing
Nomic installs remain supported. Model cards are upstream facts, not Hermaeus
quality results; sources E01-E03 are in [10](10-technology-decisions.md).

| Candidate | Verified availability versus remaining gate | R34 decision |
| --- | --- | --- |
| Current Qwen3 0.6B Q8_0 | Pinned existing artifact; publisher documents instruction-aware text embeddings and variable dimensions | **Retain baseline**; measure actual prefix/pooling/normalisation and server outputs, not just model card dimensions |
| Qwen3 4B/8B | Publisher lists larger family members; greater residency/deployment cost must be measured | **Defer by default** on 6/8 GB targets; only include if a concrete quality failure warrants cost |
| EmbeddingGemma 300M | Google model card and ggml-org GGUF card verify a text candidate and llama.cpp embeddings instructions | **Compare conditionally** after exact artifact revision/hash, model terms, input prefixes, pooling, normalisation and output dimensions work in selected runtime |
| EmbeddingGemma 2 text use | Primary model card exists and describes newer text/multimodal architecture | **Defer compatibility/adoption**: no compatible Hermaeus GGUF/runtime execution was verified. Evaluate text only if exact artefact/runtime proof is later obtained; no automatic image/audio/video scope |

Use the shipped `RagEvalService` harness, not the proposed expanded harness as
if it existed. Freeze source-labelled cases with expected_sources,
answer_keywords and should_refuse; measure Recall@K, MRR, citation hit rate,
unsupported-answer/refusal accuracy, indexing/query latency, storage, model
load/residency and competition with Chat/voice. Reindex into separate scratch
generations per embedding identity. Compare both retrieval-only and identical
answer-model end-to-end runs, with/without the existing reranker. Do not compare
vectors from incompatible models directly. No default model switch is selected
without quality preservation, practical resource benefit and owner approval.
