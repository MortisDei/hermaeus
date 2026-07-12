# Review Round 5 (r5) - July 2026

Fifth review round. r1-r4 are fully actioned (see `archived/`). r4
shipped in `0.9.44-alpha` (commit d7fb646). r5 is a feature round, not
an audit round: it promotes two subsystems that already have solid
plumbing into first-class capabilities.

**Theme 1: voice stops being "TTS for chat" and becomes a participant
in the ecosystem.** Today `ITtsService.SpeakAsync` has exactly one real
consumer (`ChatViewModel.SpeakMessageAsync`) and no notion of who is
speaking, how urgent it is, or which voice to use. Meanwhile every
provider already implements `GenerateSpeechAsync(VoiceSynthesisRequest)`
with per-call voice override and `PlayAudio` control, so the provider
architecture needs no changes at all. What is missing is a single
orchestrator that owns playback: a queue with priorities, per-channel
voice profiles, and consumers (chat replies, agent narration, doctor
warnings, benchmark completion, notification bridge, streaming speech).

**Theme 2: benchmarks stop being a record and become a recommendation
engine.** The store already captures quality, speed, stability,
resource scores, hardware snapshots, and quantization metadata per run.
The engine that turns 84 runs into "for coding on your RTX 4060, these
three models give the best quality per second" is pure deterministic
aggregation over data we already persist. One schema gap blocks it:
case tags never reach `BenchmarkResult`, so task-category grouping
cannot be computed from stored runs.

Read in order:

1. [Voice Orchestration](01-voice-orchestration.md) - the
   `IVoiceOrchestrator` queue, voice profiles, per-channel settings,
   and the six consumers including experimental streaming speech.
2. [Benchmark Insights](02-benchmark-insights.md) - tag propagation,
   the deterministic `BenchmarkInsightsService` (aggregates, pairwise
   comparisons, recommendations with confidence), and the UI panel.
3. [Roadmap](03-roadmap.md) - sequencing, test requirements, coverage
   ratchet, and explicit rejections.

Standing rules apply: deterministic-first (no LLM in the insights
engine, no LLM deciding what gets spoken), everything audible is
opt-in except the existing manual speak button, the agent approval
gate is untouched, zero-warning build, no em dashes anywhere.

**Status: fully actioned as of `0.10.0-alpha`.** Every item in docs
01-02 landed: `IVoiceOrchestrator` with priority queue/preemption/dedupe,
voice profiles and per-channel settings (`TtsSettings.Profiles`/`Channels`),
all six voice consumers (chat auto-speak + experimental streaming speech,
agent milestone narration with per-workspace voice profiles, Doctor
critical-error summary, benchmark completion, toast-to-voice bridge),
`BenchmarkResult.Tags` propagation with suite-join fallback for older
runs, the deterministic `BenchmarkInsightsService`/`BenchmarkInsightsMath`
engine, the Benchmarks page Insights tab, and the Info-only Doctor
advisory check. 55 new tests (327->382), coverage floor raised 45->47.
Committed on `main`, `0.10.0-alpha`.
