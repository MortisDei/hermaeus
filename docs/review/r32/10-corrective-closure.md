# R32 corrective closure

This document records the current corrective-pass disposition. It is an
evidence ledger, not a promise that desktop interaction or a particular local
model runtime was live-tested by the source audit.

## Release identity

R32 targets the Hermaeus beta release `0.40.0-beta`. `Directory.Build.props`
remains the version source of truth, with `0.40.0` as the prefix, `beta` as the
suffix, and matching `0.40.0.0` assembly and file versions.

The final R32 dependency adjustment moves the four Avalonia framework packages
to the coherent `12.1.2` pin. `Avalonia.AvaloniaEdit` remains at `12.0.0` on
its separate package line, and `Tmds.DBus.Protocol` remains unchanged. No
TableView, WinUI embedding, new Wayland feature, or unrelated dependency
upgrade is included.

## Current dispositions

- Data Root migration now carries the planned inventory, restart inventory,
  per-file copy and verification counts, exclusions, retained files, failures,
  and the retryable receipt. The existing owner evidence for the 62-to-58
  migration remains the live visual baseline. The new restart-now/restart-later
  dialog still needs owner validation.
- System Overview, RAG information architecture, and the Agent workspace
  four-tab structure retain their existing owner evidence. The duplicate Agent
  navigation strip is removed in source and covered by regression tests.
- Settings voice pickers now use an editable, unfiltered ComboBox per row.
  Provider catalogue items, the default sentinel, and manually entered ids use the same bound text surface, so the prior autocomplete popup/filter state is no longer part of the lifecycle. Source-level coverage covers the control contract and per-row catalogue snapshots. Repeated live open, close, selection, reopen, and cross-channel checks remain an owner gate.
- Scenario Evals now exposes a bounded running card with current/total
  completion, scenario id/title, current step/status, observed pass/fail counts, and cancellation. It retains actual partial and failed results and does not invent a percentage or timing subsystem.
- Scenario Eval results now survive restart for both suite and individual runs.
  Each persisted result carries the model id and content hash, scenario
  definition hash, evaluator contract version, runtime identity, observation
  timestamp, pass/fail outcome, counts, and check details. Matching evidence is
  shown as Pass or Fail; missing applicability evidence is Unknown; and a model,
  scenario, or evaluator identity mismatch is Stale. Runtime identity is
  retained as provenance and does not invalidate a result by itself.
- Resource receipts now prefer trusted consumer/allocation      observations, then observed component bytes, then reserved or predicted bytes. Missing values
  are labelled Not observed, Not resident, or planned; component gaps say
  attribution incomplete when a parent or component observation exists.
  Device totals remain explicitly whole-device values and are never assigned
  to a consumer.
- Doctor actions are typed and contextual. Ready checks expose no action;
  warning and error checks expose the relevant navigation, repair, or external
  target. The rendered layout and tooltip presentation remain an owner gate.
- LFM2.5 remains unresolved as a runtime observation. The source path keeps
  reasoning separate from visible answer content, replays only the active
  conversation path, and explicitly reports a reasoning-only completion. No
  model-specific prompt or provider workaround was added without a reproducible
  runtime failure.
- Resource component identity is allocation-scoped, release receipts preserve
  plan and consumer identity, and caller cancellation is preserved through the
  three streaming providers and Doctor probes. These are covered by source
  audits and focused regression tests.
- The normal voice queue is finite and its priority policy is bounded. Audio
  feedback and runtime-log queues retain their existing bounded policies.

## R32 performance tail

The owner runtime log exposed two distinct issues. First, the former slow-send
warning added component spans even though the preparation branches already ran
concurrently. Its 11.5-second value therefore was not a wall-clock measurement.
Chat now records one preparation wall-clock span, keeps the component spans for
diagnosis, and adds provider first-content time only after preparation. Second,
the four recall sources, the separate memory path, and applicable RAG paths
could repeat work for the same question. The embedding client now coalesces exact
endpoint/model/query/priority matches into one cancellable request, gives
interactive requests priority over queued optional backfill, and `MemoryStore`
coalesces the complete identical-query FTS, dense-score, hydration, and one-hop
relationship search. Each caller receives its own result list. Distinct queries
remain independent, and caller cancellation detaches only that caller.

