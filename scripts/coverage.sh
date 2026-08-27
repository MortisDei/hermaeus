#!/usr/bin/env bash
set -euo pipefail

# r29 doc 04 4.6 / 5.2: measured line coverage is 61.6%. The old 47 (and
# AGENTS.md's 45) could not fail on any regression short of deleting a quarter
# of the suite. 60 is just under the real number.
THRESHOLD="${1:-60}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/src/Hermaeus.Tests/Hermaeus.Tests.csproj"
RESULTS_DIR="$(mktemp -d "${TMPDIR:-/tmp}/hermaeus-coverage.XXXXXX")"
cleanup() {
  if [[ -n "${RESULTS_DIR:-}" && -d "$RESULTS_DIR" ]]; then
    rm -rf -- "$RESULTS_DIR"
  fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

dotnet test "$PROJECT" --no-restore --collect:"XPlat Code Coverage" --results-directory "$RESULTS_DIR" \
    -p:CoverletOutputFormat=cobertura -p:Threshold="$THRESHOLD" -p:ThresholdType=line -p:ThresholdStat=total

report="$(find "$RESULTS_DIR" -name coverage.cobertura.xml | head -n 1)"
if [[ -n "$report" ]]; then
  echo "Coverage report: $report"
fi
