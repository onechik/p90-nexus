param([ValidateRange(5, 120)][int]$Seconds = 15)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $gameRoot 'SCP Project 90.exe'
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) {
    throw 'The game is already running. Close that session before testing.'
}
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$testDir = Join-Path $gameRoot ('research\stage02\tests\' + $stamp)
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$playerLog = Join-Path $testDir 'player.log'
$started = [DateTime]::UtcNow
# Deliberately NOT batchmode: this game enters a dedicated path in batchmode.
$arguments = '-screen-fullscreen 0 -screen-width 800 -screen-height 600 --p90-probe-exit-seconds={0} -logFile "{1}"' -f $Seconds,$playerLog
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$processStart = $process.StartTime.ToUniversalTime()
$exited = $process.WaitForExit(45000)
$result = [ordered]@{
    StartedUtc = $started.ToString('o'); Pid = $process.Id
    Exited = $exited; ExitCode = $null; ProbeLog = $null; Passed = $false
}
if ($exited) { $result.ExitCode = $process.ExitCode }
else {
    # Only our exact process, never an unrelated game session.
    $stillRunning = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($stillRunning -and $stillRunning.Path -eq $exe -and $stillRunning.StartTime.ToUniversalTime() -eq $processStart) {
        Stop-Process -Id $stillRunning.Id -ErrorAction Stop
        $stillRunning.WaitForExit(5000) | Out-Null
    }
    $result['Failure'] = 'Timed out before graceful exit; diagnostic process was stopped.'
}
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination (Join-Path $testDir 'bepinex.log')
$runsDir = Join-Path $gameRoot 'research\stage02\runs'
$probeFiles = @(Get-ChildItem -LiteralPath $runsDir -Filter 'probe.jsonl' -Recurse -ErrorAction SilentlyContinue | Where-Object {
    $_.Directory.Name.EndsWith('-' + $process.Id) -and $_.LastWriteTimeUtc -ge $started
})
if ($probeFiles.Count -eq 1) {
    $result.ProbeLog = $probeFiles[0].FullName
    Copy-Item -LiteralPath $probeFiles[0].FullName -Destination (Join-Path $testDir 'probe.jsonl')
    $events = @(Get-Content -LiteralPath $probeFiles[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
    $required = @('managed_loaded','interop_ready','component_registered','unity_update','quit_requested','application_quit')
    $missing = @($required | Where-Object { $_ -notin $events.kind })
    $updates = @($events | Where-Object kind -eq 'unity_update')
    $loads = @($events | Where-Object kind -eq 'managed_loaded')
    $quits = @($events | Where-Object kind -eq 'application_quit')
    $sameThread = $loads.Count -eq 1 -and @($updates | Where-Object { $_.details.thread -ne $loads[0].details.mainThread }).Count -eq 0
    $result['Scenes'] = @($updates.details.scene | Select-Object -Unique)
    $result['MissingEvents'] = $missing
    $result['UnityUpdatesObserved'] = $updates.Count
    $result['SameThread'] = $sameThread
    $result.Passed = $exited -and $process.ExitCode -eq 0 -and $missing.Count -eq 0 -and $loads.Count -eq 1 -and $quits.Count -eq 1 -and $updates.Count -ge 2 -and $sameThread -and 'probe_failed' -notin $events.kind
} else { $result['Failure'] = 'Expected exactly one diagnostic log for this process.' }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 5
if (-not $result.Passed) { throw ('Probe test failed; inspect ' + $testDir) }
