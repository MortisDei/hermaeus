#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TRACE_FILE="${1:-$ROOT_DIR/agent.trace.jsonl}"
SCHEMA_FILE="${2:-$ROOT_DIR/docs/schemas/agent_trace.schema.json}"

dotnet run --project "$ROOT_DIR/src/Tools/TraceValidator" -- "$TRACE_FILE" "$SCHEMA_FILE"
