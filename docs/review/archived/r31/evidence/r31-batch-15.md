# R31 Batch 15 evidence

Checked: 2026-08-26 against primary upstream sources. This is a research/watch
result, not a claim that the local selected runtime has these capabilities.

| Area | Result | Decision |
| --- | --- | --- |
| Release channel | `v0.3.0` was the latest upstream release; `b10630` was a prerelease. | Keep update-channel semantics explicit and do not invent a Stable channel. |
| GLM MTP | The `v0.3.0` notes mention GLM-4.5-Air MTP. | Generic MTP help and metadata do not prove a selected model/runtime pair. Keep `Unknown` until direct engagement evidence. |
| Reconfiguration | Upstream reconfiguration remains a feedback discussion. | Watch. No runtime mutation API in R31. |
| Public speculative API | Public `llama.h` speculative exposure remains an open request. | Watch. No native binding dependency. |
| DFlash | Upstream documents `draft-dflash`, target-specific conversion, trained block-size limits, and target-state injection. | Keep conditional registry support, no production adapter or settings surface. |
| MoE prefetch/streaming | Discussion-level work, not a stable upstream contract. | Watch. `--n-cpu-moe` remains placement only. |
| Reconstructable KV | Research paper evidence exists, but no upstream production contract was found. | Watch. No local KV reimplementation. |

Primary sources:

- <https://github.com/ggml-org/llama.cpp/releases>
- <https://github.com/ggml-org/llama.cpp/blob/master/docs/speculative.md>
- <https://github.com/ggml-org/llama.cpp/issues/27469>
- <https://github.com/ggml-org/llama.cpp/discussions/25674>
- <https://github.com/ggml-org/llama.cpp/discussions/18758>
- <https://arxiv.org/abs/2603.19664>
