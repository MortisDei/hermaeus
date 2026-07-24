# 02. Tooltips and readability

Goal: fix the reported dark-mode readability bug at its root, put tooltips
and dim text on first-party styles so the bug class cannot recur, then
complete tooltip coverage across the app and guard it with a test.

## 2.1 The reported bug: dark grey text in dark mode

Owner report: the Moss-adjacent helper text (the tooltip and/or the status
text next to the icon) rendered dark grey in dark mode and was hard to
read.

Facts at spec time:

- `Styles/AppStyles.axaml` contains no `ToolTip` style at all; tooltips
  render with stock FluentTheme resources.
- `App.axaml:44-53` overrides `SystemAccentColor` and its Dark1..Light3
  variants at Application.Resources level. Resources defined there are
  variant-blind: they serve the same value to both theme variants, which
  is exactly the mechanism that produces wrong-variant pairings in
  Fluent-derived brushes.
- The Theme setting was wired into `RequestedThemeVariant` in r21
  (d3f29b7); dark mode may now be forced app-side rather than following
  the OS, which changes which resource lookups the tooltip does.
- Candidate victims near Moss: the plain-string tooltips at
  RagView.axaml:504 and ServicesView.axaml:191, and the low-opacity
  TextBlocks at RagView.axaml:505-510 (Opacity 0.5/0.6).

Required approach: reproduce first. Run the app in dark mode (Settings >
Interface > Theme), hover both Moss icons, and identify exactly which text
is dark grey (tooltip foreground vs. adjacent TextBlock). Fix the actual
cause. Then, regardless of which it was, implement 2.2 and 2.3 so neither
failure mode can come back silently.

## 2.2 First-party text-dim classes and an opacity floor

Ad-hoc `Opacity` on text is scattered everywhere (0.18 and 0.25 on the
chat empty state at ChatView.axaml:480-483, 0.4/0.5/0.6 across most
views). Some of these are below any reasonable contrast floor, and each
one is a separate opportunity to be unreadable in one variant.

- Add two `TextBlock` classes to `Styles/AppStyles.axaml`:
  - `hint`: secondary explanatory text. Opacity 0.65.
  - `faint`: tertiary/decorative text. Opacity 0.45. This is the floor;
    no user-relevant text may render dimmer than `faint`.
- Rule going forward: text uses one of these classes (or full opacity);
  raw `Opacity` on a TextBlock is reserved for genuinely decorative
  cases. Decorative icons (like the 0.12 chat-bubble PathIcon) are exempt
  from the floor.
- Sweep: convert the empty states touched by doc 01 and the Moss-adjacent
  text (RagView ingest row, Services error banner) to the classes. A full
  app-wide conversion is NOT required this round; convert what this round
  touches, plus any text you find below 0.4 opacity anywhere (those are
  bugs; ChatView.axaml:480-483 is the known offender).

## 2.3 Branded, theme-aware tooltip style

Add a `ToolTip` style to `Styles/AppStyles.axaml` with explicit
`ThemeDictionaries`-based (or `{DynamicResource}` theme-variant-scoped)
background/foreground pairs so tooltip readability never again depends on
Fluent defaults interacting with our accent overrides:

- Dark variant: Deep Moss-derived surface, Parchment-derived foreground.
- Light variant: Parchment-derived surface, Ink foreground.
- Subtle border (brand Forest at low alpha), corner radius consistent
  with existing cards, padding enough for two-line tips, `MaxWidth`
  around 360 with `TextWrapping="Wrap"` so long tips wrap instead of
  spanning the screen.
- Both variants must be checked by eye in both app themes before calling
  this done. State in the final response that this was done and how.

## 2.4 Tooltip coverage sweep

Principle: a control gets a tooltip when its visible label does not fully
describe what will happen (icon-only buttons, abbreviated labels, toggles
with side effects, anything destructive). A tooltip never merely restates
the visible label ("Browse" gets none); noise tooltips are worse than
none.

Copy rules: sentence case; a complete sentence ends with a period, a
fragment does not; describe the effect, not the gesture (never "Click
to..."); destructive actions name what is lost.

Distribution at spec time (ToolTip.Tip count per file) shows where to
look. Sweep every view, but these are the known-thin surfaces:

| Surface | Count | Notes |
| --- | --- | --- |
| SettingsTrustSectionView | 1 | Trust toggles change security behaviour; every one needs its consequence stated. |
| SettingsVoiceSectionView | 1 | |
| SystemOverviewView | 2 | |
| ConversationListView | 3 | Per-item actions (delete, rename) qualify. |
| DoctorView | 4 | Fix actions should say what the fix will do. |
| AgentView | 5 | Approval/risk controls deserve the most care in the app; an approval button must say what approving executes. |
| LogsView | 5 | |
| All dialogs (Confirm*, Delete*, Restore*, etc.) | 0 | Dialogs mostly carry their own explanatory text; only add tooltips where a button's consequence is not already spelled out in the dialog body. |

MainWindow's nav rail (15) and the heavier views are likely fine; verify
rather than pad.

## 2.5 Guard test: icon-only controls must have tooltips

Add a harness-style repo-scan test alongside the existing em-dash scan in
`ServiceTests.cs` (register in `XunitHarnessTests.HarnessCases`):

- Parse every `.axaml` under `src/Hermaeus.Desktop/` with
  `System.Xml.Linq.XDocument` (no new dependency; axaml is XML).
- Offender: a `Button` (or `ToggleButton`/`RepeatButton`) element whose
  subtree contains a `PathIcon` or `MossIcon` but no `TextBlock` and no
  text `Content` attribute, and which has no `ToolTip.Tip` attribute.
- Keep a small explicit allowlist in the test for justified exceptions,
  each with a one-line reason. Start empty; only add entries the sweep
  genuinely justifies.
- The test failure message lists file and element so the next person can
  fix it without archaeology, matching the em-dash scan's style.

This guards the floor (icon-only controls), not full coverage; full
coverage stays a review-time judgement, which is what 2.4 is.
