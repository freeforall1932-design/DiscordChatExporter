#!/usr/bin/env bash
# Starts the web interface, drives it with the UI test, and stops it again.
#
# Usage: ./ui-test/run.sh [path-to-binary] [port]
set -euo pipefail

BINARY="${1:-./publish/DiscordChatExporter.Cli}"
PORT="${2:-5156}"

cd "$(dirname "$0")/.."

"$BINARY" gui --port "$PORT" --host any --no-browser > ui-test/server.log 2>&1 &
SERVER_PID=$!

cleanup() {
  kill "$SERVER_PID" 2>/dev/null || true
  wait "$SERVER_PID" 2>/dev/null || true
}
trap cleanup EXIT

for _ in $(seq 1 60); do
  if curl -sf -o /dev/null "http://127.0.0.1:${PORT}/api/info"; then
    break
  fi
  sleep 1
done

if ! curl -sf -o /dev/null "http://127.0.0.1:${PORT}/api/info"; then
  echo "The web interface did not start."
  cat ui-test/server.log
  exit 1
fi

cd ui-test
npm install --no-audit --no-fund --silent
DCE_GUI_URL="http://127.0.0.1:${PORT}" node test.mjs
