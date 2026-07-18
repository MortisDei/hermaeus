# Changelog archive

The CHANGELOG.md in root only contains the current 10 versions of changelogs. The rest are archived here in line with the 10 version limit in the main changelog.

## [0.9.44-alpha] - 2026-07-12

Implements docs/review r4 in full: an audit of the r3 agent loop and lesson
store found the loop's feedback channels were half-wired and the lesson
store's contradiction mechanic was structurally unreachable. r4 fixes both,
then lands r3's deferred item (automatic lesson capture from task-terminal
states) on top.

### Interaction and failure semantics

- **User-reply channel.** `IAgentService.AppendUserReplyAsync` answers a
  task's `ask_user` question: appends the reply to the transcript (new
  `"user"` role) and resumes the task. Refuses when a tool approval is
  pending - a reply is never a substitute for an approval decision. The
  workbench shows a reply box that resumes the autonomous loop after
  sending, the same approve-and-continue shape as a gated-action approval
  (both now share `AgentViewModel.ResumeAgentLoopIfRunnableAsync`).
- **Real failure semantics.** `AgentTaskState.ConsecutiveStepErrors` tracks
  unparseable model responses; three in a row fails the task (recorded on
  `Decisions`, every bad step still kept in the transcript) instead of
  looping forever in `WaitingForUser`. Any step that parses successfully
  resets the counter. An unhandled model-call or tool-execution error now
  hands the task to `WaitingForUser` before rethrowing, instead of leaving
  it stranded in `Running`. Hitting `Agent.MaxAutoSteps` while still
  `Running` now also lands in `WaitingForUser` with a logged/transcripted
  note, rather than stopping silently.
- **Approved-tool transcript entries.** A gated action's result now reaches
  the transcript once approved, not just `ToolResults`' last-five window -
  previously the results of the most consequential actions could age out of
  the model's view. Removed the unused, transcript-and-lesson-bypassing
  `AgentService.ExecuteApprovedToolAsync`.
- **Native tool-call fidelity.** A tool-calling model's own prose now becomes
  the recorded thought summary instead of a synthetic "Calling X."
  placeholder; a turn requesting more than one tool call notes the dropped
  ones so the model sees next step that only the first ran.
- Small hygiene: `AgentContextBuilder`'s one-word workspace search heuristic
  now only runs on a task's first step (later steps have transcript history
  and navigation tools); `PendingSteps` drops entries once they appear in
  `CompletedSteps`; stale `KnownRisks` text about commands being blocked
  fixed to match actual policy.

### Lessons v2

- **Signature redesign.** Command/patch/approval dedupe signatures no longer
  bake in the outcome (was `command:{cmd}:{ok|fail}:{token}`, now
  `command:{cmd}`) - previously a command that failed then succeeded created
  two permanently-separate rows and the store's contradiction logic was
  unreachable for these kinds. Schema bumped to v2 with a migration that
  collapses existing outcome-suffixed rows (keeping the one with the most
  evidence) on next start.
- **Approval counter-evidence.** Approving a gated action now records
  counter-evidence against a prior rejection lesson for the same tool (via
  new `AgentLessonEvidence.CounterOnly`, which only ever weakens an
  existing lesson, never originates one), so a single early rejection can no
  longer become a standing lesson the user's own later approvals can't
  soften.
- **Structured command outcomes.** `AgentToolResult` gained `ExitCode` and
  `TimedOut`; lesson capture uses the real exit code instead of string-
  sniffing `"Exit code 0"` from the summary text, and skips capture entirely
  on a timeout (which says nothing about whether the command itself works).
- **Task-terminal capture** (the item r3 deferred). On `Complete` or
  `Failed`, a lesson keyed by a deterministic goal fingerprint
  (`AgentLessonText`: tokenize, sort, hash - no LLM) records whether goals
  like it tend to work out here; an uneventful success records nothing.
  Separately, every lesson actually shown to the model during a task that
  completes successfully gets its evidence confirmed via new
  `ILessonStore.ConfirmAsync` - the compounding half of the self-learning
  loop.
- **Relevance-aware injection.** Lesson candidates are now ranked by pinned
  status, confidence, and shared terms with the current goal/recent tools
  before packing, instead of confidence alone, so an unrelated
  high-confidence lesson can no longer crowd out one that actually bears on
  the current step.
- Polish: `SqliteLessonStore` timestamps now round-trip with
  `DateTimeStyles.RoundtripKind` instead of applying local-time conversion;
  the lesson error-token regex is now static/compiled.

### Chat-side lesson consumption (optional)

- New `Memory.ConsumeAgentLessonsInChat` setting (Settings > Memory; off by
  default). When on, `ChatViewModel` folds Global-scope agent lessons into
  the system prompt as their own read-only markdown block, alongside stored
  memories. Deliberately kept out of `BuildMemoryInjectionAsync`'s
  `InjectedMemoryIds` list, so a `[MEMORY_UPDATE]`/`[MEMORY_FORGET]` marker
  can never target a lesson - the Agent workbench's Lessons panel remains
  the only write path.

23 new tests (327 total), zero warnings. Full details in
docs/review/archived/r4/.

## [0.9.43-alpha] - 2026-07-12

Implements docs/review r3 (agent capability, memory, self-learning) in full.
r2's roadmap was fully actioned; this round targets the Agent workbench's gap
against Claude Code/Codex, upgrades memory recall and lifecycle, and adds a
new per-machine agent self-learning (lesson) store.

### Agent capability

- **Persisted step transcript.** Each task keeps `transcript.jsonl` (one line
  per assistant thought / tool result), replayed budgeted (most-recent-first)
  into every step's context (`Agent.TranscriptTokenBudget`, default 12,000
  tokens) instead of only the last five, 4000-char-truncated tool results.
  Fixed a latent bug found while building this: `AgentJson.Options` was
  indented, so anything written through it into a JSONL file (including the
  pre-existing `agent.trace.jsonl`) spread one entry's JSON across multiple
  physical lines, corrupting line-oriented replay; both now use a new
  `AgentJson.CompactOptions`.
- **Autonomous runs.** `IAgentService.RunAsync` loops `RunStepAsync` until a
  final answer, a question for the user, a gated action needing approval, a
  blocked task, or `Agent.MaxAutoSteps` (default 20). Start and
  approve-and-continue both drive the loop; a manual single-step advance
  remains available. Never auto-approves anything.
- **Native tool calling.** `LlmChatOptions.Tools` / `LlmStreamEvent.ToolCalls`
  added to the core LLM contract; implemented in `OpenAiService`,
  `LlamaCppService` (shared OpenAI-compatible wire format via the new
  `OpenAiCompatibleToolWire` helper, including streamed tool-call-delta
  accumulation), and `OllamaService` (whole, non-fragmented tool calls). The
  agent declares its fixed tool set natively and falls back to the JSON
  prompt protocol automatically when unsupported.
- **Surgical edits.** `edit_file` (unique `old_string`/`new_string` replace)
  and `create_file` (new files only, refuses to overwrite), both
  approval-gated with the same path containment as every other tool.
- **Navigation tools.** `glob_files`, regex + context-line support in
  `search_files`, line-ranged `read_file`, subdirectory/depth-scoped
  `list_files`.
- **`set_plan`** tool: a visible, agent-maintained plan checklist; executes
  immediately since it only mutates task state.
- **`run_command` template families.** Replaces the fixed verbatim-string
  dictionary with families that accept an optional, containment-checked
  path/script argument (`dotnet build`/`test [project]`, `npm test`,
  `npm run <script>` limited to scripts the workspace's own `package.json`
  declares, `cargo build`/`test`, `pytest [path]`); the safety gate now
  matches on family, not exact string, so a workspace declaring bare
  `dotnet test` also covers `dotnet test tests/Foo.csproj`. Command output
  bounded to the last 200 lines instead of dumped unbounded then hard-cut
  mid-line. Per-task remembered approvals: an identical repeat of an
  already-approved command string may auto-execute for the rest of that
  task; a different command, even in the same family, still requires its own
  approval, and the memory never survives past the task.

### Agent self-learning (new)

- New per-machine lesson store (`agent/lessons.db`, `ILessonStore` /
  `SqliteLessonStore`) capturing deterministic, evidence-backed observations
  from `run_command` outcomes, `edit_file`/`create_file`/`apply_draft_patch`
  outcomes, and user approval rejections, plus model-stated
  `[LESSON: ...]` observations. Repeated matching evidence for the same
  signature reinforces one row (confidence and evidence count rise, capped);
  a contradiction decays confidence and, below a floor, retires the lesson,
  flipping it to the new outcome so later confirming evidence can revive it.
  Pinning a lesson locks it against further automatic changes.
- Relevant lessons (global plus the current workspace) are injected into
  every step's context pack, most confident first.
- New Lessons panel in the Agent workbench: view, inline edit, pin, retire,
  reactivate, delete. Lessons only ever inform the model; the safety gate
  never reads the lesson store.

### Memory

- **Hybrid recall.** `MemoryStore.SearchAsync` blends FTS rank with cosine
  similarity against a query embedding when an embedding model is
  configured (additive schema v4, `embedding` BLOB column, lazy backfill on
  first hybrid search), falling back to the existing FTS/LIKE behaviour
  otherwise. Every result now carries a `RelevanceScore`.
- **Relevance-aware injection.** `MemoryInjectionService` selection now
  blends that relevance score with importance instead of sorting
  recency-first; unaffected for memories not retrieved via search.
- **Lifecycle.** New `RecallCount`/`LastRecalledAt` columns (schema v4);
  `MemoryLifecycle.ComputeEffectiveImportance` decays a memory's effective
  importance the longer it goes unrecalled (pinned memories never decay).
  `IMemoryStore.ArchiveStaleMemoriesAsync` auto-archives (never deletes)
  memories that decay below a floor and stay unrecalled long enough; the
  Memories panel runs it on open and now sorts by effective importance and
  shows recall stats.
- **Update/forget markers.** A model can correct or retire a memory shown to
  it this turn with `[MEMORY_UPDATE: <id> | <content>]` /
  `[MEMORY_FORGET: <id>]`; only ids actually injected into that turn are
  honored, everything else is ignored and logged
  (`ConversationMemoryService.ApplyInjectedMemoryMarkersAsync`).
- **Structured extraction.** Auto-summary now asks for JSON
  (content/category/importance/tags) instead of `[MEMORY: ...]` markers with
  keyword heuristics, giving model-supplied metadata directly; the marker
  format remains the fallback.
- **Unified workspace memory.** Deleted the legacy file-backed
  `FileAgentWorkspaceMemoryStore` (superseded by the SQLite-backed
  `WorkspaceMemoryStore` since r2); tests that exercised legacy-file import
  now write the legacy format directly instead of depending on the removed
  class.

### Housekeeping

- Moved `tests/Aether.Tests` to `src/Aether.Tests`, alongside every other
  project, updating the solution, project references, and every doc/skill
  that pointed at the old path.
- Added a CI workflow (`.github/workflows/ci.yml`): restore/build/test on
  ubuntu-latest and windows-latest.
- Added `scripts/coverage.ps1` / `scripts/coverage.sh`, a line-coverage
  ratchet via coverlet (floor recorded once measured; only ratchets up).
- Removed pointless `Task.Run` wrappers around synchronous work in
  `MemoryInjectionService`/`MemoryExtractionService`; the agent trace now
  logs the full context pack once per task plus a small delta per step
  instead of the full pack on every step.


## [0.9.42-alpha] - 2026-07-11

Implements docs/review/03-next-level-roadmap.md Phases 1 through 4 in full
(r2 review). Each phase is independently shippable; landed together here.

### Phase 1 - Provenance convergence

- **RAG citations are now a typed stream event, not a sentinel string.**
  `RagQueryService.StreamQueryAsync` used to interleave plain answer tokens
  with magic-prefixed `"__RAG_SOURCES__...__END_SOURCES__"` and
  `"__RAG_TRACE__...__END_TRACE__"` strings that every consumer had to
  independently detect and strip. It now yields a closed `RagStreamEvent`
  (`Token`/`Sources`/`Trace`) with a typed `RagTraceSummary` payload.
  `RagStreamProtocol` (the shared sentinel parser) is deleted; `RagViewModel`,
  `RagEvalService`, and `Aether.LocalApi` all switch on `Kind` instead. Fixed
  a real bug in passing: the trace event now actually carries
  `ExpandedQuery`/`QueryVariants`/`PlannerNotes`/`ContextPackingSummary`,
  which the persisted trace always computed but the old sentinel JSON never
  included, so `RagViewModel`'s Context Transparency fields for those were
  silently always empty.
