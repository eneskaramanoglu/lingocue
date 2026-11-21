# Packs a self-contained win-x64 publish folder with Velopack: Setup.exe (per-user install in
# %LocalAppData%\LingoCue, no admin rights), a portable zip, the full/delta .nupkg and releases.win.json.
# Used by release.yml; also handy locally to test install and update end to end.
#
# Usage: build/windows/pack.ps1 -PublishDir publish/win-x64 -Version 1.0.0 -OutputDir artifacts/velopack [-Vpk vpk]
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutputDir,
    [string] $Vpk = 'vpk',
    [string] $ReleaseNotes
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if (-not (Test-Path (Join-Path $PublishDir 'LingoCue.exe'))) { throw "LingoCue.exe missing from $PublishDir" }
if (-not (Test-Path (Join-Path $PublishDir 'libmpv-2.dll'))) { throw "libmpv-2.dll missing from $PublishDir" }

$vpkArgs = @(
    'pack',
    '--packId', 'LingoCue',
    '--packVersion', $Version,
    '--packDir', $PublishDir,
    '--mainExe', 'LingoCue.exe',
    '--packTitle', 'LingoCue',
    '--packAuthors', ('Enes Karamano' + [char]0x11F + 'lu'), # spelled with a code point: Windows PowerShell reads BOM-less files as ANSI
    '--icon', (Join-Path $root 'src\LingoCue.App\Assets\lingocue.ico'),
    '--shortcuts', 'Desktop,StartMenuRoot',
    '--outputDir', $OutputDir
)
if ($ReleaseNotes) { $vpkArgs += @('--releaseNotes', $ReleaseNotes) }

& $Vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }
Get-ChildItem $OutputDir | ForEach-Object { "{0,12:N0}  {1}" -f $_.Length, $_.Name }
