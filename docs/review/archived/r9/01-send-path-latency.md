# 01 - Send-path latency

## Problem statement

Every chat message stalls 1+ minute before the first token (thinking
indicator visible, no stream). The pre-stream path in
`ChatViewModel.SendAsync` is unmeasured, and code inspection convicts
one stage outright: `MemoryStore.SearchAsync` calls
`BackfillEmbeddingsAsync` (src/Aether.Services/MemoryStore.cs:442-471)
synchronously before every recall. That method selects up to 200
unembedded rows and embeds them one HTTP call at a time; every failure
is swallowed and retried on the next send, forever, so a down or
misconfigured embedding endpoint taxes every message indefinitely.
Compounding it, `LlamaCppEmbeddingService` uses a 60 s timeout
(src/Aether.Rag/Embeddings/LlamaCppEmbeddingService.cs:15) and, when
`Rag.EmbeddingBaseUrl` is blank, silently falls back to the chat
server's URL (line 36), so embed calls queue behind generation on a
single-slot llama-server. Rule for the round: instrument first (1.1),
then fix the convicted stages; anything the numbers do not convict is
out of scope.

## 1.1 Instrument the send path

`SendAsync` (src/Aether.ViewModels/ChatViewModel.cs, ~line 362) awaits
`BuildMemoryInjectionAsync`, then history truncation, then
`ChatSendOrchestrator.StreamAsync`. Time each stage: memory recall
(`SearchAsync`), injection selection, lesson context, prompt build,
and first-token wait (already measured as `FirstTokenMs`). Surface the
breakdown in `PerformanceLog` ("recall 240 ms, select 3 ms, first
token 950 ms, ...") and persist it on the chat trace
(`ChatTraceViewModel` / `ChatTraceEntry` gain a pre-stream breakdown
field; string or per-stage longs, implementer's choice, but it must
round-trip through `ChatTraceService`).

**Acceptance criteria**

- After one send, the trace panel shows where pre-stream time went,
  with no stage unaccounted for by more than a few ms.
- Timing is always on; no Debug/Release conditionals.
- A unit test covers the breakdown formatter as a pure function.

## 1.2 Move embedding backfill off the send path

`SearchAsync` must never embed anything except the query. Remove the
`BackfillEmbeddingsAsync` call from `HybridRerankAsync`
(src/Aether.Services/MemoryStore.cs:354) and run backfill from a
background loop instead: once shortly after startup (after the r8
warm-up completes, not before) and after memory writes, with a
failure cooldown so rows that fail to embed are not retried more than
once per interval (suggested: 10 minutes, capped attempts per row per
session). Rows without embeddings continue to participate in recall
via their FTS rank exactly as today (MemoryStore.cs:430-433 already
handles this).

**Acceptance criteria**

- No embed HTTP call for stored rows ever occurs inside
  `SearchAsync`; a test with a counting fake `IEmbeddingService`
  proves search embeds exactly once (the query) regardless of how
  many rows lack embeddings.
- Backfill failure logs one Warning per batch, not one per row, and
  respects the cooldown (testable via the counting fake and a fake
  clock or injected interval).
- A previously unembedded row becomes recallable by vector once the
  background pass has run (existing hybrid tests adapted).

## 1.3 Fast-fail the query embedding

A recall query embedding that cannot complete quickly is worth less
than a fast FTS-only answer. In `HybridRerankAsync`, wrap the query
`EmbedAsync` in a short linked-token timeout (suggested 3 s,
constant, not user-configurable) and fall back to the existing
rank-only scoring path (MemoryStore.cs:361-366) on timeout or error.
Log the fallback as a single Warning per process lifetime (a repeated
per-message warning is noise; a flag latch is fine), including the
endpoint it tried.

**Acceptance criteria**

- With a hanging fake embedding service, `SearchAsync` returns
  FTS-ranked results in under ~3.5 s (fake clock acceptable) instead
  of inheriting the HTTP client's 60 s timeout.
- The Warning appears once across repeated sends.

## 1.4 Make the embedding endpoint fallback visible

The silent fallback to `Llm.LlamaCppBaseUrl`
(LlamaCppEmbeddingService.cs:28-38) is a footgun: memory recall and
RAG embeds then contend with chat generation. Keep the fallback (it
is the only zero-config path) but surface it: one Info runtime-log
line at first use naming both URLs, and a Doctor advisory (Info
severity, in the existing RAG section, DoctorService.Rag.cs) when
`Rag.EmbeddingBaseUrl` is blank while memory or RAG features are
enabled, recommending a dedicated embeddings server.

**Acceptance criteria**

- Doctor scan with blank `EmbeddingBaseUrl` and memory enabled yields
  the advisory; setting the URL clears it.
- The runtime log line fires once per process, not per call.

## 1.5 Oversized context advisory

The owner's chat server ran `--ctx-size 64502`. Large KV caches spill
out of VRAM and make prompt processing crawl; users have no signal
that context size is the culprit. In the Services view, when a
managed server's configured `ContextSize` exceeds 16384, show a
static inline note (not a toast) that large contexts slow prompt
processing and increase memory use, with the configured value.
Doctor gets a matching Info advisory. No auto-tuning, no clamping;
this is information only.

**Acceptance criteria**

- Note visible at ContextSize 32768, absent at 8192, updates when the
  field changes.
- Doctor advisory carries the actual configured value.