- **Memory gained a structured `Source` (`SourceReference`)** alongside the
  existing `SourceConversationId`. Additive `MemoryStore` schema migration
  (v3, `source_json` column). Rows written before this migration backfill a
  `SourceReference` from `SourceConversationId` at read time; no data
  rewrite. `MemoryExtractionService` and `ConversationMemoryService` now
  populate it with a short content-derived title, the conversation locator,
  and a snippet.
- **Chat now actually injects memory, and shows a Sources panel.** Memory
  injection (`MemoryInjectionService`) existed since early memory work but
  nothing in `ChatViewModel` ever called it. `SendAsync` now searches
  relevant global memories (gated by the existing `Memory.Enabled` setting),
  builds a memory-context block appended to that turn's system prompt, and
  populates a new `MessageViewModel.Sources` collection with the
  `SourceReference`s actually used, rendered as a small chip row under the
  assistant's reply (tooltip shows the memory content).

### Phase 2 - Local API: from demo to substrate

- **Streaming.** `POST /v1/chat/completions` accepts `"stream": true` and
  responds with Server-Sent Events in the OpenAI `chat.completion.chunk`
  wire shape (deliberate wire compatibility with existing SSE clients, not a
  dependency on OpenAI). Buffered JSON stays the default.
- **Per-app tokens, replacing the single shared token.**
  `LocalApiSettings.Tokens` is now a list of named entries
  (`LocalApiTokenEntry`: Id, Name, secret-store reference, created-at);
  Settings > Local API manages the list (add-with-name, revoke individually,
  each action applies and saves immediately rather than waiting for the
  page's main Save button, since a pending revocation that silently reverted
  would be a real footgun for a credential list). `LocalApiTokenAuth`
  authenticates against every configured entry and records which one
  matched; `LocalApiEndpoints` now traces calls under that verified token
  name, with the caller-supplied `X-Aether-Client` header kept alongside it
  only as an unverified display hint. A load-time settings migration
  converts an existing single `ApiToken` into a "Default" named entry so
  upgrading users keep a working integration.
- **Embeddings endpoint.** `POST /v1/embeddings` wraps `IEmbeddingService`,
  returning one vector per input string plus the provider's dimensionality.

### Phase 3 - MCP hardening

- **Per-server tool allowlists.** `McpServerConfig.AllowedTools` restricts
  which of a server's declared tools are actually callable (empty means no
  restriction, matching prior behavior); configurable per server in
  Settings > MCP Servers. Also closes a real gap found while implementing
  this: `McpToolBridge.ExecuteAsync` previously forwarded any
  `mcp:{server}:{tool}` reference to the server without checking it was
  ever declared via `tools/list`; it now verifies the tool is both
  allowlisted and actually declared before calling it.

### Phase 4 - Local-only crash/lifecycle journal

- **`AppLifecycleJournalService`** (`Aether.Core.Services`, so Aether.Rag and
  Aether.Voice can both use it without a reference against the established
  Desktop/ViewModels to Services/Agent/Rag to Core dependency direction):
  one small atomic-write JSON file in the data root recording session start
  time, whether the session exited cleanly, and the last notable operation
  it was performing. Generalizes the ad-hoc preflight logging added for the
  0.9.38-0.9.40 native Kokoro ONNX crash into a reusable mechanism, now also
  wired into the RAG cross-encoder reranker's ONNX session loads. Doctor
  reports a new "Previous session exited cleanly" check, warning and naming
  the last recorded operation when the prior session never recorded a clean
  exit, purely local diagnosis with no telemetry or upload.

### Docs

- docs/features.md, docs/agent.md, and docs/security-review.md updated for
  all of the above (Local API endpoints/tokens, chat Sources panel, MCP
  allowlists, Doctor's clean-shutdown check). docs/security-review.md's
  Local API and MCP rows are refreshed for `0.9.42-alpha`; the rest of that
  document is unchanged from its `0.9.14-alpha` baseline and is flagged as
  such rather than implicitly re-certified.

## [0.9.41-alpha] - 2026-07-11

Second architecture/security review pass (docs/review/, r2). Closes every
Phase 0 item in docs/review/01-code-audit.md: two P1s, eight P2s, and the
resolvable P3s.

### Fixed
- **`LocalApi.Enabled` was a phantom toggle**: nothing ever launched the
  `Aether.LocalApi` host process, so the Settings checkbox did nothing, and
  a manually-launched host ignored the setting entirely. `LocalApiProcessManager`
  now actually starts/stops the host (packaged sibling install, or the
  dev build output when running via `dotnet run`/F5), wired into app startup,
  settings save, and shutdown; `Aether.LocalApi/Program.cs` refuses to serve
  (exit 1) when `Enabled` is false even if launched directly. `build.ps1`/`build.sh`
  now also publish `Aether.LocalApi` into a `LocalApi/` subfolder of the
  release package. A new unauthenticated `/health` endpoint backs the
  process manager's startup health-poll.
- Local API call tracing built `DetailJson` by string-interpolating the
  caller-controlled `X-Aether-Client` header, so a crafted header could
  inject malformed/extra JSON into stored trace records; it's now built
  with `JsonSerializer.Serialize`, and the caller name is stripped of
  control characters and capped at 64 characters.
- `McpClient.CallToolAsync` stringified every tool argument via `ToString()`,
  so a server expecting a JSON number/boolean/object for a declared
  parameter received a string instead; arguments now map by runtime type.
- `McpClient` never drained a spawned server's stderr; a server that logged
  more than the OS pipe buffer would block on its next stderr write, hanging
  every in-flight call until the 30-second timeout with no diagnostic. Stderr
  is now drained continuously into a bounded tail, included in failure
  messages.
- `McpClient` requests made after the server process closed its stdout used
  to hang for the full 30-second per-call timeout; the client now faults all
  outstanding and future calls immediately when the connection closes.
- The agent's `inspect_git_diff` tool read stdout/stderr only after
  `WaitForExit`, which deadlocks against git blocking on a full pipe for a
  large working tree, previously surfacing as a false "git status timed
  out." Streams are now read concurrently with the wait, matching the
  pattern `run_command` already used.
- `apply_draft_patch` wrote the user's approved file changes with a plain
  `File.WriteAllText`; a crash mid-write could truncate their source file.
  It now goes through the same temp-plus-move `AtomicFileWriter` already
  used for state files (`IAgentWorkspaceTools.ApplyDraftPatch` is now
  `ApplyDraftPatchAsync`).
- The native Kokoro model download buffered the full response (a few
  hundred MB) into memory before writing to disk; it now streams via
  `HttpCompletionOption.ResponseHeadersRead`.
- Native Kokoro speech synthesis (phonemize/tokenize/ONNX inference) ran on
  the caller's thread, which is the UI thread for `SpeakAsync`/`PreviewVoiceAsync`,
  freezing the app for the duration; it's now offloaded to a background thread.
- `secrets.local.key` (the AES key protecting every fallback-stored secret)
  was written with a bare `File.WriteAllText`, restricting permissions only
  after the file was already visible, and non-atomically. It's now written
  temp-then-move with permissions restricted before the move, matching
  `secrets.local.json`'s own write path.
- A secret that fails to decrypt under both the current and legacy format
  (e.g. a corrupted or replaced `secrets.local.key`) used to silently
  resolve to an empty string; `SecretStore` now logs a warning through
  `IRuntimeLogService` so the failure is diagnosable instead of surfacing
  only as a downstream provider auth failure.
- The local API's chat completion endpoint only forwarded
  Temperature/MaxTokens, ignoring the five other sampling parameters added
  in 0.9.39 and any saved per-model profile defaults, so API callers got
  different output than the desktop app for the same model. It now applies
  the same explicit-value / model-profile-default / global-setting
  precedence the desktop `ChatViewModel` uses.
- The local API's RAG source parsing kept a third private copy of the
  `__RAG_SOURCES__` sentinel regex/JSON-shape logic; it now calls the
  shared `RagStreamProtocol.ParseSources` like every other consumer.

### Added
- `LocalApiSettingsViewModel.ProcessStatusLabel`, shown in Settings > Local
  API, so the Enable checkbox has a visible, honest status ("Running",
  "Stopped", "Stopped (no token configured)", etc.) instead of an assumed
  effect.

### Docs
- docs/review/ now holds a second review round (r2): a code audit, an
  architecture assessment of the post-r1 v2.0/v3.0 code, and a next-level
  roadmap. r1 moved to docs/review/archived/r1/.

## [0.9.40-alpha] - 2026-07-07

### Fixed
- `NativeKokoroVoiceProvider`/`KokoroOnnxModel` resolved `LocalAiAssetsRoot` once at DI-singleton construction time and cached it forever; changing the setting later in the same running session silently kept reading/writing the old location. The assets root is now re-resolved from current settings on every access, and a loaded ONNX session/voice-style cache is dropped and reloaded if the root changes underneath it.
- Diagnosed a real crash: the Kokoro Native install actually completed successfully (model + all 28 voices downloaded, SHA256 verified) but a subsequent model load via `new InferenceSession(...)` crashed the whole process natively (bypassing all managed exception handling) on at least one machine. `InferenceSession` construction now uses explicit, conservative `SessionOptions` (basic graph optimization, single-threaded, sequential execution) instead of the all-optimizations default, trading a little inference speed for avoiding whatever fused/parallel kernel path was crashing.

## [0.9.39-alpha] - 2026-07-07

### Added
- Full LLM sampling parameters: top P, top K, min P, repeat penalty, frequency penalty, and presence penalty, alongside the existing temperature. Available as global defaults (Settings > LLM), per-model overrides (Model Management), and live per-conversation editing (`ChatViewModel`), mirroring the existing temperature pattern throughout. Each is optional; leaving a field blank sends nothing for it and the provider uses its own default. llama.cpp and Ollama receive all six; OpenAI-compatible endpoints only receive the three the real OpenAI API supports (top_p, frequency_penalty, presence_penalty), since top_k/min_p/repeat_penalty are llama.cpp/Ollama-only extensions.
- Services panel: the Stopped status now reads "Stopped, click Start to launch" instead of just "Stopped", plus a tooltip on the status dot explaining that it's independent of Doctor's "found on disk" check.

### Fixed
- Settings > Voice: Python-path/venv fields no longer stay enabled when a non-Python-backed voice provider (Kokoro Native, F5-TTS, OpenAI) is selected; they're now gated the same way the existing XTTS-only fields already were.

## [0.9.38-alpha] - 2026-07-07

### Fixed
- `SettingsViewModel.Shutdown()` stopped `XttsProcessManager` but never `KokoroProcessManager`, so the Python Kokoro process could outlive an app shutdown that didn't go through DI container disposal.

### Added
- `KokoroOnnxModel` now writes a `kokoro_native_install.log` line (with the on-disk model file size) immediately before each `InferenceSession` load, both during install and lazy-load. A native ONNX Runtime fault during session creation bypasses managed exception handling and kills the process with no other trace; this at least pins down whether a crash happens at session load versus during download/verification.

## [0.9.37-alpha] - 2026-07-07

ViewModel orchestration extraction, part 6: the three large orchestrators previously deliberately deferred (`ChatViewModel.SendAsync`, `ChatViewModel.CompareSelectedModelsAsync`, `RagViewModel.IngestAsync`).

### Added
- `Aether.Core.Services.ChatSendOrchestrator`: drives one streamed chat completion (usage/timing/cancellation/error classification), extracted from `ChatViewModel.SendAsync`. The ViewModel now only owns UI-facing message-state mutation; the streaming call itself is testable against a fake `ILlmService` with no UI plumbing.
- `Aether.Core.Services.ChatStreamAccumulator`: the render-batching throttle (flush on a time/size threshold) previously a local closure inside `SendAsync`.
- `Aether.ViewModels.ModelCompareOrchestrator`: target selection (explicit selection vs. fallback to the active model, capped at 4) and `EvalRun` to `ModelCompareResultViewModel` mapping, extracted from `ChatViewModel.CompareSelectedModelsAsync`.
- `Aether.ViewModels.RagIngestServiceSuspension`: the suspend-competing-services/restore sequence (managed embedding server, XTTS, Kokoro) extracted from `RagViewModel.IngestAsync`.
- `Aether.Rag.RagIngestRequestBuilder`: builds/updates the target `RagDataset` for an ingest run and renders the ingest health summary line, extracted from `RagViewModel.IngestAsync`.

### Fixed
- RAG ingest's service-restore failures (embedding server, XTTS, Kokoro) are now actually logged; previously the per-service error list built during restore was discarded without ever being surfaced.

## [0.9.36-alpha] - 2026-07-07

ViewModel orchestration extraction, part 5.

### Added
- `Aether.Core.Services.ChatConversationBuilder.AutoTitleFrom`: derives a conversation title from its first user message, extracted from `ChatViewModel.PersistAsync`.

## [0.9.35-alpha] - 2026-07-07

ViewModel orchestration extraction, part 4.

### Added
- `Aether.Agent.Services.AgentPatchReviewService`: the apply/reject/block status-transition, persist, and audit sequence shared by `AgentViewModel`'s `ApprovePatchAsync`/`RejectPatchAsync`/`BlockPatchAsync`. The ViewModel still owns the approval-preview UI flow (`RequestDraftPatchPreview`); this owns what happens once a decision is made.

## [0.9.34-alpha] - 2026-07-07

ViewModel orchestration extraction, part 3.

### Added
- `Aether.Services.ChatTraceService`: persists and reloads chat traces through the shared `ITraceStore`, extracted from `ChatViewModel`'s `AddChatTrace`/`PersistChatTraceAsync`/`LoadPersistedChatTracesAsync`/`TryParseChatTraceDetail` group. `ChatViewModel`'s constructor now takes `ChatTraceService?` instead of `ITraceStore?`; the ViewModel only builds/binds the UI-facing `ChatTraceViewModel`, the service owns the trace-store read/write and DetailJson mapping.

## [0.9.33-alpha] - 2026-07-07

ViewModel orchestration extraction, part 2.

### Added
- `Aether.Rag.RagDatasetHealthService`: computes a dataset's source/duplicate/missing/stale-file counts from its chunks, extracted from `RagViewModel.RefreshDatasetManagerAsync`. Testable against a temp directory instead of only through the full ViewModel.

## [0.9.32-alpha] - 2026-07-07

ViewModel orchestration extraction, part 1 (docs/review/01-architecture-review.md
item 5): pulls pure, testable logic out of `ChatViewModel`/`AgentViewModel`/
`RagViewModel` into plain services, leaving the ViewModels thinner. First of
several slices.

### Added
- `Aether.Core.Services.ChatContextUsageCalculator`: resolves the effective context window and computes usage label/percent/warning-level display values, extracted from `ChatViewModel.UpdateContextUsage`/`ResolveContextWindowLimit`. `ChatViewModel.TruncateHistoryToContextWindow` now delegates to the calculator's version, keeping its existing public signature for the one test call site that depends on it.
- `Aether.Rag.RagStreamProtocol`: parses the `__RAG_SOURCES__`/`__RAG_TRACE__` sentinel blocks `RagQueryService` interleaves into its answer stream. `RagViewModel` and `Aether.Rag.Eval.RagEvalService` each had their own independent copy of this exact parsing logic; both now share one implementation.
- `Aether.Agent.Models.WorkspaceActivationSelection`: resolves a `WorkspaceActivation`'s preferred model/dataset ids against a ViewModel's loaded candidate list. `ChatViewModel` and `AgentViewModel` each duplicated this id-to-object lookup inline.
- Unit tests for all three new pure-logic classes (`ChatContextUsageCalculatorTests`, `RagStreamProtocolTests`, `WorkspaceActivationSelectionTests`), testable without any UI plumbing for the first time.

## [0.9.31-alpha] - 2026-07-07

Closes the remaining half of docs/review/06-technical-debt.md item 4
(provider knowledge smeared across layers) without a settings schema
migration.

### Changed
- `PrivacyAuditService.IsChatProviderEnabled` and `CompositeLlmService.GetModelsAsync` independently matched a provider tag to the same `Llm.LlamaCppEnabled`/`Llm.OpenAiEnabled`/`RuntimeProfiles` flags; both now call one new `CompositeLlmService.IsProviderEnabled(tag, settings)`. Deliberately not done: replacing the two named boolean fields with a tag-keyed dictionary, which would need a `settings.json` migration for a benefit (adding a 4th provider) that hasn't materialized yet.

## [0.9.30-alpha] - 2026-07-07

Closes docs/review/01-architecture-review.md item 6 (settings monolith) with
a reasoned reject rather than a mass rewrite.

### Changed
- Docs only: evaluated giving each service only its own settings section instead of full `ISettingsService` access. Rejected as a retrofit: `ISettingsService` is referenced from 123 files, and settings persist as one atomic file/save/migrate/change-notification, so a per-section wrapper would still need the whole object for the operations that matter, making a mechanical rewrite mostly cosmetic. No bug traces back to this. New services should still prefer taking only the section they need going forward.

## [0.9.29-alpha] - 2026-07-07

Interface ceremony cleanup (docs/review/06-technical-debt.md item 3):
collapses single-implementation interfaces that had no test double and no
cross-project seam requirement to concrete classes. Internal refactor, no
behavior change.

### Changed
- 13 interfaces collapsed to concrete classes: `IBackupService`, `IBenchmarkService`, `IConversationExportService`, `IEvalEngine`, `IInspectionEngine`, `ILocalAiSetupService`, `IMemoryExtractionService`, `IMemoryInjectionService`, `IModelProfileService`, `IPrivacyAuditService`, `IRedactionService`, `IRuntimeProfileService`, `ITrustService`. Consumers now depend on the class directly; DI registers the concrete type instead of an interface mapping.
- Small data types that lived alongside a deleted interface file (`BackupResult`, `ConversationExportFormat`, `RuntimeHealth`) moved to their own file in `Aether.Core.Services`.
- The remaining single-implementation interfaces were audited and kept deliberately: about a dozen have real test-double implementations in the test suite (a genuine seam), and `ITraceStore`/`IAgentRetrievalService`/`IMcpToolBridge` are cross-project boundaries `Aether.Agent`/`Aether.Rag` are architecturally forbidden from crossing directly.

## [0.9.28-alpha] - 2026-07-07

Closes docs/review/02-dependency-review.md's staged item 4 (evaluate an
out-of-band ONNX Runtime download) with a reasoned reject rather than an
implementation.

