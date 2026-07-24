# 01. Moss presence

Goal: Moss appears everywhere `docs/mascot.md` says he belongs (empty
states, onboarding, tips) and nowhere he does not (no popups, no animation,
no interruption). One shared control, five empty states, one wizard
greeting, and copy that follows the voice rules.

The mascot art itself does not change this round. `MossIcon.axaml` is the
cute-era silhouette and `docs/mascot.md` records that the woodland-goblin
redesign needs fresh illustration work; that stays follow-up. This round
changes where the existing icon appears, not what it looks like.

## 1.1 `Controls/MossEmptyState.axaml`: one control, not five copy-pastes

A small `UserControl` in `src/Hermaeus.Desktop/Controls/` following the
`MossIcon` pattern (plain Avalonia, code-behind only for styled properties,
no business logic):

- Styled properties: `Title` (string), `Hint` (string, optional),
  `IconSize` (double, default 40), `MossTip` (string, optional tooltip on
  the icon).
- Layout: vertically stacked, horizontally centered: `MossIcon` at
  `IconSize`, then Title, then Hint. Title and Hint use the shared text
  classes from doc 02.2 (`hint` for Title at a larger size, `faint` is
  the floor for Hint); no ad-hoc `Opacity` values.
- The whole control is display-only. Callers that need action buttons
  (the chat no-model state) place them below the control in their own
  view; do not grow a content slot until a third caller needs one.

## 1.2 Empty states that gain Moss

Replace the bare "nothing here" text in each of these with
`MossEmptyState`. Copy must follow `docs/mascot.md` "Voice in UI copy":
state what is true, then what to do next; no exclamation marks; humour only
where nothing is wrong.

| View | Today | Direction for new copy |
| --- | --- | --- |
| ChatView.axaml:474-484 ("Start a conversation", generic chat-bubble PathIcon at Opacity 0.12) | model available, no messages | Title "Start a conversation"; hint keeps the pick-model-then-type instruction. This is the first screen a screenshot shows; it must read clearly (doc 02.2 kills the 0.18/0.25 opacities). |
| ChatView.axaml:486-501 ("No chat model is set up yet") | no model configured | Keep the two action buttons (setup wizard, Services) below the control, tooltips intact. |
| AgentView.axaml:735 ("No tasks yet. Start one above.") | bare TextBlock | Title plus a hint that a task runs in a sandboxed folder with approval gates; that is the app's differentiator, say it here. |
| BenchmarkView.axaml:143 ("No runs yet. Pick a suite and model above, then click Run.") | bare TextBlock | Same content, Moss-fronted. |
| MemoriesView.axaml:162-165 ("No memories yet") | bare TextBlock | Hint explains memories accumulate from chat automatically (verify exact behaviour against MemoriesView/docs before writing the sentence; do not invent). |
| RagView.axaml:71-78 ("No datasets yet") | bare TextBlock block | Hint points at ingest: pick a folder, Moss keeps it indexed. |

While in there, sweep the remaining views for other bare empty-state text
(LogsView, ModelManagementView lists, run history panels) and apply the
control where a view has a true "nothing exists yet" state. Do not force it
onto transient states (filtered-to-empty lists, in-progress loads).

## 1.3 Setup wizard greeting

`SetupWizardView.axaml:11-14` header: add a `MossIcon` (about 28px) beside
the "First-Run Setup" title, with an in-voice tooltip (the wizard is the
one place a welcome line is appropriate). The subtitle line stays factual.
No mascot on the individual steps; the wizard must not get cuter as it gets
longer.

## 1.4 Existing placements stay

- RagView.axaml:502-504 (ingest progress) and ServicesView.axaml:189-191
  (error banner) keep their MossIcon and tooltips. Re-check both tooltip
  lines against the voice rules; "Right. Let's see what broke." passes,
  keep it.
- The error-banner pattern (icon plus calm-competence tooltip) is the
  approved shape if other error banners want Moss later; do not add him to
  more error surfaces this round.

## 1.5 Close-out doc updates

`docs/mascot.md` "Current in-app usage" must list the real set of
placements after this round (empty states, wizard greeting, ingest
progress, error banner). `docs/features.md` gets one line about the
refreshed empty states; do not oversell it.

## Explicit rejections for this doc

- No animation, no timed tips, no tip-of-the-day engine, no popup Moss.
- No Moss inside the chat transcript or message bubbles. He is not the AI
  and must never appear to be answering.
- No new full-character artwork; icon-scale silhouette only.
