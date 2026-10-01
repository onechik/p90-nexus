param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $gameRoot 'research\tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_PACKAGES = Join-Path $gameRoot 'research\tools\nuget-packages'
foreach ($name in @('P90.Core.Tests', 'P90.Loader')) {
    $project = Join-Path $gameRoot ('src\' + $name + '\' + $name + '.csproj')
    & dotnet restore $project --configfile (Join-Path $gameRoot 'src\NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $name" }
    & dotnet build $project -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
}
$testDir = Join-Path $gameRoot ('research\stage06\unit-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
& dotnet --roll-forward Major (Join-Path $gameRoot 'src\P90.Core.Tests\bin\Release\net6.0\P90.Core.Tests.dll') $testDir
if ($LASTEXITCODE -ne 0) { throw "Core tests failed: $testDir" }
if ($Deploy) {
    if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game before deploying.' }
    $destination = Join-Path $gameRoot 'BepInEx\plugins\P90.Loader'
    $sampleDir = Join-Path $gameRoot 'P90\plugins'
    New-Item -ItemType Directory -Path $destination,$sampleDir -Force | Out-Null
    foreach ($name in @('P90.API', 'P90.Core', 'P90.GameAdapter', 'P90.Loader')) {
        Copy-Item -LiteralPath (Join-Path $gameRoot ('src\P90.Loader\bin\Release\net6.0\' + $name + '.dll')) -Destination $destination
    }
    foreach ($name in @('P90.Samples.Lifecycle', 'P90.Samples.Diagnostics', 'P90.Samples.ServerDiagnostics')) {
        Copy-Item -LiteralPath (Join-Path $gameRoot ('src\' + $name + '\bin\Release\net6.0\' + $name + '.dll')) -Destination $sampleDir
    }
    $probe = Join-Path $gameRoot 'BepInEx\plugins\P90.BootstrapProbe\P90.BootstrapProbe.dll'
    if (Test-Path -LiteralPath $probe) {
        $archive = Join-Path $gameRoot 'research\stage02\disabled'
        New-Item -ItemType Directory -Path $archive -Force | Out-Null
        Move-Item -LiteralPath $probe -Destination (Join-Path $archive 'P90.BootstrapProbe.dll') -Force
    }
    Get-ChildItem -LiteralPath $destination,$sampleDir -Filter '*.dll' | Get-FileHash
}
