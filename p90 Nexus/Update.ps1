param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$target = (Resolve-Path -LiteralPath $GamePath).ProviderPath.TrimEnd('\')
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game normally before updating Nexus.' }
if (-not (Test-Path (Join-Path $target 'BepInEx/plugins/P90.Loader/P90.Core.dll'))) { throw 'Existing Nexus installation required. For a clean game use Install.ps1.' }
foreach ($entry in (Get-Content (Join-Path $PSScriptRoot 'supported-game.json') -Raw | ConvertFrom-Json)) {
    $file = Join-Path $target $entry.Path
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw "Unsupported game build: $($entry.Path). Nothing updated." }
}
$paths = @('BepInEx\plugins\P90.Loader\P90.API.dll','BepInEx\plugins\P90.Loader\P90.Core.dll','BepInEx\plugins\P90.Loader\P90.GameAdapter.dll','BepInEx\plugins\P90.Loader\P90.Loader.dll','scripts\P90.ps1','scripts\P90-Console.ps1','P90 Console.cmd')
# Update the supplied example only if it is already installed. Do not add disabled samples.
if (Test-Path (Join-Path $target 'P90/plugins/P90.Samples.ServerDiagnostics.dll')) { $paths += 'P90\plugins\P90.Samples.ServerDiagnostics.dll' }
$manifest = @(Get-Content (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
foreach ($path in $paths) {
    $entry = @($manifest | Where-Object Path -eq $path)
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('GameFiles\'+$path))).Hash -ne $entry[0].SHA256) { throw "Package hash mismatch: $path" }
}
$backup = Join-Path $target ('P90\backups\nexus-0.3.0-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $backup | Out-Null
$existed = @{}
foreach ($path in $paths) {
    $destination = Join-Path $target $path
    $existed[$path] = Test-Path -LiteralPath $destination
    if ($existed[$path]) {
        $copy = Join-Path $backup $path
        [IO.Directory]::CreateDirectory((Split-Path $copy -Parent)) | Out-Null
        Copy-Item -LiteralPath $destination -Destination $copy
    }
}
try {
    foreach ($path in $paths) {
        $destination = Join-Path $target $path
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('GameFiles\'+$path)) -Destination $destination -Force
        $entry = $manifest | Where-Object Path -eq $path
        if ((Get-FileHash -LiteralPath $destination).Hash -ne $entry.SHA256) { throw "Installed hash mismatch: $path" }
    }
} catch {
    foreach ($path in $paths) {
        $destination = Join-Path $target $path
        if ($existed[$path]) { Copy-Item -LiteralPath (Join-Path $backup $path) -Destination $destination -Force }
        elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
    }
    throw
}
[pscustomobject]@{Updated=$true;GamePath=$target;Backup=$backup;Files=$paths.Count}
