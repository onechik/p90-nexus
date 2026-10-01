$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $gameRoot 'SCP Project 90.exe'
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game before testing.' }
$testDir = Join-Path $gameRoot ('research\stage04\control-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$started = [DateTime]::UtcNow
$arguments = '-screen-fullscreen 0 -screen-width 800 -screen-height 600 --p90-core-exit-seconds=20 -logFile "{0}"' -f (Join-Path $testDir 'player.log')
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$processStart = $process.StartTime.ToUniversalTime()
$checks = @()
$result = [ordered]@{ Pid=$process.Id; Passed=$false }
try {
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        $file = Join-Path $gameRoot 'P90\control\session.json'
        if (Test-Path -LiteralPath $file) {
            $session = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
            if ($session.Ready -and $session.ProcessId -eq $process.Id) { $ready=$true; break }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'No control session.' }
    $scenarios = @(
        @{Command='p90.help'; Args=@(); Expected=$true},
        @{Command='p90.plugins'; Args=@(); Expected=$true},
        @{Command='p90.commands'; Args=@(); Expected=$true},
        @{Command='server.status'; Args=@(); Expected=$true},
        @{Command='server.announce'; Args=@('Привет, Федор!'); Expected=$false},
        @{Command='p90.stop'; Args=@('sample.server'); Expected=$true},
        @{Command='server.status'; Args=@(); Expected=$false},
        @{Command='p90.start'; Args=@('sample.server'); Expected=$true},
        @{Command='p90.restart'; Args=@('sample.server'); Expected=$true},
        @{Command='server.status'; Args=@(); Expected=$true},
        @{Command='sample.hello'; Args=@(); Expected=$true}
    )
    foreach ($scenario in $scenarios) {
        $reply = & (Join-Path $PSScriptRoot 'P90.ps1') -Command $scenario.Command -Arguments $scenario.Args
        $checks += [pscustomobject]@{ Command=$scenario.Command; Passed=($reply.Success -eq $scenario.Expected); Reply=$reply }
    }
    $result['Exited'] = $process.WaitForExit(30000)
    if ($process.HasExited) { $result['ExitCode']=$process.ExitCode }
} finally {
    if (-not $process.HasExited) {
        $exact = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($exact -and $exact.Path -eq $exe -and $exact.StartTime.ToUniversalTime() -eq $processStart) { Stop-Process -Id $exact.Id; $exact.WaitForExit(5000) | Out-Null }
    }
    Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination (Join-Path $testDir 'bepinex.log')
    $files = @(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'P90\logs\runs') -Filter core.jsonl -Recurse | Where-Object { $_.Directory.Name.EndsWith('-' + $process.Id) -and $_.LastWriteTimeUtc -ge $started })
    if ($files.Count -eq 1) {
        Copy-Item -LiteralPath $files[0].FullName -Destination (Join-Path $testDir 'core.jsonl')
        $events = @(Get-Content $files[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
        $quit = @($events | Where-Object kind -eq application_quit)
        $result['CleanQuit'] = $quit.Count -eq 1 -and $quit[0].details.commands -eq 0 -and $quit[0].details.subscriptions -eq 0
    }
    $result['Checks']=$checks
    $result.Passed = $result.Exited -and $result.ExitCode -eq 0 -and $result.CleanQuit -and $checks.Count -eq 11 -and @($checks | Where-Object { -not $_.Passed }).Count -eq 0
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 7
}
if (-not $result.Passed) { throw ('Control test failed: ' + $testDir) }
