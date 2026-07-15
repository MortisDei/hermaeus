# Changelog

All notable changes to Aether will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [Unreleased]

## [0.14.0-alpha] - 2026-07-15

Implements docs/review r9 in full: the send-path latency and orphaned-server
issues that shipped as documentation-only alongside the 0.13.1-alpha crash
fix are now implemented, plus the full UI-thread-safety sweep the crash fix
started.

### Fixed

- **HTTP timeout misreported as a user cancel.** `ServerProcessManager`'s
  health-check poll used a 2 s `HttpClient` timeout; that timeout throws
  `OperationCanceledException` too, indistinguishable by type from a real
  caller cancellation, so a slow (not dead) health check could silently
  overwrite an already-diagnosed Error status with an unexplained Stopped.
  Only a genuinely cancelled token now escapes the retry loop.
- **Untuned `Task.Run` wrapper caused a benchmark-history race.**
  `BenchmarkViewModel`'s suite-change handler wrapped an already-`await`-based
  reload in `Task.Run`, mutating UI-bound collections off the UI thread.

### Added

- **Send-path instrumentation** (`ChatSendTiming`). Every send now measures
  memory recall, injection selection, lesson context, prompt build, and
  first-token wait, surfaced in `PerformanceLog` and persisted on the chat
  trace.
- **Embedding backfill moved off the send path** (`MemoryStore.
  RunEmbeddingBackfillAsync`). `SearchAsync` embeds only the query now;
  backfill runs from a background pass at startup and after writes, with a
  per-row cooldown and attempt cap so a down embedding endpoint cannot tax
  every send.
- **Fast-fail query embedding.** A 3 s timeout on the query embed falls back
  to FTS-ranked recall instead of inheriting the embedding HTTP client's 60 s
  timeout; the fallback logs once per process.
- **Embedding endpoint fallback visibility.** A blank `Rag.EmbeddingBaseUrl`
  falling back to the chat server now logs once and raises a Doctor advisory.
- **Oversized-context advisory.** Services view and Doctor both flag managed
  servers configured with `ContextSize` above 16384.
- **Job-object process containment** (`ProcessJobObject`). Managed servers,
  auto-tune probes, and voice-engine (XTTS/Kokoro) children join one Windows
  job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so they die with the
  app however it dies.
- **Port preflight with a named owner** (`PortOwnerLookup`). A conflicting
  port fails instantly, naming the port and (best-effort) its owning process,
  instead of launching a doomed process.
- **Orphan detection with an explicit Stop** (`OrphanServerDetector`). A
  leftover server from a previous session is identified by an exact
  executable-path match and offered a Stop button; anything else is reported
  only. Stopping re-verifies the PID and executable immediately before
  killing it.
- **`ViewModelBase`** (`Aether.ViewModels`). One `RunOnUi`/`RunOnUiAsync`
  implementation, replacing three private copies plus `TtsSettingsViewModel`'s
  bespoke `SynchronizationContext` usage.
- **Architecture test**: any public `ObservableCollection<T>` property on a
  ViewModel now fails the build; every ViewModel collection is
  `UiBoundCollection<T>`.

## [0.13.1-alpha] - 2026-07-15

Emergency stability fix for a crash found in first sustained field use of
0.13.0-alpha, plus the r9 review pack (`docs/review/`) covering the two
remaining field issues (send-path latency, orphaned server processes).

### Fixed

- **Cross-thread UI collection crash.** The app could hard-crash with
  Avalonia "Collection was modified; enumeration operation may not execute"
  in `PanelContainerGenerator` during chat use. Root cause: UI-bound
  `ObservableCollection`s mutated from worker threads. Marshaled the
  offending writers through the UI thread's `SynchronizationContext`:
  `ChatViewModel`'s debounced context-usage refresh (ran entirely on a
  `Task.Run` thread), persisted chat-trace loading, and memory status
  refresh; `LogsViewModel`'s `LogAdded` handler (rebuilt the visible log
  list on whichever background thread wrote the log entry).

### Added

- **`UiThreadGuard` / `UiBoundCollection<T>`** (`Aether.ViewModels`).
  ItemsControl-bound collections in `ChatViewModel` and `LogsViewModel` now
  throw immediately, with the offending call in the stack, if mutated off
  the UI thread. Armed once by the Desktop app at framework init; inert in
  headless tests (xunit's `SynchronizationContext` legitimately hops
  threads, so the guard deliberately does not arm on context presence).
  The r9 pack extends this to every ViewModel.
