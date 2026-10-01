param([ValidateSet('stage04','stage06')][string]$EvidenceStage = 'stage04')
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $gameRoot 'SCP Project 90.exe'
if (Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue) { throw 'Close the game before testing.' }
$testDir = Join-Path $gameRoot ('research\' + $EvidenceStage + '\game-tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$started = [DateTime]::UtcNow
$playerLog = Join-Path $testDir 'player.log'
$arguments = '-screen-fullscreen 0 -screen-width 800 -screen-height 600 --p90-adapter-selftest --p90-core-exit-seconds=160 -logFile "{0}"' -f $playerLog
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$processStart = $process.StartTime.ToUniversalTime()
$result = [ordered]@{ StartedUtc=$started.ToString('o'); Pid=$process.Id; Passed=$false; Mailbox=$false }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    $requested = $false
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        $sessionFile = Join-Path $gameRoot 'P90\control\session.json'
        if (-not $requested -and (Test-Path -LiteralPath $sessionFile)) {
            $session = Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
            if ($session.ProcessId -eq $process.Id -and $session.Ready) {
                $requested = $true
                $reply = & (Join-Path $PSScriptRoot 'P90.ps1') p90.plugins
                $result.Mailbox = $reply.Success -and $reply.Message.Contains('sample.server')
                $reply | ConvertTo-Json | Set-Content (Join-Path $testDir 'mailbox-result.json') -Encoding utf8
            }
        }
        Start-Sleep -Milliseconds 200
    }
    $result['Exited'] = $process.HasExited
    if ($process.HasExited) { $result['ExitCode'] = $process.ExitCode }
} finally {
    if (-not $process.HasExited) {
        $exact = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($exact -and $exact.Path -eq $exe -and $exact.StartTime.ToUniversalTime() -eq $processStart) { Stop-Process -Id $exact.Id; $exact.WaitForExit(5000) | Out-Null }
    }
    Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination (Join-Path $testDir 'bepinex.log')
    $files = @(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'P90\logs\runs') -Filter core.jsonl -Recurse | Where-Object { $_.Directory.Name.EndsWith('-' + $process.Id) -and $_.LastWriteTimeUtc -ge $started })
    if ($files.Count -eq 1) {
        Copy-Item -LiteralPath $files[0].FullName -Destination (Join-Path $testDir 'core.jsonl')
        $events = @(Get-Content -LiteralPath $files[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
        $checks = @($events | Where-Object kind -eq adapter_check)
        $quits = @($events | Where-Object kind -eq application_quit)
        $gameEvents = @($events | Where-Object kind -eq game_event)
        $result['Checks'] = $checks.Count
        $result['EventCounts'] = @($gameEvents | Group-Object { $_.details.type } | Select-Object Name,Count)
        $expectedEvents = @('ServerStarted','ServerStopped','PlayerAvailable','PlayerUnavailable')
        $result['LifecycleEventsOnce'] = @($expectedEvents | Where-Object { $type = $_; @($gameEvents | Where-Object { $_.details.type -eq $type }).Count -ne 2 }).Count -eq 0
        $loads = @($events | Where-Object kind -eq core_loaded)
        $result['SameThread'] = $loads.Count -eq 1 -and @($checks | Where-Object { $_.details.thread -ne $loads[0].details.thread }).Count -eq 0
        $result['CleanQuit'] = $quits.Count -eq 1 -and $quits[0].details.commands -eq 0 -and $quits[0].details.subscriptions -eq 0
        $result.Passed = $result.Exited -and $result.ExitCode -eq 0 -and $result.Mailbox -and $result.CleanQuit -and $result.LifecycleEventsOnce -and $result.SameThread -and $checks.Count -eq 25 -and @($checks | Where-Object { -not $_.details.passed }).Count -eq 0 -and 'adapter_selftest_passed' -in $events.kind -and 'core_failed' -notin $events.kind -and 'adapter_failed' -notin $events.kind
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testDir 'result.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 6
}
if (-not $result.Passed) { throw ('Adapter test failed: ' + $testDir) }
