#!/usr/bin/env bash
# Orchestrator: load creds (if present), then start the FastAPI server.
# Schema is applied automatically on startup (app lifespan -> init_schema).
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root, so `api.app.main` imports and creds/ resolves

# Load creds/api.env into the environment if present (env vars still win).
if [[ -f creds/api.env ]]; then
  set -a
  # shellcheck disable=SC1091
  source creds/api.env
  set +a
fi

HOST="${API_HOST:-0.0.0.0}"
PORT="${API_PORT:-8000}"
exec python3 -m uvicorn api.app.main:app --host "$HOST" --port "$PORT" "$@"