### Changed
- Docs only: evaluated shipping ONNX Runtime as a Doctor-installed component instead of a bundled package. Rejected: `Aether.Voice`'s native Kokoro provider (now the default voice provider) also depends on ONNX Runtime, so most installs need it regardless of RAG/reranker use, and dynamically loading a downloaded native runtime is a materially riskier operation than today's data-only Doctor downloads (model weights). Revisit only if base install size becomes an actual reported problem.

## [0.9.27-alpha] - 2026-07-07

Naming/vocabulary drift (docs/review/06-technical-debt.md item 7) closed as
documentation, not renamed code: `ModelProfile`/`RuntimeProfile`/
`WorkspaceProfile`/tune profiles are deliberately separate schemas sharing a
word, and are stable public types by now, so a cosmetic rename was judged
riskier than the naming friction it relieves.

### Changed
- `CONTRIBUTING.md` gained a "Vocabulary" section disambiguating "memory," "workspace," and "profile" for future contributors, and an "Explicit non-goals" section listing what Aether deliberately does not do (hosted services, accounts, telemetry, plugin API, provider failover, vector databases, an ORM, a web UI), per docs/review/07-roadmap.md's closing instruction to write these down so they survive contributor enthusiasm.

## [0.9.26-alpha] - 2026-07-07

Project-level AI configuration (docs/review/03-architectural-opportunities.md
item 10): finishes the "selecting a workspace activates its full
configuration" thought. `.aether/workspace.json`'s instruction paths already
round-tripped, but nothing ever read the files back into the agent's context.

### Added
- `AgentContextPack.ProjectInstructions`: the Agent now reads each activated workspace's declared instruction files (`AGENTS.md`, `CLAUDE.md`, etc., whichever were saved via "Save as Workspace Defaults") and folds their content into the context pack sent to the model, budget-capped like every other context section. Previously these paths were stored and shown in the UI but never actually reached the model.
- The Agent panel's Retrieved Context list now also shows activated project instructions, alongside retrieved memory and files.

## [0.9.25-alpha] - 2026-07-07

Aether as the machine's AI substrate, phase 1 (docs/review/07-roadmap.md,
3.0 long-term vision): deepens `Aether.LocalApi` and adds per-app data-flow
visibility to Privacy Audit.

### Added
- `GET /v1/models` on `Aether.LocalApi`: lists visible models (id, name, provider, context length) so a calling app can discover what's available instead of hardcoding a `modelId`.
- Every local API call is now logged to the shared `ITraceStore` as a new `TraceKind.LocalApi`, keyed by the caller's self-reported `X-Aether-Client` header (defaults to `"unknown"` if the caller doesn't send one).
- Privacy Audit gained a "Local API activity" item: reports distinct calling apps, per-app call counts, and last-seen time, sourced from that trace history. Reports "Disabled" when the host is off and "No calls yet" when enabled but unused.

### Changed
- `TraceKind` gained a fourth value, `LocalApi`, alongside `Chat`/`Rag`/`Agent`.

Explicitly deferred: per-app tokens (the client header is self-reported and advisory, not verified; today's auth is still one shared token), an embeddings endpoint, a settings/capabilities probe endpoint, and an agent run/step endpoint (needs its own design pass for how a non-interactive caller satisfies the agent's approval gate).

## [0.9.24-alpha] - 2026-07-07

Model landscape hedging audit (docs/review/07-roadmap.md, 3.0 long-term
vision): confirmed the LLM runtime layer is already thin and provider-agnostic,
and fixed the one real leak found.

### Fixed
- `RuntimeProfile.StartManagedLlamaServer`/`LinkedServerId` are llama.cpp-only concepts that were sitting on a record shared by all three `RuntimeKind`s (`LlamaCpp`, `Ollama`, `OpenAiCompatible`), meaning a future runtime kind could silently inherit meaningless llama-specific state. `RuntimeProfileService.NormalizeProfile` now forces both fields inert for any non-`LlamaCpp` profile, and the Services view's "Start managed llama.cpp" checkbox is only shown when that runtime kind is selected.

## [0.9.23-alpha] - 2026-07-07

Opens the 3.0-horizon "long-term vision" roadmap with the first slice of
"provenance everywhere" (docs/review/07-roadmap.md).

### Added
- `Aether.Core.Models.SourceReference` and `ProvenanceKind`: a small shared record for pointing back to where a piece of content came from (a RAG chunk, a memory, a workspace file, or an agent tool result), the seed of generalizing RAG's citations to the rest of the product.

### Fixed
- The agent's minimal RAG retrieval contract (`RetrievedChunk` in `Aether.Core.Services`) was silently dropping the source file/path for every chunk pulled into agent context. It now carries a `Locator`, threaded through `AgentRetrievedItem` into the Agent view's "Retrieved Context" panel, so RAG-sourced context in the Agent panel is traceable back to its file again.

### Changed
- `AgentToolResult` gained an optional `SourceReference? Source`, populated for tools with one clear locator (`read_file`, `summarize_file`, `draft_patch`, `apply_draft_patch`, `mcp:*`); left null where a single source doesn't make sense (`run_command`, `list_files`).

## [0.9.22-alpha] - 2026-07-07

Flips the native Kokoro-ONNX voice path from opt-in to the default, closing
the one deliberately open thread from the 0.9.21 v2.0 pass, and opens the
3.0-horizon roadmap (docs/review/07-roadmap.md, "Long-term vision").

### Changed
- **Kokoro (native) is now the default voice provider.** `TtsSettings.VoiceProvider` defaults to `"KokoroNative"` and `VoiceProviderRegistry.ParseProviderFromSettings` falls back to it for unrecognised settings values. The Python-based `KokoroVoiceProvider` moves to `VoiceProviderCategory.Advanced` alongside XTTS v2 and F5-TTS, staying available as a fallback rather than being removed.
- `LocalAiSetupService`'s readiness scan and setup actions no longer ask for a Python venv, Python health check, or voice packages when the active provider needs none (native Kokoro, OpenAI); it reports a plain "handled by Doctor" item for those instead. This removes a confusing "create a Python venv" prompt that would otherwise show up on a fresh install now that the default provider needs no Python at all.
- `SetupWizardViewModel` now matches the active voice provider by its stable `VoiceProvider` id instead of matching the display name against the raw settings string, fixing a pre-existing mismatch that made the wizard's provider preselection fall back to the first list entry rather than the actual configured provider.
- `NativeKokoroVoiceProvider` no longer reports the `Experimental` capability flag, reflecting its promotion to the default provider.

## [0.9.21-alpha] - 2026-07-07

This release closes out the v2.0 architecture-review roadmap
(docs/review/07-roadmap.md, "composition becomes the product"): workspace
activation, approval-gated agent command execution, an MCP tool surface, a
headless local API, and a native (opt-in) Kokoro-ONNX voice path all landed
in this pass. The one deliberately open thread is flipping the native voice
default, tracked as future work pending real-world parity testing. Remaining
work moves to the 3.0-horizon roadmap items for a future 0.9.30 pass.

