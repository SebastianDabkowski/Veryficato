param([string]$Configuration = 'Debug', [switch]$WithDeps)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build src/Verificato.Host -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    $installer = Join-Path $root "src/Verificato.Host/bin/$Configuration/net10.0/playwright.ps1"
    if ($WithDeps) { & $installer install --with-deps chromium }
    else { & $installer install chromium }
    if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }
} finally { Pop-Location }
