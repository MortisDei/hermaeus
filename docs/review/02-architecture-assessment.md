# Architecture Assessment (r2)

State of the structure after r1 was fully actioned (v1.0 hardening, the
four 1.x unifications, the five 2.0 compositions, and the v3.0
extraction/collapse pass). ~33.7k lines of C# across 10 projects plus a
sequential xunit suite of ~105 tests.

## What worked

- **The extraction pass was real, not cosmetic.** The six-part ViewModel
  orchestration extraction (0.9.32 to 0.9.37) produced genuinely pure,
  independently tested classes (`ChatSendOrchestrator`, `RagStreamProtocol`,
  `ChatContextUsageCalculator`, etc.), each with its own test file. The
  pattern of "extract + write the first-ever test for that logic" is the
  right one; keep it for anything else that leaves a ViewModel.
- **The composition root split held.** `Aether.Composition` registers the
  full non-UI graph once and both hosts consume it. Nothing UI-flavored has
  leaked into it. Architecture tests fence the three forbidden edges
  (ViewModels/Avalonia, ONNX/Rag, Agent/Mcp).
- **The reasoned rejections were kept.** Settings-monolith narrowing
  (0.9.30) and the tag-keyed provider dictionary (0.9.31) were rejected
  with written rationale instead of half-done. This discipline is worth
  more than either refactor would have been.
- **Security posture survived feature growth.** New process launches
  (`McpClient`, `AgentToolExecutor.RunCommandAsync`) use `ArgumentList`,
  never shell strings. The local API binds loopback and fails closed. The
  agent's command execution is triple-fenced. The audit (doc 01) found seam
  bugs, not posture violations, with one exception: the phantom
  `LocalApi.Enabled` toggle (P1-1), which is a docs-truth failure of the
  kind this project has explicitly promised not to ship.

## Residue and watch items

1. **The three big services are still big, and that is now acceptable.**
   `DoctorService` (1271), `LocalAiSetupService` (1025), `BenchmarkService`
   (956). r1 called for check/fix registry unification, which happened;
   what remains in Doctor is mostly per-check content, which does not
   shrink by moving it. Recommendation: stop tracking raw line count as
   debt for these three. The enforceable rule stays the AGENTS.md one: new
   subsystem knowledge lives with the subsystem, Doctor only aggregates
   `IInspectionCheckProvider` output.
2. **`ChatViewModel` (966) and `AgentViewModel` (899) are at their floor
   only if extraction is truly done.** Post-extraction they should be
   UI-state mutation plus command wiring. A quick scan suggests they mostly
   are, but each still exceeds every service in the codebase except Doctor.
   One more honest pass over each asking "does this method touch anything
   but observable properties and orchestrator calls" is cheap; if the
   answer is yes for less than ~100 lines, declare the floor reached and
   record it, r1-style, as a reasoned stop.
3. **The `__RAG_SOURCES__` sentinel protocol now has three consumers**
   (`RagStreamProtocol`, its two original call sites, plus a private
   re-implementation in `LocalApiEndpoints.ParseSources`). This is the
   stringly-typed seam r1 predicted would spread. It is also the biggest
   single argument for the deferred `SourceReference` convergence: doc 03
   sequences that work first for exactly this reason.
4. **`Aether.Voice` duplicated the download/verify pattern instead of
   sharing it.** `KokoroOnnxModel.DownloadIfMissingAsync`/`VerifySha256Async`
   re-implements what `OnnxCrossEncoderReranker` (and Doctor's component
   installs) already do: temp + SHA256 + move. Three copies is a pattern;
   extract a small `PinnedDownloader` into `Aether.Core.Services` (or
   `Aether.Services`) when next touching any of them. Not urgent.
5. **The static `HttpClient` census.** `KokoroOnnxModel` adds another
   static `HttpClient` with its own timeout policy. Fine individually, but
   the app now has several; when one needs a proxy setting or a user-agent
   policy, do it once. Watch item only.
6. **Test suite remains sequential by necessity** (shared temp data roots
   and SQLite pools). At ~105 tests this is fine; at 400 it will hurt.
   If suite time ever exceeds a couple of minutes, the fix is per-test
   isolated data roots, not re-enabling parallelization blindly. Recorded
   so the future fix is the right one.

## Explicit non-goals reaffirmed

Nothing in this round changes the r1 non-goal list (hosted services,
accounts, telemetry, in-process plugin API, provider failover, vector DBs,
ORM, web UI). MCP-over-HTTP/SSE remains out until a concrete local server
requires it.
