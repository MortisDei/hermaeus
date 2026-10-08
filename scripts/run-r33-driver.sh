#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT_DIR/scripts/verification-scratch.sh"
verification_scratch_create "r33-driver"

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
  --settings-path "$(verification_scratch_path settings/settings.json)" \
  --data-root "$(verification_scratch_path data)" \
  --workspace "$(verification_scratch_path workspace)"
