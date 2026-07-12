# 01 - Voice Orchestration

Goal: one service owns everything Aether says out loud. Consumers
declare *what* to say, *which channel* it belongs to, and *how urgent*
it is; the orchestrator decides voice, ordering, preemption, and
playback. No provider changes are required: every provider already
supports per-call voice override via
`GenerateSpeechAsync(VoiceSynthesisRequest)`
(`src/Aether.Core/Models/VoiceProviderModels.cs:120`), which the
current `SpeakAsync` path ignores (e.g.
`src/Aether.Voice/NativeKokoroVoiceProvider.cs:140` always reads
`Tts.Speaker` from settings).

## Current state (verified)

- `ITtsService` (`src/Aether.Core/Services/ITtsService.cs:5`) is
  fire-and-forget `SpeakAsync(text)`. Only real consumer:
  `ChatViewModel.SpeakMessageAsync`
  (`src/Aether.ViewModels/ChatViewModel.cs:522-550`), manual per-message
  with a single `_ttsCts`.
- `VoiceRoutingTtsService`
  (`src/Aether.Services/VoiceRoutingTtsService.cs:14`) forwards to
  `IVoiceProviderRegistry.GetActiveTtsService()`.
- Playback is per-caller: each `SpeakAsync` renders a wav and plays it.
  Synthesis is gated per provider (`_synthesisGate`) but playback is
  not, so two concurrent callers overlap audio. Centralized queueing
  fixes this class of bug for free.
- Toasts already flow through one event:
  `IToastService.ToastRaised`
  (`src/Aether.Core/Services/IToastService.cs:13`).
- Chat streaming exists: `ILlmService.StreamChatTextAsync`
  (`src/Aether.Core/Services/ILlmService.cs:81`).
- Agent workspace defaults persist to `.aether/workspace.json`
  (`src/Aether.ViewModels/AgentViewModel.cs:956`).

## Items

### 1.1 IVoiceOrchestrator core

New interface in `Aether.Core/Services`, implementation
`VoiceOrchestrator` in `Aether.Services`, registered singleton in
`AetherServiceRegistration` next to the `ITtsService` registration
(`src/Aether.Composition/AetherServiceRegistration.cs:67`).

```csharp
public enum VoiceChannel { Chat, Agent, Doctor, Benchmark, Notification, System }
public enum VoicePriority { Low, Normal, Critical }

public sealed record VoiceUtterance(
    string Text,
    VoiceChannel Channel,
    VoicePriority Priority = VoicePriority.Normal,
    string? VoiceOverride = null,
    string? DedupeKey = null);

public interface IVoiceOrchestrator
{
    Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default);
    void StopChannel(VoiceChannel channel);
    void StopAll();
    bool IsMuted { get; set; }
    event Action<VoiceChannel, string>? UtteranceStarted;
}
```

Behavior:

- Single background worker drains a queue; exactly one utterance plays
  at a time. Playback goes through the active provider's
  `GenerateSpeechAsync` with `Voice` resolved from the channel's
  profile (item 1.2), `PlayAudio: true`.
- `Critical` preempts: cancels current playback, clears queued `Low`
  items, plays immediately. `Low` items are dropped (not queued) if the
  queue already holds 3 or more items. `Normal` is FIFO.
- `DedupeKey`: if a queued (not yet played) utterance has the same key,
  the new one is dropped. Prevents double-speak when a consumer and the
  notification bridge (item 1.6) both announce the same event.
- Channel disabled in settings, or `IsMuted`, or `Tts.Enabled` false:
  `EnqueueAsync` returns without queueing (never throws; a UI channel
  toggle must not turn speech calls into error paths).
- Synthesis failures are logged and toast-surfaced at most once per
  session per provider, then silently dropped; a broken TTS install
  must not spam toasts from background narration.
- `StopAll` cancels current playback and clears the queue; it is the
  handler for a global mute action.

Acceptance criteria:

- Two concurrent `EnqueueAsync` calls never overlap audio (test with a
  fake provider recording playback intervals).
