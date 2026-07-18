# 04 - Latency truth (first-token accounting, live feedback, log noise)

The r10 server-timings work made slow sends explainable; the owner's
2026-07-18 log shows two places the story is still wrong, plus log
noise that buries the signal.

## 4.1 Explain the invisible decode gap in "before first token"

Second send: server prompt eval finished at 191.2 s
(9,744 tok / 50.95 t/s), but the client recorded first token at
302.5 s. That is 111 s of active decoding (about 950 tokens at
8.6 t/s) during which the orchestrator saw no visible content. First
send shows the same shape: prompt done at 35.6 s, client first token
at 94 s.

`ChatSendOrchestrator.StreamAsync` sets `FirstTokenMs` only on the
first non-empty `ContentDelta` (ChatSendOrchestrator.cs:45-49), so
any stream prefix that is not visible content (reasoning-channel
deltas, tool-call deltas, or transport buffering) is silently counted
as "before first token", and the user stares at a blank bubble.

Investigate with a chat trace which of these the gemma stream
actually emits, then:

- Record `FirstEventMs` (first SSE chunk of any kind) alongside
  `FirstTokenMs` (first visible content) in `ChatSendResult` and
  `ChatSendTiming`.
- The slow-send warning splits the phases: queue/prompt (server
  timings), non-content stream (FirstEvent to FirstToken), content.
- If a reasoning channel is the cause, surface it in the chat UI as
  the existing thinking affordance rather than dropping it (whatever
  the trace shows drives the fix; do not guess).

Acceptance criteria:
- `ChatSendTiming.Format` includes the new split; the warning line
  for a send like the log's would name where the 111 s went.
- Orchestrator tests: a stream with N non-content events before
  content yields FirstEventMs < FirstTokenMs.
- Root cause of the gemma gap written up in the round's findings
  (one paragraph, in the roadmap doc's completion notes).

## 4.2 Live phase feedback during long prompt processing

A 191-second prompt eval currently renders as a frozen empty bubble.
The server timings arrive only on the final chunk, too late to help.
Show a lightweight streaming placeholder driven by what we already
know client-side: elapsed time and phase ("reading prompt", "thinking"
once 4.1 lands, "writing"). No new server polling; `/slots` polling is
explicitly out of scope this round.

Acceptance criteria:
- During a send with no visible content yet, the assistant bubble
  shows phase plus elapsed seconds, updating at most once per second.
- Disappears on first visible token; no flicker for fast sends
  (grace threshold, e.g. only appears after 2 s).

## 4.3 Stop the "Failed to fetch models" spam

"Failed to fetch models from llama.cpp: No connection could be made
... (localhost:39201)" appears 60+ times in the attached log,
including bursts of 3-5 identical lines in the same second.
`LlamaCppService.GetModelsAsync` logs an error per call
(LlamaCppService.cs:66-69) and callers fan out.

Fix, two layers:
- State gating: when the managed chat server for the configured base
  URL is known Stopped, return `[]` without an HTTP attempt and
  without an error log (the composite already treats empty as
  "provider down"). The managed-server state is available via the
  process manager; a connection-refused probe against our own stopped
  server is not an error, it is the expected state.
- Log coalescing: for genuinely unexpected failures, log once per
  state transition (up -> down), not per call. A repeat within the
  same down-state logs at debug/verbose or not at all.

Acceptance criteria:
- A session where the chat server never starts produces at most one
  such line per app run.
- External (unmanaged) llama.cpp base URLs still log a real
  connection failure once.
- Composite refresh behavior (r11 caching tests) unchanged.

## 4.4 Idempotent stop logging

Every shutdown logs the "Stopping... / Stopped, click Start to
launch" pair 3 times per server (six lines, see 04:24:38-40 and
04:34:26-28). Stop is being invoked from multiple shutdown paths.
Make stop idempotent at the logging level: an already-stopped server
logs nothing.

Acceptance criteria:
- One Stopping/Stopped pair per server per shutdown in the runtime
  log; ShutdownDisposalTests extended to assert no duplicate stop
  logging.

## 4.5 Slow-send advisory names the bottleneck

The slow-send warning already carries server prompt tok/ms. With the
hardware profile available, append a diagnosis when it is clear-cut:
prompt eval below ~200 t/s with a real GPU present and CPU
build/offload configured -> "prompt was read at CPU speed (51 t/s);
see Doctor" (ties to 01/1.4). Pure threshold function, tested.

Acceptance criteria:
- Warning line for the log's second send would end with the CPU-speed
  hint; a GPU-offloaded fast prompt eval adds nothing.
