#!/usr/bin/env bash
# Option B — Manual dev (no Docker required)
# Starts the backend and frontend in parallel.
# Press Ctrl+C to stop both.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
BACKEND_PORT=5188
FRONTEND_PORT=3000

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[dev]${NC} $*"; }
warn() { echo -e "${YELLOW}[dev]${NC} $*"; }
die()  { echo -e "${RED}[dev]${NC} $*" >&2; exit 1; }

# ── prerequisites ──────────────────────────────────────────────────────────────
command -v dotnet >/dev/null 2>&1 || die ".NET SDK not found. Install .NET 10: https://dotnet.microsoft.com/download"
command -v node   >/dev/null 2>&1 || die "Node.js not found. Install Node 22: https://nodejs.org"
command -v npm    >/dev/null 2>&1 || die "npm not found."

# ── free ports if occupied ─────────────────────────────────────────────────────
free_port() {
  local port=$1
  local pids
  pids=$(lsof -ti ":$port" 2>/dev/null || true)
  if [[ -n "$pids" ]]; then
    warn "Port $port is in use — killing existing process(es): $pids"
    echo "$pids" | xargs kill -9 2>/dev/null || true
    sleep 0.5
  fi
}
free_port "$BACKEND_PORT"
free_port "$FRONTEND_PORT"

# ── frontend setup (first-time only) ──────────────────────────────────────────
FRONTEND_DIR="$REPO_ROOT/frontend"

if [[ ! -f "$FRONTEND_DIR/.env" ]]; then
  if [[ -f "$FRONTEND_DIR/.env.example" ]]; then
    log "Copying .env.example → .env (sets NUXT_API_BASE=http://localhost:$BACKEND_PORT)"
    cp "$FRONTEND_DIR/.env.example" "$FRONTEND_DIR/.env"
  else
    warn ".env.example not found in frontend/ — NUXT_API_BASE may not be set"
  fi
fi

if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
  log "Installing frontend dependencies..."
  npm --prefix "$FRONTEND_DIR" install
fi

# ── cleanup on exit ────────────────────────────────────────────────────────────
BACKEND_PID=""
cleanup() {
  echo ""
  log "Shutting down..."
  [[ -n "$BACKEND_PID" ]] && kill "$BACKEND_PID" 2>/dev/null || true
  wait 2>/dev/null || true
  log "Done."
}
trap cleanup EXIT INT TERM

# ── start backend (background) ─────────────────────────────────────────────────
log "Starting backend on http://localhost:$BACKEND_PORT ..."
dotnet run \
  --project "$REPO_ROOT/backend/src/LootBase.Api/LootBase.Api.csproj" \
  --no-launch-profile &
BACKEND_PID=$!

# give the backend a moment to bind before we print the frontend URL
sleep 2

# ── start frontend (foreground) ────────────────────────────────────────────────
log "Starting frontend on http://localhost:$FRONTEND_PORT ..."
log "Open http://localhost:$FRONTEND_PORT in your browser."
echo ""

npm --prefix "$FRONTEND_DIR" run dev
