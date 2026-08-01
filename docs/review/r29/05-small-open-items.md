# 05. Small open items

All references verified against `f03e7c1`.

## 5.1 UI copy that promises a removed feature

`ServicesVoiceSectionView.axaml:19`:

> "Local/remote text-to-speech backends for chat replies, agent narration,
> and system notifications. Per-channel routing and **named voice profiles**
> are in Settings > Voice."

Per-channel routing is in Settings > Voice. Named voice profiles are not.
`TtsSettings.Profiles` (`TtsSettings.cs:96`) and `VoiceProfile` (`:107`)
still exist on the model, but `TtsSettingsViewModel.cs:237` describes the
list as "no-longer-editable" and reads it exactly once, at `:245`, to
resolve a legacy `ProfileName` during migration. There is no UI anywhere
that creates, edits or deletes a profile.

This is what CLAUDE.md forbids in docs, sitting in the product. It is also
the likely reason the owner went looking in Settings > Voice for controls
that were never built, which is how 1.2's report arrived.

**Fix the copy, do not build the feature.** Named voice profiles are not
requested and per-channel voice selection (1.2) covers the use case. Drop
the clause; the remaining sentence is accurate.

Then check the neighbours: `docs/voice.md` and `docs/features.md` were
searched and make no such claim, so no doc edit is owed. One code comment
does: `App.axaml.cs:226` describes the shared singleton as covering "Voice
orchestration/channels/profiles". Drop the last word. Do not remove
`TtsSettings.Profiles` from the model in this round; the migration at
`:245` still reads it, and dropping a settings field is a separate change
with its own compatibility question.

## 5.2 Three different coverage floors, none of them near the truth

| Source | Stated floor |
| --- | --- |
| `CLAUDE.md:35` / `AGENTS.md:35` | 45% |
| `scripts/coverage.ps1:2` | 47 |
| `scripts/coverage.sh:4` | 47 |
| Measured actual (4.6) | **61.6%** |

The documentation and the scripts have disagreed for some time, and both
are far enough below the real number that the ratchet cannot fail on
anything short of deleting a quarter of the suite. A ratchet that cannot
catch a regression is decoration.

Set all four to the same number. 4.6 specifies 60: just under the current
value, so a genuine regression trips it and ordinary variance does not.
Update `CLAUDE.md` and `AGENTS.md` together; they are the same content and
`bd2df84` established that the tracked file is `AGENTS.md`.

## 5.3 Deferred ledger

At close-out, `docs/review/deferred.md`:

- **Close** "Deterministic timing for clock-dependent tests" (r25 5.4,
  partly closed r26 5.2). 4.5 makes the remaining component timeouts
  injectable. Cite the specific tests and the injected-timeout mechanism as
  evidence, in the style of the existing Closed rows. If 4.5's
  `VoiceOrchestratorTests` half is descoped, the row does **not** close;
  update its "why it is still open" text to name exactly what remains
  instead.
- **Add** an open row for the coverage gaps table from 4.6, so the next
  round starts from measurements rather than rediscovering them.
- **Amend** "Agent run/step endpoints on the local API" with a sentence
  distinguishing it from doc 03's steering, per doc 03's closing note. Its
  status does not change.
- **Leave unchanged** "Loading the Whisper ONNX graphs under test". Nothing
  in this round unblocks a 291 MB approval-gated download.
- Nothing else moves. No row closes on the strength of this round's plan.

## 5.4 Documentation

- `docs/features.md`: the Services page saves (1.1), the voice channel
  picker (1.2), the model card grid and source badge (doc 02), agent
  steering (doc 03), and the test-suite section from 4.7 if it lives here
  rather than in its own file.
- `docs/agent.md`: steering, per doc 03.
- `docs/voice.md`: the channel picker's behaviour when the provider has not
  listed its voices.
- `README.md`: only if the feature narrative changes. The version guard
  (`DocsCoverageGuardTests.The_readme_states_the_version_this_build_ships`)
  already covers the version string, so the bump is enforced rather than
  remembered.
- `CHANGELOG.md`: every user-visible item above. The five fixes in doc 01
  are the entries the owner will actually read; write them as symptoms
  fixed, not as components changed.
- Run r25's doc-drift guard (`DocsCoverageGuardTests`) before committing.
