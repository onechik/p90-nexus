# Requires an already running game; only p90.plugins is sent, no lifecycle commands.
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path $PSScriptRoot -Parent
$session = Get-Content -LiteralPath (Join-Path $gameRoot 'P90\control\session.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$actual = Microsoft.PowerShell.Management\Get-Process -Id $session.ProcessId
$actualStart = $actual.StartTime
$actualPath = $actual.Path
$checks = @()
$reply = & (Join-Path $PSScriptRoot 'P90.ps1') p90.plugins
if (-not $reply.Success) { throw 'Live p90.plugins failed.' }
$checks += 'live-p90.plugins'
try {
    foreach ($case in @('missing', 'wrong-path', 'reused-pid')) {
        $script:CompatibilityProcess = [pscustomobject]@{ Path=$actualPath; StartTime=$actualStart }
        $expected = 'different process start'
        if ($case -eq 'missing') { $script:CompatibilityProcess=$null; $expected='no longer running' }
        if ($case -eq 'wrong-path') { $script:CompatibilityProcess.Path='D:\other\SCP Project 90.exe'; $expected='does not point to this game copy' }
        if ($case -eq 'reused-pid') { $script:CompatibilityProcess.StartTime=$actualStart.AddSeconds(10) }
        $fixture = $script:CompatibilityProcess
        Set-Item -LiteralPath Function:Get-Process -Value ({ param($Id, $ErrorAction) return $fixture }.GetNewClosure())
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'P90.ps1') p90.plugins | Out-Null }
        catch { if ($_.Exception.Message.Contains($expected)) { $rejected=$true } else { throw } }
        if (-not $rejected) { throw ('Process guard failed: ' + $case) }
        $checks += $case
    }
} finally { Remove-Item Function:\Get-Process }
$result = [ordered]@{
    Version=$PSVersionTable.PSVersion.ToString(); Pid=$session.ProcessId
    JsonTimestampType=$session.ProcessStartedUtc.GetType().FullName
    NormalizedDeltaSeconds=($actualStart.ToUniversalTime()-([DateTime]$session.ProcessStartedUtc).ToUniversalTime()).TotalSeconds
    Passed=$true; Checks=$checks
}
$folder = Join-Path $gameRoot 'research\stage04\powershell-compatibility'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder ($result.Version + '.json')) -Encoding UTF8
$result | ConvertTo-Json
