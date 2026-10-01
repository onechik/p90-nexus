[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($GamePath)) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker=New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description='Выбери папку игры с файлом SCP Project 90.exe'
    $picker.ShowNewFolderButton=$false
    try {
        if($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){Write-Host 'Установка отменена.';exit 0}
        $GamePath=$picker.SelectedPath
    } finally {$picker.Dispose()}
}
$root=(Resolve-Path -LiteralPath $GamePath).ProviderPath
if(Test-Path -LiteralPath (Join-Path $root 'BepInEx/plugins/P90.Loader/P90.Core.dll')) {
    Write-Host 'Обновление P90 Nexus...'
    & (Join-Path $PSScriptRoot 'Update.ps1') -GamePath $root
} else {
    Write-Host 'Установка P90 Nexus...'
    & (Join-Path $PSScriptRoot 'Install.ps1') -GamePath $root
}
Write-Host 'Готово! Теперь можно скопировать папку плагина в P90\plugins и запустить игру.' -ForegroundColor Green
