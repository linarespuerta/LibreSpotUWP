#!/usr/bin/env bash
# Manage the throwaway Home Assistant: run.sh up | down | reset | logs
set -euo pipefail
cd "$(dirname "$0")"
case "${1:-}" in
  up)    podman-compose -f compose.yaml up -d ;;
  down)  podman-compose -f compose.yaml down ;;
  reset) podman-compose -f compose.yaml down || true ;;
  logs)  podman logs -f lumia-hifi-dev-ha ;;
  *)     echo "usage: $0 up|down|reset|logs" >&2; exit 2 ;;
esac
