# 03. Release pipeline

Goal: pushing an annotated tag `vX.Y.Z-alpha` produces a complete GitHub
Release (win-x64 zip, linux-x64 tar.gz, both .sha256 files,
changelog-derived notes) with zero manual assembly. This closes the "a
stranger can download it" half of go-public readiness (docs/temp/
02-credibility-and-launch.md item 2.2) and implements the owner's new
policy: minor versions always ship; patch versions only as urgent
hotfixes.

## 3.1 Versioning and tagging policy (document it, then automate it)

Add a "Releases" section to `docs/packaging.md` stating:

- Versions live in `Directory.Build.props` (VersionPrefix/VersionSuffix);
  that file is the single source of truth.
- Every minor version bump (0.28.0 -> 0.29.0) gets an annotated tag
  `v<version>` (e.g. `v0.29.0-alpha`) pushed after the release commit is
  on main with green CI. The tag push triggers the release workflow.
- Patch versions (0.29.1) are tagged and released only when they carry an
  urgent fix users need; otherwise they ride until the next minor.
- While VersionSuffix is `alpha` (or any prerelease suffix), releases are
  marked prerelease on GitHub.
- Binaries are unsigned. Release notes must say so plainly, every
  release, with per-OS SHA256 verification commands (see 3.4). Installer
  signing remains documented future work.

Add one line to the `CHANGELOG.md` header noting that minor versions are
tagged and released on GitHub from 0.29.0-alpha onward.

## 3.2 `.github/workflows/release.yml`

Trigger: `push` on tags `v*`. `permissions: contents: write` (needed to
create the release; keep it workflow-level and minimal, matching ci.yml's
explicit-permissions posture).

Jobs:

1. **verify**: check out, extract VersionPrefix and VersionSuffix from
   `Directory.Build.props`, and fail unless the pushed tag is exactly
   `v<VersionPrefix>-<VersionSuffix>` (or `v<VersionPrefix>` if the
   suffix is ever empty). A tag that does not match the tree it points at
   must die here, loudly, before anything builds.
2. **build-windows** (windows-latest, needs verify): setup-dotnet 10.0.x,
   `pwsh ./build.ps1`, upload `dist/*.zip` and `dist/*.zip.sha256` as an
   artifact.
3. **build-linux** (ubuntu-latest, needs verify): `./build.sh`, upload
   `dist/*.tar.gz` and `dist/*.tar.gz.sha256`.
4. **release** (ubuntu-latest, needs both builds): download artifacts,
   generate notes (3.3 + 3.4), then create the release with the
   preinstalled GitHub CLI:
   `gh release create "$TAG" --title "Hermaeus $TAG" --notes-file notes.md
   --prerelease dist/...` (prerelease flag driven by whether the version
   has a suffix). `GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}`.

Constraints:

- Only `actions/checkout`, `actions/setup-dotnet`,
  `actions/upload-artifact`, `actions/download-artifact`, pinned at major
  version exactly like ci.yml (`@v4`). No marketplace actions; `gh` is
  preinstalled on GitHub runners and needs no third-party wrapper.
- The workflow must not modify the repo or push anything; it consumes a
  tag, it never creates one.
- Do not resurrect per-step shell string-building with user-controlled
  input; the only external input is the tag name, which the verify job
  has already constrained to a version literal.

## 3.3 Changelog extraction: `scripts/release-notes.sh` + `.ps1`

Twin scripts, same contract, matching the existing `coverage.sh`/
`coverage.ps1` pairing:

- Input: a version string (e.g. `0.29.0-alpha`) and optionally the
  changelog path (default `CHANGELOG.md`).
- Output (stdout): the body of the `## [<version>] - <date>` section,
  from the line after the heading to the line before the next `## [`
  heading, trimmed of leading/trailing blank lines.
- Exit nonzero with a clear message if the section is missing. The
  release workflow uses this as a second guard: you cannot release a
  version you did not write a changelog entry for.
- Keep the parsing dumb and line-based; the changelog format is already
  rigid (FIFO, 10 versions, fixed heading shape).

Add harness-registered tests for the extraction logic against fixture
changelog content: section found, section missing, latest section,
archived-away version missing. If keeping the logic testable means
implementing it as a small internal helper that the scripts mirror, prefer
testing the .ps1 via `pwsh` invocation in a harness case only if that is
already an established pattern in the suite; otherwise test at the
fixture-file level with the shell available on the test host and skip
gracefully where that shell is absent. Do not add a NuGet package for
this.

## 3.4 Release notes footer (honesty block)

Every generated notes file appends a fixed footer after the changelog
section:

- "These binaries are unsigned. Windows SmartScreen will warn on first
  launch; verify the SHA256 instead of clicking through blindly."
- Exact verification commands:
  - Windows: `Get-FileHash .\hermaeus-<version>-win-x64.zip -Algorithm SHA256`
  - Linux: `sha256sum -c hermaeus-<version>-linux-x64.tar.gz.sha256`
- A pointer to `docs/packaging.md` for building from source, and a note
  that framework-dependent packages need the .NET 10 runtime.

Keep the footer in one place (a template file under `scripts/` or a
heredoc in the workflow, whichever stays readable); the exact filenames
are derived from the version, not hardcoded.

## 3.5 What stays owner-only

- Pushing the actual `v0.29.0-alpha` tag after this round lands.
- Smoke-testing the linux-x64 tar.gz on a real desktop (r11 item 2.2
  acceptance) and the win-x64 zip.
- Marking item 2.2 done in the go-public tracker once the first release
  is live.

The final response to the owner must spell out the exact commands for the
first tag push (`git tag -a v0.29.0-alpha -m ... && git push origin
v0.29.0-alpha`) and what to check on the Releases page afterwards.