### Added
- Workspace as the organizing concept (2.0 roadmap item 1, first slice): an in-repo `.aether/workspace.json` manifest records a workspace's preferred model, embedding model, linked RAG dataset, and instruction file list. `IWorkspaceActivationService` reads it (falling back to the existing app-side `WorkspaceProfile` when no manifest exists) and the Agent panel now activates the matching model/dataset automatically after "Explain Workspace" runs, instead of leaving them as stale independent selections. A new "Save as Workspace Defaults" button writes the current selection back to the manifest so it ships with the repo. Chat also gained an optional `ActiveWorkspaceRoot`/`ActivateWorkspaceCommand` so the same manifest can activate a preferred model outside the Agent panel; both are opt-in and no-op unless a workspace root is set.
- Agent: approval-gated command execution (2.0 roadmap item 2). The agent can now request `run_command`, but only for a recipe the workspace itself already declared safe (`dotnet build`, `dotnet test`, `npm test`, `cargo test`, `pytest`) via the same `.aether/workspace.json` manifest from item 1 (`WorkspaceManifest.AllowedCommands`, populated from "Explain Workspace"'s existing command-recipe detection). A command must match both a fixed, hardcoded allowlist (so a hand-edited manifest can never smuggle in an arbitrary shell command) and the workspace's own declared list; the model can only select a recipe by name, never construct a command line. Execution always requires human approval through the existing review queue, runs with a 5-minute hard timeout, and is confined to the workspace root, matching every other agent action's audit trail.
- MCP client as the tool surface (2.0 roadmap item 3): a new `Aether.Mcp` project speaks JSON-RPC 2.0 over the stdio of locally configured MCP servers (spawn, initialize handshake, `tools/list`, `tools/call`; stdio transport only, no remote HTTP/SSE servers in this pass). Servers are configured under a new Settings section (name, command, arguments, working directory, enabled) backed by `AppSettings.Mcp.Servers`. Each discovered tool is exposed to the agent as `mcp:{serverId}:{toolName}` through the existing `IAgentToolExecutor`/`IAgentSafetyGate` seam via a thin `IMcpToolBridge` interface in `Aether.Core`, so `Aether.Agent` keeps depending only on Core, never on `Aether.Mcp` directly (enforced by a new architecture test, mirroring the existing Rag/ONNX confinement rules). Every `mcp:` tool call always requires approval, regardless of what the server itself claims about the tool's safety.
- Headless core / local API (2.0 roadmap item 4): the non-UI half of the desktop app's DI graph was extracted into a new `Aether.Composition` project (`AetherServiceRegistration.AddAetherCoreServices`), shared by the desktop app and a new `Aether.LocalApi` host. `Aether.LocalApi` is an optional, off-by-default loopback-only (`127.0.0.1`) ASP.NET Core minimal API exposing exactly three endpoints per the "minimal read/action surface" scope decision: `POST /v1/chat/completions`, `GET /v1/memory/query`, and `POST /v1/rag/query`. Every request must present a token in `X-Aether-Token`, generated and saved from a new Settings "Local API" section; the host fails closed (503) rather than allowing unauthenticated access when no token has been configured yet.
- Voice convergence, native Kokoro-ONNX (2.0 roadmap item 5): a new `Aether.Voice` project adds `NativeKokoroVoiceProvider`, a fully in-process Kokoro text-to-speech path with no Python subprocess. It combines a small English-only phonemizer (a common-word dictionary plus letter-fallback rules, explicitly not a misaki port), a static phoneme-to-token tokenizer matching Kokoro's real ONNX vocabulary, and ONNX Runtime inference against the same lazy-load/SHA256-pinned-download pattern as the RAG reranker: the model and voice files are never downloaded on the synthesis path, only via an explicit Doctor install action. It is registered as a new, separate voice provider (`VoiceProvider.KokoroNative`) alongside the existing Python-based Kokoro provider rather than replacing it; the settings default stays Python-based Kokoro until native passes real-world parity testing. `IVoiceProvider.RequiredPythonVersion` is now nullable so providers that need no Python interpreter (native Kokoro, OpenAI) can say so directly instead of using a `(0, 0)` sentinel.

## [0.9.20-alpha] - 2026-07-07

This release closes out the v1.0 architecture review and roadmap
(docs/review/): all four 1.x structural debts are paid down, the 1.0
hardening checklist (retention policies, architecture tests, docs-truth,
Avalonia pin) is complete, and every Feature Audit Merge/Deprecate/
Over-engineered item has been either resolved or explicitly, honestly
deferred with reasoning recorded in the review docs. What remains is 2.0+
work (workspace-as-organizing-concept, approval-gated agent execution, MCP
client, headless core/local API, voice convergence completing) by design,
not leftover 1.x debt. The version jumps from 0.9.16 to 0.9.20 to reflect
the actual scope of this pass rather than a routine patch bump.

### Added

- Architecture and strategy review under `docs/review/` (architecture,
  dependencies, opportunities, vision, feature audit, technical debt,
  roadmap, critique, system map, Evaluation System design), plus a rewritten
  `AGENTS.md` and project skills under `.claude/skills/`.
- Provider capability model: each chat provider declares a
  `ProviderDescriptor` (tag, managed-local/local-api/remote-api kind, and
  capabilities such as streaming, usage reporting, and model pull/delete).
  `CompositeLlmService` exposes the registry and routes through it.
- Retention policies: runtime log rotation keeps the newest 10 archives per
  log file, and benchmark history keeps the newest 200 saved runs.
- Unified trace model (Context System step 1): one `TraceRecord` envelope and
  `ITraceStore` (SQLite `traces.db`, capped at 500 rows per kind) now backs
  chat, RAG, and agent traces as projections of a single schema. Chat traces
  persist across restarts (previously in-memory only), RAG traces write their
  full detail as the record payload instead of a bespoke write-only table,
  and agent steps are indexed in the shared store alongside the
  schema-validated per-task JSONL artifact.
- Shared `ContextPackBuilder` (Context System step 2): chat context snapshots,
  RAG chunk selection, and agent memory/RAG packing all use one
  budget-and-truncate algorithm in `Aether.Core` (`ContextPart` in,
  `PackedContext` out) instead of three divergent implementations.
- Memory scopes (Context System step 3): memories carry
  `Scope`/`ScopeId`/`Title` (`Global`, `Conversation`, `Workspace`) with an
  additive `memories.db` schema migration. Agent workspace notes now live in
  the shared memory store as Workspace-scoped rows via a new
  `WorkspaceMemoryStore`; legacy per-workspace `memory.json` files are
  imported once on startup and renamed to `memory.json.migrated`.
- Check/fix registry: a shared `IInspectionEngine` aggregates checks from
  `IInspectionCheckProvider` implementations, tagged by view (`doctor`,
  `trust`, `privacy`). Doctor and Trust now contribute their existing checks
  as providers instead of standalone report types with no shared shape, and
  Privacy Audit is extracted out of `SystemOverviewViewModel` into a real,
  testable `PrivacyAuditService`. New inspection areas register a provider
  instead of editing an existing service.
- Privacy Audit now derives "remote providers" from `ProviderDescriptor.IsRemote`
  and `VoiceCapability.Remote` instead of string-matching provider names
  ("OpenAI"). A remote voice provider is now flagged even when no remote chat
  provider is enabled, and adding a new remote chat provider to
  `CompositeLlmService.Providers` needs no Privacy Audit changes.
- Evaluation System step 4 (final): Benchmarks and RAG eval export now share
  one atomic write-then-rename helper (`Aether.Core.Services.AtomicFile`)
  instead of each carrying its own copy; RAG eval's report writes are now
  crash-safe (previously a plain, non-atomic write). Ranking already had a
  single implementation, so there was nothing to retire there. Fully
  deleting the two systems' own stores is deferred: nothing yet reads
  `EvalRun`s back out of `IEvalStore`, so their panels still read their own
  richer, system-specific data; see docs/review/10-evaluation-system.md.
- Evaluation System step 3: the RAG eval harness now projects each retrieval
  run onto the shared eval shape and writes it through `IEvalStore`, alongside
  its existing `run.jsonl`/`report.md` export. Retrieval metrics (Recall@K,
  MRR, citation hit, unsupported answer, refusal accuracy, grounding,
  reranker delta) become `CaseResult.Scores` entries rather than engine
  features, keeping the shared engine agnostic to how retrieval is scored.
- Evaluation System step 2: Chat's "Compare Models" now runs through the
  shared `IEvalEngine.RunQuickCompareAsync`, which executes one case against
  each selected target and returns one `EvalRun` per target, instead of a
  private streaming loop inside `ChatViewModel`. Behavior is unchanged
  (sequential execution, per-model latency/usage/error capture); the compare
  loop is no longer duplicated code.
- Evaluation System step 1 (docs/review/10-evaluation-system.md): shared
  `EvalCase`/`EvalTarget`/`EvalRun`/`CaseResult` models in `Aether.Core` and
  an additive `IEvalStore`/`SqliteEvalStore` (`eval_runs.db`, capped at the
  newest 500 runs). `BenchmarkService` now projects each saved run onto the
  shared shape and writes it through the new store alongside its existing
  `benchmarks.db` persistence; the Benchmarks UI is unchanged. This is the
  first of four steps that converge Benchmarks, Compare Models, and the RAG
  eval harness onto one engine.
- Runtime capability discovery: `LlamaCppService` probes llama-server's
  `/props` endpoint and `OllamaService` probes `/api/show` per model, caching
  the result and populating `LlmModel.ProbedContextLength`.
  `ModelProfileService.ApplyProfiles` uses the probed value as the context
  size when no explicit per-model override is saved, so chat's context-budget
  math uses a real number instead of a guessed default. The Model Management
  context-size field now shows "Detected: N" as its placeholder when a probe
  succeeded.

### Changed (audited, no code change)

- Workspace file browser panel (docs/review/05-feature-audit.md: rated
  "Merge", flagged as "a file manager inside a chat app is scope creep").
  Audited
  before cutting: the "Workspace Files" panel is a flat, search-only list
  feeding a preview pane, nested under "Draft Patch"; `SelectedWorkspaceFile`
  is the only way to choose a patch target, and `AgentWorkspaceTools.ListFiles`/
  `SearchFiles` are read-only, extension-filtered, path-bounded (no folder
  tree, no rename/move/delete/create). This is already scoped to exactly
  what patch review needs, not a general file manager; trimming it would
  remove the sole patch-target picker with no replacement, a regression
  rather than a simplification. Left unchanged.

### Removed

- Memory encryption toggle (docs/review/05-feature-audit.md: rated
  "Over-engineered"). `MemorySettings.EncryptMemoriesAtRest` and the
  Settings UI "Encrypt at rest" checkbox turned out to be a phantom
  control: the flag was persisted but no code anywhere actually performed
  AES encryption or decryption on memory content, so the setting implied a
  security property Aether didn't have. Removed the setting and the UI
  control rather than picking a default for it, since there was no real
  encryption to default on. `Memory.IsEncrypted` stays in the schema
  (always `false` now, harmless) rather than trigger a `memories.db` column
  migration for this cleanup.
- Local tasks/reminders/automations (docs/review/05-feature-audit.md: rated
  "Deprecate"). Deleted `TasksViewModel`, `AutomationScheduler`,
  `IAutomationScheduler`, `TasksView`, `TaskAutomationModels.cs`
  (`LocalTaskItem`, `ScheduledAutomation`, `AutomationRunHistory`), and the
  `Tasks`/`Automations` lists from `AppSettings`. Existing `settings.json`
  files with those fields simply ignore them on next load; no migration
  needed since nothing reads them anymore. The sidebar lost its "Tasks and
  automations" entry point. This is unrelated to the Agent Workbench's own
  task-state persistence (`IAgentTaskStateStore`/`FileAgentTaskStateStore`),
  which is untouched.
- Benchmarking suite scope trim (docs/review/05-feature-audit.md: rated
  "Over-engineered"). Deleted `ExportAllZipAsync` (a thin wrapper that re-ran
  `ExportAllAsync` and zipped the result, a duplicate bulk-export path next
  to the existing "Export All" folder export) and the "All / Latest per
  model / Last N" ranking-mode picker (`RankingFilterMode`, its three
  buttons, and the `NumericUpDown` for N). Rankings now always show the
  latest run per model, the only mode that actually answers "which model
  should I use," instead of a small analytics dashboard with three
  interchangeable views. Left alone, deliberately: the triple CSV/JSON/MD
  export per run, the run-info/case-info dialog split, and the 12 starter
  suites, since cutting those changes user-facing export formats and
  existing test expectations rather than removing pure surface duplication;
  see docs/review/05-feature-audit.md for the full trim list considered and
  what was scoped out.

### Changed

