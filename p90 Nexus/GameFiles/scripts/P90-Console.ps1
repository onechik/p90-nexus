param([switch]$LibraryOnly, [string]$GamePath)
$ErrorActionPreference = 'Stop'

function Test-P90ConsoleSession([string]$Root, $Process) {
    try {
        $session = Get-Content -LiteralPath (Join-Path $Root 'P90/control/session.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $session.Ready) { return $false }
        if (-not $Process) { $Process = Get-Process -Id $session.ProcessId -ErrorAction Stop }
        return ($Process.Id -eq $session.ProcessId -and $Process.Path -eq (Join-Path $Root 'SCP Project 90.exe') -and
            [Math]::Abs(($Process.StartTime.ToUniversalTime() - ([DateTime]$session.ProcessStartedUtc).ToUniversalTime()).TotalSeconds) -le 2)
    } catch { return $false }
}

function Resolve-P90ConsoleGame {
    if ($GamePath) { return (Resolve-Path -LiteralPath $GamePath).ProviderPath.TrimEnd('\') }
    $localRoot = Split-Path $PSScriptRoot -Parent
    if (Test-P90ConsoleSession $localRoot) { return $localRoot }
    $running = @(Get-Process -Name 'SCP Project 90' -ErrorAction SilentlyContinue)
    $candidates = @($running | ForEach-Object {
        if ($_.Path) {
            $root = Split-Path $_.Path -Parent
            if (Test-P90ConsoleSession $root $_) { $root }
        }
    } | Sort-Object -Unique)
    if ($candidates.Count -eq 1) { return $candidates[0] }
    if ($candidates.Count -gt 1) { throw ('Открыто несколько копий игры с P90. Открой консоль рядом с нужной игрой или укажи -GamePath. Найдены: ' + ($candidates -join '; ')) }
    if ($running.Count -gt 0) { throw 'Игра запущена, но активное подключение P90 не найдено. Дождись загрузки ядра; проверь установку Nexus в запущенной копии и одинаковые права доступа окон.' }
    return $localRoot
}

function Invoke-P90ConsoleCommand([string]$Command, [string[]]$Arguments = @()) {
    $target = Resolve-P90ConsoleGame
    if ($script:LastP90Target -ne $target) {
        Write-Host ('Подключение к игре: ' + $target) -ForegroundColor Cyan
        $script:LastP90Target = $target
    }
    & (Join-Path $PSScriptRoot 'P90.ps1') -Command $Command -Arguments $Arguments -GamePath $target
}

function ConvertFrom-P90ConsoleLine([string]$Line) {
    $lineText = $Line.Trim()
    if (-not $lineText) { return $null }
    $parts = $lineText -split '\s+', 2
    $name = $parts[0].ToLowerInvariant()
    $tail = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $aliases = @{
        pl='p90.plugins'; com='p90.commands'; h='help'; ex='exit'; res='p90.restart'
        st='p90.start'; sp='p90.stop'; stat='server.status'; ppl='server.players'
        ann='server.announce'; cl='server.clear'
        plugins='p90.plugins'; commands='p90.commands'; start='p90.start'
        stop='p90.stop'; restart='p90.restart'; status='server.status'
        players='server.players'; announce='server.announce'; clear='server.clear'
    }
    if ($aliases.ContainsKey($name)) { $name = $aliases[$name] }
    $arguments = New-Object 'System.Collections.Generic.List[string]'
    if ($name -eq 'server.announce') {
        # Announcement text is literal; quotes are optional. Never evaluate user input.
        if ($tail.Length -gt 0) {
            if ($tail.Length -ge 2 -and (($tail[0] -eq '"' -and $tail[$tail.Length-1] -eq '"') -or ($tail[0] -eq "'" -and $tail[$tail.Length-1] -eq "'"))) { $tail = $tail.Substring(1,$tail.Length-2) }
            $arguments.Add($tail)
        }
    } else {
        $buffer = New-Object Text.StringBuilder
        $quote = [char]0
        $inToken = $false
        foreach ($character in $tail.ToCharArray()) {
            if ($quote -ne [char]0) {
                if ($character -eq $quote) { $quote = [char]0 } else { [void]$buffer.Append($character) }
            } elseif ($character -eq '"' -or $character -eq "'") {
                $quote = $character; $inToken = $true
            } elseif ([char]::IsWhiteSpace($character)) {
                if ($inToken) { $arguments.Add($buffer.ToString()); [void]$buffer.Clear(); $inToken=$false }
            } else { [void]$buffer.Append($character); $inToken=$true }
        }
        if ($quote -ne [char]0) { throw 'Не закрыта кавычка. Пример: myplugin.command "текст с пробелами"' }
        if ($inToken) { $arguments.Add($buffer.ToString()) }
    }
    [pscustomobject]@{ Command=$name; Arguments=$arguments.ToArray() }
}

function Show-P90PluginCommands {
    try {
        $reply = Invoke-P90ConsoleCommand -Command 'p90.commandinfo'
        if (-not $reply.Success -and $reply.Message -eq 'Command not available: p90.commandinfo') {
            Write-Host 'Ядро старой версии: владельцы и описания появятся после обновления P90.' -ForegroundColor Yellow
            $legacy = Invoke-P90ConsoleCommand -Command 'p90.commands'
            if (-not $legacy.Success) { throw $legacy.Message }
            Write-Host $legacy.Message
            return
        }
        if (-not $reply.Success) { throw $reply.Message }
        $catalog = @($reply.Message | ConvertFrom-Json -ErrorAction Stop | ForEach-Object { $_ })
        Write-Host 'Команды, полученные из работающих плагинов:' -ForegroundColor Cyan
        if ($catalog.Count -eq 0) { Write-Host 'Сейчас ни один плагин не зарегистрировал команды.'; return }
        $shortNames = @{'server.status'='status'; 'server.players'='players'; 'server.announce'='announce'; 'server.clear'='clear'}
        foreach ($group in ($catalog | Group-Object PluginId | Sort-Object Name)) {
            Write-Host ('Плагин: ' + $group.Name) -ForegroundColor Green
            foreach ($entry in ($group.Group | Sort-Object Name)) {
                $name = [string]$entry.Name
                $hint = if ($shortNames.ContainsKey($name)) { '  (коротко: ' + $shortNames[$name] + ')' } else { '' }
                Write-Host ('  ' + $name + $hint)
                if ($entry.Description) { Write-Host ('    ' + ($entry.Description -replace "`n", "`n    ")) }
                else { Write-Host '    Автор не добавил описание.' }
                if ($entry.Usage) { Write-Host ('    Как использовать: ' + $entry.Usage) }
                else { Write-Host '    Способ использования не указан автором.' }
            }
        }
        Write-Host 'Вводи любое имя из списка и аргументы. Например: myplugin.command "текст"'
        Write-Host 'Список и описания запрашиваются заново по help или commands.'
    } catch {
        Write-Host ('Список команд пока недоступен: ' + (Get-P90ConsoleError $_.Exception.Message)) -ForegroundColor Yellow
        Write-Host 'После загрузки игры введи help или commands.'
    }
}

function Show-P90ConsoleHelp {
    Write-Host @'
  plugins / pl              Список плагинов
  stop / sp ID              Остановить (пример: sp sample.server)
  start / st ID             Включить плагин
  restart / res ID          Перезапустить и перечитать настройки
  commands / com            Команды установленных плагинов
  status / stat             Состояние сервера (sample.server)
  players / ppl             Игроки (sample.server)
  announce / ann ТЕКСТ       Объявление (sample.server)
  clear / cl                Убрать объявление (sample.server)
  cls                       Очистить это окно
  help / h                  Показать эту подсказку
  exit / ex                 Закрыть консоль (игра продолжит работать)

Вместо sample.server пиши ID из списка plugins.
Команды плагинов загружаются из игры, а не из фиксированного списка консоли.
Полные команды вроде p90.plugins тоже работают.
Для замены DLL нужно закрыть и снова запустить игру; restart её не заменяет.
'@
    Show-P90PluginCommands
}

function Show-P90ConsoleReply($Request, $Reply) {
    if (-not $Reply.Success) { Write-Host ('Не выполнено: ' + $Reply.Message) -ForegroundColor Yellow; return }
    if ($Request.Command -in @('p90.plugins','server.players','server.status')) {
        try { $data = $Reply.Message | ConvertFrom-Json -ErrorAction Stop } catch { Write-Host $Reply.Message; return }
        if ($Request.Command -eq 'p90.plugins') {
            if (-not $data) { Write-Host 'Плагины не найдены.'; return }
            $states = @{Active='Включён'; Stopped='Остановлен'; Faulted='Ошибка запуска'; Starting='Запускается'; Stopping='Останавливается'}
            @($data) | ForEach-Object { [pscustomobject]@{ 'ID плагина'=$_.Id; 'Версия'=$_.Version; 'Состояние'=$states[[string]$_.State] } } | Format-Table -AutoSize | Out-Host
        } elseif ($Request.Command -eq 'server.players') {
            if (-not $data) { Write-Host 'Готовых игроков пока нет.'; return }
            @($data) | Select-Object @{n='Имя';e={$_.Name}},ConnectionId,NetId | Format-Table -AutoSize | Out-Host
        } else {
            Write-Host ('Сборка поддерживается: ' + $(if ($data.Supported) {'да'} else {'нет'}))
            Write-Host ('Ты сейчас хост: ' + $(if ($data.IsServer) {'да'} else {'нет'}))
            Write-Host ('Сцена: ' + $data.Scene)
            Write-Host ('Готовых игроков: ' + @($data.Players).Count)
            if ($data.DisabledReason) { Write-Host ('Причина недоступности: ' + $data.DisabledReason) }
        }
    } elseif ($Request.Command -in @('p90.stop','p90.start','p90.restart')) {
        Write-Host ('Готово: ' + $Request.Command + ' ' + ($Request.Arguments -join ' ')) -ForegroundColor Green
    } elseif ($Request.Command -in @('server.announce','server.clear')) {
        Write-Host 'Готово: объявление отправлено в игру.' -ForegroundColor Green
    } else { Write-Host $Reply.Message }
}

function Get-P90ConsoleError([string]$Message) {
    if ($Message -match 'Start the game with P90 first|P90 is not running|recorded game process is no longer running') {
        return 'Игра с P90 ещё не запущена или уже закрыта. Запусти её, дождись меню и повтори команду. Консоль можно оставить открытой.'
    }
    if ($Message -match 'recorded PID|recorded.*process start') {
        return 'Консоль и игра относятся к разным запускам или копиям. Открой P90 Console.cmd рядом с запущенным EXE игры. Подробности: ' + $Message
    }
    if ($Message -match 'No reply before timeout') {
        return 'Игра не ответила вовремя. Команда ещё может выполниться: проверь результат в игре перед повтором. Подробности: ' + $Message
    }
    return $Message
}

if ($LibraryOnly) { return }
try { $Host.UI.RawUI.WindowTitle = 'P90 Nexus — управление плагинами' } catch { }
Write-Host 'P90 Nexus — управление плагинами' -ForegroundColor Cyan
Write-Host ('Папка консоли: ' + (Split-Path $PSScriptRoot -Parent))
Write-Host 'Эту консоль можно открыть до запуска игры. Для начала введи plugins.'
Show-P90ConsoleHelp
while ($true) {
    try {
        $line = Read-Host 'P90'
        if ($null -eq $line) { break }
        $request = ConvertFrom-P90ConsoleLine $line
        if ($null -eq $request) { continue }
        if ($request.Command -in @('exit','quit')) { break }
        if ($request.Command -in @('help','?','p90.help')) { Show-P90ConsoleHelp; continue }
        if ($request.Command -eq 'cls') { Clear-Host; continue }
        if ($request.Command -eq 'p90.commands') { Show-P90PluginCommands; continue }
        if ($request.Command -in @('p90.stop','p90.start','p90.restart') -and $request.Arguments.Count -ne 1) {
            Write-Host 'Укажи один ID плагина. Пример: stop sample.server. Список ID: plugins.' -ForegroundColor Yellow
            continue
        }
        $reply = Invoke-P90ConsoleCommand -Command $request.Command -Arguments $request.Arguments
        Show-P90ConsoleReply $request $reply
        if ($reply.Success -and $request.Command -in @('p90.stop','p90.start','p90.restart')) { Show-P90PluginCommands }
    } catch {
        Write-Host ('Ошибка: ' + (Get-P90ConsoleError $_.Exception.Message)) -ForegroundColor Yellow
    }
}
