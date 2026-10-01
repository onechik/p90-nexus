$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'P90-Console.ps1') -LibraryOnly
$checks = New-Object 'System.Collections.Generic.List[string]'
function Check([string]$Name, [bool]$Passed) {
    if (-not $Passed) { throw "FAILED: $Name" }
    $checks.Add($Name)
}
$request = ConvertFrom-P90ConsoleLine '  plugins  '
Check 'plugins alias' ($request.Command -eq 'p90.plugins' -and $request.Arguments.Count -eq 0)
$aliases = @{pl='p90.plugins';com='p90.commands';h='help';ex='exit';res='p90.restart';st='p90.start';sp='p90.stop';stat='server.status';ppl='server.players';ann='server.announce';cl='server.clear'}
foreach ($alias in $aliases.Keys) { Check ('short alias ' + $alias) ((ConvertFrom-P90ConsoleLine $alias).Command -eq $aliases[$alias]) }
$request = ConvertFrom-P90ConsoleLine 'restart sample.server'
Check 'lifecycle argument' ($request.Command -eq 'p90.restart' -and $request.Arguments[0] -eq 'sample.server')
$request = ConvertFrom-P90ConsoleLine 'announce Привет, Федор!'
Check 'literal announcement' ($request.Arguments.Count -eq 1 -and $request.Arguments[0] -eq 'Привет, Федор!')
$request = ConvertFrom-P90ConsoleLine 'server.announce ""'
Check 'empty announcement' ($request.Arguments.Count -eq 1 -and $request.Arguments[0] -eq '')
$request = ConvertFrom-P90ConsoleLine 'custom.test "two words" '''' simple'
Check 'custom command quoted arguments' ($request.Arguments.Count -eq 3 -and $request.Arguments[0] -eq 'two words' -and $request.Arguments[1] -eq '')
$literal = '$(throw ''must not execute''); & exit | anything'
$request = ConvertFrom-P90ConsoleLine ('announce ' + $literal)
Check 'shell syntax remains text' ($request.Arguments[0] -ceq $literal)
$rejected = $false
try { ConvertFrom-P90ConsoleLine 'custom.test "unclosed' | Out-Null } catch { $rejected = $true }
Check 'unclosed quote rejected' $rejected
Check 'blank line' ($null -eq (ConvertFrom-P90ConsoleLine '   '))
Check 'clear preserves announcement command' ((ConvertFrom-P90ConsoleLine 'clear').Command -eq 'server.clear')
Check 'friendly offline message' ((Get-P90ConsoleError 'Start the game with P90 first.') -match 'Запусти')

# Exercise the actual interactive loop with a simulated transport, without touching the game.
$run = Join-Path $root ('research/console-tests/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'P90-Console.ps1') $run
$stub = @'
param([string]$Command,[string[]]$Arguments,[string]$GamePath)
if ($Command -eq 'offline') { throw 'Start the game with P90 first.' }
if ($Command -eq 'p90.stop') { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'stopped'), 'yes') }
if ($Command -eq 'p90.start') { Remove-Item (Join-Path $PSScriptRoot 'stopped') -ErrorAction SilentlyContinue }
if ($Command -eq 'p90.commandinfo') {
    $list = if (Test-Path (Join-Path $PSScriptRoot 'stopped')) { '' } else { 'custom.discovered' }
    Add-Content (Join-Path $PSScriptRoot 'discovery.txt') ('LIST:' + $list)
    $json = if ($list) { '[{"Name":"custom.discovered","PluginId":"owner.actual","Description":"Custom description","Usage":"custom.discovered ARG"}]' } else { '[]' }
    return [pscustomobject]@{Success=$true;Message=$json}
}
if ($Command -eq 'p90.plugins') { return [pscustomobject]@{Success=$true;Message='[{"Id":"sample.server","Version":"0.2.0","State":"Active"}]'} }
if ($Command -eq 'fail') { return [pscustomobject]@{Success=$false;Message='Expected refusal'} }
return [pscustomobject]@{Success=$true;Message=('ECHO:'+$Command+':'+($Arguments -join '/'))}
'@
[IO.File]::WriteAllText((Join-Path $run 'P90.ps1'),$stub)
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = 'powershell.exe'
$info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $run 'P90-Console.ps1') + '"'
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
foreach ($line in @('plugins','offline','fail','custom.test "two words" next','help','stop sample.server','start sample.server','commands','custom.discovered','exit')) { $process.StandardInput.WriteLine($line) }
$process.StandardInput.Close()
if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Console test timed out.' }
$output = $outputTask.Result
$stderr = $errorTask.Result
Check 'interactive exit' ($process.ExitCode -eq 0 -and $stderr.Length -eq 0)
Check 'interactive plugin list' ($output -match 'sample.server')
Check 'negative response shown' ($output -match 'Expected refusal')
Check 'loop continues after offline error' ($output -match 'ECHO:custom.test:two words/next')
Check 'discovered command shown by help' ($output -match '(?m)^\s+custom\.discovered\s*$')
Check 'discovered command executes without console changes' ($output -match 'ECHO:custom.discovered:')
Check 'actual owner shown independently of name' ($output -match 'owner.actual')
Check 'plugin description shown' ($output -match 'Custom description')
Check 'plugin usage shown' ($output -match 'custom.discovered ARG')
$discovery = @(Get-Content (Join-Path $run 'discovery.txt'))
Check 'live refresh on startup help stop start commands' (($discovery -join '|') -eq 'LIST:custom.discovered|LIST:custom.discovered|LIST:|LIST:custom.discovered|LIST:custom.discovered')
[ordered]@{Passed=$true;Shell=$PSVersionTable.PSVersion.ToString();Checks=$checks.ToArray();InteractiveTransport='Simulated; no game commands sent'} | ConvertTo-Json | Set-Content (Join-Path $run 'result.json') -Encoding utf8
Get-Content (Join-Path $run 'result.json')
