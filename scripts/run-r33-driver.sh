#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/hermaeus-r33-driver.XXXXXX")"

cleanup() {
  if [[ -n "${RUN_ROOT:-}" && -d "$RUN_ROOT" ]]; then
    rm -rf -- "$RUN_ROOT"
  fi
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

run_options=()
configuration="Debug"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      run_options+=(--no-build)
      shift
      ;;
    --configuration)
      configuration="${2:?Missing value for --configuration}"
      shift 2
      ;;
    -h|--help)
      echo "Usage: bash scripts/run-r33-driver.sh [--configuration <name>] [--no-build]"
      exit 0
      ;;
    *)
      echo "Usage: bash scripts/run-r33-driver.sh [--configuration <name>] [--no-build]" >&2
      exit 2
      ;;
  esac
done

dotnet run --project "$ROOT_DIR/src/Tools/R33Driver/R33Driver.csproj" \
  --configuration "$configuration" "${run_options[@]}" -- \
  --settings-path "$RUN_ROOT/settings/settings.json" \
  --data-root "$RUN_ROOT/data" \
  --workspace "$RUN_ROOT/workspace"
