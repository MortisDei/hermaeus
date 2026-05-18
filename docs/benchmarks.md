# Benchmarks & System Overview

## Benchmarks

The **Benchmarks** workspace runs local prompt suites against selected models and
stores immutable run history under the Aether data root. It discovers running
provider models and GGUF files under the configured AI assets root so users do
not need to paste each local model path by hand.

Benchmarks are intended for practical local model comparison, not lab-grade
hardware benchmarking.

### Benchmark Runs

Runs record the following metrics and metadata:

- First-token latency
- Total latency
- Approximate tokens/sec
- Deterministic quality checks
- Resource deltas (CPU, memory, storage changes)
- Pass rate, failure count, and weighted rankings
- Run metadata including model identity, backend/runtime, context size,
	sampler settings, app version, OS, CPU, RAM, and GPU summary
- Suite version, case version, scoring profile, and run mode
- Cold and warm phase attempts where possible, with repeated iterations per case

Runs are exported to JSON, Markdown, and CSV so the full metadata set can be
reviewed later.

The default action is a one-click benchmark pass. With **Run all suites**
enabled, Aether runs every built-in suite for the selected model. Turning it off
runs only the highlighted suite. Selecting a discovered local GGUF configures
the managed chat `llama-server`, restarts it when the model changes, and starts
it when it is stopped.

### Reproducibility in Benchmarks

Benchmarks are designed for repeatable, practical comparison across models,
runtimes, and hardware profiles.

Aether records run metadata such as model identity, quantisation, runtime,
sampler settings, context size, hardware summary, app version, and suite
version so results can be compared fairly over time.

Quality checks are deterministic where possible and are versioned with the
benchmark suites. Performance metrics such as latency and throughput are
measured observations and may vary due to system load, model warmup, cache
state, GPU clocks, thermals, and runtime behaviour.

For more stable results, use repeated runs and compare median values rather
than single-run measurements.

### Cold and Warm Runs

Benchmarks distinguish cold-start measurements from warm-run measurements where
possible.

- Cold runs include model load or startup overhead.
- Warm runs measure already-loaded model behaviour.
- Repeated iterations help separate startup noise from steady-state speed.

### Built-In Starter Suites

Aether includes starter suites covering:

- Speed smoke tests for latency and throughput
- Instruction following for formatting and direct instruction adherence
- Light reasoning for short local reasoning tasks
- RAG answer style for grounded, citation-friendly responses
- Refusal behaviour for insufficient-context safety checks
- Coding assistant checks for logs, configuration, and safe next steps
- Context pressure checks for medium prompt summarisation and trade-offs

### Suite Versioning

Suites and cases carry version identifiers so historical runs remain meaningful
after prompts, scoring profiles, or evaluation rules change.

Example:

```json
{
	"suiteId": "instruction-following",
	"suiteVersion": "1.2.0",
	"caseId": "json-only-response",
	"caseVersion": "1.0.1",
	"expectedBehaviourVersion": "1.0.1",
	"scoringProfile": "coding-helper-v1"
}
```

### Failure Tracking

Benchmark runs record failures explicitly rather than skipping them. Common
failure categories include:

- timeout
- model load failure
- connection failure
- empty response
- malformed response
- quality check failed
- refusal mismatch
- context overflow
- OOM or VRAM failure
- cancelled by user

These categories help distinguish model quality issues from infrastructure or
resource problems.

### Iterations and Summary Stats

One run is a data point. Several runs are a benchmark.

Each case can run multiple iterations. The benchmark UI reports summary stats
such as:

- Median first-token latency
- Min and max latency
- P95 latency
- Median tokens/sec
- Standard deviation
- Failure count
- Pass rate

The UI keeps the display simple while preserving the raw iteration results for
inspection and export.

### Benchmark Categories

The starter suites are organised by purpose:

- Performance suites: first token, throughput, long context, memory pressure
- Behaviour suites: instruction following, refusal behaviour, formatting,
	tool-call style
- RAG suites: grounded answer style, citation correctness, refusal when
	unsupported, source faithfulness
- Coding suites: patch explanation, small bug diagnosis, config troubleshooting,
	command suggestion safety

For Aether specifically, a local dev assistant suite should also cover log,
config, and code excerpt analysis with safe next-step suggestions and no
invented APIs.

### Ranking Profiles

Different users care about different trade-offs, so benchmark rankings should
use named profiles instead of one universal score.

- Fast Chat: speed weighted high, quality moderate
- Coding Helper: instruction following and factuality weighted high
- RAG Answering: citation correctness and grounding weighted high
- Low VRAM: memory usage and stability weighted high

### Baseline Comparison

Every benchmark run should be compared against a chosen baseline model so the
result is easy to interpret.

Example:

- Baseline: `Mistral 7B Q4_K_M`
- Candidate: `Phi-4 mini Q4_K_M`
- First token: 12% slower
- Tokens/sec: 18% faster
- Pass rate: 7% better
- Memory: 800 MB higher

### Resource Sampling Notes

Resource deltas are sampled before, during, and after a run. They are best-effort
and may miss short-lived spikes on some platforms.

For GPU-capable systems, record:

- VRAM before
- VRAM peak if available
- VRAM after
- GPU utilisation if available
- GPU name
- Probe method
- Probe error if unavailable

### Benchmark Data Retention

Benchmark run history stores prompts, outputs, scores, and runtime metadata
under the Aether data root. Users can export, delete, or archive benchmark
history from the workspace.

When benchmarking a remote provider, prompt content is sent to that configured
provider.

### Features

- Saved run history for comparison and trend analysis
- Deterministic quality checks for reproducible assessment where possible
- Rankings and weighted scoring profiles
- One-click full-suite benchmark runs
- Local GGUF discovery from the AI assets root
- Managed `llama-server` model switching for discovered local GGUF files
- Reruns for validation
- Export to Markdown, JSON, and CSV formats

## System Overview

The **System Overview** page shows the local machine and app environment:

- App version
- Operating system
- CPU information
- RAM (total and available)
- Process memory usage
- Data-root storage usage
- Database footprint breakdown
- Managed component status (llama-server, XTTS, etc.)
- Best-effort GPU/VRAM visibility

### GPU Detection

- NVIDIA systems use `nvidia-smi` when available
- Other GPU probes degrade gracefully with informative messages
- Manual GPU configuration available in Settings and Services

### llama.cpp Tuning and Updates

The Services auto-tune action now probes descending GPU layer candidates and
keeps the highest candidate that starts and reaches `/health`, with CPU fallback
as the final candidate. Successful tune results are saved per GGUF model file
with model size and modified-time metadata. When that model is selected again,
Aether reapplies the saved GPU layer, thread, context, and extra-argument
profile before starting the managed server.

Doctor alerts when local GGUF models do not have matching tuned profiles. It
also checks the configured `llama-server` binary version and, when GitHub
releases are reachable, compares it with the latest `llama.cpp` release. If an
update is available, Doctor can download the matching platform asset, replace
the older managed binary, and update the managed server paths. If the update
check cannot reach GitHub, Doctor reports the local version it could read and
keeps the scan usable offline.

Doctor also verifies the pinned `nomic-embed-text-v1.5` embedding GGUF by
SHA256 when a nomic embedding model is installed. If the file does not match
the pinned artifact, Doctor offers the same verified embedding-model install
action used by first-run setup.

### Storage Tracking

The System Overview displays:

- Total data root size
- Individual database sizes
- Cache footprints
- Model and asset directory sizes

This helps monitor storage usage and plan data cleanup or archival.
