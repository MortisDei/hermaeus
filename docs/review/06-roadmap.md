# 06. Roadmap

## Scope contract

**In r30:** every acceptance criterion in docs 01 through 05. Automatic
GGUF/runtime capability detection, separate reasoning content, and full
reasoning preservation are mandatory scope, not stretch goals.

**r30 add-on:** benchmark provenance also records managed llama-server KV cache
K/V types and Flash Attention on new local-GGUF runs, including run details,
comparisons, and exports. It is additive storage and measured configuration
truth only, not an optimisation feature.

**r30 Batch #3 add-on:** runtime capability discovery now gates speculative
launches and separate prompt-processing threads, records meaningful capability
drift to Activity, and gives Moss one deduped capability heads-up per changed
snapshot. It does not expose arbitrary draft-model, backend-sampling, cache,
or performance-debug controls without a stable runtime contract and measured
single-user benefit.

**Deferred to r31 before implementation starts:** audio feedback, normalized
model-facing tool outcomes, and the empirical experience store. Those remain
worthwhile cross-cutting designs. The bounded transcript-compaction and
successful-loop diagnostic work landed as a post-scope r30 add-on and does not
introduce a normalized outcome vocabulary.

This is the owner sanity-check boundary. Do not replace doc 05 with a launch
checkbox or treat an Unknown capability as Unavailable to save work.

## Version and branch

Ships as **0.37.0-alpha**. Bump `VersionPrefix`, `AssemblyVersion`, and
`FileVersion` in `Directory.Build.props`; update README and changelog version
surfaces. Branch `r30/round` from `main`, one PR, merge commit. The owner alone
pushes `v0.37.0-alpha` after merge.

## One-month sequence

Strict order. Commit after each numbered row so an interrupted implementation
loses at most one bounded unit.

| # | Work | Why here |
| --- | --- | --- |
| 1 | 01.1 golden-path Linux reproduction | No onboarding code changes before the observed boundary is proven |
| 2 | 01.2 download adoption and retry | Removes the confirmed existing-file dead end |
| 3 | 01.3 immediate Services usability | Completes L-P0 rather than stopping at a file on disk |
| 4 | 01.4 and 01.5 recovery plus root persistence | Closes both L-P1 reports while the harness is active |
| 5 | 05.1 GGUF and executable capability facts | Establishes the evidence contract used by Models and Services |
| 6 | 02.1 canonical KV migration and launch | Data contract for every later model item |
| 7 | 02.2 and 02.3 shared defaults and truthful fit | The non-descopable Models/Services parity request |
| 8 | 02.7 and 02.8 capability-aware companion choices | Fixes stale live config and proves built-in MTP from GGUF data |
| 9 | 05.2 provider-neutral reasoning stream | Adds the transport contract before persistence or UI consumes it |
| 10 | 05.3 persistence, branching, budgeting, and replay | Makes separate reasoning survive a real conversation |
| 11 | 05.4 capability probe and preservation control | Completes the runtime and history behavior against the real template |
| 12 | 05.5 transcript and export presentation | Makes the new model legible only after its data path is complete |
| 13 | 02.4 download state and progress | Shares destination/adoption reasoning with doc 01 where practical |
| 14 | 02.5 safe model deletion | Destructive path gets its own commit and review |
| 15 | 03.3 Memories commands | Broken actions before polish |
| 16 | 04.1 through 04.5 benchmark truth | Fixtures first; reasoning-only output remains an honest failure |
| 17 | 03.1, 03.2, 03.4, 03.5 and 02.6 | Bounded presentation and input corrections after contracts settle |
| 18 | Full docs, changelog, version, build, test, coverage | Close-out reads the actual shipped state |

Rows 9 through 12 are one vertical feature split into reviewable commits. Do
not release between them.

## Test budget

Expected **100 to 130 new tests**, plus updates to existing launch, settings,
profile, chat, export, benchmark, and guard tests.

| Source | New tests |
| --- | ---: |
| Linux onboarding | 14-18 |
| KV/default/fit/download/delete/pickers | 30-38 |
| UI correctness | 10-14 |
| benchmark truth | 16-20 |
| capabilities and reasoning | 30-40 |

If the count is materially lower, the likely omissions are Linux process-level
reload, destructive path rejection, old divergent K/V migration, negative
benchmark controls, capability cache invalidation, reasoning-only cancellation,
or history replay under unsupported and disabled states.

Run:

