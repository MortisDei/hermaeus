# 02. One model, one set of truthful defaults

## Boundary decision

The Models page owns model identity, sampling preferences, and defaults that
follow a local GGUF. Services owns a concrete process: executable, port, slots,
threads, GPU placement, server lifecycle, and live launch arguments.

r30 shares exactly two runtime defaults across the surfaces: context size and
one KV cache type. It does not clone the Services editor into every model card.
This is the smallest interpretation of the owner's parity request that remains
true when one GGUF is used by more than one server config.

## 2.1 Collapse K and V to one KV cache type

Keep the llama.cpp launch contract, which still has separate
`--cache-type-k` and `--cache-type-v` flags, but expose and persist one user
choice and emit it to both flags.

- add one canonical `KvCacheType` to `ServerConfig`, default `f16`;
- migrate existing JSON additively in `SettingsService.NormalizeManagedServers`.
  If old K and V match, preserve that value. If they differ, use K as the
  deterministic canonical value, set both legacy fields to it, and write one
  warning to the redacted runtime log on that first normalization;
- retain `KvCacheTypeK/V` only as backwards-read compatibility fields for this
  release. New UI and new saves never let them diverge;
- replace the two Services ComboBoxes with one labelled `KV cache` and explain
  that Hermaeus applies it to both K and V;
- update validation, fit math, dirty-state comparison, launch arguments,
  benchmark metadata, exports, and tests to use the canonical value.

Acceptance criteria:

- every new config stores one value and launches with matching K and V flags;
- an old matching pair migrates byte-for-value; an old divergent pair follows
  the documented K rule and logs once;
- f16 still emits no redundant flags if that is the current builder contract;
- no UI or public documentation claims independent K/V control after r30.

## 2.2 Context and KV cache are shared per-model defaults

Add nullable `ModelProfile.DefaultKvCacheType` using the same option vocabulary
as Services. In the Models editor, group Context size and KV cache under
`Runtime defaults`; keep the existing sampling fields under a separate label.
Every field retains a tooltip. The owner report predates r29's tooltip pass,
but current source already has tooltips on all existing controls, so this item
adds missing controls and guards the coverage rather than rewriting the editor.

Flow in both directions:

- Models Save writes `DefaultContextSize` and `DefaultKvCacheType` through
  `ModelProfileService` and refreshes matching stopped Services cards;
- selecting a different model in Services applies an existing fresh
  `LlamaTuneProfile` context first, then the model-card context default, then
  leaves the current context alone. KV has no tune value, so model-card KV then
  leave-as-is is its precedence;
- Services Save Config writes the current context and KV values back to the
  selected local model's `ModelProfile`, then saves once through the existing
  settings/profile flows. Models reflects them on refresh;
- applying defaults happens only on a real model-path change. Collection resets
  and same-path rebinds never overwrite unsaved edits.

Server-only values never flow into `ModelProfile`: executable, port, slots,
threads, GPU layers, auto-start, memory lock/map, CPU MoE, flash attention,
projector, draft path, and ExtraArgs stay on Services. Auto Tune continues to
own its file-versioned tune profile and continues to win for context.

Acceptance criteria:

- Models to Services and Services to Models round-trip context and one KV value;
- a fresh tune profile still wins context without changing the stored card
  default;
- same-path refresh preserves an in-progress edit;
- two servers selecting the same model receive its defaults on selection but
  retain independent server-only settings;
- every new icon-only control has a tooltip and the tooltip guard passes.

## 2.3 Fit chips use the selected KV precision

`ModelFitEstimator.Estimate(..., info, contextSize)` currently hard-codes
`KvCacheMath.DefaultBytesPerElement` for both caches
(`ModelFitEstimator.cs:49-75`). Pass the model profile's canonical KV type into
the projection and use `KvCacheMath.ResolveBytesPerElement` once for both K and
V. The fit reason names context, KV type, weights, and KV bytes.

The Models card recomputes after a saved context/KV change and while the editor
contains a valid unsaved change, so the user sees the consequence before Save.
The Hugging Face browser and wizard have no per-model profile yet and continue
using the documented f16 conservative default.

Acceptance criteria:

- the same GGUF/context produces a smaller KV estimate for q8_0 than f16;
- a boundary fixture changes from Partial offload to Fits GPU when the reduced
  KV bytes genuinely cross the VRAM threshold;
- changing an unrelated sampling field does not change fit;
- missing GGUF metadata retains the existing fallback wording.

## 2.4 Download state and progress are visible before the click

When a Hugging Face file set is built, resolve every selected entry's planned
destination and compare disk plus manifest state. Expose `NotDownloaded`,
`Partial`, `Downloaded`, and `Downloading` as explicit VM state.

- Downloaded renders `On disk` and is disabled;
- Downloading renders the rounded `DownloadPercent` on the button;
- Partial renders `Complete set` and fetches only missing entries after
  verifying that existing required entries belong to the same repo path/hash;
- an unrelated collision still refuses and never overwrites;
- changing projector/draft checkboxes recomputes the selected set's state;
- completion forces the existing Models refresh and updates state without
  closing/reopening the browser.

Acceptance criteria:

- a locally complete file set never offers an active Download button;
- progress is monotonic across multi-file sets and the displayed percentage
  matches `DownloadPercent`;
