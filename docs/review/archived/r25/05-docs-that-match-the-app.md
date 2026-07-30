# 05. Docs that match the app

## The problem

The owner's observation: the README does not get updated every round.

Concretely, at `1bd3f2d`, one commit after r24 merged:

- r24 shipped Projects, Recall, the command palette, watched RAG sources,
  the Activity feed and local speech recognition.
- README's "Major Features" section (`README.md:29-137`) mentions none of
  them. There is no Activity, no palette, no dictation, no watched sources
  and no Recall in the feature narrative at all.
- The documentation list (`README.md:205-217`) **was** updated, with
  `docs/projects.md` and `docs/recall.md` both added correctly.

So the failure is specific and diagnosable rather than general neglect: the
index of documents is maintained because it is an obvious checklist item,
and the feature narrative is not because nothing checks it. Anyone opening
the repository today reads a description of 0.30.0.

This repository already prefers mechanical fixes to process reminders:
`HarnessRegistrationGuardTests` catches unregistered tests,
`NamingConsistencyTests` catches rename drift, and an axaml scan catches
missing tooltips. Documentation drift should be caught the same way.

## 5.1 A docs-coverage guard test

- Parse `src/Hermaeus.Desktop/Views/MainWindow.axaml` for
  `Show*PanelCommand` bindings. There are twelve today, at `:45`, `:54`,
  `:63`, `:72`, `:82`, `:100`, `:109`, `:118`, `:138`, `:147`, `:156` and
  `:183`.
- Derive the panel name from the command name (`ShowActivityPanelCommand`
  gives `Activity`).
- Assert each name appears in `README.md` and in `docs/features.md`.
- **It fails today, on Activity.** That is the point: the guard's first act
  is to catch the exact drift that prompted it, before anyone has to
  remember anything.

Keep it dumb. A regex over one axaml file plus two substring checks. A
guard test that needs maintenance is a guard test that gets deleted the
first time it is inconvenient.

## 5.2 A standing ledger of deferred items

New file `docs/review/deferred.md`: one table of every item any round
deferred rather than rejected, with the round that deferred it, the stated
reason, and its current status.

Today that history is spread across ten roadmaps in
`docs/review/archived/` and is only findable by grepping for the word
"deferred". That is how an item goes missing for twenty rounds without
anyone deciding to drop it.

Seed it from 5.3 below. Each round's close-out updates it, which is a
two-line addition to an existing checklist rather than a new process. Link
it from the README's documentation list.

## 5.3 The audit

Everything twenty-four rounds deferred, verified against `1bd3f2d`.

### Being implemented this round

| Item | Deferred by | Now |
| --- | --- | --- |
| Conversation branching and message-edit forks | r24, recorded as "the leading r25 candidate rather than smuggled in" (`archived/r24/06-roadmap.md:186-189`) | Doc 01 |
| In-process Whisper | r24, rejected as too large alongside three other features (`archived/r24/06-roadmap.md:176-179`) | Doc 03 |

### Already closed, ledger records it so nobody re-checks

| Item | Deferred by | Closed by | Evidence |
| --- | --- | --- | --- |
| Per-app tokens for the local API | r1 (`archived/r1/07-roadmap.md:166-171`) | r2 | `LocalApiSettings.Tokens` (`LocalApiSettings.cs:43-48`) |
| Embeddings endpoint | r1 | r2 | `POST /v1/embeddings` (`LocalApiEndpoints.cs:139`) |
| Structured source reference on memories | r1 (`archived/r1/07-roadmap.md:184-190`) | later round | `MemoryStore.cs:976`, round-trip and backfill tested at `ServiceTests.cs:2452-2476` |
| Chat consuming RAG and memory citations | r1 ("net-new UI, not part of this slice") | later round | `MessageViewModel.CitationSources` / `MemorySources`; doc 02 rebuilds the presentation |
| Per-feature model-usage counters | r5 (`archived/r5/02-benchmark-insights.md:186-190`) | r6 | `UsageInsight` (`BenchmarkInsightsModels.cs:51-59`) |
| Task-terminal lesson capture | r3 | r4 (`archived/r4/02-lessons-v2.md:106`) | |
| Recent-tasks list | r15 | r16 (`archived/r16/03-workbench-and-desktop.md:9`) | |
| N-gram speculative decoding | r18 4.4 | shipped | `ServerConfig.NgramSpeculative`, emitted at `ServerProcessManager.cs:584-587` |

### Still deferred, reasons restated so r26 does not re-litigate

- **Agent run/step endpoints on the local API** (r1, restated r2
  `archived/r2/03-next-level-roadmap.md:70-72`). The blocker is unchanged
  and is not about effort: there is no design for how a non-interactive
  caller satisfies the agent's approval gate. The gate is the product. An
  endpoint that bypasses it is not a smaller version of the feature, it is
  the opposite of the feature. This needs its own design pass, not a slot
  in a feature round.

- **Workflow composition and task orchestration** (r1 Opportunities #9,
  `archived/r1/03-architectural-opportunities.md:117-124`). The stated
  condition for revisiting was observed sequencing pain from real
  `run_command` use. That evidence still does not exist. r1's own words
  remain the argument: "Building an orchestrator before the primitives are
  proven is how projects acquire their worst code."

