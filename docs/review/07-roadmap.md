# 07. Roadmap and sequencing

## Version

Ships as **0.35.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.34.0). Minor bump: constrained
decoding, a constrained agent planner, draft acceptance reporting and
Activity links are all user-visible. After merge the owner tags
`v0.35.0-alpha`, which becomes the release through the tag-driven workflow.
Agents never push a tag.

## Progress table

**Update this table in the same commit as the work it describes.** A fresh
agent with no memory of any conversation should be able to read this table
plus `git log --oneline main..HEAD` and know exactly where to resume.

| # | Item | Landed |
| --- | --- | --- |
| 1 | 6.1 `run_command` classification pinned | yes |
| 2 | 4.1 Per-test timing in CI, both legs | yes |
| 3 | 1.1 `LlmOutputConstraint` on `LlmChatOptions` | yes |
| 4 | 1.2 llama.cpp sends the constraint | yes |
| 5 | 1.3 OpenAI and Ollama, with honest refusal | yes |
| 6 | 1.4 `SupportsOutputConstraints` capability flag | yes |
| 7 | 1.5 Memory auto-summary asks for a schema | yes |
| 8 | 1.6 Trace records whether shape was enforced | yes |
| 9 | 5.1 Planner protocol schema | no |
| 10 | 5.2 Planner call sends the constraint | no |
| 11 | 5.5 Gate provably unchanged | no |
| 12 | 5.3 Fallbacks confirmed intact | no |
| 13 | 5.4 Parse-failure message stops blaming the model | no |
| 14 | 5.6 Parse failures countable | no |
| 15 | 2.1 `ChatServerTimings` carries draft counters | no |
| 16 | 2.2 Speed Check runs repeated iterations | no |
| 17 | 2.3 Observed spread reported | no |
| 18 | 2.4 Acceptance shown beside speed | no |
| 19 | 2.5 Doctor check for drafting that never engages | no |
| 20 | 3.1 Activity target resolver | no |
| 21 | 3.2 Activity view navigates | no |
| 22 | 3.3 Four missing event sources | no |
| 23 | 3.4 Adjacent rows grouped | no |
| 24 | 4.2 Fix what 4.1 named | no |
| 25 | 4.3 Parallel collection (only if justified) | no |
| 26 | 4.4 CI note recorded | no |
| 27 | 6.2 Docs guard over enumerable facts | no |
| 28 | 6.3 NuGet caching | yes |
| 29 | 6.4 Speed Check forward pointer | no |
| 30 | 6.5 Docs, changelog, deferred ledger | no |
| 31 | Close-out: archive pack, PR | no |

## Sequencing (strict)

1. **6.1 first.** Resolved during planning: the documentation is correct and
   `run_command` routes through `EvaluateCommand`, so this is now a comment,
   a table fix and a regression test rather than an investigation. It goes
   first because 6.2's guard asserts against the table it corrects.

2. **4.1 second, and then leave it alone.** It must be pushed early because
   its output is a CI artifact from a real run, and every later decision in
   doc 04 depends on reading it. Push it, then work on doc 01 while it runs.
   Do not write 4.2 from a hypothesis while the measurement is pending.

3. **Doc 01, in order 1.1, 1.2, 1.3, 1.4, 1.5, 1.6.** 1.1 is the contract
   and everything after it in the round depends on it. **1.2 is the item
   that can invalidate two documents**: if the per-request constraint field
   does not reach the sampler on `b10195`, stop and reassess before building
   1.3 through 1.6, and before starting doc 05 at all. The proof is the poem
   test in 1.2, not a successful HTTP 200. 1.5 is the payoff and should not
   be skipped to save time; without a real consumer this round ships
   plumbing.

4. **Doc 05, in order 5.1, 5.2, 5.5, 5.3, 5.4, 5.6.** Note that 5.5 runs
   third, immediately after the constraint is wired: the test proving the
   safety gate is unchanged lands with the change it guards, not at the end
   of the document where it can be descoped. 5.3 is a verification pass
   rather than new code, and it comes before the message rewrite in 5.4
   because 5.4's wording depends on which fallbacks still fire.

5. **Doc 02, in order.** 2.1 is the data and everything else reads it. 2.1
   has the same verification risk as 1.2 and the same instruction: if the
   installed build reports no draft counters, record that and continue with
   2.2 and 2.3, which stand on their own.

6. **Doc 03, in order 3.1, 3.2, 3.3, 3.4.** 3.1 is pure and testable
   without UI. 3.3 is independent of 3.1 and 3.2 and can be interleaved or
   done by a different session.

7. **4.2, then 4.3 only if 4.1 justified it.** By this point the CI artifact
   from step 2 has been read.

8. **Doc 06's remainder, then close-out.**

## Test budget

Roughly 70 to 90 new tests, against 1169 at the start of the round:

- Doc 01: ~25. Constraint construction and refusal, per-provider
  serialization, capability flag, schema-matches-record, constrained and
  unconstrained paths through memory extraction, fallback behaviour
  unchanged.
