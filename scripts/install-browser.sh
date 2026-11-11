#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Verificato.Host
# Requires PowerShell 7. On Linux, --with-deps may require administrator privileges.
pwsh -NoProfile -File src/Verificato.Host/bin/Debug/net10.0/playwright.ps1 install chromium "$@"
