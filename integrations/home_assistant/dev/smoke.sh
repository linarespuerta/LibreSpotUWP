#!/usr/bin/env bash
# One-command smoke test: fresh throwaway HA + integration + Lumia simulator.
# Set KEEP=1 to leave the container running afterwards.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
"$HERE/run.sh" reset >/dev/null 2>&1 || true
"$HERE/run.sh" up
cleanup() { [ "${KEEP:-0}" = 1 ] || "$HERE/run.sh" reset >/dev/null 2>&1; }
trap cleanup EXIT
cd "$HERE/.."
uv run python dev/smoke.py
