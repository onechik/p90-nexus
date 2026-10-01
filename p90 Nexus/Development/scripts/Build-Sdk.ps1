$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
# Build and validate first; never replace binaries in a running game.
& (Join-Path $PSScriptRoot 'Build-Core.ps1')
$sdk = Join-Path $gameRoot 'sdk'
New-Item -ItemType Directory -Path (Join-Path $sdk 'lib') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameRoot 'src\P90.API\bin\Release\net6.0\P90.API.dll') -Destination (Join-Path $sdk 'lib\P90.API.dll')
Copy-Item -LiteralPath (Join-Path $gameRoot 'src\NuGet.Config') -Destination (Join-Path $sdk 'NuGet.Config')
$api = Get-Item -LiteralPath (Join-Path $sdk 'lib\P90.API.dll')
[ordered]@{ ApiMajor=1; TargetFramework='net6.0'; BuildSdk='10.0.401'; ApiSHA256=(Get-FileHash $api.FullName).Hash; ExternalPluginPackages=@(); Runtime='Game loader runtime .NET 6.0.7'; RefPack='Microsoft.NETCore.App.Ref 6.0.36' } | ConvertTo-Json | Set-Content (Join-Path $sdk 'sdk-manifest.json') -Encoding utf8
