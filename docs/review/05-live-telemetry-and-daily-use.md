# 05. Live telemetry and daily use

This group makes useful runtime facts available without turning Chat into a
dashboard, and closes two daily-use behaviors with explicit policy.

## 5.1 Live telemetry model

Build on doc 02's `IRuntimeTelemetrySource`. Add a ViewModels-owned projection
for the currently active Chat model/runtime:

```text
LiveModelTelemetrySnapshot(
  RuntimeIdentity,
  ModelIdentity,
  CapturedAtUtc,
  Metrics[],
  ConfigurationFacts[],
  HealthConditions[])
```

Each metric retains value/unit, current/average/peak semantics, evidence source,
trust state, and missing reason. No ViewModel runs `nvidia-smi`, parses logs, or
polls HTTP itself.

The active runtime comes from the model actually serving the current Chat
request and its launch snapshot, not merely the selected model dropdown. During
warm-up, failure, external-provider use, or model switch, the identity/status is
explicit.

## 5.2 Pop-out/flyout behavior

Add an icon-only button with tooltip near Chat's active model/runtime affordance.
It opens a lightweight flyout or detachable window that can remain visible
while Chat continues. It does not consume transcript space permanently.

Show, where trustworthy:

- current and rolling-average decode tok/s;
- prompt throughput and TTFT for the latest request;
- tokens served and context usage/limit;
- process RAM and current/peak VRAM;
- predicted/observed KV footprint and cache type;
- Flash Attention configured/observed state;
- speculative mechanism, drafted/accepted counts and acceptance;
- runtime/model identity and useful headroom.

Unknown rows either say why or stay in a compact unavailable section. Never
render an absent counter as zero. Detailed history, experiments, and comparison
links open Lab.

## 5.3 Sampling and lifecycle

- Idle flyout closed: no high-rate telemetry polling. Keep only request-level
  metrics already produced by Chat/runtime.
- Flyout open or an active notification condition: bounded sampling, default no
  faster than once per second for OS/process metrics.
- Closing the flyout releases timers/subscriptions. Switching model/server
  clears rolling state and begins a new identity.
- Retain only a bounded in-memory window for the pop-out. Durable observations
  go through Lab/experience when a controlled run requests them.
- External OpenAI-compatible/Ollama runtimes expose only what their current
  contracts report. Do not fabricate local memory metrics.

## 5.4 Runtime health conditions

Create deterministic condition evaluators over telemetry/prediction evidence:

- critically low VRAM headroom;
- meaningful spill/offload observed where a GPU-resident config was expected;
- observed comparable memory materially above GPU Fit prediction;
- context approaching its configured/effective limit;
- sustained performance collapse against a compatible observed baseline;
- runtime process unhealthy/unavailable when Chat expects it.

Thresholds are explicit, testable constants or settings only if users need to
change them. High GPU utilization is not a warning. A model using most of the
GPU as intended is not a fault.

Comparison conditions require compatible fingerprints and minimum duration or
sample count. One slow token/request does not become sustained collapse.

## 5.5 Notification policy

- Deduplicate by condition plus runtime/model/config identity.
- Notify only on transition into a materially actionable state.
- Apply a cooldown and require recovery before repeating, except a materially
  worse severity transition.
- Each notification states the observed fact, evidence quality, and one useful
  action or link to Lab/Services.
- Unknown/low-trust evidence may appear in the flyout but does not raise a
  critical alert.
- Persisting notification history uses the existing redacted Activity/runtime
  log paths, not a new store.

## 5.6 Chat streaming scroll anchoring

Current `ChatView` already implements the requested behavior with
`_pinnedToBottom`, `ScrollChanged`, extent-growth snapping, and a bottom
threshold. R31 does not replace it.

Extract or expose the pin-state transition as a testable helper without moving
Avalonia types into ViewModels. Add regression coverage for:

- pinned + extent growth follows the new bottom;
- user offset away from bottom unpins;
- extent growth while unpinned preserves the user's offset;
- manual return within threshold re-pins;
- completion/final action-row growth while unpinned does not snap;
- sending a new message follows the existing deliberate re-pin behavior;
- conversation switch starts pinned.

Live Linux/COSMIC verification uses a long streaming response: scroll upward,
hold a fixed paragraph while tokens and final controls arrive, return manually
to bottom, and confirm following resumes. Completion must not move the user to
another position.

## 5.7 Audio-feedback event policy

Add one Core/Voice event vocabulary, not calls to a player scattered through
ViewModels:

