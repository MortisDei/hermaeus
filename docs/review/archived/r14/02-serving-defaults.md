# 02 - Serving defaults (slots, context truth, cache reuse)

Aether launches llama-server with only `-m/--port/--host/--ctx-size`
plus optional threads/gpu-layers (ServerProcessManager.cs:357-406).
Everything else rides on llama.cpp defaults, and current builds
default to 4 parallel slots. The owner's log shows what that costs a
single-user chat app:

- 2026-07-18 05:21: `n_slots = 4, n_ctx_slot = 16128`. The user
  configured a much larger context; each request actually gets a
  quarter of it. On 07-16 the same split gave `n_ctx_slot = 4096`.
- 2026-07-18 05:25 (second send, 9,744-token prompt): the scheduler
  chose a cold slot (`selected slot by LRU, t_last = -1`) and
  reprocessed the entire prompt at 51 tokens/sec for 191 seconds, even
  though another slot held a 4,045-token cache from the send three
  minutes earlier.

## 2.1 Default to a single slot

Add `Slots` to `ServerConfig` (default 1) and emit `--parallel N`.
Chat and embeddings servers both default to 1; the embeddings server
processes one query at a time today anyway. Existing saved configs
without the property behave as 1.

This is the biggest single win in the round after the GPU build: with
one slot the full `--ctx-size` belongs to the conversation and every
send hits the same KV cache, so a follow-up send only reprocesses the
suffix that changed.

Acceptance criteria:
- `BuildLaunchArguments` emits `--parallel 1` by default; a
  user-supplied `--parallel` in ExtraArgs wins (same precedence rule
  the code already applies for `--pooling`).
- Services page exposes Slots as an advanced field, not a headline
  control.
- Launch log line confirms `n_ctx_slot` equals the configured context
  size on a default config.

## 2.2 Explicit prompt-cache behavior

Aether never sends `cache_prompt` in chat requests (no occurrence in
src). Current llama-server defaults it to true, and the log shows
reuse working for tiny requests, but we are one upstream default-flip
away from silently reprocessing every prompt. Set `cache_prompt: true`
explicitly in `LlamaCppService` chat requests, and pass
`--cache-reuse 256` at launch so edited or re-rolled prompts can reuse
KV chunks past the first divergence instead of falling back to the
longest exact prefix.

Acceptance criteria:
- Request-body test asserts `cache_prompt` is present and true for
  llama.cpp chat calls (snake_case wire format).
- `--cache-reuse 256` appears in default launch args; ExtraArgs
  override wins.

## 2.3 Context-size truth in the UI and limit math

`ProbeContextLengthAsync` feeds `ProbedContextLength` from `/props`.
Verify what that value is under multi-slot (total vs per-slot) and
make every consumer use the per-slot number, because that is the real
ceiling a conversation hits. With 2.1 defaulting slots to 1 the values
coincide, but the math must stay honest for anyone who raises Slots.

Acceptance criteria:
- A unit test documents the chosen source field and the per-slot
  computation (`n_ctx / slots` when only totals are exposed).
- The chat context-limit warning (existing trim/limit machinery)
  triggers against the per-slot value.

## 2.4 Silence the embeddings batch clamp

Every embeddings start logs the upstream warning pair "embeddings
enabled with n_batch (2048) > n_ubatch (512) ... setting n_batch =
n_ubatch = 512". Pass `-b 512 -ub 512` (or one coherent pair) for
embeddings-mode servers so the server starts clean. Cosmetic, small,
zero risk.

Acceptance criteria:
- Embeddings launch args include the batch pair; the clamp warning no
  longer appears in a fresh start's log.
