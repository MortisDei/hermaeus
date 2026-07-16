# 05 - Roadmap

## Version

Implementing this pack ships **0.16.0-alpha**. Version bump lives only
in `Directory.Build.props`, per r8 convention.

## Sequencing

1. **1.3 (executable resolution)** first: smallest fix with the largest
   field impact (default Windows config becomes startable; root cause
   of the r10 dead-Embeddings-server follow-up), and 1.1/1.2 want the
   shared resolver in place to verify their output.
2. **1.1 + 1.2 (installer rebuild)** next, as one unit: real asset
   names, zip extraction with the zip-slip guard, shared by pinned and
   latest paths; unblocks Doctor's fix action.
3. **Doc 02 in item order.** 2.1 (Ollama streaming) and 2.2 are
   independent quick fixes; 2.4 depends on nothing but touches routing,
   so land it with tests before 2.5; 2.6 (judge removal) is
   mechanical.
4. **Doc 03 in item order.** 3.1 (migration manifest) before 3.6 so
   backup consumes the same manifest.
5. **Doc 04 in item order**, then remaining doc 01 items (1.4-1.8).

## Test expectations

Rough guide, not a quota: 1.1 (3, incl. zip-slip), 1.2 (3), 1.3 (4),
1.4 (2), 1.5 (1), 1.6 (2), 1.7 (3), 1.8 (1), 2.1 (2), 2.2 (1), 2.3 (2),
2.4 (3), 2.5 (1), 2.6 (1), 2.7 (1), 2.8 (1), 3.1 (3), 3.2 (1), 3.3 (1),
3.4 (2), 3.5 (1), 3.6 (1), 3.7 (1), 4.1 (1), 4.2 (1), 4.3 (1), 4.4-4.6
(2). Expect roughly 45-50 new tests, from 589. All tests run without a
live llama-server, Ollama, network, or audio device: HTTP boundaries
use fake handlers, downloads use zip/file fixtures (including a
captured GitHub release-JSON fixture for asset selection), process
launches use the existing seams, playback asserts selection logic only.

## Docs touch

`docs/features.md`: remove/adjust any judge wording (2.6), note Ollama
streaming, note the installer actually installing. `CHANGELOG.md` per
the standing rule. `docs/security-review.md` gains an r11 subsection
(below). Settings/Services tooltip for ExtraArgs quoting rules (1.4).

## Security review touch

- 1.1/1.2 rework the only self-updating binary download in the app:
  document the zip-slip guard, the pinned-tag provenance decision
  (hash-pinned vs HTTPS+GitHub-only), and that extraction never writes
  outside the install directory.
- 1.6 closes the unverified-download gap the security posture already
  claims is closed; record the corrected state.
- 3.1 moves `secrets.local.*` between roots: temp-then-move with
  restrictive permissions, old-root copies limited to the explicit
  `.aether-backups` folder the user is told about.
- 2.8 stops a secret reference string from being sent as a bearer
  token (information-leak class, low severity, loopback-typical).
- No new network surface anywhere in the pack.

## Explicit rejections

Checked against archived rounds and rejected for r11; do not re-propose
without new evidence:

- **New NuGet dependencies for any of this** (zip handling, audio
  playback, diffing). System.IO.Compression and OS-native playback
  cover it; the no-new-NuGet default stands.
- **Implementing the LLM benchmark judge.** r7's deterministic-checks
  principle stands; 2.6 removes the phantom surface. If the owner wants
  judging, it is a separately-specced feature round.
- **Auto-selecting or auto-switching providers when routing is
  ambiguous (2.4).** Unknown routing yields an explicit error; nothing
  silently picks an endpoint for a conversation.
- **Auto-killing whatever holds the auto-tune port (1.5).** Same as
  r9: name the owner, refuse to probe; only the user-clicked,
  identity-verified orphan Stop ever kills a foreign PID.
- **A general settings-wide path rewriter for 4.5.** Only the launch
  path stops mutating config; deliberate normalization stays where the
  user edits settings.
- **Bundling ffmpeg for Windows playback (4.2).** OS-native playback
  is sufficient and dependency-free.
- **Migrating `settings.json` itself in 3.1.** It bootstraps the data
  root and stays in LocalApplicationData by design (unchanged from the
  original migration design).
- **WAL mode / connection pooling changes to "fix" 3.6.** Backup
  consistency comes from the SQLite backup primitive, not from
  changing journal modes product-wide.
