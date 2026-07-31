# 05. Small open items

Items that do not need a document of their own. Each is independent and each
can be cut without affecting anything else in the round.

## 6.1 Resolve the `run_command` risk-table discrepancy

**This one first, because it may be a real defect in a security document.**

`docs/agent.md:529-536` documents the risk levels as a table naming tools:

| Level | Examples | Behaviour |
| --- | --- | --- |
| Safe | list files, search, glob, read, `set_plan` | execute directly |
| Review | edit_file, create_file, apply_draft_patch, **run_command**, mcp: calls | queue for approval |
| Blocked | shell, network, install, commit, push | do not execute |

`AgentSafetyGate` disagrees on its face. `run_command` is in `HighRiskTools`
(`AgentSafetyGate.cs:19-30`), and `Evaluate` tests `HighRiskTools` at
`:55-56`, returning `Blocked`, before reaching the mutating-substring
heuristics at `:58-66`. So `Evaluate("run_command")` returns Blocked while
the documentation says Review.

There is a second method. `EvaluateCommand` (`:71`) resolves a command
family, refuses undeclared families, and returns `RequiresApproval` for a
declared one (`:102`). If real `run_command` dispatch goes through
`EvaluateCommand`, the documentation describes the effective behaviour and
`Evaluate`'s `HighRiskTools` entry is either defence in depth or dead.

**Planning did not determine which.** Trace the actual dispatch path in
`AgentService` and then do exactly one of:

- If `EvaluateCommand` governs: the table is right. Add a comment at
  `AgentSafetyGate.cs:19` naming which method governs `run_command` so the
  next reader does not repeat this. If the `Evaluate` path is genuinely
  unreachable for `run_command`, say so in the comment rather than deleting
  the entry.
- If `Evaluate` governs: the table is wrong in a security document, and
  `docs/agent.md` is corrected in the same commit.

Either way, add the regression test that pins the answer.

The table also omits `summarize_file`, `draft_patch` and `inspect_git_diff`
from Safe, though all three are in `ReadOnlyTools`
(`AgentSafetyGate.cs:7-16`). Fix the table.

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