The observed background embedding backfill denial is not itself the cause of the
foreground timeout. Backfill is optional background work and remains subject to
whole-workload admission. Foreground query embedding uses the already-resident
embedding server without re-admitting the runtime, and a priority gate prevents
queued backfill from taking the next physical embedding slot. Embedding logs
now separate logical coalescer wait, endpoint gate wait, physical request and
response time, optional server timing headers, response parsing, payload size,
and total logical time; persistence/cache work is explicitly not applicable to
the embedding client. Lexical fallback remains explicit when a query embedding
times out. Source, memory-search, and embedding timings are recorded without
query text.

Documents no longer load and tokenize every chunk for Recall. Its bounded scan
index and FTS candidate ids are combined before content hydration, and its stage
logs report semantic and lexical candidates, hydrated rows, returned hits, scan,
FTS, hydration, scoring, and total time. RRF remains ordering-only; the source
returns the strongest underlying semantic or lexical evidence so RecallService
does not discard useful documents at its calibrated relevance floor. Memory,
conversation, and task searches also report candidate, relevance-survivor,
relationship/dense-only, and returned counts. Chat logs continue to report
selected context items separately from actually injected source references.

The supplied owner log contains the pre-repair observations, including recall
around 2.2 to 3.0 seconds, recall injection around 3.0 to 4.3 seconds, and
provider first content around 5.0 to 6.1 seconds. The embedding endpoint was not
listening during this source audit, so no post-repair hardware latency median is
claimed here. Warm and cold owner dogfood timings remain a live verification
gate.

## Capability-cache disposition

The access-denied warning at `C:\Hermaeus\capability-cache.json` was traced to
the cache reader's live file handle remaining in scope while the atomic replace
ran. The read is now isolated in a helper whose handle is disposed before the
replacement. The existing warning and failure-state reporting remain in place;
permissions and owner data were not changed during this audit.

## Repository-agent skills

The five repository skills were audited because their `.claude/skills` copies had drifted from current AGENTS.md rules, source behaviour, and authoritative docs. All five were retained and migrated to `.agents/skills/` as the one canonical source: `add-a-feature`, `build-and-verify`, `review-round`,
`security-posture`, and `storage-and-data-root`. `.claude` contained no other configuration, and no compatibility mechanism was required by the repository agent tooling available for this pass, so duplicate independent skill copies were not kept.

The audit corrected the feature workflow's current Composition root and
settings/test conventions; the verification skill's approved-host handling, 60% coverage floor, final coverage ordering, process audit, and live GUI gate;
the review skill's owner-controlled PR/version/tag/release boundaries and evidence vocabulary; the security skill's declared-command versus blocked `run_command` semantics and current process, secret, download, path, and approval controls; and the storage skill's requested/configured/pending/effective/observed Data Root lifecycle, restart-time inventory, SHA256 verification, rollback, receipts, backup exclusions, and additive SQLite rules. AGENTS.md now indexes the five skills and states that it is authoritative.
Current-facing references were searched and reconciled while archived history was left historical.

## Final verification evidence

- The Windows CI failure was reproduced in the Release test run. It was a
  test-owned asynchronous observation race: `Progress<string>` captured the
  xUnit context, while the fake runner signalled readiness before posted
  progress callbacks had run. The regression now uses the existing inline
  test synchronization context while starting the command, so it observes
  the actual callbacks without changing production scheduling, timeouts,
  assertions, or execution ordering.
- The formerly flaky test passed 20 separate Release repetitions after the
  repair, and the focused current-version run passed.
- Focused affected-area tests passed: 73 passed, 0 failed, 0 skipped.
- Full sequential suite passed: 2,676 passed, 0 failed, 0 skipped, using
  `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-restore` with results outside the repository.
- `dotnet build Hermaeus.sln --no-restore -c Debug` passed with 0 warnings and
  0 errors. The Release build with `-c Release --no-restore` passed with 0
  warnings and 0 errors.
- `git diff --check` was clean before coverage.
- The one final coverage run passed with exit code 0 and accepted the
  repository's 60% floor. Its report stayed outside the repository and was cleaned by the script, so no unsupported point percentage is recorded here.
- The process audit found the expected reusable C# Dev Kit MSBuild and
  VSTest hosts plus the Avalonia BuildServices collector. No detached test run was identified and no process was terminated.

## Owner live gates

Owner validation has passed for the Doctor action presentation and Agent tab layout. The Data Storage migration was exercised successfully against the owner's real data. The restart-now/restart-later lifecycle and migration accounting are accepted for R32 based on that live migration evidence plus regression coverage; a second destructive migration is not required for closure.

## Verification boundary

Build, test, coverage, commit, and push results belong to the final
implementation handoff and must be reported with the exact command and result.
Merge, version tags, and releases remain owner actions.
