param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $gameRoot 'research\tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_PACKAGES = Join-Path $gameRoot 'research\tools\nuget-packages'
$project = Join-Path $gameRoot 'src\P90.BootstrapProbe\P90.BootstrapProbe.csproj'
& dotnet restore $project --configfile (Join-Path $gameRoot 'src\NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw "Probe restore failed: $LASTEXITCODE" }
& dotnet build $project -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw "Probe build failed: $LASTEXITCODE" }
if ($Deploy) {
    $destination = Join-Path $gameRoot 'BepInEx\plugins\P90.BootstrapProbe'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $gameRoot 'src\P90.BootstrapProbe\bin\Release\net6.0\P90.BootstrapProbe.dll') -Destination $destination
    Get-FileHash -LiteralPath (Join-Path $destination 'P90.BootstrapProbe.dll')
}