- Voice convergence status audit (docs/review/02-dependency-review.md,
  docs/review/07-roadmap.md). No code change: confirmed the settings-default
  and UI-labeling half of "Kokoro-ONNX default, Python providers marked
  advanced/best-effort" was already in place (Kokoro is the settings
  default; `VoiceProviderCategory` already marks XTTS/F5 "Advanced" in the
  Settings UI). Documented, rather than silently left stale, that Kokoro
  itself still runs as a Python 3.12 subprocess (`KokoroProcessManager`),
  not native ONNX Runtime inference: the "ONNX" half of that roadmap line
  was aspirational, not fact. Making it real is a feature build (tokenizer,
  phonemizer, ONNX session, audio postprocessing), correctly scoped at 2.0
  ("voice convergence completes"), not folded into this pass.
- AvaloniaEdit usage audit (docs/review/02-dependency-review.md). Exactly one
  call site, read-only chat-markdown code-block rendering
  (`MarkdownViewer.cs`), no editing anywhere in the app. Deleted a dead,
  unused method (`ResolveHighlighting`) and de-duplicated shared style
  values between the two code-block rendering branches. Decision: keep the
  dependency rather than replace it with a bespoke highlighter, since that
  would mean reimplementing per-language tokenizers, a feature build, not a
  refactor, for a single well-scoped, actively-maintained package.
- Collapsed two provider tag-switches into the capability model
  (docs/review/06-technical-debt.md item 4). `CompositeLlmService.StreamChatAsync`
  now routes through a delegate table keyed by `ProviderDescriptor.Tag`
  instead of a hand-written switch; `ServicesViewModel`'s duplicate
  provider-label switch now reads `CompositeLlmService.DescriptorFor(...)`.
  Adding a provider means one dictionary entry, not two separate switches to
  update in sync. Left alone, deliberately: the settings-flag-per-provider
  shape (`Llm.LlamaCppEnabled` etc.) and `PrivacyAuditService`'s tag switch,
  since collapsing those means a settings-schema migration, a bigger and
  riskier change than this cleanup.
- Decoupled `Aether.Agent` from `Aether.Rag` (docs/review/06-technical-debt.md
  item 11). `AgentContextBuilder` now depends on a new
  `Aether.Core.Services.IAgentRetrievalService` seam (dataset existence check
  + scored-chunk retrieval) instead of `RagQueryService`/`SqliteRagStore`
  directly; `Aether.Rag.AgentRetrievalService` implements it. Deleted two
  pieces of dead code found along the way: `AgentModels.cs`'s
  `AgentRagRetrievalResult` (never constructed anywhere) and Core's
  `IRagService`/`RagResult`/`RagSource` (never implemented or consumed).
  `Aether.Agent.csproj` no longer references `Aether.Rag.csproj`; a new
  architecture test enforces it stays that way.
