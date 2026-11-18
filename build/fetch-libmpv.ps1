<#
.SYNOPSIS
  Downloads the pinned Windows libmpv build (shinchiro/mpv-winbuild-cmake) and extracts libmpv-2.dll.
  The DLL is not committed; the App build calls this script when the DLL is missing.
  To upgrade: change $Release/$Asset/$Sha256 together and record the change in docs/adr/0001-video-player.md.
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'native/win-x64')
)

$ErrorActionPreference = 'Stop'

$Release = '20261111'
$Asset   = 'mpv-dev-x86_64-20261111-git-bcde6b93cc.7z'
$Sha256  = '273c079b11ea5575822f972756f4c9a71149a5c10130353abeb8586738938452'
$Url     = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Release/$Asset"

$dll = Join-Path $Destination 'libmpv-2.dll'
if (Test-Path $dll) {
    Write-Host "libmpv already present: $dll"
    exit 0
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("lingocue-libmpv-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    $archive = Join-Path $work $Asset
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $archive -UseBasicParsing

    $actual = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) {
        throw "SHA256 mismatch for $Asset. Expected $Sha256, got $actual."
    }

    $extract = Join-Path $work 'x'
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    # Windows 10+ tar (libarchive) reads 7z.
    & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item (Join-Path $extract 'libmpv-2.dll') $dll
    Write-Host "libmpv extracted to $dll"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
