# Agent Local API contract

Status: design contract and authorization policy only. Agent execution routes
are not exposed. `Hermaeus.LocalApi` is a separate process with its own
dependency-injection root, while Desktop owns the active `AgentService` and
file-backed task state. Running another independent Agent service against the
same `task_state.json` files would not serialize runs, approvals, steering, or
cancellation. Atomic file replacement does not solve that ownership race.

`GET /v1/capabilities` therefore reports `agent` as unusable with the reason:

> Agent execution is unavailable because Desktop and Local API do not share a
> single task-mutation owner.

The conditional routes are not mapped or advertised. This is a deliberate
security boundary, not a runtime probe failure.

## Version 1 contract

`AgentApiContract.SchemaVersion` is `1`. The reviewed conditional surface is:

- `POST /v1/agent/tasks`: create a New task from a saved workspace profile,
  allowed visible model, and optional allowed Project. Creation does not start
  the task.
- `POST /v1/agent/tasks/{id}/start`: start a run and return a run id. Long work
  returns an accepted status and is observed through the run resource.
- `GET /v1/agent/tasks/{id}` and `GET /v1/agent/runs/{runId}`: return status,
  normalized outcome, current step, pending-decision summary, and safe relative
  links.
- `POST /v1/agent/tasks/{id}/steer`: enqueue bounded user text with the same
  authority as Desktop steering. It is never approval.
- `POST /v1/agent/tasks/{id}/continue`: continue only when no pending decision
  or unanswered user question would be bypassed.
- `GET /v1/agent/tasks/{id}/output`: return the report, reservations,
  provenance references, and path-safe artifact metadata.
- `GET /v1/agent/tasks/{id}/decisions`: return read-only opaque decision ids,
  fingerprints, risk, reason, and `desktopReviewRequired: true`.

There is no approval or denial route. A token, a repeated fingerprint, steering
text, and a continue request carry no approval authority.

The v1 request and response records live in `Hermaeus.LocalApi.LocalApiModels`.
They use saved identifiers instead of arbitrary workspace paths. Responses do
not contain workspace roots, data-root paths, raw commands, secrets, or logs.

## Per-token Agent scope

Every named Local API token has an additive `LocalApiAgentScope`. Older settings
deserialize to schema version 1 with Agent disabled, no operations, no saved
workspace profiles, no model or Project allowlists, no cross-owner reads, and a
one-run concurrency ceiling. Migration therefore grants nothing.

The scope binds:

- an explicit operation allowlist;
- one or more existing saved workspace profile ids;
- an optional exact visible-model allowlist, where an empty list adds no model
  restriction beyond current visibility;
- an exact Project allowlist for any requested Project;
- whether read-only access may include tasks owned by another token; and
- a bounded per-token concurrency limit from one to four runs.

Task ownership uses the verified token id, not the token's display name or the
untrusted `X-Hermaeus-Client` header. Another token's task is reported as not
found unless Desktop explicitly granted broader read-only access. That broader
read flag never grants start, steering, continue, or approval authority.

`AgentApiPolicy` is side-effect free. A future HTTP adapter must first resolve
the supplied ids through the saved workspace, Project, and currently visible
model stores, then pass those verified facts to the policy. It rejects disabled
or unknown scope versions, undeclared operations, arbitrary or removed ids,
owner mismatches, excess concurrency, oversized input, and any start or continue
that would cross a Desktop decision or unanswered question.

## Ownership gate for future execution

Execution routes can ship only after one service owns every mutation for a task,
serializes per-task work, exposes scoped cancellation, and recovers run ownership
after restart. Viable designs are a local authenticated coordinator shared by
Desktop and Local API, or a single Agent host that Desktop calls. Adding route
handlers to today's independent Local API process is not viable.

When that owner exists, scope changes must be explicit Desktop settings actions
through the existing named-token and secret-store flow. Revocation must affect
the next request. The current scope object is inert and has no UI editor because
the corresponding execution surface is unavailable.

There is no live Agent-client verification path because no execution endpoint
is exposed. Automated tests pin the policy, DTO version, absence of approval
authority, absence of mapped Agent routes, and capability reason.