- Doc 05: ~15. Schema agrees with `AgentPlannerResponse` and with the C#
  enums, constrained response deserializes without repair, the three
  parse-failure message branches, and 5.5's gate regression across at least
  three high-risk tools.
- Doc 02: ~15. Timings parsing with and without draft fields, iteration and
  spread arithmetic, acceptance display including the missing-versus-zero
  distinction, the Doctor check's three states.
- Doc 03: ~15. Resolver mappings, totality over known operations, four event
  sources, recorder failure isolation.
- Doc 04: ~0 to 5. Mostly workflow changes. 4.3 adds none directly; it moves
  existing tests.
- Doc 06: ~5. The guard assertions, plus 6.1's regression test.

If the count comes in far under this, the likely cause is 1.5, 3.3 or 5.5
being implemented without their failure paths tested.

## Descope order (if the round overruns)

Cut from the bottom:

1. **3.4** (adjacent-row grouping). Named in its own item as the first
   thing to cut.
2. **4.3** (parallel collection). Expected to be descoped by evidence
   anyway.
3. **5.6** (parse failures countable). The improvement is real without it;
   this only makes it measurable.
4. **1.6** (constraint in the trace). Useful, not load-bearing.
5. **5.4** (parse-failure message). The old message stays accurate for
   unconstrained providers, so leaving it is wrong only in the constrained
   case rather than wrong outright.
6. **2.5** (Doctor check). The acceptance number in 2.4 already answers the
   question; the Doctor check just makes it findable without running a
   benchmark.
7. **3.3** (four event sources). Genuinely valuable and genuinely
   independent; it survives to a later round intact.

**Do not descope 6.1.** It is small, it is already answered, and it pins a
classification in the document people read to understand the safety gate.

**Do not descope 4.1.** It is a workflow change and a table in a PR
description. If the rest of doc 04 is cut, 4.1 still leaves the next round
knowing where the 419 seconds goes.

**Do not descope 1.5.** Doc 01 without a consumer is a feature nobody uses
and a set of types nobody has exercised against a real model.

**Do not descope 2.1.** It is the answer to the question r27's own commit
message asked.

**Do not descope 5.5 if 5.2 lands.** A constrained planner without the
regression test proving classification is unchanged is the one combination
in this round that could quietly weaken a safety property. If the round is
too tight for both, cut 5.2 and keep the schema (5.1) as unused groundwork;
do not cut the test and keep the feature.

## Housekeeping

- **`docs/review/deferred.md`.** No row moves to Closed on the strength of
  this round's plan. One candidate: if doc 04's 4.1 measurement names either
  of the two remaining clock-dependent tests
  (`MainWindowViewModelStartupTests`'s 150ms debounce assertion,
  `McpTests`'s 5000ms failure assertion) as a real cost, and 4.2 fixes them
  with r26 5.2's drain-the-posted-work template, then that row closes with
  the fix as evidence. If 4.1 does not name them, the row keeps its current
  status and its current wording. Do not close it on the grounds that this
  round looked at CI.
- No new Open row is added for the agent tool-call path. Doc 01 originally
  deferred it; doc 05 does it.
- Archive the pack to `docs/review/archived/r28/` at close-out.

## Practical warnings

- **A constraint field the server ignores looks exactly like success.** This
  is the round's main way to ship something that does nothing, and it now
  threatens two documents rather than one. r27 hit the same class of problem
  with `--draft-max`, which had been removed upstream and printed a notice
  while changing nothing. 1.2 and 2.1 are both written against surfaces this
  pack verified only partially, and both carry an explicit instruction to
  check against the running server. Honour it before doc 05 is started.

- **Doc 05 is the one place in this round where a mistake is a safety
  mistake.** Everything else fails visibly: a link goes nowhere, a number is
  missing, CI is slow. A constrained planner implemented carelessly could
  let a schema-valid response carry its own approval decision. It cannot
  today, because classification happens in code from the tool name, and 5.5
  exists to keep it that way. Read the safety argument at the top of doc 05
  before writing any of it.

- **Do not run the app casually to test doc 01 or doc 05.** `dotnet run` on
  the Desktop project reads and writes the same
  `%LOCALAPPDATA%\Hermaeus\settings.json` as the owner's installed app.
  Never force-kill it. A live instance also holds DLLs and will fail the
  next `dotnet build` with a file lock.

- **`MemoryExtractionService`'s three fallbacks and `AgentService`'s repair
  path are load-bearing until the day they are not.** 1.5 and 5.3 remove
  none of them. A round that deletes `TryRepairActionType` because the enum
  is constrained now breaks every unconstrained provider silently, and the
  failure mode is an agent run that stalls with an unhelpful message.

- **Doc 03 touches `MainWindowViewModel`**, which is a hot spot and moves
  often. 3.2's whole instruction is to reuse the existing navigator rather
  than add a second one; if that turns out to require a refactor larger than
  the feature, say so and descope 3.2 rather than forking navigation.

- **The Speed Check restarts the managed server.** 2.2's iteration increase
  multiplies the time a run takes. Confirm the new duration is tolerable on
  the owner's hardware before settling on the number, and put the measured
  duration in the PR.

## Explicit rejections (do not do these)

Considered and declined, with reasons. Engage with these rather than
re-proposing them.

- **A self-optimizing meta-agent that proposes changes to its own
  architecture.** The app ships as a compiled binary, so a self-proposed
  source change is unbuildable on a user's machine. A micro-benchmark can
  validate a scalar knob and cannot validate an architectural change, so the
  audit log would record decisions nobody can evaluate before approving.

- **Automatic tuning, a settings sweep, or a "find the best configuration"
  button.** Rejected in r25, r26 and r27. r27's stated reason (a round that
  measures a thing should not also grow the thing being measured) is
  weakened now that the measurement exists, so the reason that carries it is
  r23 2.3: picking a winner is rating, and the app does not rate itself. Doc
  02 makes the existing measurement interpretable instead.