| Event | Default | Non-audio equivalent |
| --- | --- | --- |
| `TaskNeedsApproval` | quiet cue on | visible pinned decision strip/toast |
| `TaskCompleted` | quiet cue on | terminal status/toast |
| `TaskFailed` | distinct restrained cue on | error state/toast/log |
| `ManagedRuntimeReady` | off by default | status badge |
| `ManagedRuntimeFailed` | quiet cue on | error callout/toast/log |
| `LongOperationCompleted` | off by default | progress completion/toast |
| `RecordingStarted` | optional, off by default | persistent recording indicator |
| `RecordingStopped` | optional, off by default | indicator removal/transcript state |

Do not cue ordinary token arrival, successful button clicks, navigation,
high GPU use, every notification, or every tool step. The table is the complete
R31 list; adding an event requires policy and non-audio equivalent review.

## 5.8 Settings and accessibility

Put preference-only controls on Settings, not Services:

- Audio feedback enabled;
- volume 0-100;
- mute;
- per-event toggles for the explicit list, with restrained defaults;
- "Do not play cues while TTS is speaking" on by default.

Add fields to the existing appropriate settings domain through
`SettingsService`; do not add another save path. Mute does not destroy the saved
volume. Volume/mute apply before playback starts. Every event has a simultaneous
visual/text equivalent, so cues are supplementary and the UI never relies on
hearing.

Respect platform/user reduced-motion or assistive settings where available,
but do not invent an unverified OS-wide sound preference. Document what is and
is not detected.

## 5.9 Playback arbitration and assets

Add an `IAudioFeedbackService` that receives semantic events, resolves an
embedded short WAV asset, applies settings/dedupe/arbitration, and calls shared
playback infrastructure. It owns one bounded queue and cancellation.

- TTS has priority. With the default policy, cues arriving during TTS are
  suppressed, not delayed until they become misleading.
- Repeated identical events within the cooldown coalesce.
- Failure to find a player or play a cue records one restrained diagnostic and
  never breaks the originating operation.
- `AudioPlayback` currently invokes PowerShell with a script containing a file
  path. R31 must remove that shell-string construction before reusing it for a
  wider cue surface. Prefer an in-process Windows playback API already available
  in the platform/BCL, or an argument-only safe player path. Do not interpolate
  paths into `-Command`.
- Assets are small, source-controlled, license-documented, normalized in level,
  and reviewed on Windows and Linux. No downloaded sound pack and no new NuGet
  dependency.

## 5.10 Acceptance criteria

### R31 implementation status

The current implementation provides the bounded shared sampler, request-level
Chat metrics, identity-scoped projection, deterministic health policy and
notification gate, Chat pin-state seam, audio event policy/settings, bounded
cue queue, TTS suppression, and argument-only Windows playback invocation.
Per-process VRAM, runtime launch identity attachment for ordinary Chat, and
live platform behavior remain `Unknown` until their stated manual gates run.

- Telemetry flyout follows the runtime actually serving Chat, shows source and
  Unknown states, and releases sampling when closed.
- Metrics reset on runtime/model identity change and never mix histories.
- Health alerts are deterministic, comparable-fingerprint gated, deduplicated,
  recoverable, and never triggered merely by high utilization.
- Chat preserves reading position when unpinned throughout streaming and
  completion, and resumes only after a deliberate return to bottom.
- Audio events come only from the explicit list through one service.
- Mute/volume/per-event settings persist through the one settings flow; visual
  equivalents always remain.
- TTS arbitration suppresses cues by default, and playback failure cannot fail
  the original operation.
- No playback path uses a user-controlled shell command string.

## 5.11 Test and live-verification budget

Expected automated coverage: 30-40 tests.

- telemetry identity switch, rolling values, missing/zero/source state, sampling
  start/stop, and bounded history;
- each health condition's threshold, compatibility, duration, dedupe, recovery,
  cooldown, and no-high-utilization warning;
- scroll pin-state transitions and completion growth;
- audio default policy, mute/volume, per-event toggles, dedupe, TTS suppression,
  queue bounds, playback failure, settings round trip, and safe process/API
  invocation;
- tooltip/visual-equivalent guards.

Linux/COSMIC live gate:

1. Leave the telemetry surface visible during normal Chat and confirm it tracks
   the active runtime without UI jank or polling after close.
2. Exercise one safe synthetic health threshold and recovery; confirm one
   notification, no spam, and a useful link.
3. Perform the scroll scenario in 5.6.
4. Listen to every enabled cue at 25%, 100%, mute, and during TTS; confirm visual
   equivalents and clean app shutdown with no orphan player process.

Windows live gate is required for the rewritten safe playback path and the same
mute/TTS behavior. Automated cross-platform tests are not a substitute for
hearing the packaged assets once on each platform.
