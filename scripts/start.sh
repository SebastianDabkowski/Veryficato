#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -d src/Verificato.UI/node_modules ]]; then npm --prefix src/Verificato.UI ci; fi
npm --prefix src/Verificato.UI run build
dotnet build src/Verificato.Host
exec dotnet run --project src/Verificato.Host --no-build --no-launch-profile -- "$@"
