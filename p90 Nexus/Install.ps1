param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$target = (Resolve-Path -LiteralPath $GamePath).ProviderPath.TrimEnd('\')
$payload = Join-Path $PSScriptRoot 'GameFiles'
if (-not (Test-Path -LiteralPath (Join-Path $target 'SCP Project 90.exe') -PathType Leaf)) { throw 'Select the folder containing SCP Project 90.exe.' }
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close SCP Project 90 before installation.' }
# Refuse existing loaders and framework data; never overwrite an installation.
foreach ($name in @('BepInEx','dotnet','P90','winhttp.dll','doorstop_config.ini','.doorstop_version','MelonLoader','version.dll')) {
    if (Test-Path -LiteralPath (Join-Path $target $name)) { throw "Existing mod/runtime detected: $name. Use a clean game copy." }
}
foreach ($entry in (Get-Content (Join-Path $PSScriptRoot 'supported-game.json') -Raw | ConvertFrom-Json)) {
    $path = Join-Path $target $entry.Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing game file: $($entry.Path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Unsupported game build: $($entry.Path). Nothing installed." }
}
$manifest = @(Get-Content (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
$actual = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
if ($actual.Count -ne $manifest.Count) { throw 'Package file count mismatch.' }
foreach ($entry in $manifest) {
    if ([IO.Path]::IsPathRooted($entry.Path) -or $entry.Path -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Invalid manifest path.' }
    $source = Join-Path $payload $entry.Path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Package damaged: $($entry.Path)" }
    if (Test-Path -LiteralPath (Join-Path $target $entry.Path)) { throw "Destination file already exists: $($entry.Path). Nothing installed." }
}
foreach ($entry in $manifest) {
    $destination = Join-Path $target $entry.Path
    [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
    [IO.File]::Copy((Join-Path $payload $entry.Path), $destination, $false)
}
Write-Host 'P90 Nexus installed. Start Steam, then start SCP Project 90.exe from this folder.'
Write-Host 'The first launch generates interop assemblies and takes longer. See README.ru.md.'
