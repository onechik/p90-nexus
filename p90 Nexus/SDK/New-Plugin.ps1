param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Z][A-Za-z0-9]{2,39}$')][string]$Name,
    [Parameter(Mandatory=$true)][string]$Id,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
if ($Id -cnotmatch '\A[a-z][a-z0-9.-]{2,46}\z' -or $Id -match '\A(core\z|p90\.|(?:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|\z))') { throw 'Choose a lowercase plugin ID, 3-47 characters, without reserved names/prefixes.' }
$api = Join-Path $PSScriptRoot 'lib\P90.API.dll'
if (-not (Test-Path -LiteralPath $api)) { throw 'SDK API library is missing. Run scripts/Build-Sdk.ps1 in the framework workspace.' }
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $target) { throw 'Destination already exists. Choose a new directory; existing files will not be overwritten.' }
New-Item -ItemType Directory -Path $target,(Join-Path $target 'lib') -Force | Out-Null
foreach ($file in @('Plugin.csproj','Plugin.cs','Build.ps1')) {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot ('template\' + $file)))
    $text = $text.Replace('__NAME__',$Name).Replace('__ID__',$Id)
    [IO.File]::WriteAllText((Join-Path $target $file),$text,(New-Object Text.UTF8Encoding($false)))
}
Copy-Item -LiteralPath $api -Destination (Join-Path $target 'lib\P90.API.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'global.json') -Destination $target
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NuGet.Config') -Destination $target
[pscustomobject]@{ Project=(Join-Path $target 'Plugin.csproj'); PluginId=$Id; BuildScript=(Join-Path $target 'Build.ps1') }
