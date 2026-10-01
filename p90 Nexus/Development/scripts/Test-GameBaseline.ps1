$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $gameRoot 'SCP Project 90.exe'
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game before testing.' }
$env:DOTNET_CLI_HOME = Join-Path $gameRoot 'research\tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_PACKAGES = Join-Path $gameRoot 'research\tools\nuget-packages'
$project = Join-Path $gameRoot 'src\P90.GameBaseline\P90.GameBaseline.csproj'
& dotnet restore $project --configfile (Join-Path $gameRoot 'src\NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Baseline restore failed.' }
& dotnet build $project -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
$testDir = Join-Path $gameRoot ('research\stage04\baseline-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$loaderPath = Join-Path $gameRoot 'BepInEx\plugins\P90.Loader\P90.Loader.dll'
$savedLoader = Join-Path $testDir 'P90.Loader.dll.disabled'
$baselinePath = Join-Path $gameRoot 'BepInEx\plugins\P90.GameBaseline.dll'
if (Test-Path -LiteralPath $baselinePath) { throw 'Unexpected existing baseline module.' }
$originalHash = (Get-FileHash -LiteralPath $loaderPath).Hash
$process = $null
Move-Item -LiteralPath $loaderPath -Destination $savedLoader
try {
    Copy-Item -LiteralPath (Join-Path $gameRoot 'src\P90.GameBaseline\bin\Release\net6.0\P90.GameBaseline.dll') -Destination $baselinePath
    $arguments = '-screen-fullscreen 0 -screen-width 800 -screen-height 600 --p90-game-baseline -logFile "{0}"' -f (Join-Path $testDir 'player.log')
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
    $processStart = $process.StartTime.ToUniversalTime()
    $exited = $process.WaitForExit(45000)
    $result = [ordered]@{ Pid=$process.Id; Exited=$exited; ExitCode=$null; LoaderRestored=$false }
    if ($exited) { $result.ExitCode=$process.ExitCode }
} finally {
    if ($process -and -not $process.HasExited) {
        $exact = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($exact -and $exact.Path -eq $exe -and $exact.StartTime.ToUniversalTime() -eq $processStart) { Stop-Process -Id $exact.Id; $exact.WaitForExit(5000) | Out-Null }
    }
    if (Test-Path -LiteralPath $baselinePath) { Move-Item -LiteralPath $baselinePath -Destination (Join-Path $testDir 'P90.GameBaseline.dll.disabled') }
    Move-Item -LiteralPath $savedLoader -Destination $loaderPath
    Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination (Join-Path $testDir 'bepinex.log')
}
$result.LoaderRestored = (Get-FileHash -LiteralPath $loaderPath).Hash -eq $originalHash
$log = Get-Content -LiteralPath (Join-Path $testDir 'bepinex.log') -Raw
$playerLog = Get-Content -LiteralPath (Join-Path $testDir 'player.log') -Raw
$result['BaselineDone'] = $log.Contains('BASELINE_DONE')
$result['P90CoreLoaded'] = $log.Contains('Loading [P90 Core')
$result['GuitarExceptions'] = ([regex]::Matches($playerLog,'at GuitarItem\.Update')).Count
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding utf8
$result | ConvertTo-Json
if (-not $result.Exited -or $result.ExitCode -ne 0 -or -not $result.LoaderRestored -or -not $result.BaselineDone -or $result.P90CoreLoaded) { throw ('Invalid baseline run: ' + $testDir) }
