# Review round 28: Small models, kept honest

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 07 is the roadmap and sequencing contract.

## Why this round exists

r27 made the app fast and gave it a way to prove it. The proof came back
null: the first recorded Speed Check (`docs/benchmarks.md`, "First recorded
result") measured `draft-mtp` at 70.2 median tok/s against `ngram-mod`'s
69.7, one cold iteration per case, and said plainly that this cannot be
distinguished from noise. The commit that recorded it (`6ba9522`) named its
own unfinished business: "a reproduction should confirm the draft model
actually loaded", because uniform tok/s across four deliberately different
content shapes is equally consistent with decode being bandwidth-bound and
with drafting never engaging at all.

That is the shape of this round. r27 built instruments. r28 is about the
instruments telling you enough to act on, and about the app asking less of
the model in the first place.

**The app asks a 4B model to produce valid JSON and hopes.**
`MemoryExtractionService` requests structured extraction, deserializes it
(`MemoryExtractionService.cs:94`), and when that fails falls back to
`JsonDocument.Parse` over a salvaged candidate (`:171`), and when that fails
falls back to regex over `[MEMORY: ...]` markers (`:14`). Three parsers
stacked in a row is not a design; it is the fossil record of a model that
keeps getting it wrong. Every one of those layers exists because output
shape was a request rather than a constraint. The installed `llama-server`
(b10195) has supported `--grammar` and `--json-schema` the whole time, and
nothing in this repository sends either: a search for `grammar`,
`json_schema` or `response_format` across `src/` returns two hits, both in
the *voice* path asking for WAV audio. Constrained decoding is the single
largest available improvement to small-model reliability in this app, and
it is currently at zero.

**Nothing reports whether drafting engaged.** `SpeedCheckComparison`
(`SpeedCheck.cs:78-98`) reports tok/s, prompt tok/s, first-token latency and
a configuration delta. llama-server reports draft acceptance in its own
timings and the app does not read it, so the one number that would have made
r27's null result interpretable is the one number missing. A speed check
that cannot tell "no benefit" from "never ran" is measuring the wrong thing.

**Activity records what happened and never says where.** `ActivityEvent`
(`ActivityModels.cs:20-28`) is deliberately "one deterministic fact ...
never a model-written summary", which is right and stays. But a failed RAG
ingest row does not link to its trace, a server restart does not link to the
benchmark run that followed it, and four event sources named as missing in
r24 (`features.md:909-911`: model downloads, backup/restore, memory
auto-archive, the voice backend) are still not wired in.

**Windows CI takes five times as long as Linux and it is entirely the test
step.** Measured on run `30612541757`: restore 36s vs 15s, build 65s vs
68s (Windows is *faster* at build), test **491s vs 72s**. The build being
identical rules out the usual suspects. 123 of 188 test files touch
`TempDir`, `SqliteConnection` or `NewSettings`, and
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`
(`XunitHarnessTests.cs:4`) means all of them run one after another. Nobody
knows which tests spend the 491 seconds, which is why this round measures
before it changes anything.

**And the agent tells its own user that local-first works worse.** When the
planner's JSON protocol fails to parse, `DescribeParseFailure`
(`AgentService.cs:1701-1703`) explains that "the model replied in prose
instead of the JSON action format it was asked for. Smaller local models do
this; a model with stronger instruction following, or one that supports
native tool calling, avoids it." That message is accurate today and it is an
admission: the agent's action protocol is a schema written in prose
(`AgentService.cs:62-81`), asked for politely on every step, defended by an
extractor, a targeted repair for the one enum small models always get wrong
(`:1531-1537`), and an error budget. Native tool calling escapes it only
partly, because MCP tools are never declared natively (`:85-89`) and reach
the model through that same text protocol regardless of provider.

So: doc 01 makes malformed output unrepresentable rather than recoverable.
Doc 02 makes the speed check able to distinguish a null result from a
no-op. Doc 03 links Activity rows to the things they describe and finishes
r24's coverage. Doc 04 finds out where Windows CI actually goes. Doc 05
points doc 01's contract at the agent, which is where it is worth the most.
Doc 06 is small items, including a documentation guard extended to
enumerable facts and a discrepancy between the agent risk table and the
agent risk code.

## Documents

| Doc | Theme |
| --- | --- |
| `01-output-that-cannot-be-malformed.md` | A structured-output contract on `LlmChatOptions`, per-provider constrained decoding, honest degradation when a provider cannot constrain, and memory auto-summary as the first consumer |
| `02-drafting-you-can-verify.md` | Draft acceptance statistics read from the server, repeated iterations with observed spread, and a Doctor check for drafting configured but never engaging |
| `03-activity-that-links.md` | Deterministic links from an Activity row to its artifact, time-window grouping, and the four event sources r24 left unwired |
| `04-the-windows-test-gap.md` | Per-test timing on both matrix legs, fix what the measurement names, and an opt-in parallel collection only if the evidence supports one |
| `05-an-agent-a-small-model-can-drive.md` | A real JSON schema for the planner protocol, the constraint wired into the planner call, every fallback kept, and a test proving the safety gate is unchanged |
| `06-small-open-items.md` | The `run_command` risk-table discrepancy, docs guard over enumerable facts, NuGet caching, a forward pointer on the first Speed Check result, docs |
| `07-roadmap.md` | Ships as 0.35.0-alpha; sequencing, test budget, descope order, housekeeping, explicit rejections |

## Standing rules for the implementing agent

- **Verify before implementing.** Every file:line reference in this pack was
  exact against tree `c4d9d9b` (v0.34.0-alpha, the r27 merge). Re-verify
  before editing. `ChatViewModel.cs` (2644 lines), `AgentViewModel.cs`
  (2494), `ServicesViewModel.cs` (1858) and `RagViewModel.cs` (1187) all
  move often and CLAUDE.md names them as hot spots.

- **Doc 01's per-request field names are NOT verified and you must verify
  them.** What is verified is that `C:\AI\llama-server\b10195\llama-server.EXE
  --help` lists `--grammar`, `--grammar-file`, `-j/--json-schema` and
  `-jf/--json-schema-file`. Those are process flags. This round sends
  constraints **per request**, which is a different surface, and the field
  names on `/completion` and `/v1/chat/completions` were read from
  documentation rather than from the running server. Do what r27 did with
  `--spec-type`: hit the real endpoint and read what it accepts before
  writing the serializer. A request field that the server silently ignores
  produces unconstrained output that looks exactly like success.

- **Constrained decoding must degrade honestly, never silently.** A provider
  that cannot constrain must say so at the call site and the existing
  parse-and-fallback path must remain. 1.5 removes no fallback. The point of
  this round is that the fallback stops being the common path, not that it
  stops existing.

- **Doc 02 reports, it does not rate.** Settled by r23 2.3, restated in r26
  and r27: the app reports what happened, it does not grade itself. Draft
  acceptance is a number the server produces and the app relays. Repeated
  iterations report the observed spread. Neither may become a confidence
  interval, a significance claim, a grade, or a recommendation to change a
  setting.

- **Doc 03 does not add model-written synthesis.** `ActivityModels.cs:14-18`
  states the rule in the type's own summary: never a model-written summary.
  Links are deterministic. Time grouping is arithmetic. If an item in doc 03
  seems to want a sentence explaining *why* two rows are related, that item
  is out of scope, not under-specified.

- **Doc 04 measures before it changes.** 4.1 lands and its output is read
  before 4.2 is written. A round that guesses at the cause of a 491-second
  test step and optimises the guess is how a CI job gets slower and less
  trustworthy at the same time. If 4.1 shows the time is concentrated in a
  handful of tests, 4.3's parallel collection is descoped, not attempted.

- **Test parallelization is opt-in, never a global flip.** CLAUDE.md's rule
  stands unless 4.1 produces evidence against it, and even then the default
  stays serial: a test joins a parallel collection by being explicitly
  marked, so a new test is serial unless someone deliberately says
  otherwise. Do not delete `XunitHarnessTests.cs:4`.

- **Doc 05 constrains a shape, never a trust decision.** The agent's safety
  gate classifies on tool name and mutation, and the dispatch path already
  overrides whatever the model claims about `requires_approval`
  (`AgentService.cs:363-370`, `AgentSafetyGate.cs:41-45`). A constrained
  planner response is easier to parse and exactly as untrusted. 5.5 is the
  regression test that pins this and is not optional; if implementing doc 05
  ever seems to require touching `AgentSafetyGate`, stop, because it does
  not.

- No em dashes anywhere. Zero warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.

- **No new NuGet packages.** Nothing in this round needs one. Doc 01's
  schema emission is `System.Text.Json`, already used throughout
  `MemoryExtractionService`. Doc 04's timing output is VSTest's own TRX
  logger.

- Schema changes are additive and go through `SqliteMigrationRunner`.

- Secrets and process rules are unchanged: no shell-string launches,
  `ProcessStartInfo.ArgumentList` only, redact before persisting. Doc 02
  reads server-reported timings; it does not change how a server launches.

- Update `README.md`, `docs/features.md`, `docs/benchmarks.md`,
  `docs/agent.md` and `CHANGELOG.md`. Run r25's doc-drift guard. Do not
  document planned behaviour as existing behaviour.

- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
  Icon-only controls need tooltips; the guard test scans axaml and fails
  without one.

- `docs/review/deferred.md` is updated at close-out. No row moves to Closed
  on the strength of this round's plan alone; see 07's housekeeping for the
  one candidate and its condition.

- This round lands via pull request per `docs/pull-requests.md`: branch
  `r28/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.

## If this session was interrupted

- The pack is the contract. Nothing in it depends on remembering a
  conversation.
- Doc 07 carries a sequencing table with an explicit "landed / not landed"
  column. Update it as work lands, in the same commit as the work. That
  table, plus `git log --oneline main..HEAD`, is the whole recovery
  procedure.
- Commit after each document. An interrupted
  session should only cost one document.
