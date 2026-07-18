# 05 - Roadmap

## Version and scope

Ships as **0.18.0-alpha** (minor bump: user-visible feature round).
Version lives solely in `Directory.Build.props`. CHANGELOG entry per
convention, 10-version FIFO enforced (archive the oldest to
`docs/changelog-archive.md`). `docs/features.md` (or the current
features doc) updated for: Models library rework, auto-tune from
Models, fits chips, folder organizer, HF update checks + browser,
chat sampling flyout, real Windows system info.

## Sequencing

1. **Doc 01 (system truth)** first: 1.1-1.4 are independent small
   fixes; 1.5 (`HardwareProfile`) is a dependency of 2.5 and 3.4.
2. **2.1 + 2.2** (compact cards + scroll fix) next: they change the
   surface every later Models-page item lands on, and 2.2 needs live
   reproduction before the layout changes mask it. Diagnose 2.2's
   root cause BEFORE building 2.1, then fix both.
3. **2.3 + 2.4** (auto-tune) - lifts the shared tune-profile store.
4. **2.5** (fits chip) - needs 1.5.
5. **2.6** (folder organizer) - independent of 3.x but produces
   provenance records, so land it before 3.2 to make the owner's
   existing models update-checkable.
6. **Doc 03** in order: 3.1 manifest, 3.2 check, 3.3 update, 3.4
   browser.
7. **Doc 04** any time (independent).

## Tests

~35-45 new tests from the current 688, following existing harness
conventions (HarnessCases where the suite does that, xUnit [Fact]
where the neighboring tests do). Pure-logic first: OS-name mapper,
GPU registry parser seam, fit estimator boundaries, tune-staleness
predicate, migration planner + reference rewrite, manifest
round-trip, HF JSON fixture parsing, update-swap failure paths,
BuildChatOptions flow-through. No test may require a live
llama-server or live network (fixtures + counting/fake handlers).
Live verification on the owner's machine is still mandatory for: ss3
scroll reproduction, System page values, one real HF search +
tiny-model download, and the organizer on a scratch folder first.

Zero-warning build throughout (`TreatWarningsAsErrors`). No literal
em/en dashes in any source or doc (SourceStringsAvoidLongDashes
scans; use ASCII escapes when a test needs the glyph).

## Security review touch

Add an r13 subsection to `docs/security-review.md`:
- New outbound surface: huggingface.co API + resolve downloads,
  manual-only, HTTPS-only, host-allowlisted, anonymous; disclosed in
  Privacy Audit when configured (3.2).
- Download integrity: every HF download verified against the tree
  API's `lfs.oid` SHA256 before being trusted (consistent with the
  starter-model posture; note the oid comes from the same origin as
  the file, i.e. this is origin-integrity, not independent
  attestation - same deliberate stance as r11's llama-server
  provenance decision, state it explicitly).
- Data-mutation surfaces: folder organizer (move-only,
  preview+confirm, empty-dir cleanup separately confirmed, no file
  deletion) and model update (atomic swap, original restored on any
  failure, running models refused).
- Registry reads (1.2-1.4) are read-only HKLM queries, no new
  privileges.

## Explicit rejections (do not re-propose)

- **No background or scheduled update polling.** Update checks are a
  button. Aether's local-first "0 outbound destinations" posture is a
  headline feature; silent periodic phoning to HF would break it.
- **No auto-applying updates**, ever, even opt-in, this round.
- **No renaming of model files** during organizing or updating. The
  filename is the HF update-matching identity and encodes the quant.
  Friendly names are what model profiles' DisplayName is for.
- **No deletion of model files without a per-action confirmation.**
  The organizer moves; the updater swaps and removes the .previous
  copy only after a successful swap (that removal is part of the
  confirmed update action, allowed).
- **No HF tokens / gated repos / private repos.** Anonymous public
  access only; storing HF credentials expands the secret surface for
  marginal alpha value.
- **No hash-based reverse lookup** to identify unknown local models
  (HF has no such API); manual repo linking (3.1) covers it.
- **No WMI dependency** for system info (registry + P/Invoke +
  nvidia-smi only; WMI is slow and flaky on stripped installs).
- **No separate nav panel for the HF browser**; it lives inside the
  Models page. The toolbar is crowded enough (owner has 14 icons).
- **No virtualized ItemsControl rewrite** for the Models list; 2.1's
  collapsed cards make 32+ rows cheap without it. Revisit only if a
  real user shows up with hundreds of models.
- **Keep the 2-minute model list cache** and Force refresh checkbox
  as-is (ModelManagementViewModel.cs:60-66); this round does not
  touch model discovery semantics beyond the organizer's refresh.
