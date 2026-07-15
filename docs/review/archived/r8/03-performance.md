# 03 - Performance

## Problem statement

Two optimization commits landed between r6 and r7 without review.
One (`ad618da`) fixed a real win (no redundant llama-server restart
in benchmarks). The other (`aea2326`) put embedding warm-up on the
startup critical path and changed memory recall semantics in a way
that probably hurts retrieval quality. Beyond those, the app has no
startup timing visibility, chat renders unvirtualized, and streaming
rebuilds the whole markdown tree per debounce tick. Rule for this
round: **measure first, keep every change observable in the runtime
log, no speculative tuning.**

## 3.1 Startup phase timing

`InitializeAppAsync` (src/Aether.Desktop/App.axaml.cs:68-122) runs
settings load, six store inits, embedding warm-up, voice probe, and
`vm.InitializeAsync` with no timing. Wrap each phase in a
`Stopwatch` and emit one Info runtime-log entry per phase plus a
total ("Startup: settings 12 ms, stores 85 ms, warm-up 2100 ms,
voice probe 40 ms, viewmodels 300 ms, total 2540 ms"). This is the
baseline every other startup change is judged against.

**Acceptance criteria**

- One log entry per phase, Info level, Service category; total line
  last. No timing in Release-vs-Debug conditionals; always on.
- A unit test covers the formatting helper (pure function taking
  phase name/ms pairs).

## 3.2 Move embedding warm-up off the critical path

App.axaml.cs:85-101 awaits `EmbedAsync("warmup")` (an ONNX model
load, typically seconds) before `vm.InitializeAsync` (line 116), so
conversations and panels do not populate until the embedding model
has loaded, on every launch, even for users not using memory
injection. Change to fire-and-forget: start the warm-up task after
`vm.InitializeAsync` completes, log its duration when it finishes,
keep the existing catch/log. The first chat message may still pay
the cold cost if the user is faster than the warm-up; that is the
correct trade.

**Acceptance criteria**

- `vm.InitializeAsync` no longer awaits warm-up (verifiable in the
  3.1 timing log: warm-up duration reported separately, after
  total).
- Warm-up failure still logs a Warning and never faults startup.

## 3.3 Fix the memory recall regression from aea2326

`MemoryStore.HybridRerankAsync` (src/Aether.Services/MemoryStore.cs,
vector loop around line 376-405) now only admits a vector-matched
memory as a candidate when it was already an FTS hit **or** its
cosine exceeds a hard 0.7, and fetches each such row with an
individual `GetByIdAsync` (N+1). Before `aea2326`, every embedded
memory was scored and eligible. 0.7 cosine is a high bar for typical
embedding models; semantically related memories that share no
keywords with the query are now silently excluded, which is exactly
the case hybrid recall exists for.

Fix, keeping the good part (the id+embedding projection):

- Score all embedded rows as before: `0.5 * ftsScore + 0.5 * cosine`.
- Collect scores in memory, take the top K (K = the existing caller
  limit passed into the method, or its current default) plus all FTS
  hits, then hydrate the non-FTS survivors in **one** query
  (`WHERE id IN (...)` with parameters).
- Delete the 0.7 constant.

**Acceptance criteria**

- Regression test: a memory with no FTS overlap and cosine ~0.6
  (construct vectors directly) surfaces in results when K allows.
- Test that hydration issues a bounded number of queries (assert via
  a counting connection wrapper if available, otherwise assert the
  SQL contains `IN` and is executed once; implementer's choice).
- Existing hybrid-recall tests still pass unchanged.

## 3.4 Long-conversation rendering

ChatView renders `Messages` in a plain `ItemsControl` inside a
`ScrollViewer` (ChatView.axaml:305-310): every message materializes
its full control tree (including a MarkdownViewer each) on
conversation open, and stays alive forever. For long conversations
this is the dominant open-conversation cost.

Approach: pagination, not virtualization (virtualizing panels
interact badly with variable-height selectable text and the
scroll-to-bottom behavior; rejected in doc 05). Render the most
recent 100 messages; a "Show earlier messages" button at the top of
the scroll prepends the next 100. `ChatViewModel` owns the window
(computed view over the full loaded list; persistence and memory
extraction continue to see all messages).

Measure before/after with the same conversation (500+ messages,
scripted; report open time in the PR description from the 3.1-style
stopwatch placed temporarily or from manual timing).

**Acceptance criteria**

- Conversation open renders at most 100 message controls (assert the
  windowed collection size at VM level; 100 is a named constant).
- Sending or streaming a message never trims the window below the
  newest messages; scroll-to-bottom behavior unchanged.
- History truncation for prompts (ChatViewModel.cs:357-363) still
  operates on the full message list, not the render window (test).

## 3.5 Streaming markdown: rebuild only the tail

`MarkdownViewer` re-parses and rebuilds the entire control tree
every 75 ms debounce tick during streaming
(MarkdownViewer.cs:137-151). Parsing already happens off-thread;
the rebuild is UI-thread work proportional to the whole message so
far, so long replies get progressively jankier.

Change `Render(MarkdownDocument)` to reuse: keep the previous
document's top-level block source texts (each Markdig `Block` knows
its `Span`; slice the source string). On re-render, walk from the
start; while block source text matches the previously rendered
block's source text, keep the existing control; rebuild from the
first mismatch onward (during append-only streaming that is just the
last block or two). Fall back to full rebuild whenever counts or
types disagree. FontSize/IsError changes always full-rebuild
(existing behavior at line 74-77).

**Acceptance criteria**

- Golden non-regression: for ~10 representative markdown documents,
  rendering incrementally in 40-character increments yields a final
  control tree equivalent to a one-shot render (compare block count
  and per-block text; expose an internal test hook, do not
  screenshot).
- A counter of reused vs rebuilt blocks is exposed internally and
  asserted in a streaming test (majority reused for a 3-paragraph
  append-only stream).

## 3.6 Redundant llama-server restart audit

`ad618da` fixed BenchmarkViewModel restarting llama-server when the
model had not changed (BenchmarkViewModel.cs:438-440). Audit the
other restart call sites
(ServicesViewModel.cs:270 `SelectModelAndRestartAsync` and its
callers, including the :719-731 SelectModel path, ChatViewModel model
switching, LocalApi if applicable) for the same pattern and apply
the same guard: if the requested model path equals the currently
loaded one and the server is healthy, skip the restart.

**Acceptance criteria**

- Each call site either has the guard or a one-line code comment
  stating why a restart is always required there.
- VM-level test for at least the ServicesViewModel path: selecting
  the already-active model does not call restart (fake server
  manager records calls).