- **r9 review pack** (`docs/review/`): send-path latency instrumentation
  and embedding backfill relocation, job-object child-process containment,
  port preflight and orphan detection, full UI-thread-safety sweep with an
  architecture test.

## [0.13.0-alpha] - 2026-07-15

Implements docs/review r8: voice pronunciation, onboarding, performance, and
technical debt. Theme: polish what exists rather than add new capability.
Also reviews and corrects two optimization commits (`ad618da`, `aea2326`)
that landed between r6 and r7 without a review pass.

### Voice pronunciation

- **Text normalization** (`Aether.Voice/KokoroTextNormalizer.cs`). Numbers,
  currency, percentages, ordinals, clock times, and a handful of standalone
  symbols (`&`, `+`, `/`, `@`, `=`) are expanded to plain English words before
  phonemization. Fixes the bug where digits were silently dropped entirely
  (`MapLetter` mapped non-letters to empty, and the tokenizer skipped
  unrecognized characters) - "You have 3 errors" previously spoke as "You
  have errors".
- **CMU Pronouncing Dictionary** (`Aether.Voice/CmuPronouncingDictionary.cs`,
  `ArpabetIpaMap.cs`). The ~250-word inline dictionary and its letter-by-letter
  rule fallback are replaced as the primary pronunciation source by the
  ~126,000-word CMUdict (BSD-style CMU license; see `THIRD-PARTY-NOTICES.md`),
  embedded gzip-compressed. ARPABET phones map to Kokoro's IPA vocabulary with
  stress marks; unstressed vs. stressed AH (schwa vs. wedge) is handled
  specially. The rule-based fallback still exists for genuinely unknown words,
  fixed to no longer emit the vocabulary-unsupported "ɝ" symbol and to read
  word-initial "gh" as a hard /g/ (ghost) rather than /f/ (a rule meant only
  for word-final "gh", as in laugh).
- **User pronunciation lexicon** (`Aether.Voice/KokoroUserLexicon.cs`) at
  `{DataRoot}/voice/lexicon.txt`, `word = ipa` per line, reloaded when the
  file changes. Seeded with defaults for words no dictionary would know,
  including the app's own name ("Aether" was mispronounced before this).
  Settings > Voice gained an "Open pronunciation lexicon" button.
- **Suffix morphology**: possessives and common suffixes (`'s`, `s`, `es`,
  `ed`, `ing`) retry against the lexicon chain with correct voicing rules
  (e.g. "aether's" resolves via the stem "aether" plus a voiced /z/).
- **Unknown acronyms** are spelled out by letter name (e.g. "GGUF" as "gee
  gee you eff") rather than mispronounced as a word; acronyms cmudict
  already knows (api, cpu, html, usa) use their real dictionary entries.
- ~20 golden regression sentences assert exact IPA output and zero dropped
  characters through tokenization.

### Onboarding and usability

- **Guided starter model download** (`Aether.Services/StarterModelCatalog.cs`).
  The setup wizard's model step offers a hardware-aware download (three
  VRAM tiers, Qwen2.5 3B/7B/14B Instruct Q4_K_M) alongside the existing
  manual path picker, with SHA256 verification via the existing
  `ModelDownloadService`.
- **Voice install from the wizard**: the Voice step gained an "Install now"
  button for Kokoro (native) that calls the same `IDoctorService` install
  path Settings/Doctor already use, so first-run setup can finish chat and
  voice in one pass instead of requiring a separate trip to Settings.
- **Markdown tables and clickable links** (`Aether.Desktop.Controls.MarkdownViewer`).
  Tables (parsed by Markdig but previously unrendered) now render as a grid
  with a bold header row. Links are clickable (embedded via
  `InlineUIContainer`, since Avalonia's text-flow `Inline`/`Run`/`Span` have
  no pointer events) with an https/http-only scheme gate; anything else
  (`file:`, `javascript:`, `data:`) renders as inert styled text.
- **Empty states**: Chat gets a distinct "no chat model configured" state
  with links to the setup wizard and Services (RAG and Memories already had
  empty states); Agent's workspace field and Benchmark's run history gained
  matching empty-state guidance.
- **Tooltip sweep** across the settings sections, Doctor, Memories, Model
  Management, Logs, and System Overview views that previously had none.

### Performance

- **Startup phase timing**: `App.axaml.cs` logs settings/store-init/voice-probe/
  viewmodel timings plus a total, so future startup work has a baseline.
- Embedding-model warm-up moved off the startup critical path (fire-and-forget
  after the UI is usable, logged separately when it completes) instead of
  blocking every launch behind an ONNX load.
