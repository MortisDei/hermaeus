#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: ./release-notes.sh <version> [changelog-path]

Prints the body of the "## [<version>] - <date>" section from the changelog
(everything after the heading line, up to but not including the next "## ["
heading), trimmed of leading/trailing blank lines.

  version          Version string as it appears in the heading, e.g. 0.29.0-alpha.
  changelog-path   Path to the changelog. Default: CHANGELOG.md.
USAGE
}

if [[ $# -lt 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  usage >&2
  exit 1
fi

VERSION="$1"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHANGELOG="${2:-$ROOT_DIR/CHANGELOG.md}"

if [[ ! -f "$CHANGELOG" ]]; then
  echo "release-notes: changelog not found: $CHANGELOG" >&2
  exit 1
fi

status=0
awk -v version="$VERSION" '
  $0 ~ "^## \\[" version "\\]" { found = 1; printing = 1; next }
  printing && /^## \[/ { printing = 0 }
  printing { lines[++n] = $0 }
  END {
    if (!found) { exit 1 }
    start = 1
    while (start <= n && lines[start] ~ /^[[:space:]]*$/) start++
    last = n
    while (last >= start && lines[last] ~ /^[[:space:]]*$/) last--
    for (i = start; i <= last; i++) print lines[i]
  }
' "$CHANGELOG" || status=$?

if [[ $status -ne 0 ]]; then
  echo "release-notes: no changelog section found for version $VERSION in $CHANGELOG" >&2
  exit 1
fi
