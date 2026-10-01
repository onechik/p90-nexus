param([string]$Package = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\stage06\host-update'))
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game normally before installing the update.' }
$manifest = Get-Content -LiteralPath (Join-Path $Package 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$allowed = @('P90.API.dll','P90.Core.dll','P90.GameAdapter.dll','P90.Loader.dll')
if (@($manifest.Files).Count -ne 4 -or @($manifest.Files.Name | Select-Object -Unique).Count -ne 4) { throw 'Invalid package file list.' }
foreach ($file in $manifest.Files) {
    if ($file.Name -notin $allowed -or (Get-FileHash -LiteralPath (Join-Path $Package $file.Name)).Hash -ne $file.SHA256) { throw 'Invalid package name or SHA256.' }
}
$target = Join-Path $gameRoot 'BepInEx\plugins\P90.Loader'
if (-not (Test-Path -LiteralPath $target)) { throw 'Existing P90 installation not found. This is an update, not a loader installer.' }
$backup = Join-Path $gameRoot ('P90\backups\stage06-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $target $name) -Destination $backup }
try {
    foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $Package $name) -Destination (Join-Path $target $name) -Force }
    foreach ($file in $manifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $file.Name)).Hash -ne $file.SHA256) { throw 'Installed hash mismatch.' }
    }
} catch {
    foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $target $name) -Force }
    throw
}
[pscustomobject]@{ Installed=$true; Backup=$backup; Next='Start the game normally; use p90.plugins to check.' }
