# R31 Batch 18: final Beta dogfood UX and upstream audit

Checked 2026-08-27 on `r31/round`. This close-out adds no authority and no
runtime capability claim.

## Completed UX closure

- The DI-shared voice settings model refreshes authoritative provider voices on
  normal settings load and provider change. Channel selectors retain the global
  default sentinel and any explicit typed voice ID. Loading, unavailable, and
  retry states name the selected provider instead of implying a populated list.
- Agent's Run tab states the normal approval-gated path and a task-derived next
  action. The existing pinned decision strip, response, run outcome, Changes,
  Workspace, and History remain the detailed review surfaces.

## b10635 to b10642 audit

The exact local read-only upstream comparison was `fc35562` (b10635) to
`925e117` (b10642), six commits. It contains KV cell token ID tracking, RPC
async/backend APIs, Vulkan cross-entropy kernels, and upstream UI/CI changes.
None is a stable llama-server command-line, HTTP, telemetry, or capability
contract consumed by Hermaeus. KV tokens are deferred as internal cache work;
RPC and Vulkan work have no managed-server path impact; UI/CI changes are
already outside the product boundary. No code integration was required.

The owner's successful update, model/mmproj load, 98k context, and embeddings
are live observations, not an automated product claim. Unsupported cache reuse
and KV shift remain self-disabling. The experimental audio warning and pruning
message are expected upstream/runtime behaviour.

## Remaining live gates

Run the packaged Windows and Linux/COSMIC routes with a real provider and a
long stream. This batch does not claim either result from unit, harness, build,
or coverage evidence.
