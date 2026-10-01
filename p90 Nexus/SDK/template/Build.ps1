$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_NOLOGO = '1'
    & dotnet restore (Join-Path $PSScriptRoot 'Plugin.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build (Join-Path $PSScriptRoot 'Plugin.csproj') -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $pluginFolder=Join-Path $PSScriptRoot 'dist\__NAME__'
    New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net6.0') -Filter '*.dll' | Where-Object Name -ne 'P90.API.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $pluginFolder -Force}
    Write-Host ('Copy this entire folder into P90\plugins: '+$pluginFolder)
} finally { Pop-Location }
