$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$run = Join-Path $gameRoot ('research\stage06\sdk-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
$projectRoot = Join-Path $run 'OnechikHello'
New-Item -ItemType Directory -Path $run -Force | Out-Null
& (Join-Path $gameRoot 'sdk\New-Plugin.ps1') -Name OnechikHello -Id onechik.hello -Destination $projectRoot
$env:NUGET_PACKAGES = Join-Path $gameRoot 'research\tools\nuget-packages'
& (Join-Path $projectRoot 'Build.ps1')
$env:DOTNET_CLI_HOME = Join-Path $gameRoot 'research\tools\dotnet-home'
$verifier = Join-Path $gameRoot 'src\P90.SdkVerifier\P90.SdkVerifier.csproj'
& dotnet restore $verifier --configfile (Join-Path $gameRoot 'src\NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Verifier restore failed.' }
& dotnet build $verifier -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Verifier build failed.' }
# Build an isolated executable host for the exact runtime shipped with the game.
# Copies only; neither the live game's runtime nor system dotnet is modified.
$runtimeRoot = Join-Path $gameRoot 'research\tools\net6-test-host'
$shared = Join-Path $runtimeRoot 'shared\Microsoft.NETCore.App\6.0.7'
$dotnetExe = (Get-Command dotnet -CommandType Application).Source
$dotnetRoot = Split-Path $dotnetExe -Parent
$fxr = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$fxrTarget = Join-Path $runtimeRoot ('host\fxr\' + $fxr.Name)
New-Item -ItemType Directory -Path $shared,$fxrTarget -Force | Out-Null
Copy-Item -LiteralPath $dotnetExe -Destination (Join-Path $runtimeRoot 'dotnet.exe')
Copy-Item -LiteralPath (Join-Path $fxr.FullName 'hostfxr.dll') -Destination $fxrTarget
Get-ChildItem -LiteralPath (Join-Path $gameRoot 'dotnet') -File | Copy-Item -Destination $shared
$runner = Join-Path $runtimeRoot 'dotnet.exe'
& $runner --list-runtimes
$coreTests = Join-Path $gameRoot 'src\P90.Core.Tests\bin\Release\net6.0\P90.Core.Tests.dll'
& $runner $coreTests (Join-Path $run 'core-net6')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed on game runtime.' }
$binary = Join-Path $projectRoot 'bin\Release\net6.0\OnechikHello.dll'
& $runner (Join-Path $gameRoot 'src\P90.SdkVerifier\bin\Release\net6.0\P90.SdkVerifier.dll') $binary (Join-Path $run 'plugin-verification') onechik.hello
if ($LASTEXITCODE -ne 0) { throw 'Independent SDK plugin verification failed.' }
[ordered]@{ Passed=$true; Project=$projectRoot; Binary=$binary; BinarySHA256=(Get-FileHash $binary).Hash; Runtime='6.0.7'; ApiSHA256=(Get-FileHash (Join-Path $projectRoot 'lib\P90.API.dll')).Hash; GameRuntimeSHA256=(Get-FileHash (Join-Path $gameRoot 'dotnet\coreclr.dll')).Hash; TestRuntimeSHA256=(Get-FileHash (Join-Path $shared 'coreclr.dll')).Hash } | ConvertTo-Json | Set-Content (Join-Path $run 'result.json') -Encoding utf8
Get-Content (Join-Path $run 'result.json')