- Critical preempts a playing Normal; ordering test: enqueue N1 N2 C1,
  observed playback starts N1 (cancelled), C1, N2.
- Dedupe drops the second of two same-key queued utterances.
- Muted/disabled channels produce zero provider calls.

### 1.2 Voice profiles and per-channel settings

Extend `TtsSettings` (`src/Aether.Core/Models/TtsSettings.cs`):

```csharp
public sealed class VoiceProfile
{
    public string Name { get; set; } = "";
    public string VoiceId { get; set; } = "";   // provider voice, e.g. "af_heart"
    public double? Speed { get; set; }           // null = provider default
}

public sealed class VoiceChannelConfig
{
    public bool Enabled { get; set; }
    public string ProfileName { get; set; } = ""; // empty = default voice
}

// on TtsSettings:
public List<VoiceProfile> Profiles { get; set; } = [];
public Dictionary<string, VoiceChannelConfig> Channels { get; set; } = [];
```

- Defaults: `Chat` enabled (preserves today's manual speak button),
  every other channel disabled. Missing dictionary entries mean
  "default config for that channel", so existing settings files load
  unchanged (additive JSON, no settings migration needed - verify a
  pre-r5 settings file round-trips).
- Profile resolution: channel config -> named profile -> `VoiceId`
  passed as `VoiceOverride`; fall back to `Tts.Speaker` when the
  profile or voice is missing. Unknown voice ids already fall back
  safely in providers (`NativeKokoroVoiceProvider.NormalizeVoice`,
  `src/Aether.Voice/NativeKokoroVoiceProvider.cs:213`).
- TTS settings UI (`TtsSettingsViewModel`) gains: profile list editor
  (name, voice picker from `GetVoicesAsync`, speed), per-channel
  enable + profile dropdown, and a global mute toggle bound to
  `IVoiceOrchestrator.IsMuted`. Keep it one section; this page is
  already dense.

Acceptance criteria:

- Channel with profile X speaks with X's voice id (assert on the
  `VoiceSynthesisRequest.Voice` the fake provider receives).
- Deleting a profile that a channel references degrades to default
  voice, no exception.
- Pre-r5 `settings.json` without the new fields deserializes with
  Chat enabled and other channels disabled.

### 1.3 Chat consumer: speak replies

- Migrate `SpeakMessageAsync`
  (`src/Aether.ViewModels/ChatViewModel.cs:522`) to
  `IVoiceOrchestrator` (Channel Chat, Normal priority). Replace the
  local `_ttsCts` dance with `StopChannel(VoiceChannel.Chat)` before
  enqueueing.
- New setting `Tts.AutoSpeakChatReplies` (bool, default false): when
  on, each completed assistant message is enqueued automatically.
  Hook the point where the assistant message is finalized, not the
  streaming loop. Strip markdown/code fences before speaking: speak
  prose, announce code blocks as "code block omitted" (small
  deterministic sanitizer, unit-tested; it also serves item 1.8).

Acceptance criteria:

- Manual speak button behavior unchanged from the user's view.
- Auto-speak off by default; when on, one utterance per assistant
  message, none for user messages or errors.
- Sanitizer: fenced code blocks and inline backticks never reach the
  provider.

### 1.4 Agent narration

- `AgentViewModel` (drives the loop via `AgentService.RunAsync`,
  `src/Aether.Agent/Services/AgentService.cs:464`) narrates milestones
  on the Agent channel, Normal priority: task started, waiting for
  approval (Critical - the run is blocked on the user), waiting for
  user reply (Critical), task terminal states Completed/Failed with a
  one-line reason.
- Do NOT narrate individual steps or tool calls; long runs would be
  unbearable. Milestones only, and use `DedupeKey` = taskId +
  milestone so retries do not re-announce.
- Narration text is built from existing state (status + step count),
  never from model output verbatim: model text can be arbitrarily long
  and is not trusted for audio. One sentence max.

Acceptance criteria:

- A run that ends WaitingForApproval produces exactly one Critical
  utterance; approving and finishing produces exactly one terminal
  utterance.
