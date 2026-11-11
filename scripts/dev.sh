#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -d src/Verificato.UI/node_modules ]]; then npm --prefix src/Verificato.UI ci; fi
dotnet build src/Verificato.Host
(cd src/Verificato.UI && exec node node_modules/vite/bin/vite.js --host 127.0.0.1) &
vite_pid=$!
trap 'kill "$vite_pid" 2>/dev/null || true' EXIT INT TERM
for _ in {1..40}; do
  kill -0 "$vite_pid" 2>/dev/null || { echo 'Vite exited'; exit 1; }
  if curl -fsS http://127.0.0.1:5180/ >/dev/null 2>&1; then break; fi
  sleep 0.25
done
dotnet run --project src/Verificato.Host --no-build --no-launch-profile -- --dev true "$@"
