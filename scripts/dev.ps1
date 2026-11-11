param([switch]$NoOpen, [switch]$PrintBootstrap)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vite = $null
Push-Location $root
try {
    if (!(Test-Path src/Verificato.UI/node_modules)) {
        npm --prefix src/Verificato.UI ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    dotnet build src/Verificato.Host
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    $vite = Start-Process -FilePath (Get-Command node).Source -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1') -WorkingDirectory (Join-Path $root 'src/Verificato.UI') -NoNewWindow -PassThru
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($vite.HasExited) { throw 'Vite exited. Check whether port 5180 is already occupied.' }
        try { $null = Invoke-WebRequest 'http://127.0.0.1:5180' -TimeoutSec 1; $ready = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'Vite did not become ready.' }
    dotnet run --project src/Verificato.Host --no-build --no-launch-profile -- --dev true --no-open $NoOpen.IsPresent.ToString().ToLowerInvariant() --print-bootstrap $PrintBootstrap.IsPresent.ToString().ToLowerInvariant()
} finally {
    if ($vite -and !$vite.HasExited) { $vite.Kill($true); $vite.WaitForExit() }
    Pop-Location
}