- Channel disabled (the default): zero utterances, zero behavior
  change to the agent loop. The approval gate itself is untouched.

### 1.5 Doctor critical warnings

- After a doctor run completes (`DoctorService`,
  `src/Aether.Services/DoctorService.cs:15`), speak a single summary
  utterance on the Doctor channel only when at least one check has
  `DoctorCheckStatus.Error` (`src/Aether.Core/Models/DoctorModels.cs:6`):
  "Doctor found N critical issues: <first check title>...". Critical
  priority. One utterance per run, never per check.

Acceptance criteria:

- Run with only Warning/Ready/Info: silent.
- Run with 2 Errors: one utterance naming the count.

### 1.6 Benchmark completion and notification bridge

- Benchmark: when a run reaches terminal state in
  `BenchmarkService.RunAsync` (save paths at
  `src/Aether.Services/BenchmarkService.cs:168-187`), the ViewModel
  enqueues on the Benchmark channel: "Benchmark <suite> on <model>
  complete: <passed> of <total> passed." Cancelled runs say cancelled.
  Keep the announcement in the ViewModel layer; `BenchmarkService` has
  no UI dependencies today and should stay that way.
- Notification bridge: new small service subscribing to
  `IToastService.ToastRaised`, enqueueing Warning toasts as Normal and
  Error toasts as Critical on the Notification channel, `DedupeKey` =
  title + message. Info/Success toasts are never spoken. Registered in
  composition, active only when the Notification channel is enabled.

Acceptance criteria:

- Bridge speaks Error toast once even if the same toast fires twice
  within the queue window.
- A consumer that both toasts and enqueues with the same DedupeKey
  yields one utterance.

### 1.7 Workspace voice profiles

- The agent workspace profile persisted to `.aether/workspace.json`
  (`src/Aether.ViewModels/AgentViewModel.cs:956`) gains an optional
  `VoiceProfileName`. When set and the Agent channel is enabled,
  narration for tasks in that workspace uses that profile (passed as
  `VoiceOverride`), letting different projects have recognizably
  different narrators.
- Unknown profile name degrades to the channel default. Field is
  optional in JSON; existing workspace files load unchanged.

Acceptance criteria:

- Workspace with profile "deep" narrates with deep's voice id;
  workspace without the field uses the Agent channel profile.

### 1.8 Streaming speech (experimental)

- Setting `Tts.StreamingChatSpeech` (bool, default false, labelled
  experimental, only meaningful when `AutoSpeakChatReplies` is on).
- While the LLM streams, a sentence chunker accumulates tokens and
  enqueues complete sentences (terminator `.` `!` `?` followed by
  whitespace, minimum 60 chars per chunk to avoid staccato, flush
  remainder on completion). Each chunk is a Chat-channel Normal
  utterance; the orchestrator's serialized queue provides ordering,
  and synthesis of chunk N+1 can overlap playback of chunk N because
  synthesis and playback are separate stages (this is the whole win).
- Stopping generation calls `StopChannel(Chat)`: no orphaned speech
  after the user hits stop.
- The chunker is a pure `Aether.Core` class, fully unit-tested
  (sentence boundaries, abbreviation false-positives are acceptable
  v1, code fences suppressed via the 1.3 sanitizer, remainder flush).
- Implementation note for synthesis/playback overlap: orchestrator
  renders with `PlayAudio: false` + `OutputPath` into the scratch
  temp dir, plays the file itself (reuse `KokoroAudioPlayback` style
  playback via the provider result), deletes after playback. If a
  provider returns no file path, fall back to `PlayAudio: true`
  serial mode. Do not build a mixer; one audio stream at a time.

Acceptance criteria:

- Chunker: given a token stream, emitted chunks concatenate to the
  original text (minus sanitized regions), each chunk >= 60 chars
  except the final flush.
- Cancel mid-stream: no further utterances play.
- Feature off: streaming path adds zero overhead (no chunker
  allocation).

## Explicitly out of scope for this doc

See roadmap rejections: no STT/wake word, no audio ducking or mixing,
no per-message voice picker in chat, no speech for Info toasts.
