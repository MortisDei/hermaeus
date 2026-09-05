# R32 corrective closure

This document records the current corrective-pass disposition. It is an
evidence ledger, not a promise that desktop interaction or a particular local
model runtime was live-tested by the source audit.

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

## Repository-agent skills

The five repository skills were audited because their `.claude/skills` copies had drifted from current AGENTS.md rules, source behaviour, and authoritative docs. All five were retained and migrated to `.agents/skills/` as the one canonical source: `add-a-feature`, `build-and-verify`, `review-round`,
`security-posture`, and `storage-and-data-root`. `.claude` contained no other configuration, and no compatibility mechanism was required by the repository agent tooling available for this pass, so duplicate independent skill copies were not kept.

The audit corrected the feature workflow's current Composition root and
settings/test conventions; the verification skill's approved-host handling, 60% coverage floor, final coverage ordering, process audit, and live GUI gate;
the review skill's owner-controlled PR/version/tag/release boundaries and evidence vocabulary; the security skill's declared-command versus blocked `run_command` semantics and current process, secret, download, path, and approval controls; and the storage skill's requested/configured/pending/effective/observed Data Root lifecycle, restart-time inventory, SHA256 verification, rollback, receipts, backup exclusions, and additive SQLite rules. AGENTS.md now indexes the five skills and states that it is authoritative.
Current-facing references were searched and reconciled while archived history was left historical.

## Final verification evidence

- Focused affected-area tests passed: 64 passed, 0 failed, 0 skipped.
- Full sequential suite passed: 2,668 passed, 0 failed, 0 skipped, using
  `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-restore` with results outside the repository.
- `dotnet build Hermaeus.sln --no-restore` passed with 0 warnings and 0
  errors. The Release build with `-c Release --no-restore` passed with 0
  warnings and 0 errors.
- The final staged `git diff --cached --check` was clean before coverage.
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