- partial completion downloads only missing files;
- a manifest match uses repo id plus repo file plus final path, not filename
  alone;
- manually placed files are labelled on disk only when the exact planned path
  exists, and are never silently claimed as verified provenance.

## 2.5 Safe deletion from a model card

Add a trash icon beside Configure. It is visible only for a local GGUF and has a
tooltip. Refuse while any running server uses the file. Confirmation names the
full path and whether only the main file or a manifest-proven downloaded set is
being removed.

Deletion rules:

- reject symlinks, traversal, paths outside the configured AI Root, and any path
  that is not a regular file;
- for a manifest-proven HF file set in its own download directory, offer the
  exact manifest-associated files. Never infer a directory-wide delete from a
  shared filename or repo name;
- for a manual/local file, delete only the selected GGUF;
- remove matching manifest and model-profile rows after disk deletion succeeds;
- clear stopped server configs that reference the deleted main file, save once,
  invalidate model caches, and refresh Models, Services, and Chat;
- do not recursively delete directories. Empty directory cleanup is a separate
  existing confirmed operation.

Acceptance criteria:

- cancel changes nothing;
- running, outside-root, symlink, and non-file targets are refused;
- successful deletion removes exactly the confirmed files and dependent state;
- a partial filesystem failure reports which files remain and does not claim
  success;
- tooltip and path-safety regression tests cover the button and service helper.

## 2.6 The editor uses available width

Keep the r29 flyout, but remove its fixed 620px content width. In Desktop layout
code, size it to `clamp(620, model-page viewport minus 64, 1100)` and recompute
on viewport size changes. Narrow windows keep the current scroll behavior; wide
windows expand the grid and avoid needless horizontal or wrapped controls.
This is layout code, not business logic, and may remain in the view layer.

Acceptance criteria:

- at a wide viewport the editor is visibly wider than 620px and all fields use
  the space;
- at a narrow viewport it remains fully reachable by scrolling and never leaves
  the window bounds;
- opening, resizing, closing, and reopening does not retain a stale width.

## 2.7 Projector and draft choices follow the selected model

The current refresh methods insert the old projector/draft path into the new
candidate list (`ServicesViewModel.cs:307-371`). That preservation explains the
stale Gemma MTP and projector reports. It was useful for collection reset, but
it must not cross a real model change.

Introduce typed choices rather than placing sentinel text in path collections:

- projector: `None` plus detected external projectors;
- draft: `None`, `Built in`, plus detected external MTP heads;
- `None` clears the path and disables the corresponding launch feature;
- `Built in` emits `draft-mtp` with no `--spec-draft-model` path;
- an external draft emits both the type and its validated path;
- on a real main-model change, clear the prior model's choice, rescan, and choose
  None unless exactly one external candidate exists. For multiple projector
  candidates, prefer an exact F16-labelled candidate, then deterministic
  ordinal filename order. Never overwrite an explicit choice on same-model
  refresh;
- a choice outside the detected list may be reinserted only when it belongs to
  the same main model and passed the existing explicit file-picker validation.

The installed b10227 help confirms `--spec-type` remains a comma-separated list
whose default is none. Keep n-gram and MTP composable. There is no evidence in
the current help or stored runs that one disables the other, so r30 does not add
mutual exclusion or a warning.

Acceptance criteria:

- changing Gemma to Nemotron clears Gemma's draft and projector values
  immediately without navigation;
- None never serializes as a filesystem path;
- Built in emits no draft-model path;
- same-model collection resets keep a valid explicit choice;
- F16 wins among multiple projector variants;
- draft path validation and vocabulary mismatch refusal remain intact.

## 2.8 Capability-aware companion defaults

Doc 05 defines the capability service and evidence model. Use it here rather
than maintaining picker-specific filename or architecture guesses.

- offer `Built in` automatically when GGUF metadata reports a positive
  `{architecture}.nextn_predict_layers` value and the selected executable
  advertises `draft-mtp`;
- select `Built in` by default on a real model change when that positive result
  exists and no saved choice exists. Never replace an explicit same-model
  choice;
- show detected capabilities and their evidence in the Models editor. Distinguish
  `Available`, `Unavailable`, and `Unknown`;
- treat sibling projector discovery as `External companion available`, not as a
  claim that the main GGUF embeds vision support;
- keep manual external choices for unknown models. Automatic detection improves
  the default path without turning uncertainty into a refusal.

Acceptance criteria:

- the owner's Qwen3.5 MTP fixture is detected from
  `qwen35.nextn_predict_layers=1`, not its filename;
- a qwen35 fixture without that key is Unknown, not automatically MTP-capable;
- an executable without `draft-mtp` never produces an enabled Built in choice;
- a same-model refresh does not replace the user's valid explicit choice;
- every displayed result names the GGUF, runtime, template, or companion evidence
  that produced it.

## Tests and documentation

Budget 30 to 38 tests across settings normalization, launch arguments, shared
defaults, fit math, download state/progress, deletion safety, stale picker
reset, sentinel serialization, F16 preference, capability evidence, and n-gram
plus MTP composition.
Update `docs/features.md`, `docs/llama-cpp-features.md`, `docs/benchmarks.md` for
metadata shape changes, and `CHANGELOG.md`.