- **Draft-model speculative decoding** (r18 4.4,
  `archived/r18/04-llama-server-engine-options.md:141-147`). A real
  speedup, correctly deferred again. It needs a second model file, a second
  VRAM budget, and a draft-model picker whose wrong answer silently costs
  performance rather than failing visibly. That is a whole doc, and this
  round already has four. **The strongest r26 candidate on this list.**

- **A settings and capabilities probe endpoint on the local API** (r1).
  Small and still open; `LocalApiEndpoints.cs` has `/health`,
  `/v1/chat/completions`, `/v1/memory/query`, `/v1/rag/query`, `/v1/models`
  and `/v1/embeddings`, and nothing that reports what the instance can do.
  Good r26 filler, too small to lead a round.

- **MCP HTTP and SSE transport** (r2 Phase 3). `McpClient` is stdio only
  (`McpClient.cs:79`). No demand observed; local servers are the use case.

- **Remaining provenance convergence** (r1). Narrower than r1 described,
  and worth recording accurately: r2 already replaced the
  `__RAG_SOURCES__` sentinel strings with a typed `RagStreamEvent`
  (`src/Hermaeus.Rag/RagStreamEvent.cs`). What remains is that
  `RagStreamEvent` carries `RagTraceChunk` rather than `SourceReference`.
  Chat does not use that path at all: it builds `SourceReference` directly
  from packed chunks (`ChatViewModel.cs:1491-1496`). So the divergence only
  affects the RAG panel's own trace view, where the richer shape is
  currently earning its keep. Not worth converging until something needs
  it. Doc 02 deliberately does not touch this.

- **Multi-machine sync of the data root** (r1, r2). Still no cloud, by
  design. User-owned file sync of the data root remains the answer and
  needs no code.

## 5.4 Test-suite health (added mid-round, at the owner's request)

Three defects in the test infrastructure itself, all found while
investigating suite runtime. Recorded here because they are the same class
of problem as the rest of this doc: the tooling was reporting something it
could not actually support.

- **`TempDir.Dispose` slept for up to 3.4 seconds per temp root.** It
  retried `Directory.Delete` with a growing backoff over 10 attempts
  (`Thread.Sleep(75 * attempt)`) and still rethrew on the last one, so it
  paid a large, unbounded cost and kept the failure mode it was added to
  prevent. Deleting a temp directory is housekeeping, not an assertion. Now:
  try, clear the SQLite pools, one 25ms retry, then defer the path to a
  single process-exit sweep. Cleanup can no longer fail a test.

- **`WaitForAsync` existed in five near-identical private copies** with
  three different timeout policies, and **two of them returned silently on
  timeout**. A wait helper that gives up quietly converts a real failure
  into a confusing downstream assertion, or into a silent pass. Consolidated
  into one `Helpers.WaitForAsync` that always asserts and names what it was
  waiting for. The polling itself stays: it is the documented workaround for
  `RunOnUi` posting rather than running inline under xUnit's
  `AsyncTestSyncContext` (r12 02-async-and-threading.md), not a smell.

- **One genuinely flaky test.**
  `AcceptanceTests.AgentUiAcceptance_PatchQueueMetadataIsRendered` fired an
  async `[RelayCommand]` via `ICommand.Execute` (fire and forget) and then
  polled a 3000ms wall clock for the result. It failed in 2 of 4 full-suite
  runs and passed in isolation. Fixed by awaiting the command's own task,
  not by widening the budget.

**Measurement discipline for future rounds.** Suite wall clock on this
machine varies by roughly a minute run to run, which is enough to invent a
regression that is not there. A controlled back-to-back check is the only
reliable comparison: this round recorded 2m47s at `0142f9f` against 3m06s at
the doc 04 commit, which is inside the noise, while earlier readings of
1m20s on the same code turned out to be a quiet machine rather than a
faster suite. Do not act on a single timing observation.

**Not fixed, recorded deliberately.** Two tests assert that something has
*not* happened yet after a fixed `Task.Delay`
(`MainWindowViewModelStartupTests` asserting a debounce has not fired at
150ms; `McpTests` asserting a call fails in under 5000ms rather than waiting
out a 30s timeout). Both are races in principle. Making them deterministic
needs an injectable clock in production code, which is a larger change than
the risk justifies and is not what this round is for. Neither has been
observed to fail.

## 5.5 Reconcile the stale passages

Do these as part of the round's close-out, once the features they describe
exist:

- **README "Major Features"**: add Projects, Recall and the command
  palette, watched sources, Activity and speech input from r24, plus this
  round's branching. This is the item that prompted the whole doc.
- **`docs/features.md`**: anything describing regenerate as replacing the
  previous answer, once doc 01 lands.
- **`docs/voice.md`**: the local speech model's identity, language coverage
  and punctuation behaviour, once doc 03 lands.
- **`docs/benchmarks.md`**: how Best overall is computed and when it
  declines to name a winner, once doc 04 lands.

## Testing

Roughly 4 to 6, plus the test-health work in 5.4 which removes cost and
nondeterminism rather than adding cases.

The panel-coverage guard itself. A test that the guard actually fails when
given a fixture README missing a panel name, because a guard test that
cannot fail is worse than none. A test that `docs/review/deferred.md`
exists and is linked from the README documentation list.