```powershell
dotnet build Hermaeus.sln
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --results-directory "$env:TEMP\hermaeus-r30-tests"
pwsh ./scripts/coverage.ps1
```

The coverage floor remains 60%. Do not add padding tests to move the number.

## Descope order if the month is at risk

Cut from the top of this list first, and update this scope contract plus
`deferred.md` in the same commit:

1. 02.6 responsive editor width.
2. 03.5 export consolidation. Keep doc 05's reasoning-aware Markdown and JSON
   output even if the two existing export buttons remain.
3. 03.2 ComboBox wheel fix, only if reproduction says it is intermittent and no
   stable shared popup mechanism is found. Record the routing trace.
4. 02.5 model deletion.
5. 02.4 download progress presentation, keeping correct on-disk detection if it
   shares doc 01's adoption helper.

Do not descope below this line:

6. any of doc 01;
7. 02.1 through 02.3, 02.7, or 02.8;
8. any of doc 05, including capability Unknown states and reasoning-preserve;
9. 03.1 cursor gaps, 03.3 Memories, or 03.4 neutral numeric defaults;
10. doc 04's proven false-fail fixes.

## Practical warnings

- Do not use the owner's real settings or data in tests. The benchmark rows in
  doc 04 become minimal fixtures and nothing more.
- Do not run Desktop casually. It shares the installed app's settings and data
  root. Close it cleanly and never force-kill it.
- Model deletion is permanent. Resolve and validate exact files before showing
  confirmation, then delete only that immutable confirmed set.
- `ServicesViewModel`, `ModelManagementViewModel`, and `ChatViewModel` are hot
  spots. Keep capability merge, history policy, and shared model defaults in
  small services or helpers instead of adding more cross-subsystem policy to
  those large classes.
- JSON message and settings changes are additive. No SQLite schema change is
  expected. Derived capability state is an atomic cache, not settings.
- Do not write a Jinja parser. `/props.chat_template_caps` is the runtime's
  authoritative template interpretation.
- A popup-wheel guard that merely asserts a handler exists proves nothing.
  Keep a pure offset test and record live Windows/Linux verification.

## Housekeeping

- Update `docs/features.md` for every shipped behavior.
- Update `docs/benchmarks.md` for scorer/version semantics and reasoning-only
  final-answer behavior.
- Update `docs/llama-cpp-features.md` with b10227 capability detection,
  reasoning extraction, and preservation as shipped work.
- Update `CHANGELOG.md`, maintaining its ten-version FIFO into
  `docs/changelog-archive.md`.
- Update README version and any panel/feature inventory caught by
  `DocsCoverageGuardTests`.
- Archive this pack to `docs/review/archived/r30/` only at close-out, after its
  progress/landed state is truthful.

## Explicit rejections

- **No second settings save path.** Wizard, Models, and Services route through
  existing services and atomic persistence.
- **No entire Services editor in a model card.** Process-instance settings do
  not become model metadata.
- **No independent K and V UI.** llama.cpp accepts it; the owner deliberately
  reversed r18's product decision.
- **No silent overwrite on download or delete.** Existing unrelated files are
  conflicts, and deletion targets are exact.
- **No recursive model-directory deletion.** Empty directory cleanup remains a
  separately confirmed action.
- **No filename capability guesses.** MTP comes from GGUF NextN metadata,
  runtime support comes from the selected executable, template support comes
  from `/props`, and uncertainty remains visible.
- **No reasoning checkbox without plumbing.** The control ships only with
  separate deltas, persisted message data, conditional history replay,
  transcript rendering, and export.
- **No raw `<think>` scraping in Core.** Provider/runtime parsers own reasoning
  syntax. Legacy inline content remains unchanged.
- **No hidden reasoning in memory, Recall, ordinary search, title generation,
  speech, or ordinary Copy.** Those systems consume the answer unless a future
  design explicitly changes their contract.
- **No mutual exclusion between n-gram and MTP.** The current server explicitly
  accepts a list and there is no measured evidence that composition is dead.
- **No LLM benchmark judge, fuzzy-everything matcher, or historical rescoring.**
  Fix only the deterministic false-fail boundaries proven in doc 04.
- **No audio in r30.** A couple of ad hoc beeps without mute, accessibility, and
  event policy would become another inconsistent subsystem.
- **No additional TLB feature work in r30.** The bounded transcript replay
  add-on is complete; normalized outcomes and empirical experience remain
  deferred and do not enter through its replay metadata.
- **No new NuGet packages.** Every item is buildable from current dependencies.
- **No version tag or release action.** Owner-only.
