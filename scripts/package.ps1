param([string]$Runtime = 'win-x64', [switch]$IncludeChromium)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    npm --prefix src/Verificato.UI ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    npm --prefix src/Verificato.UI run build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
    dotnet test Verificato.slnx --filter 'Category!=Browser'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $output = Join-Path $root "artifacts/verificato-$Runtime"
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }
    dotnet publish src/Verificato.Host -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item LICENSE, README.md, THIRD-PARTY-NOTICES.md -Destination $output
    New-Item -ItemType Directory -Force (Join-Path $output 'docs') | Out-Null
    Copy-Item docs/implementation-status.md, docs/architecture-c4.md, docs/api.md -Destination (Join-Path $output 'docs')
    if ($IncludeChromium) {
        if ($Runtime -ne 'win-x64' -or !$IsWindows) { throw 'Bundled Chromium is currently supported only for win-x64 built on Windows.' }
        $previous = $env:PLAYWRIGHT_BROWSERS_PATH
        try {
            $env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $output 'browsers'
            & (Join-Path $output 'playwright.ps1') install chromium
            if ($LASTEXITCODE -ne 0) { throw 'Browser packaging failed.' }
        } finally { $env:PLAYWRIGHT_BROWSERS_PATH = $previous }
    }
    Copy-Item scripts/launch.cmd, scripts/install-packaged-browser.ps1 -Destination $output
    $checksums = Get-ChildItem $output -File -Recurse | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetRelativePath($output, $_.Path))" }
    $checksums | Set-Content -Encoding utf8 (Join-Path $output 'SHA256SUMS.txt')
    Write-Host "Package created: $output"
} finally { Pop-Location }
