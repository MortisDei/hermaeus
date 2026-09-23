#!/usr/bin/env bash
set -euo pipefail

# r29 doc 04 4.6 / 5.2: measured line coverage is 61.6%. The prior lower
# ratchets could not fail on any regression short of deleting a quarter of the
# suite. 60 is just under the real number.
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

dotnet test "$PROJECT" --no-restore --collect:"XPlat Code Coverage" --results-directory "$RESULTS_DIR"

report="$(find "$RESULTS_DIR" -name coverage.cobertura.xml -print -quit)"
if [[ -z "$report" ]]; then
  echo "Coverage test run produced no Cobertura report." >&2
  exit 1
fi

awk -v threshold="$THRESHOLD" '
  BEGIN {
    if (threshold !~ /^[0-9]+([.][0-9]+)?$/ || threshold + 0 > 100) {
      print "Coverage threshold must be between 0 and 100." > "/dev/stderr"
      exit 2
    }
  }
  /<coverage[[:space:]]/ {
    found = 1
    if ($0 !~ /lines-covered="[0-9]+"/ || $0 !~ /lines-valid="[0-9]+"/) {
      print "Coverage report has invalid line counts." > "/dev/stderr"
      exit 2
    }
    covered = $0
    sub(/.*lines-covered="/, "", covered)
    sub(/".*/, "", covered)
    valid = $0
    sub(/.*lines-valid="/, "", valid)
    sub(/".*/, "", valid)
    if (valid + 0 <= 0 || covered + 0 > valid + 0) {
      print "Coverage report has invalid line counts." > "/dev/stderr"
      exit 2
    }
    percent = 100 * covered / valid
    printf "Line coverage: %.2f%% (%d/%d); floor %s%%.\n", percent, covered, valid, threshold
    if (percent < threshold + 0) {
      print "Line coverage is below the floor." > "/dev/stderr"
      exit 1
    }
    exit
  }
  END {
    if (!found) {
      print "Coverage report has no coverage root." > "/dev/stderr"
      exit 2
    }
  }
' "$report"

echo "Coverage report: $report"