- **A grade, score, confidence interval, or recommendation on a speed check,
  an acceptance rate, or an activity outcome.** Settled by r23 2.3 and
  restated every round since. 2.3's observed spread is a description of what
  was seen, not a statistical claim, and its copy must not imply one.

- **A model-written summary that links activity, benchmarks and traces into
  a likely cause.** Directly contrary to `ActivityModels.cs:14-18`. The data
  does not support the inference: `docs/benchmarks.md` states that latency
  and throughput vary with system load, warmup, cache state, GPU clocks and
  thermals, so temporal correlation between a benchmark delta and a nearby
  event is noise more often than signal. A confident wrong "why" costs more
  than silence because it aims the next hour of debugging at a fabrication.

- **An LLM-driven or adaptive risk classifier for the agent.** CLAUDE.md:
  risk classification is deterministic, never bypass it. A classifier that
  reads text to decide risk is injectable through any content the agent
  reads, which turns the strongest safety property in the app into the
  softest. The advisory variant is also rejected: two risk signals where one
  is occasionally wrong costs the deterministic one its authority. Note that
  doc 05 is not this: constraining a response's *shape* leaves every trust
  decision exactly where it is.

- **Letting a constrained planner response carry its own approval or risk
  decision.** The whole point of 5.5. `requires_approval` and `risk_level`
  remain fields the model fills in and the code overrides.

- **Changing the four action kinds, the planner prompt's wording, or the
  protocol's shape.** 5.1 describes the existing shape in a schema. A round
  that constrains a protocol should not also redesign it.

- **Declaring MCP tools natively.** Their tool list arrives at runtime, and
  r26 already rejected widening what a server claims about itself into a
  safety decision. Constraining the text protocol covers them.

- **A retry loop that re-prompts the planner on parse failure.** The error
  budget and `AskUser` fallback are deliberate. Automatic retries change how
  much work runs without approval.

- **Removing any of `MemoryExtractionService`'s fallbacks or
  `TryRepairActionType`.** See the practical warning above.

- **Generating documentation from code.** Rejected in r25 and unchanged. 6.2
  asserts that a name appears in a file; it does not write prose.

- **Probing for output-constraint support.** 1.4 reports what a provider
  declares. r26 rejected making the capabilities endpoint probe for the same
  reason: an endpoint that loads a model to find out whether a model loads
  is a denial-of-service handle wearing a health check's name.

- **A user-facing grammar or schema editor.** `LlmOutputConstraint.Grammar`
  exists because llama.cpp's surface has it. No UI points at it this round,
  and a round that adds constrained decoding should not also add a way to
  author constraints.

- **Flipping `DisableTestParallelization` globally.** 4.3 is opt-in and the
  default stays serial. The rule in CLAUDE.md was right about why; if 4.1
  produces evidence, the rule gets refined, not deleted.

- **Sharding the test suite across runners.** Halves wall clock, doubles
  runner minutes, teaches nothing about why the tests are slow. Not before
  4.1, and not instead of it.

- **Dropping or reducing the Windows CI leg.** The app ships on Windows and
  is developed on Windows.

- **A CI wall-clock budget or regression gate.** r27 rejected one for
  startup. A timing assertion on a shared runner is a flaky test with a
  stopwatch.

- **Editing `docs/benchmarks.md`'s first recorded Speed Check result.** It
  is an honest record of what was known when it was taken. 6.4 adds a
  pointer after it.

- **New NuGet packages.** Standing rule. Nothing here needs one: doc 01's
  and doc 05's schemas are hand-written `const string`s checked against
  their records by tests (following `BuildFixedToolDefinitions`, which
  already does exactly this at `AgentService.cs:92`), doc 04's timings come
  from VSTest's own TRX logger, and doc 02 reads two more fields out of a
  JSON object the app already parses.