- **Memory recall regression fix** (`Aether.Services/MemoryStore.cs`):
  `aea2326` had restricted hybrid-recall candidates to FTS hits plus a hard
  cosine > 0.7 threshold, and fetched each non-FTS candidate individually
  (N+1 queries). Every embedded row is scored again; only the plausible
  top-K non-FTS ids are hydrated, in one batched `WHERE id IN (...)` query.
- **Chat pagination**: conversations render only the newest 100 messages
  (`ChatViewModel.VisibleMessages`), with a "Show earlier messages" button
  to reveal more. Persistence, memory extraction, and prompt-history
  truncation still operate on the full message list.
- **Incremental markdown re-render**: `MarkdownViewer` now reuses each
  top-level block's existing control when its source text is unchanged
  between renders, rebuilding only from the first changed block onward,
  instead of rebuilding the entire document on every streaming debounce tick.
- Audited every model-restart call site; both existing guards
  (`ServicesViewModel.SelectModelAndRestartAsync`'s path comparison,
  `BenchmarkViewModel.PrepareModelAsync`'s model-ID fast-skip) were already
  correct and are now covered by tests. Chat model switching and
  `Aether.LocalApi` have no restart call sites.

### Technical debt

- **Harness registration guard** (`HarnessRegistrationGuardTests`): a
  reflection-based test asserts every public parameterless Task-returning
  method on the custom harness's test classes is registered in
  `HarnessCases` exactly once. Immediately caught six previously-dead tests
  across the codebase (beyond the two r6 already found) that existed but
  never ran: `AcceptanceTests.AgentUiAcceptance_PatchQueueMetadataIsRendered`,
  `AcceptanceTests.TraceSchema_FileIsValidAndSampleConforms` (also fixed a
  broken relative path in the same test), `RagTests.RagDirectoryIngestPersistsCompletedFileBatches`,
  `ServiceTests.BenchmarkExportAllCreatesBatchFolder`,
  `ServiceTests.MemoryStoreCountsByConversationWork`, and
  `TtsTests.VoicePreviewSkipsBlankText`. All six now run and pass.
- `SettingsSectionViewModels.cs` (881 lines, 11 classes) split one class per
  file; `DoctorService.cs` (1411 lines) split into partial-class files by
  domain (Startup, Storage, Runtime, Voice, Rag, System, Benchmarks). Both
  are pure moves with no logic changes.

### Fixed

- The chat "thinking" pulse animation (`aea2326`) never actually ran: it
  bound to `IsGenerating` on `MessageViewModel`, which has no such property,
  and set a `RenderTransform` via both a malformed attribute and a property
  element. Now bound to the message's own `IsStreaming`.

56 new tests (461 -> 517). Coverage floor: 49 -> 50 (narrative target).

## [0.12.0-alpha] - 2026-07-15

Implements docs/review r7: the Agent Scenario Suite, a library of small,
deterministic scenario workspaces that regression-test emergent agent
behaviour (retrieval, memory, the safety gate, approvals, and lessons
interacting) instead of testing any one feature in isolation. Every check is
a pure predicate over recorded artifacts (task status, safety-gate rows,
file hashes, answer substrings) - no LLM judge.

### Agent Scenario Suite

- **Engine** (`Aether.Agent`). `AgentScenarioManifest`/`AgentScenarioExpectations`
  model a scenario (goal, max steps, auto-approved tools, seeded memory/lessons,
  files placed outside the workspace for traversal tests, expectations).
  `AgentScenarioChecks` is a pure evaluator covering 11 deterministic check
  types (final status, approval-required/blocked/forbidden-execution per
  tool, files read/unread, files changed/unchanged, answer must/must-not
  mention, max new lessons, minimum gated risk, revertible-patch capture).
  `IAgentScenarioStore` loads built-in scenarios (shipped under
  `agent-scenarios/` next to the app) plus user scenarios from
  `{DataRoot}/agent-scenarios`, with user scenarios overriding built-ins by
  id. `IAgentScenarioRunner` executes a scenario against a real
  `AgentService` in a fully isolated sandbox: a copied workspace, a
  throwaway agent data root (its own task store and lesson store), and
  approvals that only ever grant (per-scenario `auto_approve` list) or are
  left pending - a scenario run can never deny (which would itself write a
  rejection lesson) and never runs an unapproved command. The user's real
  agent data root is untouched by any scenario run.
- **Library**: ten built-in scenarios, one behaviour each - conflicting
  documentation, prompt injection in a workspace file, secrets that must
  stay out of answers, a declared command that still needs approval, an
  undeclared command family that gets blocked outright, stale remembered
  context versus current code, a path-traversal read attempt, an
  approved-and-reverted edit, honest admission of undocumented behaviour,
  and lesson-store hygiene on a trivial task.
- **Workbench UI**: a "Scenario Evals" panel on the Agent view runs the
  suite (or one scenario) against the workbench's selected model with live
  progress, per-row pass/fail with failed-check detail, and a headline
  pointing at the exported report (`eval-runs/{suiteId}/report.md` +
  `run.jsonl`, alongside a new `EvalMode.AgentScenario` projection into the
  shared eval store).

### Other

- Doctor: llama-server-missing check now offers a "Download llama.cpp" fix
  action; build-number parsing handles the current `version: NNNN`
  llama-server output (no `b` prefix); tray-support check distinguishes
  confirmed Windows/macOS support from advisory-only Linux support.
- Model Management merges detected local GGUF files with runtime-reported
  models and shows a Running badge per model.
- Services: managed llama-server model path gets a detected-model dropdown;
  saving a managed server's port now syncs `Llm.LlamaCppBaseUrl` /
  `Rag.EmbeddingBaseUrl` and any linked runtime profile so chat/RAG follow
  the port instead of silently pointing at a stale one.
- Chat: forcing a model refresh now invalidates `CompositeLlmService`'s
  cached model list instead of returning stale data.
- Memory: `HybridRerankAsync` narrowed its SQLite scan to `id, embedding`
  and defers loading full high-relevance rows, cutting prompt-prep I/O.
  Embedding model warms up during app startup to avoid first-message
  latency. Kokoro phonemizer gained Magic-E vowel shifting, more digraph
  coverage, rhotic vowel rules, and dedicated affricate phoneme tokens.
- Fixed a shutdown crash by wrapping the exit handler's `ServiceProvider`
  disposal in try/catch/finally.

## [0.11.0-alpha] - 2026-07-12

Implements docs/review r6 in full: answerability. A first-time user can now
answer, from visible UI, where their data lives, whether anything leaves the
machine, which model answered, why files/chunks were selected, why a patch
was flagged risky, and whether they can undo it.

### Answerability surfaces

- **Local/remote badges.** Chat's model selector shows a "Local"/"Remote"
  badge (`ChatViewModel.IsSelectedModelRemote`, driven by the shared
  `CompositeLlmService.Providers` registry, the same source Privacy Audit
  already used).
- **Outbound-destinations summary.** System Overview's Privacy Audit is
  retitled "Privacy audit - what can leave this machine" and gains a
  one-line count (`PrivacyAuditService.CountOutboundDestinationsAsync`)
  across remote chat/voice providers, web-ingest-enabled RAG datasets, and
  MCP servers.
- **Data root affordances.** Settings > Data gets "Open folder" buttons for
  the data root and AI assets root (resolved live, not the raw text box),
  plus a "Your data, your machine" explainer and matching statement in
  System Overview.
- **Toolbar labels.** `Ui.ShowNavLabels` (default off) adds text captions
  next to the 13 previously icon-only toolbar buttons.
- **Per-message model attribution.** Audited and found already correct
  end-to-end (`Message.ModelId`/`DurationMs` round-trip through
  `messages_json`); added a regression test rather than new code.
- **RAG retrieval breakdown.** `RagTraceChunk` now carries per-signal vector/
  keyword/rerank scores and a deterministic plain-language summary ("Ranked
  2nd of 8: strong semantic match, term 'x' matched 3 times, reranker
  confirmed this ranking"), shown in the RAG source inspector.
- **Agent risk reasons + recipe transparency.** `AgentPendingToolAction`
  carries the safety gate's `Reason`; the review queue shows it plus, for a
  pending `run_command` approval, what it actually executes
  (`AgentApprovalPreview` - the exact npm script body from package.json, or
  a fixed provenance note for dotnet/cargo/pytest).
- **Agent context receipt.** Each step's context pack summarizes per
  section (Memory/RAG/Workspace files/Project instructions/Lessons) as item
  count + token estimate (`AgentContextReceiptBuilder`), omitting empty
  sections rather than showing them blank.
- **Applied-patch revert.** `AgentDraftPatch` captures a pre-image at apply
  time (both the manual draft-patch queue and direct `edit_file`/
  `create_file`/`apply_draft_patch` approvals); Revert restores it or
  deletes a created file, refusing if the file changed again since.
- **New-lesson review strip.** A task reaching a terminal state with newly
  *created* (not merely reinforced) lessons shows them once with Keep/Retire
  actions, tracked via `AgentTaskState.NewLessonIds`.

### Usage-aware benchmark insights

- **`model_usage` rollup.** `SqliteTraceStore` maintains a durable per-
  model/day/kind counter table alongside the existing trace log, unaffected
  by trace pruning. `IModelUsageService` exposes windowed summaries.
- **Usage insights.** `BenchmarkInsightsMath.BuildReport` takes optional
  per-kind usage; for any activity (Chat/RAG/Agent) with 20+ calls in 30
  days it names the dominant model, and recommends a switch only when a
  real leaderboard entry outranks it by the same 10-point gap threshold as
  the r5 Doctor advisory. A "Based on your usage" card appears in the
  Benchmarks Insights tab; the Doctor advisory gets at most one appended
  usage-aware sentence, Info severity only.

### Platform cleanup

- **Removed `InspectionEngine`/`IInspectionCheckProvider`.** Dead
  aggregation layer nothing consumed; Doctor/Trust/Privacy Audit already
  each own their checks and views directly.
- **Remote voice disclosure.** Settings > Voice shows an inline note per
  enabled channel when the active provider is remote; Privacy Audit gains a
  matching item.

Coverage floor raised 47 -> 48.

## [0.10.0-alpha] - 2026-07-12

Implements docs/review r5 in full: voice stops being "TTS for chat" and
becomes a shared service for the whole app, and the benchmark store grows a
deterministic recommendation engine on top of data it already persists.

### Voice orchestration

- **`IVoiceOrchestrator`.** A single background worker drains a priority
  queue (Low/Normal/Critical) one utterance at a time, so two consumers can
  never overlap audio without a mixer. Critical utterances preempt the
  current playback and clear queued Low items; same-key `DedupeKey`
  utterances collapse; channels default to disabled except Chat, which
  preserves the pre-r5 manual speak button.
- **Voice profiles and per-channel settings.** `TtsSettings.Profiles`
  (named voice/speed combinations) and `TtsSettings.Channels` (per-channel
  enable + profile) are new, additive settings fields; a Settings > Voice
  card exposes a mute toggle, auto-speak, streaming speech, and the
  channel/profile editors.
- **Six consumers.** Chat can auto-speak replies (`Tts.AutoSpeakChatReplies`,
  off by default) and, experimentally, speak them sentence-by-sentence while
  the model is still streaming (`Tts.StreamingChatSpeech`, via a new
  `SentenceChunker`). The agent narrates milestones only (task started,
  waiting for approval/reply at Critical priority, terminal Complete/Failed)
  and can use a per-workspace voice profile stored in `.aether/workspace.json`.
  Doctor speaks one Critical summary per scan when it finds Errors.
  Benchmarks announce run completion. A new `VoiceNotificationBridge`
  forwards Warning/Error toasts onto the Notification channel.
- All spoken chat text passes through `ChatSpeechSanitizer`, which replaces
  fenced/inline code with a spoken placeholder so TTS never reads code
  character by character.

### Benchmark insights

- **Tag propagation.** `BenchmarkCase.Tags` now copies onto
  `BenchmarkResult.Tags` at scoring time; `BenchmarkInsightsService`
  suite-joins tags back onto older runs whose originating suite still
  exists, so historical runs still feed per-tag leaderboards.
- **`BenchmarkInsightsService` + `BenchmarkInsightsMath`.** Deterministic,
  no LLM: groups runs by model + quantization + runtime, filters to
  hardware comparable to the current machine (GPU name + VRAM within 10%,
  or both CPU-only), and ranks by `QualityPerSecond = quality *
  log2(1 + tokens/sec)`. Requires at least 2 runs and 10 cases before a
  model appears in any leaderboard; flags stale results (>60 days old or a
  different Aether minor version). Produces pairwise comparison sentences
  ("X scores 4% lower than Y but runs 2.3x faster").
- **Insights tab** on the Benchmarks page: best-overall card, per-tag
  leaderboards with comparison sentences, caveats, and a re-run shortcut.
  Loaded on demand, not on page open.
- **Doctor advisory check.** Info-only: when the selected chat model ranks
  more than 10 points behind the best comparable model, Doctor names both.
  Never Warning/Error, never switches anything automatically.

55 new tests (327->382), coverage floor raised 45->47. Ships as 0.10.0-alpha because
two new user-facing capabilities landed in the same round. Archived at
docs/review/archived/r5/.

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

