# R30 final dogfood and release-readiness pass

This pass closes the final bounded defects found while exercising the completed
r30 branch. It does not add a new subsystem or broaden the release scope.

## Root causes and fixes

- Linux llama.cpp release archives contain relative link entries for versioned
  companion libraries. Extraction previously discarded those entries, leaving
  an executable that existed but could not load. Safe in-root links are now
  validated and materialized as regular files. Doctor executes the selected
  binary and distinguishes missing, non-starting, non-zero, unknown-build, and
  usable states. Unrelated build identifier schemes are not compared.
- Download callbacks produced an update for nearly every network buffer and
  Doctor retained every line. Source progress is rate-limited, embedding
  progress is coalesced, and the visible diagnostic tail is bounded. The
  operation remains owned by the singleton Doctor view model across navigation.
- Data-root migration treated the live process lock as user data and could
  collide with its own exclusive handle. The fixed bootstrap lock is excluded
  from migration and backup enumeration while remaining the process gate.
- Error notifications were visible only in notification history. They now also
  enter the existing redacted Runtime Logs path. Stored UTC timestamps are
  formatted locally at the presentation boundary.
- Setup navigation reloaded settings and reset transient wizard state. An
  incomplete setup now has a persistent Resume setup route and returns to the
  same step. Explicit reruns still reset intentionally.
- Approval actions appeared runnable before their generated plan had been
  reviewed. Approval is now enabled only for the exact reviewed action.
- Managed embedding warm-up raced the service startup sequence. It now waits
  for the matching localhost service to reach Running, skips incomplete setup,
  and still probes external or unmanaged endpoints directly.
- Local AI setup verified pinned model bytes but did not record their source.
  Successful pinned Hugging Face installs now use the existing model manifest
  provenance contract.
- A health request could lose a process-exit race without its fault being
  observed. Both race participants are now observed and responses are disposed.
- Doctor popups and service error callouts now use solid, readable surfaces;
  the previous-session warning title now agrees with its warning detail.
- The Linux window used Avalonia's default `Hermaeus.Desktop` WM class while
  the installed desktop entry expected `Hermaeus`, and the main window did not
  set its canonical icon. The application now emits the `hermaeus` X11/XWayland
  identity used by `hermaeus.desktop`, sets the Moss window icon, and keeps the
  installed theme icon under the same name.
- The extracted Linux shell launcher was opened as text by COSMIC Files, while
  the adjacent Desktop Entry was not a reliable archive launch surface. The
  package now exposes `Hermaeus` as a relocation-safe link to the native .NET
  apphost, with a suffix-free internal executable that file managers identify
  as an application. Root shell scripts had the same file-manager failure, so
  install and uninstall now use dedicated native apphost launchers with a
  graphical confirmation/result window. Their shell implementations live under
  `app/integration/`; the installed Desktop Entry remains the standards-based
  application-menu and themed-icon path.
- Debug and source launches use the same `hermaeus` WM class as release builds.
  A generic taskbar icon can still appear when the shell has no installed
  `hermaeus.desktop` entry to match; the installed package is the release icon
  verification path.
- Doctor previously had only an OS heuristic for Linux tray support. A click on
  the current session's Hermaeus tray now records positive integration evidence
  shared with Doctor, producing Ready; no evidence remains Info.
- Chat's empty state equated zero currently available models with incomplete
  onboarding. Setup completion now gates the wizard affordance, so completed
  users are directed to Services while incomplete and intentional rerun flows
  remain available.

## Preserved behavior

- SettingsService remains the only settings persistence path.
- Risk classification, plan review, and approval gates are not bypassed.
- Model SHA256 verification, archive traversal rejection, state-file atomicity,
  secret redaction, and localhost managed-server binding remain intact.
- Existing models, data roots, external endpoints, and Kokoro native install
  routing are not deleted or silently replaced.
- No new package dependency or license change was introduced.

## Regression evidence

Focused regressions cover safe archive link materialization and traversal
rejection, progress coalescing and completion, build comparison states,
data-root migration with an exclusively held lock, notification log bridging
and local time formatting, setup resume, Doctor navigation ownership, approval
gating, and model provenance. The release gate also includes the warnings-as-
errors solution build and the full sequential xUnit suite.

## Audit and validation boundaries

The final gate completed a value-redacted scan of current tracked content and
all pre-commit reachable history for likely credentials, personal email addresses,
machine-specific home paths, host and private-network identifiers, and tracked
logs or build artefacts. No high-confidence credential or current tracked PII
was found, and the final diff contains no newly introduced identifying
information. One historical commit message contains a machine-specific local
model path; its value is not reproduced here. It is not a credential or an
identity, and history was not rewritten automatically. Raw candidate values
remain private and untracked.

Live visual confirmation, actual managed llama.cpp repair against an installed
Linux release, and live audio confirmation require the desktop runtime and are
manual release checks. No screenshot artefacts are part of this pass.
