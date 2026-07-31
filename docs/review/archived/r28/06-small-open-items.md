# 06. Small open items

Items that do not need a document of their own. Each is independent and each
can be cut without affecting anything else in the round.

## 6.1 Pin the `run_command` risk classification

**Resolved during planning. This item records the answer and pins it; it is
not an investigation.**

`docs/agent.md:529-536` documents the risk levels as a table naming tools,
listing `run_command` under Review ("queue for approval"). `AgentSafetyGate`
appears to disagree: `run_command` is in `HighRiskTools`
(`AgentSafetyGate.cs:19-30`) and `Evaluate` tests that set at `:55-56`,
returning `Blocked`, before reaching the mutating-substring heuristics at
`:58-66`.

**The documentation is correct.** `run_command` never reaches `Evaluate`.
`AgentService.cs:382-384` dispatches it explicitly:

```csharp
else if (string.Equals(nextTool, "run_command", StringComparison.OrdinalIgnoreCase))
{
    var requestedCommand = AgentToolExecutor.Arg(response.NextAction.Arguments, "command");
    decision = _safetyGate.EvaluateCommand(requestedCommand, manifest?.AllowedCommands ?? []);
```

`EvaluateCommand` (`AgentSafetyGate.cs:71`) resolves a command family,
blocks anything the workspace has not declared, and returns
`RequiresApproval` for a declared family (`:102`). That is Review, exactly
as documented. The `HighRiskTools` entry is defence in depth for any future
caller reaching the generic path, not dead code and not a contradiction.

One nuance the table does not capture and should:
`AgentService.cs:386-394` can flip that `RequiresApproval` to `Allowed` when
the identical command string was already approved once in this task
(`RememberedCommandApprovals`). The comment there is explicit that this
never widens to the template family. r26 rejected widening it further; this
item only documents that it exists.

Work:

- Add a comment at `AgentSafetyGate.cs:19` naming `EvaluateCommand` as the
  method that governs `run_command`, so the next reader does not repeat this
  trace.
- Add a regression test pinning both halves: `Evaluate("run_command")`
  returns Blocked, and the dispatch path routes through `EvaluateCommand`
  and yields RequiresApproval for a declared family.
- Fix the table's Safe row, which omits `summarize_file`, `draft_patch` and
  `inspect_git_diff` though all three are in `ReadOnlyTools`
  (`AgentSafetyGate.cs:7-16`).
- Add the remembered-approval nuance to `docs/agent.md` beside the table.

## 6.2 The docs guard covers enumerable facts

`DocsCoverageGuardTests` (r25 doc 05 5.1) exists because documentation drift
has cost this repository twice: r24 shipped six features the README's
narrative never mentioned, and r27 5.2 found the README claiming 0.24.0-alpha
against a `Directory.Build.props` reading 0.33.0, a gap that had been open
since r24. Both were caught by a person, and the guard was widened
afterwards each time.

The guard's own header states its design constraint and it is a good one:
"Deliberately dumb: one regex over one file, then a case-insensitive
substring check. A guard that needs maintenance is a guard that gets
deleted the first time it is inconvenient."

Extend it in that style to facts that are enumerated in code and enumerated
in prose:

- **`docs/agent.md`'s risk table against `AgentSafetyGate`'s two hash sets.**
  Every tool name in `ReadOnlyTools` and `HighRiskTools` appears somewhere in
  the table's section. Land this after 6.1, so it pins a resolved answer
  rather than freezing a discrepancy.
- **`docs/benchmarks.md`'s recorded-metadata list against
  `BenchmarkMetadata`'s properties.** The doc claims a specific set is
  recorded (`docs/benchmarks.md`, "Runs record the following metrics and
  metadata"); assert each property name appears.
- **CLAUDE.md's settings-section list against `AppSettings`.** CLAUDE.md
  enumerates the domain sections (Llm, Tts, Rag, Ui, DataManagement, Memory,
  Mcp, LocalApi, Agent); assert each is a property on `AppSettings` and that
  no section exists on `AppSettings` that CLAUDE.md does not name.

Substring assertions over names, in the existing file. Not prose analysis,
not schema comparison, and explicitly **not** generating documentation from
code, which r25 rejected: "5.1 asserts that a name appears in a file. It does
not write prose."

## 6.3 NuGet caching in CI

Restore costs 36s on Windows and 15s on Linux with no cache configured
(`.github/workflows/ci.yml`). Enable package caching.

Worth 20 to 30 seconds per run, which is small and is also the only free
saving available. It is listed here rather than in doc 04 because it is
unrelated to the 419-second test gap and must not be confused for a fix
for it.

Keep it boring: `actions/setup-dotnet`'s own cache input, or
`actions/cache` over the NuGet packages folder keyed on the lock files. No
new actions beyond what the workflow already uses plus a first-party one.

## 6.4 A forward pointer on the first Speed Check result

`docs/benchmarks.md`'s "First recorded result (r27, 0.34.0-alpha)" section
stays exactly as written. It is an honest record of what was known when it
was taken and it does not get edited to look better in hindsight.

Add a short note after it: r28 added draft acceptance statistics and
repeated iterations (doc 02), so a rerun can now distinguish "drafting
engaged and did not help" from "drafting never engaged". Name the two items
so the connection is findable.

Do not add a rerun result. If the owner reruns it, that is their entry to
write, as this one was.

## 6.5 Docs, changelog, deferred ledger

- `docs/features.md`: constrained decoding (doc 01), draft acceptance and
  spread (doc 02), Activity links and the four new event sources (doc 03).
  The Activity section's admission at `:909-911` that four sources are not
  wired in gets corrected, not deleted, once 3.3 lands.
- `docs/benchmarks.md`: doc 02's acceptance statistics and iteration change,
  plus 6.4.
- `docs/agent.md`: whatever 6.1 resolves, plus doc 05's constrained planner
  protocol and the changed parse-failure message (5.4). The "Action Risk
  Levels" section gains a sentence stating that a constrained response is
  still classified identically, because that is the question a reader of
  that section will have.
- `README.md`: version, and the feature narrative if doc 01 lands. Run the
  r25 guard.
- `CHANGELOG.md`: one entry per shipped item, written as behaviour, not as
  a list of types added.
- `docs/review/deferred.md`: see doc 07's housekeeping.
