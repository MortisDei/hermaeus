# Benchmarks & System Overview

## Benchmarks

The **Benchmarks** workspace runs local prompt suites against selected models and
stores immutable run history under the Aether data root.

### Benchmark Runs

Runs record the following metrics:

- First-token latency
- Total latency
- Approximate tokens/sec
- Deterministic quality checks
- Resource deltas (CPU, memory, storage changes)
- Pass rate and weighted rankings

### Built-In Starter Suites

Aether includes starter suites covering:

- Speed smoke tests
- Instruction following
- Light reasoning
- RAG answer style
- Refusal behavior

### Features

- Saved run history for comparison and trend analysis
- Deterministic quality checks for reproducible assessment
- Rankings and weighted scoring
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

### Storage Tracking

The System Overview displays:

- Total data root size
- Individual database sizes
- Cache footprints
- Model and asset directory sizes

This helps monitor storage usage and plan data cleanup or archival.

## Determinism in Benchmarks

Benchmarks prioritize reproducibility and consistency:

- Identical input prompts produce comparable outputs for quality assessment
- Latency and throughput metrics are stable across runs
- Quality checks are deterministic and version-controlled
- Results can be compared across model versions and hardware configurations
