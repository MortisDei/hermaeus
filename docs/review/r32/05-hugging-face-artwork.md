# 05. Hugging Face model and repository artwork

The Models page currently presents Hugging Face search results as text rows and
file download controls. R32 adds publisher-provided artwork to the repository
selection and relevant download cards without turning model browsing into an
arbitrary remote-image beacon or an image-decoder attack surface.

## 5.1 Source and provenance

Hugging Face documents `thumbnail` as optional model-card metadata for a URL
used in social sharing. The existing `GET /api/models/{repoId}` call already
reads `cardData` and the immutable repository `sha`; acquire only the
string-valued `cardData.thumbnail` from that response, capped at 2,048 UTF-8
bytes. Do not scrape the card Markdown or make a second unpinned card request.
Extend `HfModelCard` with a bounded artwork descriptor:

```text
HfArtworkDescriptor(
  RepoId,
  RevisionSha,
  DeclaredValue,
  RepositoryPath?,
  SourceKind,
  FetchPolicy,
  CacheKey)
```

Source order:

1. a relative thumbnail path, or canonical same-repository Hugging Face resolve
   URL, whose normalized path exists in the selected revision's file tree;
2. a documented Hugging Face-hosted social thumbnail URL that passes the same
   exact-host policy and can be acquired without credentials;
3. the existing local generic model mark.

If `sha` is absent or not a 40-character immutable revision, do not fetch art.
`main` is not an artwork cache identity. For a repository file, build the
request from repo id, immutable sha, and normalized tree path rather than
trusting the declared absolute URL.

Do not scrape Markdown for the first image. It may be a badge, tracking pixel,
diagram, screenshot, external advertisement, or huge asset. Do not infer art
from filenames such as `logo.png` without explicit model-card metadata.

The UI identifies the image as repository artwork and retains repo/revision
provenance in its cache metadata. Artwork never contributes to model identity,
compatibility, trust, ranking, or download selection.

## 5.2 Untrusted URL policy

The metadata value is publisher-controlled. Treat it as hostile input.

- Accept HTTPS only.
- Prefer repo-relative paths resolved through the existing pinned Hugging Face
  revision URL builder. Reject traversal, encoded separators/dot segments,
  fragments, control characters, and paths absent from the selected tree.
- The publisher-declared URL may start only at `huggingface.co`, with the
  canonical repository/revision/path relationship validated above. Do not
  accept `hf.co` merely because it is a shortener, a suffix rule such as
  `*.hf.co`, or a publisher-controlled external host.
- Normalize hostnames and reject user-info, non-default ports, IP literals,
  localhost, private/link-local destinations, and ambiguous Unicode hostnames.