- Folded the Session Usage panel into Memories (Feature Audit: Merge).
  `MemoriesViewModel` now carries a conversation filter (per-conversation
  memory counts, the old panel's whole purpose) with a "Clear" reset and a
  CSV export scoped to the selected conversation. `SessionUsageViewModel`,
  `SessionUsageDetailViewModel`, and their views are deleted; the sidebar has
  one memory entry point instead of two.
- `ILlmService` contract break ahead of 1.0: sampling parameters moved into
  an `LlmChatOptions` record, the interface has a single event-based
  `StreamChatAsync` (text-only callers use the `StreamChatTextAsync`
  extension), and the unused `PullModelAsync`/`DeleteModelAsync` methods were
  removed.
- `Aether.Core` no longer references CommunityToolkit.Mvvm (it was unused);
  Core is now BCL-only and an architecture test enforces it.
- Tests standardized on `dotnet test` (the previous `dotnet run` invocation
  was a silent no-op); xunit parallelization is disabled because harness
  cases share temp data roots and SQLite pools.

### Fixed

- Pinned `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 to replace the vulnerable
  native SQLite pulled transitively by Microsoft.Data.Sqlite
  (GHSA-2m69-gcr7-jv3q); bumped Microsoft.Data.Sqlite and
  System.Numerics.Tensors to 9.0.9. Fresh restores previously failed under
  warnings-as-errors.
- Local AI asset detection now prefers the actual on-disk casing of the
  models directory instead of a guessed `Models`/`models` variant, with a
  platform-appropriate comparer; fixes wrong reranker/model paths and 24
  Windows-only test failures.
- Temp-directory cleanup in tests clears SQLite connection pools and retries,
  so test databases delete reliably on Windows.

### Tests

- Architecture tests: ViewModels must not reference Avalonia, ONNX Runtime
  stays confined to `Aether.Rag`, and `Aether.Core` may not gain package
  references.
- `SqliteMigrationRunner` contract pinned (ordering, idempotence, target
  version, scope independence).
- Provider descriptor registry semantics pinned (unique tags, remote surface,
  model-management capabilities).


## [0.9.16-alpha] - 2026-05-18

### Added

- RAG dataset manager now includes a "Delete" button on each dataset to remove
  datasets with a confirmation dialog warning. Deletion cascades to all
  associated chunks and is permanent.

### Changed

- Project version metadata bumped to `0.9.16-alpha`.
- Single-iteration benchmark runs now report run mode as `Cold` instead of
  `ColdWarm` in saved runs and exports.
- Built-in benchmark starter suites now include code generation, structured
  output stress, multi-step reasoning, Aether workflows, and hallucination
  resistance suites.
- RAG settings now discover installed reranker folders under `Models/rerank`
  and expose them through a selector.
- Embedding GGUF discovery now uses the dedicated `Models/embed` folder, so
  chat/code GGUFs no longer appear as RAG embedding choices.
- Aether Doctor now runs in the background after launch and raises a startup
  notification when errors or warnings are found.
- Managed Services now normalize duplicate default Chat/Embeddings cards and
  stop a running peer on the same port before starting another managed server.
- RAG ingest progress now separates overall progress from current-stage
  progress and labels file batches alongside embedding batches.
 - Managed llama.cpp chat and embedding defaults now use separate high localhost
  ports so both servers can run at once without colliding with common scan
  targets.
- RAG now uses a dedicated embedding base URL, so embedding requests can point
  at a separate embeddings-capable server while chat keeps using the chat
  server endpoint.
- Managed Services now show the `--embeddings` toggle only on the Embeddings
  server card, which removes the confusing checkbox from the Chat card.
- RAG settings now expose the embedding server base URL next to the embedding
  model selector.
- Benchmark view startup continues to load benchmark data before display, so
  the panel no longer opens as an empty black canvas.
  Chat and embeddings now use separate localhost ports by default so both servers can run at once.
- RAG now uses a dedicated embedding base URL instead of sharing the chat endpoint.
- The embeddings toggle only appears on the Embeddings managed server card.
- RAG settings now expose the embedding server base URL.
- Legacy localhost defaults are migrated away from 8080 and 8081 on load.   

### Fixed

- Benchmark rankings now collapse multiple runs for the same model to the best
  scoring run as intended.
- Directory RAG ingest now chunks, embeds, and stores local files in bounded
  file batches instead of holding the full corpus and all embeddings in memory
  until the end.
- RAG settings now select the installed Doctor-managed reranker when no explicit
  reranker path has been saved.
- Doctor now installs the pinned nomic embedding model under `Models/embed` and
  migrates a verified root-level copy into that folder.
- RAG ingest now starts the managed embedding server after suspending competing
  LLM services, so a shared chat/embedding port works when only one process is
  active.
- RAG ingest now retries oversized embedding inputs with smaller clamps instead
  of failing the full run on one chunk.
- Benchmark view now loads on open even if the startup path missed it, so it no longer shows a blank canvas.
- Auto-tune settings now persist correctly instead of snapping GPU layers back to 999 when starting the server.
- Delete dataset dialog warning warning no longer triggers the Avalonia XAML warning, because the dialog now has a public parameterless constructor.

### Tests

- Added regression coverage for expanded starter-suite seeding and
  single-iteration benchmark export run-mode labeling.
- Added regression coverage for persisted directory-ingest file batches after
  cancellation.
- Added regression coverage for reranker folder discovery and settings
  selection.
- Added regression coverage for dedicated embedding model discovery and Doctor
  embedding-model migration.
- Added regression coverage for Doctor startup warning notifications.
- Added regression coverage for duplicate managed service card cleanup.
- Added regression coverage for non-resetting RAG ingest progress and oversized
  embedding-input retries.
- Verified with `dotnet build` and `dotnet test` after the split-endpoint and
  UI changes.

## [0.9.15-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.15-alpha`.
- Desktop startup now initializes independent SQLite stores in parallel.
- Session usage detail view models are created per detail navigation instead of
  being reused as singleton state.
- Benchmarks now include a confirmed clear-history action for saved run
  history.
- Benchmark view model dropdown now persists selected model when navigating
  away and returning to the view instead of resetting to the first entry.
- Benchmark iterations reduced from 3 runs per test case to 1 run for faster
  feedback.
- Benchmark rankings now group saved runs by model so the list shows the best
  run per model instead of a flat duplicate run history, and the tabular view
  includes the number of runs behind each ranking.
- Benchmark Rankings display restructured with column headers (Model, Runs,
  Score, Pass Rate, Speed) for clarity, and split into two tabs: Rankings
  (best-per-model tabular view) and All Results (full saved run list).
- Chat conversation management now provides dual access: existing three-dots
  flyout menu plus new right-click context menu on conversation list items.

### Added

- Benchmark test info modal dialog displaying test case details including
  prompt, expected results (keywords, regex patterns, refusal requirement),
  and system prompt with selectable/copyable text.
- Benchmark results view now shows info button (ℹ) next to each test case to
  open the test details modal, and benchmark ranking rows expose the same test
  details action for the best run in each model group.
- Benchmark exports now support exporting all saved benchmark runs into one
  timestamped folder containing per-run exports plus a generated index.
  - Added option to create a single zip archive of all exported runs.
  - The export UI offers both per-run folder export and a single ZIP export.
  - Replaced the lightweight toast run-info with a full run-info dialog accessible
    from the run list and rankings. The dialog displays run summary, metrics,
    and per-result summaries and provides an export action.
  - Added ranking timeframe filters: `All`, `Latest per model`, and `Last N runs`.
    Use the new UI controls on the Rankings tab to switch modes and set `N`.
- Chat conversation context menu with options: Pin, Archive, Export Markdown,
  Export JSON, and Delete.

### Fixed

- Chat input now honors the `Ctrl+Enter to send` setting.
- Chat and RAG views now unsubscribe old scroll handlers when their data
  context changes.
- Markdown rendering now parses off the UI thread, avoids heavyweight editors
  for short code blocks, and handles malformed ordered-list starts safely.
- Crash logs now write under the app base directory instead of the launch
  working directory.
- Tray setup now survives missing tray assets and responds to tray setting
  changes at runtime.
- Logs folder opening now handles missing shell openers without crashing.
- Draft patch preview now uses the injected patch diff service.
- Restore backup confirmation now uses a dedicated AXAML dialog instead of an
  inline C# control tree.

### Tests

- Added regression coverage for clearing saved benchmark run history without
  removing benchmark suites.

## [0.9.14-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.14-alpha`.
- Benchmarks now discover local GGUF models from the AI assets root, run all
  built-in suites by default, and auto-switch the managed chat `llama-server`
  when a discovered model is selected.
- Services auto-tune now probes descending GPU layer candidates and keeps the
  highest candidate that reaches `/health`, with CPU fallback.
- Successful Services auto-tune results are now saved as per-GGUF profiles and
  automatically reapplied before managed server start.
- Doctor now checks the configured `llama-server` version and compares it with
  the latest `llama.cpp` release when GitHub releases are reachable.
- Doctor now warns for untuned local GGUF models, offers a `llama.cpp` update
  install action, and verifies pinned `nomic-embed-text-v1.5` files by hash.
- Doctor embedding model install now downloads the default
  `nomic-embed-text-v1.5` GGUF from a pinned Hugging Face commit and verifies
  its SHA256 before configuring RAG.
- Doctor embedding install now updates the embedding server model path to the
  downloaded model, replacing stale or wrong paths instead of only filling an
  empty value.

### Fixed

- Failed embedding model hash verification now removes the downloaded file and
  reports the verification failure through Doctor progress.
- Chat readback and voice preview now skip empty text instead of sending a
  blank request to Kokoro.

### Tests

- Added regression coverage for successful verified Doctor embedding installs,
  stale embedding server path replacement, and hash mismatch rejection.
- Added regression coverage for GGUF discovery, auto-tune candidate planning,
  GPU-layer log parsing, and llama.cpp build parsing.
- Added regression coverage for Doctor untuned-GGUF reporting and current
  platform llama-server asset selection.

## [0.9.13-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.13-alpha`.
- Agent task-index initialization now reconciles existing `task_state.json`
  files on each first initialization per process, so JSON state remains the
  source of truth if a previous save wrote JSON before the SQLite index update.
- Data-root migration now moves Agent state under `agent/`, including
  `task_index.db`, task JSON, logs, traces, and workspace memory files.

### Fixed

- Backup restore containment checks now use case-sensitive path comparison on
  Linux and macOS while preserving case-insensitive checks on Windows, blocking
  case-variant sibling directory escapes.

### Tests

- Added regression coverage for case-sensitive backup restore containment,
  Agent state data-root migration, and Agent task-index reconciliation from
  JSON source files.

## [0.9.12-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.12-alpha`.
- Conversation, memory, and RAG SQLite stores now maintain an
  `aether_schema_versions` table and run additive schema changes through a
  versioned migration runner instead of ad hoc initialization-only column
  checks.
- Agent task listing and review queue data now use a SQLite-backed
  `agent/task_index.db` catalog with indexed status and update-time columns,
  while `task_state.json` remains the durable per-task source of truth.
- Existing Agent task JSON files are backfilled into the task index on first
  initialization when the index is empty.

### Tests

- Added coverage that Agent recent-task and review-queue lists are served from
  the SQLite index and that the Agent index records its schema version.

## [0.9.11-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.11-alpha`.
- Local fallback secrets now derive encryption keys with a fresh random salt per
  stored ciphertext while still reading older fallback vault entries.
- RAG query caching now uses a tighter byte ceiling and refuses to retain a
  single oversized dataset cache entry.
- Conversation auto-summary now checks recent summary memories through a
  targeted conversation query instead of loading a broad recent-memory page.
- Benchmark starter suites are seeded once per process and data root.
- Ollama HTTP access now uses a shared client owned by the service type instead
  of creating a disposable client per service instance.

### Fixed

- Agent workspace path checks now use case-sensitive comparisons on Linux and
  macOS while preserving case-insensitive checks on Windows.
- Agent task IDs now use an explicit safe-character allowlist, reject path
  separators, reject Windows reserved device names, and enforce a length cap.
- Generated XTTS API scripts now escape configured paths as normal Python
  string literals, preventing quote or newline injection into the script body.
- Memory FTS tables now rebuild only when the FTS table is first created,
  avoiding a cold-start full reindex on every process start.
- Model management cache refreshes are now lock-protected so overlapping UI or
  background refreshes cannot race the cache list.
- Per-conversation memory override entries that merely duplicate the global
  setting are pruned during settings saves, and the remaining override map is
  capped.
- Voice provider installation checks now use the `IVoiceProvider` contract
  instead of concrete Kokoro/XTTS casts.
- Main-window folder filter refresh now restores its reload guard with
  `finally`, so exceptions cannot leave filter changes suppressed.
- RAG ingest service restoration is awaited and logged if restoration fails
  after an ingest attempt.
- Runtime log redaction now covers AWS-style access keys, Azure-style key
  assignments, and password query or assignment parameters.
- Runtime log archive collision handling now has a bounded suffix search with a
  GUID fallback.

### Tests

- Added coverage for salted local fallback secret ciphertexts, escaped XTTS
  script paths, stronger Agent task ID validation, settings memory override
  pruning, and expanded log redaction patterns.

## [0.9.10-alpha] - 2026-05-17

### Changed

- Project version metadata bumped to `0.9.10-alpha`.
- Settings, local fallback secrets, toast history, Agent JSON state,
  conversation exports, benchmark exports, and generated XTTS scripts now use
  atomic file replacement to reduce corruption risk during interrupted writes.
- Unreadable `settings.json` files are copied aside with a `.corrupt-*` suffix
  before defaults are loaded.
- Voice provider child processes are now killed on cancellation, and diagnostic
  command previews quote embedded quotes safely.

### Fixed

- OpenAI voice synthesis now resolves saved `secret:` API key references before
  sending requests to the configured endpoint.
- `llama-server` PATH lookup now ignores empty PATH segments so the current
  working directory is not treated as an implicit install location.
- Agent task state storage now rejects unsafe task IDs such as `..` instead of
  resolving them into parent directories.

## [0.9.9-alpha] - 2026-05-17

### Changed

- Runtime logs now redact persisted entries before they reach disk as well as UI log views.
- Conversation list and memory timestamps now store UTC values and render local
- Local AI setup now kills child processes when setup is cancelled and quotes
  command previews more safely.
- RAG metadata parsing now writes warning log entries when source or trace
  markers cannot be parsed.
- Toast history load/save failures now produce runtime warnings instead of
  disappearing silently.

### Fixed

- Backup restore path checks now reject path-prefix escapes and directory
  entries before extraction.
- Agent workspace tools now reject symlink ancestors and skip symlinked
  directories or files during local inspection.
- Workspace instruction discovery now uses path-boundary checks when deciding
  whether files are inside the selected root.
- OpenAI-compatible, llama.cpp, and Ollama streaming now tolerate malformed
  JSON event fragments without aborting the whole response.
- Model downloads now preserve the existing destination file until the completed
  temp file successfully replaces it.
- Runtime log rotation now avoids deleting an existing archive when two
  rotations happen in the same second.
- Trust & Safety now detects host override flags written as `--host=value` or
  equivalent `listen-host` forms.
- One-way Avalonia value converters now return unset values for back conversion
  instead of throwing in binding paths.
- Startup voice-provider probing now logs failures instead of swallowing them.
- Redaction now covers GitHub-style tokens and query-string secret parameters.

## [0.9.8-alpha] - 2026-05-17

### Changed

- Toast popups now use an opaque background so underlying page text does not
  bleed through them.
- Local AI setup scans are now voice-provider aware: Kokoro no longer triggers
  XTTS script or XTTS model actions, and package install plans are only shown
  when the selected provider's imports are missing.
- The Kokoro voice list now includes the full current English voice set from
  Kokoro-82M instead of the earlier small sample list.

### Fixed

- Doctor now labels the Python check with the selected voice provider's actual
  required Python version and no longer renders duplicate diagnostics inline.
- Agent summary counters now render in separate columns instead of overlapping
  in the first card.
- Local AI setup now detects a direct `llama-server` binary path as installed.
- Project version metadata bumped to `0.9.8-alpha`.



## [0.9.7-alpha] - 2026-05-17

### Changed

- Chat now truncates request history to the selected context window while
  preserving the full stored conversation, and stopped streaming responses are
  marked as incomplete instead of reloading as finished messages.
- Agent tools now execute the supported local read-only operations immediately
  and can apply draft patches only after approval; commands, network access,
  installs, commits, pushes, uploads, downloads, and history rewrites remain
  blocked.
- RAG query caching is now bounded by approximate memory size as well as dataset
  count, and cache warmups log their current footprint.
- Task and automation reminder checks now use UTC internally while preserving
  local date picker behaviour for older unspecified due dates.
- SQLite store initialization and settings save/load now use async gates to
  avoid concurrent initialization or settings writes.
- First-run startup now skips heavyweight panel loading until the setup wizard
  is completed, so the wizard appears sooner and initial launch feels snappier.
- Toast notifications now use a toast-shaped toolbar icon, and the stored
  notification history shows the newest items first.
- Settings now populate the embedding model dropdown from discovered local
  GGUF files and restart the embedding server after a model change is saved.
- Settings internals are now split into domain section view models and section
  views while preserving the single settings screen and save flow.
- Agent draft patch review now opens as an awaited modal dialog instead of
  routing through a full app panel.
- Top toolbar icons are now grouped more coherently so the main workspace,
  operations, logs, notifications, and settings read left to right more
  naturally.

### Fixed

- Memory injection now uses the full configured budget instead of stopping at
  half the memory count or half the character budget.
- XTTS API script generation now delegates to the shared generator instead of
  carrying a duplicated template in the setup service.
- ONNX reranker downloads are pinned to a Hugging Face commit and verified with
  SHA256 before loading.
- Backup restore now warns that encrypted local secrets need the original
  `secrets.local.key`, which is intentionally excluded from backups.
- Benchmark CSV export now normalizes embedded newlines inside fields.
- Extra local runtime arguments now handle escaped quotes inside quoted values.
- Voice preview skips audio players that are not present on `PATH`.
- Doctor embedding model detection no longer treats arbitrary `e5` substrings
  as embedding model evidence.
- The Ollama service now disposes its owned `HttpClient` instead of carrying a
  misleading static-client comment.
- Conversation export is available for Markdown and JSON from chat and the
  conversation details flyout.
- Setup wizard startup now returns early when the wizard is required, instead
  of loading the rest of the app before showing onboarding.
- Setup wizard step visibility no longer depends on runtime-mutated XAML
  converters, preventing a blank first-run wizard surface on launch.
- Local AI asset detection now prefers the existing `Models` directory when it
  contains GGUF files, so Doctor and reranker installs do not drift into a
  separate lowercase `models` folder.
- Doctor no longer treats arbitrary chat GGUF files as embedding models, skips
  backend health checks until a dedicated embedding model is installed, and
  does not report Linux global hotkeys as a problem.
- Toast history trimming now preserves newest-first ordering when the cap is
  exceeded.

## [0.9.6-alpha] - 2026-05-17

### Fixed

- Memories now shows the empty state only when there are no memories.
- Removed the placeholder Phi-4 SHA256 entry so setup no longer fails every
  default model download against a known-wrong hash.
- Local fallback secrets now use a stable per-data-root key file and
  `Rfc2898DeriveBytes` instead of hostname-derived hand-rolled PBKDF2.
- TTS settings now unsubscribes process status events during shutdown.
- Chat regeneration now uses stored original message text and attachment paths
  instead of parsing the display-only attachment summary.
- Background panel load failures now surface a warning toast as well as a
  runtime log entry.
- Conversation FTS rebuilds now run only when the FTS table is missing or a
  schema migration actually changes columns.
- Conversation auto-summary throttling now caches the last summary timestamp
  per conversation to avoid a DB read on every assistant response.
- Routine settings saves now skip data-root migration unless a previous data
  root is explicitly provided, preventing startup crashes when the selected
  data root already contains existing databases.
- RAG ingest embedding failures now return actionable guidance when llama.cpp
  responds with 501/404, including a hint to enable `--embeddings` and point
  `LlamaCppBaseUrl` at an embeddings-capable server.
- Embeddings-mode managed server launches now default to `--pooling mean`
  (unless explicitly overridden) to keep llama.cpp OpenAI-compatible
  embeddings requests from failing with pooling type `none`.
- RAG ingest now clamps per-chunk embedding input size before calling
  `/v1/embeddings`, preventing single oversized chunks from failing a full
  ingest run on servers with smaller embedding token limits.
- Doctor now detects missing embedding models and offers one-click download
  from Hugging Face (nomic-embed-text-v1.5-Q4_K_M by default), automatically
  configuring the embedding server model path.
- RAG ingest now pauses non-embedding LLM servers and TTS services during
  document indexing to reduce memory pressure, then restores them after
  ingest completion (or cancellation/failure).
- First-run startup now skips heavyweight panel loading until the setup wizard
  is completed, so the wizard appears sooner and initial launch feels snappier.
- Toast notifications now use a toast-shaped toolbar icon, and the stored
  notification history shows the newest items first.

## [0.9.7-alpha] - 2026-05-17

### Changed

- Chat now truncates request history to the selected context window while
  preserving the full stored conversation, and stopped streaming responses are
  marked as incomplete instead of reloading as finished messages.
- Agent tools now execute the supported local read-only operations immediately
  and can apply draft patches only after approval; commands, network access,
  installs, commits, pushes, uploads, downloads, and history rewrites remain
  blocked.
- RAG query caching is now bounded by approximate memory size as well as dataset
  count, and cache warmups log their current footprint.
- Task and automation reminder checks now use UTC internally while preserving
  local date picker behaviour for older unspecified due dates.
- SQLite store initialization and settings save/load now use async gates to
  avoid concurrent initialization or settings writes.
- First-run startup now skips heavyweight panel loading until the setup wizard
  is completed, so the wizard appears sooner and initial launch feels snappier.
- Toast notifications now use a toast-shaped toolbar icon, and the stored
  notification history shows the newest items first.
- Settings now populate the embedding model dropdown from discovered local
  GGUF files and restart the embedding server after a model change is saved.
- Settings internals are now split into domain section view models and section
  views while preserving the single settings screen and save flow.
- Agent draft patch review now opens as an awaited modal dialog instead of
  routing through a full app panel.
- Top toolbar icons are now grouped more coherently so the main workspace,
  operations, logs, notifications, and settings read left to right more
  naturally.

### Fixed

- Memory injection now uses the full configured budget instead of stopping at
  half the memory count or half the character budget.
- XTTS API script generation now delegates to the shared generator instead of
  carrying a duplicated template in the setup service.
- ONNX reranker downloads are pinned to a Hugging Face commit and verified with
  SHA256 before loading.
- Backup restore now warns that encrypted local secrets need the original
  `secrets.local.key`, which is intentionally excluded from backups.
- Benchmark CSV export now normalizes embedded newlines inside fields.
- Extra local runtime arguments now handle escaped quotes inside quoted values.
- Voice preview skips audio players that are not present on `PATH`.
- Doctor embedding model detection no longer treats arbitrary `e5` substrings
  as embedding model evidence.
- The Ollama service now disposes its owned `HttpClient` instead of carrying a
  misleading static-client comment.
- Conversation export is available for Markdown and JSON from chat and the
  conversation details flyout.
- Setup wizard startup now returns early when the wizard is required, instead
  of loading the rest of the app before showing onboarding.
- Setup wizard step visibility no longer depends on runtime-mutated XAML
  converters, preventing a blank first-run wizard surface on launch.
- Local AI asset detection now prefers the existing `Models` directory when it
  contains GGUF files, so Doctor and reranker installs do not drift into a
  separate lowercase `models` folder.
- Doctor no longer treats arbitrary chat GGUF files as embedding models, skips
  backend health checks until a dedicated embedding model is installed, and
  does not report Linux global hotkeys as a problem.
- Toast history trimming now preserves newest-first ordering when the cap is
  exceeded.
- Project version metadata bumped to `0.9.7-alpha`.

### Changed

- Draft patch preview decisions now use async callbacks rather than
  fire-and-forget `Action` callbacks.
- Backups now exclude the local fallback secret key file, and restore supports
  an explicit overwrite path for future UI use.
- Starter benchmark checks now use more specific expected keywords.
- Project version metadata bumped to `0.9.6-alpha`.

## [0.9.5-alpha] - 2026-05-16

### Added

- Chat context inspector and trace viewer for transparent model calls.
- Compare Models workflow for side-by-side prompt checks across selected models.
- RAG dataset manager with lifecycle, embedding, stale/missing source, and
  reindex status details.
- Privacy audit dashboard for remote-provider, local-server, secret, log, and
  backup visibility.

### Changed

- Project version metadata bumped to `0.9.5-alpha`.

## [0.9.4-alpha] - 2026-05-16

### Added

- Structure-aware RAG chunking for markdown headings, code symbols, PDF pages,
  log events, and web pages, with metadata carried through storage and traces.
- Query planning now emits multiple rewritten variants and records them in RAG
  traces for later inspection.
- Context packing now respects a configurable token budget and records packing
  summaries plus refusal reasons in traces.
- Eval harness results now report Recall@K, MRR, citation hit rate,
  unsupported answer rate, refusal accuracy, and reranker rank delta.
- Benchmarks now capture richer run metadata, record repeated cold and warm
  attempts where possible, and surface median, p95, and failure summaries for
  more useful comparisons.
- Voice setup now documents the consolidated provider architecture, isolated
  venv requirement for F5-TTS and XTTS v2, and Apple Silicon `mps` hardware
  backend support.
- Agent workspace now includes an Explain Workspace flow with workspace profile
  persistence, project instruction detection, safe command recipes, risk notes,
  suggested `AGENTS.md`, and a RAG ingest plan saved to workspace memory.

- Tools: added a lightweight CLI `TraceValidator` tool for validating
  `agent.trace.jsonl` event lines. It now uses `JsonSchema.Net` to validate
  each trace line against `docs/schemas/agent_trace.schema.json`, and ships
  with convenience runner scripts at `scripts/validate_trace.sh` and
  `scripts/validate_trace.ps1`.
- Schemas: added example JSON Schema at `docs/schemas/agent_trace.schema.json`
  to document the fields emitted in agent traces and to guide future
  validation tooling.
- Tests: added an acceptance test that exercises trace schema validation and
  a view-model style UI acceptance test asserting patch-queue metadata is
  surfaced by the `AgentViewModel`.

### Changed

- RAG retrieval now uses query-variant BM25 scoring, structural boosts, and
  budget-aware context selection before answer generation.
- RAG queries can now refuse early when the retrieved context is too weak to
  answer reliably.

### Fixed

- RAG storage and trace handling now tolerate older databases while persisting
  the new chunk, planner, and packing metadata.

## [0.9.3-alpha] - 2026-05-16

### Added

- Agent workbench now surfaces task state, goal, summary, recent task history,
  review queue counts, workspace memory counts, and retrieved context counts in
  a compact summary strip at the top of the panel.
- Agent workbench now includes a workspace file browser with query, list,
  preview, and summarise behaviour for faster read-first inspection.
- Agent workbench now supports draft patch proposals from workspace files,
  with rationale, generated patch preview, approval-gated queueing, and
  approve/reject actions for queued patches. Approving a draft patch now writes
  the proposed content back to the selected workspace file and refreshes the
  preview immediately.
- Draft patch decisions now expose explicit pending, applied, rejected, and
  blocked states, plus a direct block action and clearer queue counts in the
  agent summary strip.
- Agent workbench now shows a capability disclosure callout that states the
  current slice is read-first and approval-gated, with shell, network, and
  remote-control actions kept out of scope.
- Session usage panel: per-conversation memory counts and recent activity.
- Draft patch diff preview: side-by-side visual comparison with line numbers,
  colour-coded changes (green for additions, red for removals), and gutter lines.
  Preview opens automatically after generating a patch and allows approve or
  cancel before queueing.

### Changed

- Project version metadata bumped to `0.9.3-alpha`.

## [0.9.2-alpha] - 2026-05-16

### Added

- RAG ingest cancellation controls in the UI, with token propagation through
  the pipeline so long-running ingests can be stopped cleanly.
- Conversation search now uses SQLite FTS5 for faster full-text lookup across
  title, messages, folder, and tags, with LIKE fallback for malformed or very
  short queries.
- Chat memory foundations: new memory domain model and settings, SQLite-backed
  memory store with FTS5 search, extraction service for `[MEMORY: ...]`
  markers, and context injection service with token-budget selection.
- New Memories panel in the main toolbar for reviewing, searching, pinning,
  archiving, and deleting saved memories.
- Settings now include chat memory controls for enable/disable, prompt
  injection toggle, auto-summary threshold, injection token budget, encryption
  toggle, and auto-archive days.
- Phase 5 memory automation: conversations are now auto-analysed in the
  background after assistant replies, and durable memories are extracted and
  persisted only when conversation importance exceeds the configured threshold.
- Memory test coverage now includes store CRUD/search behaviour, extraction
  marker parsing/cleanup, and injection token-budget prioritisation.
- Chat now shows a live memory status line (enabled state and recent counts)
  in the header area for clearer memory visibility.

### Changed

- Project version metadata bumped to `0.9.2-alpha`.
- RAG query prompt generation now applies per-dataset `PromptTemplate`
  configuration, honoring `{context}` and `{question}` placeholders.
- Benchmark starter suite seeding is now ID-aware, so missing default suites
  are inserted even when the database already contains other suites.
- SQL connection string construction in `SqliteRagStore` is now cached and only
  rebuilt when the resolved database path changes.
- Kokoro and F5-TTS Python scripts are now shipped as embedded resources rather
  than multi-hundred-line C# string literals.
- Local AI setup scripting and action definitions are now split into dedicated
  helper classes so the service file is easier to navigate and maintain.
- Chat context usage estimation is now debounced to reduce repeated full
  recalculations while typing or when messages/attachments change rapidly.

### Fixed

- HttpClient pooling: Converted instance `HttpClient` creation to static class
  fields across 11 service classes (OpenAiService, LlamaCppService,
  OllamaService, RuntimeProfileService, ModelDownloadService,
  LlamaCppEmbeddingService, KokoroVoiceProvider, XttsV2VoiceProvider,
  OpenAiVoiceProvider, RagPipeline, OnnxCrossEncoderReranker) to prevent socket
  exhaustion under concurrent connections.
- `ReaderWriterLockSlim` disposal: `CompositeLlmService` now implements
  `IDisposable` to properly dispose the shared model cache lock.
- Async-over-sync anti-pattern: `ServiceTests.SystemInfoSafeFallback()` now
  uses proper async/await instead of `GetAwaiter().GetResult()`.
- Port validation: `ServerProcessManager.NormalizeConfig()` now validates port
  range (1-65535) before process launch to catch invalid configurations early.
- AutoTune debugging: `ServerProcessManager.AutoTuneAsync()` now tracks and
  includes process exit code in timeout exception for better diagnostics.
- Event lifecycle: `ServicesView` now properly unsubscribes from
  `ObservableCollection.CollectionChanged` on view unload to prevent memory
  leaks.
- Symlink security hardening: `AgentWorkspaceTools.ResolveSafePath()` now
  rejects symbolic links to prevent path traversal attacks.
- Secret storage encryption: `SecretStore` now uses AES-256-CBC encryption
  with PBKDF2 key derivation (using machine identifier and 10,000 iterations)
  instead of Base64 encoding, with backward compatibility fallback.
- Model download integrity: `LocalAiSetupService` now supports SHA256 hash
  verification when trusted hash metadata is available. Hash mismatches fail the
  setup action with a clear error message.
- Settings voice sample import compile break: `SettingsView` now correctly wires
  `ImportTtsVoiceSampleCommand` through `TtsSettingsViewModel`.
- Reranker directory resolution parity between Doctor and runtime reranker:
  both now use the same path resolution strategy.
- `MainWindowViewModel` search debounce now disposes prior
  `CancellationTokenSource` instances to avoid CTS leaks while typing.
- `KokoroVoiceProvider` and `F5TtsVoiceProvider` now serialise synthesis calls
  with a semaphore to avoid concurrent Python process stampedes.
- WAV playback fallback chain restored in `VoiceProviderProcessRunner`:
  `paplay` → `pw-play` → `aplay` → `ffplay`.
- `ServicesView` now unsubscribes old collection-changed handlers when
  DataContext changes, preventing duplicate subscriptions.
- `AutomationScheduler` settings saves are now guarded and failures are logged
  to runtime logs instead of being silently swallowed.
- Chat attachment paths are now persisted in `Message` and round-tripped
  through `ConversationStore`, so regenerate can reattach after app restart.
- RAG ingest health reporting now uses `IngestReport.Health` directly;
  `__health__` sentinel rows are no longer emitted by web ingest.
- Legacy unused services removed: `XttsService` and `OghmaRagService`.
- `CompositeLlmService` shared model cache state is now lock-protected, and
  provider model refresh now runs concurrently instead of sequential timeouts.
- `RagQueryService` dataset chunk cache and LRU metadata are now synchronised
  for concurrent query/warm/clear access.
- Model Management UI no longer shows the misleading non-destructive
  "Cleanup" action.
- Data root migration now carries all local SQLite families used by chat and
  memory features (`conversations.db*`, `memories.db*`, `benchmarks.db*`).
- Feature documentation now includes a dedicated Memory section describing the
  UI, settings controls, and background auto-summary behaviour.
- Chat auto-summary failures are now captured in runtime logs as warnings
  instead of being silently swallowed.



## [0.9.1-alpha] - 2026-05-15

### Changed

- Voice provider configuration now uses a dropdown selector to clearly indicate
  the active provider, with conditional display of provider-specific settings
  below the selector (Kokoro voice/device/speed controls, F5-TTS options, etc.).
- Test project `Aether.Tests` is now configured as a proper xUnit test project
  (`Microsoft.NET.Test.Sdk` + xUnit runner), replacing the custom executable
  harness so `dotnet test` provides discovered, passed, and failed test counts.
- xUnit harness cases are now grouped into per-domain fixtures (Backup,
  Services, RAG, TTS, Agent) for clearer CI output while preserving the same
  underlying coverage set.
- Settings: add Kokoro managed service controls and health probe at startup; TTS speed persisted in settings.
- Kokoro: Start/Stop managed service, venv-aware startup, and GPU-friendly install notes (supports CUDA, AMD, Intel via environment guidance).
- Kokoro: Start/Stop managed service, venv-aware startup, and GPU-friendly venv setup. The Local AI setup now detects NVIDIA (CUDA) or ROCm and suggests the appropriate device; settings persist TTS device and speed. Manual override available in Settings.
- Local AI setup now attempts to install a matching PyTorch wheel for the selected backend (CUDA/ROCm/CPU) before installing XTTS packages.
- Local AI setup now explicitly warns when Intel or AMD GPUs are detected without a supported wheel path and falls back to CPU for XTTS setup instead of silently masking the hardware.
- Chat: avoid inserting internal runtime/TTS error messages into chat history; surface as runtime logs and toasts instead.
- RAG Embeddings section expanded with clarification that embeddings convert
  document text to vectors for semantic search and are cached after first
  compute, plus notes that reranker is optional and improves search relevance.
- Tooltips for Local AI Setup Plan and Approve buttons now include more
  detailed guidance on what each action accomplishes (preview vs. execute).
- Toast notifications repositioned from top-right to top-left below the menu
  bar for improved visibility and less overlap with active content areas.

### Fixed

- Chat message list no longer shows a selected-row grey slab when clicked;
  Chat view now uses a non-selectable `ItemsControl` inside a `ScrollViewer`
  instead of a selectable `ListBox`.
- Window title now updates correctly when switching between non-Chat panels
  (Settings, Agent, Models, RAG, Services, Tasks, etc.).
- Agent view Grid layout: fixed TextBox/Browse button field overlap by adding
  a third column definition for proper button sizing.
- Services view: eliminated duplicate items appearing on scroll or after
  collection changes by preventing multiple CollectionChanged subscriptions on
  the same handler.
- Python version validation now uses per-provider requirements; Kokoro requires
  Python 3.12 while F5-TTS and XTTS v2 require Python 3.11, with appropriate
  health check messages for each provider.
- Reranker model path now resolves correctly against LocalAiAssetsRoot instead
  of the incorrect DataRootDirectory reference.
- Resource leak in MarkdownViewer: timer now properly stopped and unsubscribed
  from events when control is detached from visual tree; prevents timer tick
  events continuing after control disposal.
- Resource leak in ServerProcessManager.AutoTuneAsync: TaskCompletionSource
  now guaranteed to complete even on abnormal process exit; added defensive
  catch block and finally-block check to ensure completion never hangs.
- MainWindowViewModel panel commands now consistently use sync commands with
  background async loading; ShowChatPanelAsync and ShowAgentPanelAsync
  converted to ShowChatPanel and ShowAgentPanel using RunBackgroundTaskAsync
  pattern for consistency with other panel switching commands.
- CompositeLlmService timeouts tuned: per-provider timeout increased from 2s
  to 5s to accommodate slower model providers; model cache duration increased
  from 30s to 300s (5 minutes) for more stable model discovery; added explicit
  error classification for timeout vs other exceptions.
- RagQueryService LRU cache simplified: replaced complex Queue-based TouchCache
  with LinkedList for O(n) instead of O(n^2) eviction; cleaner implementation
  with direct node removal and addition to end.
- ChatViewModel regenerate now uses proper attachment storage: AttachedFilePaths
  collection added to MessageViewModel to store file paths; regeneration
  retrieves paths from message model instead of fragile marker-based parsing.
- AgentService JSON extraction improved: added brace-matching logic to properly
  handle nested structures and escaped quotes; validates extracted JSON with
  JsonDocument.Parse before returning; fallback behavior now attempts multiple
  extraction strategies instead of failing on first mismatch.
- SqliteRagStore connection pooling: added Pooling=true and Max Pool Size=5
  to SQLite connection string for automatic connection reuse across operations.
- SettingsView.axaml binding consistency: added missing Tts. prefix to
  IsLegacyVoiceBackend bindings for UI visibility conditions.

- Window title now updates correctly when switching between non-Chat panels
  (Settings, Agent, Models, RAG, Services, Tasks, etc.).
### Changed

- Runtime log entry buffer capacity reduced from 2000 to 1000 entries to reduce
  in-memory overhead during long-running sessions while maintaining adequate
  debugging history.
- Setup Wizard toolbar icon removed to reduce visual clutter; the wizard
  remains accessible via the Settings panel.

## [0.9.0-alpha] - 2026-05-14

### Added

- Local AI setup now offers approval-gated downloads for a default Phi-4 mini
  reasoning GGUF file when no GGUF models are present, plus a platform-specific
  `llama-server` binary download when the runtime is missing.
- Added resumable model download support and binary installation helpers for
  large local assets.
- The existing Settings scan flow now surfaces the new download actions without
  requiring a separate setup screen.
- The first-run Setup Wizard now surfaces Kokoro onboarding directly in the
  voice step, including the provider install plan and risk notes.
- Added regression coverage for resumable downloads and llama-server release
  data validation.
- Voice provider abstraction with capabilities, health checks, and install
  plans for Kokoro, F5-TTS, XTTS v2, and optional OpenAI voice.
- Local AI setup install plan previews that must be reviewed before approval.
- Runtime log viewer with filters, copy actions, and redacted diagnostics export.
- Aether Doctor screen with environment checks for storage, runtimes, voice,
  RAG, GPU visibility, and secrets, plus diagnostics copy and navigation.
- Python health validator that rejects broken or non-relocatable Python
  installs and surfaces actionable diagnostics.
- First-run Setup Wizard: a 6-step guided onboarding that configures data
  roots, chat backend, model paths, voice provider, and runs the Aether
  Doctor before starting. The wizard sets `SetupWizardCompleted` to skip on
  subsequent launches after finish or skip.
- RAG ingest dry-run, duplicate-source reporting, and per-document ingest
  summaries surfaced in the RAG panel before writing changes.
- Agent review queue with approve/reject actions for waiting tasks, plus a
  file-backed workspace memory panel for reusable workspace notes.

### Fixed

- Refactor LLM provider routing in CompositeLlmService from hardcoded model
  name prefix checks to metadata-driven ProviderTag routing; adds canonical
  provider tagging to LlmModel discovery for OpenAI, Ollama, and llama.cpp.
- Extract shared ExtraArgsParser utility for quote-aware command-line argument
  tokenization; eliminates duplication between ServerProcessManager and
  TrustService and centralizes parsing logic.
- Guard SetupWizardViewModel runtime synchronisation loop to prevent infinite
  re-entrancy via PropertyChanged events; replace event-driven sync with
  guarded partial methods.
- Fix MarkdownViewer timer lifecycle: move subscription to OnDetachedFromVisualTree,
  add IDisposable implementation, and properly dispose timer on control detach
  to prevent resource leaks.
- Add logging for fire-and-forget background tasks in MainWindowViewModel;
  capture exceptions in RunBackgroundTaskAsync helper and route them to
  RuntimeLogService instead of silently discarding them.
- Bound RagQueryService cache to 5 datasets maximum with LRU eviction policy;
  prevent unbounded memory growth during long-running query sessions and
  corpus re-rankings.
- Harden OllamaService.ParseId to detect and reject model names containing
  extra colons that would cause silent truncation; add ParseIdGuarded variant
  for safer model name extraction.
- Fix test harness and CI-only fakes to match new voice provider APIs and
- Restructure AppSettings into hierarchical configuration objects: TtsSettings
  (voice provider, TTS configuration), RagSettings (RAG service, reranking),
  LlmSettings (LLM providers, model defaults), UiSettings (theme, hotkeys, tray),
  and DataManagementSettings (storage paths). This improves maintainability and
  reduces cognitive load by grouping related settings under semantic namespaces
  throughout Core, Services, ViewModels, and tests.
  Python validator constructor; repaired several setup & log view bindings.
- Refactor: extract TTS settings and commands into `TtsSettingsViewModel`; clean
  `SettingsViewModel`, update Settings view bindings, and move XTTS status handling
  into the nested viewmodel to improve separation of concerns and testability.
- Refactor Rag ingest: decompose embedding and storage into cancellable,
  batched helpers (`EmbedChunksAsync`, `StoreChunksAsync`) to improve
  responsiveness and make cancellation/reporting deterministic during large
  ingests.
- Tests: add cancellation tests that verify RAG ingest cancels cleanly during
  embedding and storage phases to prevent long-running uninterruptible work.
- Tests: split the monolithic custom harness further by extracting backup and
  migration coverage into `BackupMigrationTests`, keeping `Program.cs` as a
  thinner runner and reducing the surface area for future test refactors.
- Tests: continue the harness split by extracting agent coverage into
  `AgentTests`, including task-state persistence, review queue behaviour,
  workspace memory, path safety, context packing, safety gating, and trace
  logging.
- Refactor AppSettings into dedicated configuration types for LLM, TTS, RAG,
  UI, and data management settings, so each domain is owned by a focused model
  instead of one oversized settings object.

## [0.8.7-alpha] - 2026-05-13

### Added

- Voice provider groundwork for a pluggable local voice layer, with Kokoro as
  the recommended default readback engine, F5-TTS as the advanced cloning
  option, and XTTS v2 retained as the legacy compatibility backend.
- Voice setup terminology updates from XTTS-only wording to voice-provider
  wording in preparation for the pluggable layer.
- XTTS validation and repair improvements that require Python 3.11 and detect
  broken or incompatible venvs before install.

### Changed

- XTTS setup actions are now framed as voice backend setup rather than a boxed-
  in single-provider workflow.

## [0.8.6-alpha] - 2026-05-12

### Added

- Settings Trust & Safety scan for configured local executables, scripts,
  models, XTTS paths, runtime endpoints, hashes, and AI-root scope warnings.
- Advisory trust warnings for network-facing `llama-server` extra args such as
  `--host 0.0.0.0`.
- Direct chat file context injection for selected local text/code files, with
  attach/drop UI, bounded reads, skipped-file status, and history summaries.
- Digital PDF support in local RAG directory ingest through managed PdfPig text
  extraction, with image-only PDFs skipped as health warnings.
- Syntax-highlighted fenced Markdown code blocks in chat and RAG answers through
  AvaloniaEdit.
- Chat context usage indicator with provider-reported token usage when
  available and local estimates while drafting.

## [0.8.5-alpha] - 2026-05-12

### Added

- Optional RAG web URL loader that stays disabled by default and only ingests
  explicitly listed HTTP(S) pages when enabled for a dataset.
- Web ingest HTML text extraction with script/style stripping, small page
  limits, URL deduplication, and regression coverage for the default-disabled
  posture.
- Experimental Aether Agent workbench with explicit task state, compact context
  packs, local task logs/traces, read-only workspace tools, safety gating, and
  optional RAG-backed retrieval.
- Agent regression coverage for task-state serialization, workspace path
  safety, context packing, tool policy, and the fake-model agent loop.
- Approval-gated Local AI Setup assistant for scanning an AI folder, detecting
  models, venvs, XTTS v2 assets, voices, output folders, and rerankers.
- Structured setup actions for creating a venv, creating XTTS support folders,
  installing XTTS packages, and generating an XTTS v2 API script after explicit
  user approval.

## [0.8.4-alpha] - 2026-05-12

### Added

- Repeatable Linux and Windows archive packaging scripts.
- Linux package layout with desktop launcher metadata, user-local desktop
  install/uninstall scripts, icon asset, license notices, archive, and SHA256.
- Windows package layout with launch helper, license notices, ZIP archive, and
  SHA256.
- App and tray icon assets derived from the Aether branding sheet.
- Packaging documentation covering runtime requirements and self-contained
  builds.
- Refreshed security review and threat model for secrets, local runtimes,
  backup/restore, RAG ingest, tray behavior, and packaging.
- Expanded tests for RAG BM25/hybrid scoring, runtime profile validation,
  secret reference migration, and shell-free process argument construction.
- Opt-in Windows system-wide hotkeys for Quick Chat, New Chat, and Services,
  with Linux reported as unavailable until reliable compositor support exists.

## [0.8.3-alpha]

### Added

- OS credential-store integration for secrets via Linux Secret Service,
  macOS Keychain, and Windows Credential Manager.
- Local fallback secret vault remains available when no OS store is present.

## [0.8.2-alpha]

### Added

- Tray icon with show, quick chat, new chat, services, stop services, and quit
  actions.
- Local hotkeys for quick chat, new chat, services, and closing quick chat.
- Settings toggles for tray icon, minimize-to-tray, and local hotkeys.
- Documentation for tray behavior, local hotkeys, and close-button shutdown
  semantics.

## [0.8.1-alpha]

### Added

- Configurable local AI assets root for models, XTTS, venvs, and encoders.
- Detected-path application for XTTS script/python/voices/output and ONNX
  reranker assets.

### Changed

- Removed machine-specific XTTS script defaults; Aether now asks for a path
  when XTTS is started without one.

## [0.8.0-alpha]

### Added

- Central repo version metadata for all assemblies.
- Source-available private/noncommercial licensing posture.
- Commercial licensing documentation.
- Contribution terms for dual noncommercial and commercial distribution.
- Public-release notice and third-party component notice.

### Current Product State

- Native Avalonia desktop shell for Linux and Windows.
- Local-first chat history with folders, tags, pins, archive, search, rename,
  and delete.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible APIs.
- Managed `llama-server` start/stop, logging, auto-start, and GPU auto-tune.
- RAG ingest, citations, source inspector, traces, eval harness, and ONNX
  reranking.
- XTTS v2 launch, voice discovery, preview, import, and memory-only playback.
- Configurable data root, migration preview, backup, restore, and data-safety
  tests.

### 1.0 Release Gates

- Concrete local-first OCR ingestion.
- Optional web loader that is disabled by default.
- Linux and Windows packaging.
- Security review and threat model refresh.
- Expanded tests for RAG scoring, runtime validation, backup/restore, secret
  migration, and process argument safety.
