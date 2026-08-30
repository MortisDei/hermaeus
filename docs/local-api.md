# Local API

The optional Local API is a loopback-only HTTP surface for local tools such as
editor extensions and scripts. It is off by default and is not a replacement
for the desktop UI or a remote service.

## Availability and host

Enable it in Settings > Local API. Hermaeus starts the host only when it is
enabled and at least one named token exists. The host independently refuses to
serve when the setting is off or no token exists. It binds to `127.0.0.1` and
does not bind to `0.0.0.0`.

Each caller has a named token stored through the secret store. Tokens can be
revoked individually. Saving a token or port change persists the settings,
restarts the owned child, waits for its health check, and only then reports the
new runtime state. Every route except `GET /health` requires the matching
`X-Hermaeus-Token` header. `X-Hermaeus-Client` is an optional display hint and
is not an authentication or authorization signal.

The host reports status in Settings. Every authenticated request is recorded
in the shared local trace store with the verified token name. The optional
client hint is recorded separately as unverified text so Privacy Audit can
show which named callers have used the surface.

## Current routes

| Method | Route | Behavior |
| --- | --- | --- |
| `GET` | `/health` | Unauthenticated process health check returning `ok`. |
| `POST` | `/v1/chat/completions` | Buffered JSON by default, or Server-Sent Events when `stream` is true. Supports the desktop sampling parameters and reasoning content. |
| `POST` | `/v1/embeddings` | Embeds a non-empty array of input strings with the configured embedding provider. |
| `GET` | `/v1/memory/query` | Searches local Memories with a bounded result limit. |
| `POST` | `/v1/rag/query` | Queries a selected RAG dataset and returns the answer plus source metadata. |
| `GET` | `/v1/models` | Returns visible models and their available context lengths. |
| `GET` | `/v1/capabilities` | Reports usable features and routes without loading a model, starting a server, making a network call, or running an embedding pass. |

Chat sampling precedence is explicit: a request value wins, then the selected
model's saved profile default, then the global LLM setting, then the provider's
own default when no value is supplied. The streamed response uses the
`chat.completion.chunk` wire shape for compatibility with existing clients;
this does not add a dependency on OpenAI.

Capabilities report counts and readiness reasons without naming local paths,
keys, tokens, or dataset names. A failed store read or unavailable dependency
is reported as unavailable for that feature, not hidden behind a successful
health response.

## Agent boundary

The Local API includes a versioned Agent DTO and pure authorization policy, but
it does not expose Agent execution routes. Desktop owns the active Agent
service and file-backed task state, while the Local API is a separate process.
Until one owner serializes task mutation, approvals, steering, cancellation,
and restart recovery, mapping those routes would permit competing services to
race the same task files.

See the [Agent Local API contract](agent-api.md) for the conditional route
surface, per-token scope, and ownership gate. A token, repeated fingerprint,
steering instruction, or continue request is never approval, and there is no
Local API approval or denial route.

## Data and security boundaries

- The API is local-only, but any configured local caller that learns a valid
  token can use that token until it is revoked.
- Chat, Memory, RAG, and embedding requests may send content to a remote
  provider if the corresponding Hermaeus provider is remote. The desktop's
  Privacy Audit remains the place to review that route.
- Responses do not expose workspace roots, data-root paths, raw commands,
  secrets, or logs.
- Request tracing is best effort and never makes a successful API operation
  fail.
