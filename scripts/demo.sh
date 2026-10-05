#!/usr/bin/env bash
# Starts the demo upstream and the LiteGate gateway, then exercises caching,
# rate limiting and the metrics endpoint. Run `dotnet build LiteGate.slnx` first.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet run --project "$ROOT/src/LiteGate.DemoUpstream" --no-build --urls http://localhost:5100 &
UPSTREAM_PID=$!
dotnet run --project "$ROOT/src/LiteGate.Gateway" --no-build --urls http://localhost:5080 &
GATEWAY_PID=$!

cleanup() { kill "$UPSTREAM_PID" "$GATEWAY_PID" 2>/dev/null || true; }
trap cleanup EXIT

wait_for() {
  for _ in $(seq 1 30); do
    curl -sf "$1" >/dev/null 2>&1 && return 0
    sleep 1
  done
  echo "Service did not start: $1" >&2
  exit 1
}

wait_for http://localhost:5100/api/time
wait_for http://localhost:5080/_health

echo
echo "== First GET (expect X-Cache: MISS) =="
curl -s http://localhost:5080/api/time
echo
echo "== Second GET (expect X-Cache: HIT) =="
curl -s http://localhost:5080/api/time
echo
echo "== no-store response is never cached =="
curl -s http://localhost:5080/api/no-store
curl -s http://localhost:5080/api/no-store
echo
echo "== Burst to trigger rate limiting (status codes) =="
for _ in $(seq 1 15); do
  curl -s -o /dev/null -w "%{http_code} " http://localhost:5080/api/no-store
done
echo
echo "== Metrics =="
curl -s http://localhost:5080/_metrics
echo
