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
- Tokens/sec, measured from the provider's own prompt/decode timings when it
	reports them (llama.cpp), or estimated from output length otherwise - each
	result is labeled `server-measured` or `estimated from characters` so the
	two are never confused
- Deterministic quality checks
- Resource deltas (CPU, memory, storage changes) for display; the scoring
	weight for this slot is neutral (reserved, not currently a real signal -
	see Resource Sampling Notes)
- Pass rate, failure count, and weighted rankings
- Run metadata including model identity, backend/runtime, context size, GPU
	layers, thread count, model path, and quantization (all sourced from the
	managed server actually serving a local GGUF model, not app-process values)
- Suite version, case version, scoring profile, and run mode
- Cold-only single-iteration runs, or cold and warm phase attempts when suites
	use repeated iterations per case

Runs are exported to JSON, Markdown, and CSV so the full metadata set can be
reviewed later.

The default action is a one-click benchmark pass. With **Run all suites**
enabled, Aether runs every built-in suite for the selected model. Turning it off
runs only the highlighted suite. Selecting a discovered local GGUF model in the
dropdown only updates a status hint; the managed chat `llama-server` is switched
to that model (restarted if a different model is currently loaded, started if
stopped) only when **Run** is actually clicked, so browsing the dropdown never
triggers a 1-2 minute restart on its own.

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

- Cold runs have no prior KV cache state for the prompt. Since llama-server
	keeps prompt-cache reuse on by default (for fast follow-up chat sends),
	Aether explicitly disables it for each case's first (Cold) iteration so a
	suite run immediately after a previous one cannot get a warm prefill from
	leftover KV state while still being reported as Cold.
- Warm runs are only reported when a suite uses more than one iteration per
	case; those later iterations keep the prompt cache enabled and can reuse
	cached prefill state from the first run.
- Repeated iterations help separate cache-state effects from steady-state speed.

### Built-In Starter Suites

Aether includes starter suites covering:

- Speed smoke tests for latency and throughput
- Instruction following for formatting and direct instruction adherence
- Light reasoning for short local reasoning tasks
- RAG answer style for grounded, citation-friendly responses
- Refusal behaviour for insufficient-context safety checks
- Coding assistant checks for logs, configuration, and safe next steps
- Context pressure checks for medium prompt summarisation and trade-offs
- Code generation checks for structurally plausible function output
- Structured output stress checks for nested JSON and enumerated formats
- Multi-step reasoning checks for intermediate working and final answers
- Aether workflow checks for summary, memory marker, and system prompt tasks
- Hallucination resistance checks for uncertainty on fictional or unverifiable
	claims

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
	structured output, hallucination resistance, tool-call style
- RAG suites: grounded answer style, citation correctness, refusal when
	unsupported, source faithfulness
- Coding suites: patch explanation, small bug diagnosis, config troubleshooting,
	command suggestion safety, structural code generation
- Aether workflow suites: conversation summarisation, memory marker extraction,
	and assistant system prompt generation

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

Resource deltas are sampled before, during, and after a run and shown for
reference. They are best-effort and may miss short-lived spikes on some
platforms. The model itself runs in a separate process (`llama-server`) or on
a remote endpoint, so Aether's own process memory delta says nothing about the
model's actual resource use; the resource term in the weighted ranking score
is therefore always neutral rather than computed from that delta. The before/
after memory and VRAM snapshots are still recorded and exported for manual
inspection.

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
under the Aether data root. Users can export individual runs, delete individual
runs, or clear all saved run history from the workspace after confirmation.

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
- Confirmed clear-history action for saved benchmark runs
- Export all runs into a timestamped folder index or a single zip archive
- Run info dialog: view run-level summary, metrics, and export from a dialog window
- Ranking timeframe filters: view All runs, Latest per model, or Last N runs
- The Rankings tab shows each run's rank and a proportional score bar
  (relative to the top score in the current filter) alongside a Details
  button that opens the same run-info dialog used elsewhere, instead of a
  bare table

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
