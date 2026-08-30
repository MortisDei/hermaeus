# Review round 30: What is actually broken

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

r30 is a one-month live-use repair round. It comes from the owner's Windows
dogfooding and a clean Pop!_OS installation, not from a speculative code audit.
The local source notes are [`docs/temp/owner-notes.md`](../temp/owner-notes.md)
and [`docs/temp/tlb-lessons.md`](../temp/tlb-lessons.md). They are intentionally
outside the committed product documentation, but they are the wording and
evidence behind this pack.

The severe failure is the first-run path. On Pop!_OS the wizard recommended an
appropriate model, downloaded enough of it that a retry said it already
existed, then left Services unable to use it. The same run appeared to accept a
Data Root and AI Root before Settings showed different values. A new user did
everything the product asked and ended with no model.

The most repeated daily-use request is one model setting shown as several
different truths. Services still exposes independent K and V cache controls
(`ServerConfig.KvCacheTypeK/V`, `ServicesView.axaml:405-419`), while the Models
editor has no cache control at all. Its fit chip uses the default f16-sized KV
projection even when the owner has selected q8_0. r30 makes context and one KV
cache type shared per-model defaults, without pretending that ports,
executables, slots, or other server-instance settings belong to a model.

r30 also closes the reasoning gap instead of adding a cosmetic server toggle.
GGUF NextN metadata and llama-server's own help and `/props` capability data
provide automatic evidence for built-in MTP, reasoning extraction, template
preservation, and modalities. Separate reasoning then runs through the full
transport, message, persistence, branch, history, transcript, local API, and
export path. Unknown remains visible and never becomes a filename guess.

The smaller reports are confirmed gaps, not a new architecture:

- r29's cursor fix deliberately skips mixed-content containers. The Projects
  switcher, sidebar toggle, message action row, edit action, and regenerate row
  are all outside its automatic coverage.
- the Models download view already tracks `DownloadPercent`, but the button
  renders only `Downloading...`.
- the Hugging Face browser does not calculate an on-disk state until the user
  clicks Download and hits a collision.
- Memories has commands and parameters in source. The disabled live controls
  therefore require a binding/runtime reproduction, not new commands.
- benchmark run data under the owner's real data root proves several
  deterministic false fails. Doc 04 names the exact runs and scorer causes.
- benchmark exports and comparisons must identify the KV cache K/V types and
  Flash Attention setting that produced a local llama-server result. Those
  engine choices materially affect memory use and can affect performance, so
  an omitted value is not reproducibility.

## Scope

| Doc | Theme |
| --- | --- |
| `01-linux-onboarding.md` | A starter download that becomes a usable selected model, recoverable failure, and roots that remain saved on Linux |
| `02-models-and-services.md` | One per-model KV default, truthful fit, download state and progress, safe deletion, responsive editor, and honest companion pickers |
| `03-ui-correctness.md` | Cursor gaps, dropdown wheel input, Memories actions, neutral numeric defaults, and one Chat export action |
| `04-benchmark-truth.md` | False-fail fixtures from the owner's runs and narrowly corrected deterministic scoring |
| `05-reasoning-and-capabilities.md` | Automatic GGUF/runtime capability detection and complete reasoning extraction, preservation, persistence, replay, UI, and export |
| `06-roadmap.md` | 0.37.0-alpha, strict sequence, test budget, descope boundary, deferred work, and explicit rejections |
| `07-final-dogfood.md` | Final dogfood root causes, bounded fixes, regression evidence, privacy audit, and remaining manual verification |
| `08-final-public-release-security-audit.md` | Adversarial public-release security sign-off, findings, remediation, validation, and release conditions |

### r30 add-on: measured engine provenance

The r30 draft PR also records the managed llama-server KV cache K/V types and
Flash Attention setting on new local-GGUF benchmark runs. The additive fields
flow through saved `run_json`, details, comparisons, and JSON, Markdown, and
CSV exports. Historical data stays unchanged and says not recorded rather than
being backfilled from a presumed default.

### r30 add-on: compact agent replay and voice test signals

The add-on also compacts only proven-identical successful tool outcomes in the
model-facing agent transcript replay, leaving the raw transcript untouched and
making a three-or-more unchanged sequence visible as a diagnostic without
blocking it. `VoiceOrchestratorTests` now use provider signals rather than
fixed sleeps, and `VoiceProviderRegistry` has behavior coverage for settings
aliases, fallback, persistence, catalog metadata, and service mapping.

## Deliberately not in r30

- Audio feedback. It is a feature request, not part of repairing a broken
  one-month round.
- Normalized model-facing tool outcomes and an empirical experience store. They
  remain larger cross-cutting designs; transcript compaction and its diagnostic
  are the bounded r30 add-on described above.

Every deferred item is recorded in `docs/review/deferred.md`; none is silently
dropped.

## Standing rules

- Branch `r30/round` from `main`; one PR; the owner alone pushes the tag.
- No new NuGet packages.
- No shell-string process launches, raw secrets, non-atomic state writes,
  path traversal, or symlink-following deletion.
- `SettingsService` remains the one settings save flow.
- User-visible changes update `docs/features.md`, the relevant workflow docs,
  `CHANGELOG.md`, and the version surfaces named in doc 06.
- Read `docs/testing.md` before changing tests. Register every new harness case,
  use `[WindowsOnlyFact]` for Windows-only work, and put test results outside
  the repository.
- No em dashes in code, docs, or UI text.
