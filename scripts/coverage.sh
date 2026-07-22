#!/usr/bin/env bash
set -euo pipefail

THRESHOLD="${1:-47}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/src/Hermaeus.Tests/Hermaeus.Tests.csproj"
RESULTS_DIR="$ROOT_DIR/dist/.coverage"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

dotnet test "$PROJECT" --collect:"XPlat Code Coverage" --results-directory "$RESULTS_DIR" \
    -p:CoverletOutputFormat=cobertura -p:Threshold="$THRESHOLD" -p:ThresholdType=line -p:ThresholdStat=total

report="$(find "$RESULTS_DIR" -name coverage.cobertura.xml | head -n 1)"
if [[ -n "$report" ]]; then
  echo "Coverage report: $report"
fi
