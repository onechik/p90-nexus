param([string]$OutputName = ('P90-Nexus-0.2.0-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')))
$ErrorActionPreference = 'Stop'
if ($OutputName -notmatch '\A[A-Za-z0-9._-]+\z') { throw 'OutputName must be a simple filename.' }
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'P90 Nexus'
$payload = Join-Path $package 'GameFiles'
$manifest = @(Get-Content (Join-Path $package 'payload-sha256.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
if (@(Get-ChildItem $payload -File -Recurse).Count -ne $manifest.Count) { throw 'Payload file count changed.' }
foreach ($entry in $manifest) {
    if ((Get-FileHash -LiteralPath (Join-Path $payload $entry.Path)).Hash -ne $entry.SHA256) { throw "Payload changed: $($entry.Path)" }
}
# Only these generated distribution subdirectories are replaced, within the package.
foreach ($name in @('Documentation','SDK','Development')) {
    $destination = [IO.Path]::GetFullPath((Join-Path $package $name))
    $expected = [IO.Path]::GetFullPath($package).TrimEnd('\') + '\'
    if (-not $destination.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped package.' }
    if (Test-Path -LiteralPath $destination) {
        if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing a linked directory.' }
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destination | Out-Null
}
function Copy-SelectedTree([string]$Source, [string]$Destination, [string[]]$Extensions) {
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        $relative = $file.FullName.Substring($Source.Length + 1)
        if ($relative -match '(^|[\\/])(bin|obj|\.dotnet-home|\.git)([\\/]|$)') { continue }
        if ($file.Extension -notin $Extensions) { continue }
        $target = Join-Path $Destination $relative
        [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
Copy-SelectedTree (Join-Path $root 'docs') (Join-Path $package 'Documentation') @('.md')
Copy-SelectedTree (Join-Path $root 'sdk') (Join-Path $package 'SDK') @('.md','.cs','.csproj','.ps1','.json','.Config','.dll')
Copy-SelectedTree (Join-Path $root 'src') (Join-Path $package 'Development/src') @('.cs','.csproj','.props','.json','.Config')
Copy-SelectedTree (Join-Path $root 'scripts') (Join-Path $package 'Development/scripts') @('.ps1')
@'
# Исходники P90 Nexus

Здесь находятся наши исходники и скрипты. Для работы поместите src и scripts в корень отдельной игры с установленным Nexus и уже созданным BepInEx/interop, содержимое соседнего SDK — в каталог sdk этой игры.

Порядок сборки и архитектура: [руководство ядра](../Documentation/CORE-DEVELOPMENT.ru.md). Процедура выпуска: [RELEASE](../Documentation/RELEASE.ru.md). Скрипты Package-Nexus и Install-CoreUpdate рассчитаны на дерево проекта с подготовленным комплектом/артефактами; они не заменяют обычный Install.ps1.
'@ | Set-Content (Join-Path $package 'Development/README.ru.md') -Encoding utf8
Copy-Item (Join-Path $root 'docs/CHANGELOG.ru.md') (Join-Path $package 'CHANGELOG.ru.md')
$releaseManifest = Join-Path $package 'release-manifest.json'
@(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object FullName -ne $releaseManifest | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path=$_.FullName.Substring($package.Length+1); Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash }
}) | ConvertTo-Json -Depth 4 | Set-Content $releaseManifest -Encoding utf8
$output = Join-Path $root 'artifacts/stage07'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zip = Join-Path $output ($OutputName + '.zip')
if (Test-Path -LiteralPath $zip) { throw 'Archive already exists; choose a new output name.' }
Compress-Archive -LiteralPath $package -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip).Hash
($hash + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content ($zip + '.sha256') -Encoding ascii
[pscustomobject]@{ Folder=$package; Archive=$zip; SHA256=$hash }
