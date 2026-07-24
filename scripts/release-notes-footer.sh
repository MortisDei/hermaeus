#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: ./release-notes-footer.sh <version>

Prints the fixed honesty-block footer appended to every generated GitHub
Release's notes: unsigned-binary warning, per-OS SHA256 verification
commands, and a pointer to docs/packaging.md. Filenames are derived from
<version>, e.g. 0.29.0-alpha.
USAGE
}

if [[ $# -lt 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  usage >&2
  exit 1
fi

VERSION="$1"

cat <<FOOTER

## Verifying this release

These binaries are unsigned. Windows SmartScreen will warn on first launch;
verify the SHA256 instead of clicking through blindly.

Windows:

\`\`\`powershell
Get-FileHash .\\hermaeus-$VERSION-win-x64.zip -Algorithm SHA256
\`\`\`

Linux:

\`\`\`bash
sha256sum -c hermaeus-$VERSION-linux-x64.tar.gz.sha256
\`\`\`

Building from source and the full packaging reference: see
[docs/packaging.md](https://github.com/MortisDei/hermaeus/blob/main/docs/packaging.md).
Framework-dependent packages need the .NET 10 runtime installed.
FOOTER
