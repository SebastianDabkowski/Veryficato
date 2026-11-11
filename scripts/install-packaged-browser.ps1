$ErrorActionPreference = 'Stop'
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $PSScriptRoot 'browsers'
& (Join-Path $PSScriptRoot 'playwright.ps1') install chromium
if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }
Write-Host 'Chromium installed. Start launch.cmd.'
