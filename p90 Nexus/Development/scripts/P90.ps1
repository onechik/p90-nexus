param(
    [Parameter(Position=0)][string]$Command = 'p90.help',
    [Parameter(Position=1,ValueFromRemainingArguments=$true)][string[]]$Arguments = @(),
    [ValidateRange(1,30)][int]$TimeoutSeconds = 20,
    [string]$GamePath
)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
if ($GamePath) { $gameRoot = (Resolve-Path -LiteralPath $GamePath).ProviderPath.TrimEnd('\') }
$control = Join-Path $gameRoot 'P90\control'
$sessionFile = Join-Path $control 'session.json'
if (-not (Test-Path -LiteralPath $sessionFile)) { throw 'Start the game with P90 first.' }
$session = Get-Content -LiteralPath $sessionFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $session.Ready) { throw 'P90 is not running. Start the game first.' }
$gameProcess = Get-Process -Id $session.ProcessId -ErrorAction SilentlyContinue
if (-not $gameProcess) {
    throw 'The recorded game process is no longer running.'
}
if ($gameProcess.Path -ne (Join-Path $gameRoot 'SCP Project 90.exe')) {
    throw 'The recorded PID does not point to this game copy, or its executable path cannot be read.'
}
# Windows PowerShell 5.1 leaves JSON timestamps as strings; casting an ISO UTC
# string to DateTime produces local time. PowerShell 7 may already return UTC
# DateTime. Normalize BOTH operands before subtraction in either shell.
$recordedStartUtc = ([DateTime]$session.ProcessStartedUtc).ToUniversalTime()
$actualStartUtc = $gameProcess.StartTime.ToUniversalTime()
if ([Math]::Abs(($actualStartUtc - $recordedStartUtc).TotalSeconds) -gt 2) {
    throw ('The recorded PID belongs to a different process start. Recorded UTC: {0}; actual UTC: {1}.' -f $recordedStartUtc.ToString('o'),$actualStartUtc.ToString('o'))
}
$id = [Guid]::NewGuid().ToString('N')
$request = [ordered]@{ Id=$id; Session=$session.Session; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Command=$Command; Arguments=@($Arguments) }
$path = Join-Path $control ('inbox\' + $id + '.json')
$temporary = $path + '.tmp'
[IO.File]::WriteAllText($temporary, ($request | ConvertTo-Json -Compress -Depth 4), (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $temporary -Destination $path
$replyPath = Join-Path $control ('replies\' + $id + '.json')
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $replyPath) {
        $reply = Get-Content -LiteralPath $replyPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $reply | Select-Object Success,Message
        return
    }
    Start-Sleep -Milliseconds 100
}
throw "No reply before timeout. Request ID: $id. The command may still execute until its 30-second expiry; check P90/logs/core.jsonl before retrying."
