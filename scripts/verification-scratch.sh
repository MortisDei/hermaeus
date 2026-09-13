#!/usr/bin/env bash

# Source this file from a verification driver or tool. It deliberately does
# not change the caller's shell options.

VERIFICATION_SCRATCH_ROOT="${VERIFICATION_SCRATCH_ROOT:-}"
VERIFICATION_SCRATCH_PARENT="${VERIFICATION_SCRATCH_PARENT:-}"
VERIFICATION_SCRATCH_NAMESPACE="${VERIFICATION_SCRATCH_NAMESPACE:-}"

verification_scratch_validate_namespace() {
  local namespace="${1:-}"
  [[ "$namespace" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]
}

verification_scratch_validate_stale_minutes() {
  local stale_minutes="${1:-}"
  [[ "$stale_minutes" =~ ^[0-9]+$ ]]
}

verification_scratch_reclaim_stale() {
  local parent="$1"
  local namespace="$2"
  local stale_minutes="$3"
  local candidate marker owner_pid line

  [[ -d "$parent" ]] || return 0

  while IFS= read -r -d '' candidate; do
    marker="$candidate/.hermaeus-verification-owner"
    [[ -f "$marker" ]] || continue

    owner_pid=""
    while IFS= read -r line; do
      case "$line" in
        pid=*) owner_pid="${line#pid=}" ;;
      esac
    done < "$marker"

    if [[ "$owner_pid" =~ ^[0-9]+$ ]] && kill -0 "$owner_pid" 2>/dev/null; then
      continue
    fi

    rm -rf -- "$candidate"
  done < <(
    find "$parent" -mindepth 1 -maxdepth 1 -type d \
      -name "${namespace}.*" -mmin "+$stale_minutes" -print0
  )
}

verification_scratch_install_traps() {
  trap 'exit 130' INT
  trap 'exit 143' TERM
  trap 'verification_scratch_cleanup' EXIT
}

verification_scratch_create() {
  local namespace="${1:-}"
  local stale_minutes="${2:-60}"
  local temp_parent="${TMPDIR:-/tmp}"
  local parent root owner_pid

  if [[ -n "$VERIFICATION_SCRATCH_ROOT" ]]; then
    echo "verification scratch root already exists: $VERIFICATION_SCRATCH_ROOT" >&2
    return 2
  fi
  if ! verification_scratch_validate_namespace "$namespace"; then
    echo "invalid verification scratch namespace: $namespace" >&2
    return 2
  fi
  if ! verification_scratch_validate_stale_minutes "$stale_minutes"; then
    echo "invalid verification scratch stale age in minutes: $stale_minutes" >&2
    return 2
  fi
  if [[ "$temp_parent" != /* ]]; then
    echo "TMPDIR must be an absolute path: $temp_parent" >&2
    return 2
  fi

  parent="$temp_parent/hermaeus-verification-runs"
  mkdir -p -- "$parent"
  verification_scratch_reclaim_stale "$parent" "$namespace" "$stale_minutes"

  if ! root="$(mktemp -d -- "$parent/${namespace}.XXXXXX")"; then
    rmdir -- "$parent" 2>/dev/null || true
    return 1
  fi

  VERIFICATION_SCRATCH_PARENT="$parent"
  VERIFICATION_SCRATCH_NAMESPACE="$namespace"
  VERIFICATION_SCRATCH_ROOT="$root"
  owner_pid="${BASHPID:-$$}"
  if ! printf 'pid=%s\nnamespace=%s\n' "$owner_pid" "$namespace" \
    > "$root/.hermaeus-verification-owner"; then
    rm -rf -- "$root"
    rmdir -- "$parent" 2>/dev/null || true
    VERIFICATION_SCRATCH_ROOT=""
    VERIFICATION_SCRATCH_PARENT=""
    VERIFICATION_SCRATCH_NAMESPACE=""
    return 1
  fi
  verification_scratch_install_traps
}

verification_scratch_path() {
  local relative="${1:-}"
  if [[ -z "$VERIFICATION_SCRATCH_ROOT" || -z "$relative" ]]; then
    return 2
  fi
  case "$relative" in
    /*|.|..|../*|*/../*|*/..) return 2 ;;
  esac
  printf '%s/%s\n' "$VERIFICATION_SCRATCH_ROOT" "$relative"
}

verification_scratch_cleanup() {
  local root="$VERIFICATION_SCRATCH_ROOT"
  local parent="$VERIFICATION_SCRATCH_PARENT"
  local namespace="$VERIFICATION_SCRATCH_NAMESPACE"

  if [[ -n "$root" && -n "$parent" && -n "$namespace" \
    && "$root" == "$parent/${namespace}."* ]]; then
    rm -rf -- "$root"
  fi
  if [[ -n "$parent" ]]; then
    rmdir -- "$parent" 2>/dev/null || true
  fi

  VERIFICATION_SCRATCH_ROOT=""
  VERIFICATION_SCRATCH_PARENT=""
  VERIFICATION_SCRATCH_NAMESPACE=""
}
