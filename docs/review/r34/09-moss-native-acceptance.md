# 09. Moss native UI and acceptance

Authority: [brand specification](../../hermaeus-branding.md) and
[Moss specification](../../mascot.md). Preserve native Avalonia controls,
readable hierarchy, Deep Moss/Forest/Sage/Parchment with restrained Copper/Amber,
user-selectable fonts, technical information density, keyboard navigation and
accessible pointer interaction. No browser frontend, ornamental mascot,
gradient/card overhaul or animated novelty is selected.

## Visual evidence actually inspected

**VE, planning:** opened and visually inspected all four tracked images:
`docs/images/chat.png`, `models.png`, `doctor.png`, `onboarding.png`.
Their latest Git change is `56849c9`, 2026-08-23. These are historical screenshots,
not captures of baseline HEAD or new Windows/Linux acceptance. Doctor visibly
shows `0.37.0-alpha`, confirming that at least that surface predates R33.

| Image | Observed pixels | Planning implication, not a current defect claim |
| --- | --- | --- |
| Chat | Compact icon navigation, readable response hierarchy, substantial transcript space, dense small diagnostic text above response, technical footer with a local path | Preserve reading space; check current disclosure/legibility and public screenshot privacy. Do not infer streaming/focus/clipboard behaviour from still pixels |
| Models | Sparse two-model content with actions at top; model badges/actions differ from current richer source catalogue | Re-capture role groups, fit/companion/provenance states before making density or reflow decisions |
| Doctor | Long vertically spaced findings with repeated secondary actions and low-emphasis detail | Inspect current remediation semantics and keyboard focus on actual error/Unknown/Ready rows before any visual consolidation |
| Onboarding | Clear three-step action group and labelled roots, large unused area | Confirm readability, picker behaviour, skip/back/next and meaningful next action at minimum size and high DPI |

No retained current screenshots cover Agent, Lab, Services, Settings, RAG,
Recall palette, Memories/revisions, Benchmarks, Activity, Logs or System.
Historical R33 owner/native receipts cover selected interactions, not a complete
current screenshot matrix. Existing public images include local filesystem
paths despite `docs/images/README.md` disallowing them. U1 should replace them
with actual sanitised fixtures rather than masking pixels and implying a new
product capture. Nothing is deleted or fabricated in this planning pass.

## Bounded UX questions for U1

| Surface | Job and acceptance to inspect in actual UI |
| --- | --- |
| Agent | Goal/current action and pending decision visible without changing tabs; prepared versus applied/readback/conflict distinct; native editor Ctrl+S and approval boundary clear; parent/child source identity and interruption next action |
| Lab | Requested/candidate/effective state and source restoration distinct; missing measurements readable as Unknown; one primary next action, raw evidence disclosed; Apply requires exact review |
| Services | Configured versus unsaved versus running values, model-switch drafts, stopped/starting/error, GPU Fit prediction and process observations; owner controls explicit saves/restarts |
| Settings | Preferences autosave, runtime controls remain on Services; typography/theme preview and persistence; fallback vault visibility; Data Root configured/pending/effective states and restart action |
| RAG | Ask versus administration, explicit per-question dataset scope, local/web source label, ingest progress, refusal, failed/cancelled and citation/revision inspection |
| Recall | Ctrl+K command/search distinction, source-kind/project/archive context, keyboard movement/Enter/Escape/focus restore; degraded keyword-only state and clear-index confirmation |
| Memories/Activity | Assertion/revision/pinned/archive state separate from operational trace; confirmation and no loss of selected revision after navigation |
| Benchmarks | Completion versus evidence eligibility, truthful empty rankings, native detail dialog, idempotent load and explicit refresh |
| Doctor/Models/setup | Correct remediation target and readiness, role/fit/provenance reflow, readable progress; errors state fact and next step without mascot jokes |
| Chat/Logs/System | Long Markdown/code selection, scroll pin, copy, telemetry teardown, Unknown labels, source-sensitive logs and useful diagnostic disclosure |

Review touched controls at minimum 720x500 and normal/maximised sizes,
100/150/200% Windows DPI and actual Linux scaling. Check dark/light/system
theme, native/default/custom fonts, long model names/paths and text scaling.
Measure contrast and focus visibility in the actual palette; source hex codes
alone cannot prove legibility. Icon-only buttons need tooltips and accessible
names, empty states reuse MossEmptyState, destructive confirmations stay owned,
and Tab/Shift+Tab/Enter/Escape must work without a pointer.

Do not redesign a screen from XAML alone. Capture the current job/state first,
identify the observed friction, then make the smallest correction and capture
the same state after. If no defect is observed, Retain is a complete decision.

## Interaction references, not architecture imports

Primary sources U01-U03 in [10](10-technology-decisions.md) were inspected for
useful ideas. These are **recommendations/inferences**, not cloned screens or
claims of comparative speed/security:

- Jan: keep model choice and local API/service configuration discoverable.
  Evaluate Hermaeus's existing Chat/Services separation with a real task.
- AnythingLLM: clear distinction between attaching context to one conversation
  and embedding shared knowledge is useful. Preserve Hermaeus's existing
  Knowledge scope, generation and citation semantics.
- Open WebUI: explicit model-specific capability disclosure and source access
  can inform an inspection checklist. Do not import its frontend, automatic
  tools, configuration defaults or dependency stack.

No external app was installed, launched, benchmarked or allowed to read owner
data. There is no visual superiority claim.

## Native evidence receipt

For **both** Windows 11/RTX laptop and Pop!_OS/COSMIC/GTX desktop, record exact
package SHA/commit, renderer/backend, scaling, theme/font, fixture and action
sequence, before/after screenshots, binding/runtime warnings, keyboard/pointer
observations and owner result. Cover empty, populated, loading, error,
cancelled, Unknown and approval states where meaningful. Screenshots prove
appearance; callback counts/process exits/interaction logs prove different
claims. Keep native control, captured pixels and owner acceptance separate.

Tray close/minimise, disable/re-enable, second-launch restoration, clean Quit,
native file pickers/portals and modal placement need explicit interaction
receipts. Close the test app normally, preserve owner settings and confirm no
owned children remain. Current platform/visual cells are **Unknown** until run.
The screenshot-verification skill is required when implementation reaches real
capture work; no capture workflow or app launch is performed by this plan.
