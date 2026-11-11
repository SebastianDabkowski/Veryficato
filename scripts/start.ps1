param([int]$Port = 5078, [string]$DataDirectory, [switch]$NoOpen, [switch]$PrintBootstrap)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!(Test-Path src/Verificato.UI/node_modules)) {
        npm --prefix src/Verificato.UI ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    npm --prefix src/Verificato.UI run build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
    dotnet build src/Verificato.Host
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    $arguments = @('run', '--project', 'src/Verificato.Host', '--no-build', '--no-launch-profile', '--', '--port', "$Port", '--no-open', $NoOpen.IsPresent.ToString().ToLowerInvariant(), '--print-bootstrap', $PrintBootstrap.IsPresent.ToString().ToLowerInvariant())
    if ($DataDirectory) { $arguments += @('--data-dir', [IO.Path]::GetFullPath($DataDirectory)) }
    & dotnet @arguments
} finally { Pop-Location }
