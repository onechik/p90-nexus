param([ValidateRange(5, 30)][int]$Seconds = 15, [ValidateSet('stage03','stage06')][string]$EvidenceStage = 'stage03')
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $gameRoot 'SCP Project 90.exe'
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game before testing.' }
$testDir = Join-Path $gameRoot ('research\' + $EvidenceStage + '\game-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$started = [DateTime]::UtcNow
$playerLog = Join-Path $testDir 'player.log'
$arguments = '-screen-fullscreen 0 -screen-width 800 -screen-height 600 --p90-core-selftest --p90-core-exit-seconds={0} -logFile "{1}"' -f $Seconds,$playerLog
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$processStart = $process.StartTime.ToUniversalTime()
$exited = $process.WaitForExit(45000)
$result = [ordered]@{ StartedUtc = $started.ToString('o'); Pid = $process.Id; Exited = $exited; ExitCode = $null; Passed = $false }
if ($exited) { $result.ExitCode = $process.ExitCode }
else {
    $stillRunning = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($stillRunning -and $stillRunning.Path -eq $exe -and $stillRunning.StartTime.ToUniversalTime() -eq $processStart) {
        Stop-Process -Id $stillRunning.Id
        $stillRunning.WaitForExit(5000) | Out-Null
    }
    $result['Failure'] = 'Timed out; stopped only the diagnostic process.'
}
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination (Join-Path $testDir 'bepinex.log')
$files = @(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'P90\logs\runs') -Filter 'core.jsonl' -Recurse -ErrorAction SilentlyContinue | Where-Object {
    $_.Directory.Name.EndsWith('-' + $process.Id) -and $_.LastWriteTimeUtc -ge $started
})
if ($files.Count -eq 1) {
    Copy-Item -LiteralPath $files[0].FullName -Destination (Join-Path $testDir 'core.jsonl')
    $events = @(Get-Content -LiteralPath $files[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
    $loads = @($events | Where-Object kind -eq 'core_loaded')
    $checks = @($events | Where-Object kind -eq 'selftest_check')
    $quits = @($events | Where-Object kind -eq 'application_quit')
    $updates = @($events | Where-Object kind -eq 'unity_update')
    $sameThread = $loads.Count -eq 1 -and $updates.Count -ge 2 -and @($updates | Where-Object { $_.details.thread -ne $loads[0].details.thread }).Count -eq 0
    $cleanQuit = $quits.Count -eq 1 -and $quits[0].details.commands -eq 0 -and $quits[0].details.subscriptions -eq 0
    $result['Checks'] = $checks.Count
    $result['SameThread'] = $sameThread
    $result['CleanQuit'] = $cleanQuit
    $result['Scenes'] = @($updates.details.scene | Select-Object -Unique)
    $result.Passed = $exited -and $process.ExitCode -eq 0 -and $sameThread -and $cleanQuit -and $checks.Count -eq 11 -and @($checks | Where-Object { -not $_.details.passed }).Count -eq 0 -and 'core_selftest_passed' -in $events.kind -and 'quit_requested' -in $events.kind -and 'core_failed' -notin $events.kind
} else { $result['Failure'] = 'Expected exactly one core log for this process.' }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 6
if (-not $result.Passed) { throw ('Core game test failed: ' + $testDir) }
