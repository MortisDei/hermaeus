# Review Round 9 (r9)

Theme: **stability under real use**. r8 polished the product; first
sustained field use immediately surfaced three problems: a hard crash
(Avalonia "Collection was modified" from cross-thread collection
mutation), 1+ minute to first token on every chat message, and a model
Start button that silently reverted until the fourth click. The crash
fix and its enforcement guard (`UiThreadGuard` / `UiBoundCollection<T>`,
marshaling fixes in `ChatViewModel` and `LogsViewModel`) shipped ahead
of this pack in 0.13.1-alpha, in the same commit that adds it. r9
finishes the job: no UI-bound state may be touched off the UI thread
anywhere, the send path must be observable and fast, and a crash must
never leave orphaned llama-server processes that block the next launch.

Field evidence backing this round (2026-07-15, owner's machine):

- Crash stack in `PanelContainerGenerator.OnItemsChanged`, during a
  chat wait. Root cause class confirmed: background-thread mutation of
  ItemsControl-bound `ObservableCollection`s.
- After the crash, an orphaned `llama-server` (parent PID dead) held
  port 42069, 3 GB RAM, and 33 GPU layers. Nothing ties child servers
  to the app's lifetime on abnormal exit; a new server cannot bind the
  held port, exits instantly, and the Start button reverts with the
  cause buried in the log ring.
- Every send stalls before the first token. `MemoryStore.SearchAsync`
  runs `BackfillEmbeddingsAsync` on the send path (up to 200 sequential
  HTTP embed calls, 60 s timeout each, failures silently retried on
  every subsequent send), and with `Rag.EmbeddingBaseUrl` unset the
  embedding client falls back to the chat server, queueing embeds
  behind generation on a single-slot llama-server. The owner's server
  also runs `--ctx-size 64502`; KV-cache spill is a plausible
  co-factor, so the round mandates measurement before tuning.

Priorities (binding):

- **Never crash the UI from a worker thread.** Enforced, not hoped for.
- **Never lose a llama-server.** Children die with the app, however
  the app dies; port conflicts are diagnosed, named, and actionable.
- **The send path is observable.** Time every stage; fix what the
  numbers convict; no speculative tuning.

## Documents

- `01-send-path-latency.md` - send-path instrumentation, embedding
  backfill off the send path, fast-fail query embedding, embedding
  endpoint fallback visibility, oversized-context advisory.
- `02-server-lifecycle.md` - job-object kill-on-close, port preflight
  with named owner, orphan detection with an explicit user-approved
  stop, honest Starting-state transitions.
- `03-ui-thread-safety.md` - one RunOnUi helper, full ViewModel sweep
  onto `UiBoundCollection<T>` with marshaled service events, and an
  architecture test that keeps it that way.
- `04-roadmap.md` - version, sequencing, test expectations, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r8 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
explicitly rejected; zero-warning builds (`TreatWarningsAsErrors`
solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable.
Anything touching process launching or termination must follow the
`security-posture` skill.