- Disable automatic redirect following for the fetch. Permit at most five
  HTTPS hops. Same-Hub redirects must remain on `huggingface.co`; the terminal
  content hop may use only an exact host in the implementation's reviewed
  delivery-host set. The initial set is the current primary-source list:
  `cas-server.xethub.hf.co`, `cas-server.xethub-eu.hf.co`,
  `transfer.xethub.hf.co`, `transfer.xethub-eu.hf.co`,
  `us.aws.cdn.hf.co`, `us.gcp.cdn.hf.co`, `cdn-lfs-us-1.hf.co`,
  `cdn-lfs-eu-1.hf.co`, plus `cas-bridge.xethub.hf.co` for ordinary
  non-Xet-aware resolve downloads. Delivery hosts may not redirect to another
  origin. Keep this exact set beside tests; changing it requires primary-source
  review rather than a runtime wildcard or remotely mutable allowlist.
  Primary references are Hugging Face's current
  [download/firewall contract](https://huggingface.co/docs/hub/en/models-downloading)
  and its [Xet migration description](https://github.com/huggingface/blog/blob/main/migrating-the-hub-to-xet.md).
- Reject query strings on the publisher-declared value. A validated
  Hugging Face response may redirect to an exact allowlisted CDN/CAS host with
  provider-generated signed query parameters; validate each hop, never persist
  or log that transient URL/query, and key the cache by repo/sha/path/content
  hash instead.
- Never send Local API, provider, or Hugging Face access tokens for artwork.
- Never fetch an arbitrary external `thumbnail` URL. Show the fallback and a
  bounded status explaining that external artwork was not loaded for privacy.

This policy is deliberately stricter than the user-entered RAG web loader. A
thumbnail loads as decoration, not because the user explicitly requested a
specific URL.

## 5.3 Content and decode limits

- Accept PNG, JPEG, and WebP only after both declared MIME and magic bytes
  match. Exclude SVG, GIF/animation, ICO, HTML, XML, PDF, and polyglot content.
- Use response-header streaming and a pooled bounded copy that stops after
  2 MiB. A missing, negative, conflicting, or false `Content-Length`, chunked
  response, decompression, or slow stream does not bypass the cap.
- Before constructing any Avalonia/Skia bitmap, parse bounded PNG IHDR, JPEG
  marker, or WebP VP8/VP8L/VP8X headers; reject animation and compute
  `width * height * 4` with checked arithmetic against dimension, decoded-pixel,
  and decoded-byte caps. Recheck decoded dimensions after construction.
- Strip reliance on metadata profiles and animation frames. Orientation may be
  normalized deterministically.
- A decode failure is a card-local fallback, never a failed model search or
  download.

Use Avalonia's existing bitmap support only after the independent preflight has
made allocation safe. If a correct bounded parser/decoder cannot be implemented
and audited with the existing stack, either drop the unsupported format or add
one narrowly justified, maintained dependency. The no-new-dependency
preference never authorizes decoding untrusted bytes before enforceable limits.

## 5.4 Cache contract

Store artwork under the configured AI assets/data cache policy only after the
location is reviewed against backup/privacy semantics. Do not place it beside
model weights or in the repository checkout.

Cache key includes normalized repo id, exact revision sha, repository path or
stable source kind, and content hash. It never includes a transient signed CDN
URL. A small metadata row/file records MIME, byte count, dimensions, ETag when
supplied, fetched time, and last access. Writes use temp plus atomic move.

Rules:

- Search is manual, so artwork network access starts only after the user opens
  the browser and searches/selects a repository. No startup refresh.
- Cache hits do not contact the network.
- Revision change may fetch a new entry; it does not overwrite evidence for the
  old revision in place.
- Enforce total byte/count limits and least-recently-used eviction. Never evict
  model files or manifests.
- Data Management shows artwork-cache size and a Clear action. Clear is
  confirmed only if consistent with other cache actions and must not affect
  downloaded models.
- Backup excludes rebuildable artwork unless existing cache policy explicitly
  includes equivalent assets. Documentation states the choice.

## 5.5 Search and card presentation

Hugging Face search returns light result metadata but the detailed card fetch
already occurs after repository selection. Avoid 25 simultaneous detail calls
merely to decorate every search row.

Initial presentation:

- search rows retain the generic mark, repository id, downloads, and selection
  behavior;
- selecting a repository immediately publishes its card with a generic mark and
  loading state;
- card metadata, artwork, file tree, companions, and model-fit work run as
  separately cancellable operations under the same selection generation;
- stale results from the previously selected repository cannot replace the
  current card;
- file download cards reuse the selected repository artwork at thumbnail size;
  they do not fetch one image per quantization/file;
- installed model cards may reuse cached repository artwork only when the
  manifest has verified repo and revision provenance. User-selected local
  avatars remain a separate preference and win where currently intended.

Keep text primary. Artwork has fixed bounds, no layout jump after load, a
fallback, accessible label, tooltip naming its repository origin, and no
meaning conveyed only by color/image.

## 5.6 Failure and privacy UX

Card-local states are `Loading`, `Available`, `NoDeclaredArtwork`,
`ExternalBlocked`, `Invalid`, and `Unavailable`. Most render the same fallback;
the distinction appears in a restrained tooltip/detail, not a wall of warnings.

Never include the full remote URL or query string in ordinary runtime logs.
Log repo id, revision, stable failure code, host, and bounded exception type.
Redaction still applies.

The first manual search can explain that Hermaeus contacts Hugging Face for
repository metadata and artwork. Existing anonymous/manual network semantics
remain. Artwork does not cause traffic when the browser is closed.

## 5.7 Acceptance criteria

- A valid repo-relative thumbnail appears on the selected repository and its
  download cards, keyed to the exact revision.
- Missing, external, oversized, redirected-to-disallowed-host, wrong-MIME,
  malformed, animated, SVG, and decode-bomb candidates fall back safely.
- Missing/invalid immutable sha or a thumbnail path absent from the exact tree
  never starts an artwork fetch.
- No artwork request carries app/provider secrets.
- Cancellation/rapid selection cannot display the wrong repository's image.
- Cache hit is offline, cache clear leaves model/manifest files unchanged, and
  cache bounds are enforced.
- Search/download continues when artwork fails.
- Installed cards use artwork only with verified manifest provenance; local
  profile avatar semantics remain intact.

## 5.8 Test and live-verification budget

Expected automated coverage: 20-25 tests.

- metadata parsing, relative resolution, revision pinning, and fallback order;
- declared-origin, exact delivery-host, scheme/port/IP/user-info/redirect
  validation, including authorization stripping and signed-query redaction;
- byte, MIME, magic, dimension, pixel, and format limits;
- truncated headers, integer overflow, decompression/chunked-limit behavior,
  oversized dimensions before decoder construction, and post-decode mismatch;
- cache key, atomic write, hit, eviction, clear, and data-root switch;
- cancellation and stale-selection generation;
- cancellation during headers/body/decode/cache move, redirect loops, timeout,
  corrupt cache, eviction races, disk-full/temp cleanup, and app shutdown;
- UI fallback/layout/accessibility guards and no coupling to download success.

Owner live gates on Linux/COSMIC and Windows:

- valid, missing, blocked-external, and malformed thumbnail repositories;
- slow/offline artwork while file metadata and downloads remain usable;
- cache survives restart and Clear removes only artwork;
- narrow-window and high-DPI card layout with keyboard selection and screen
  reader/tooltip text.
