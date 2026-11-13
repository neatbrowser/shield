<#
.SYNOPSIS
  Puts neat_adblock.dll (the filter engine) into native\ so the build can copy it next to the app.

.DESCRIPTION
  Default: downloads the file from the latest release of neatbrowser/neat-adblock and checks its SHA-256
  against the .sha256 file published beside it.
  -Build: builds it from source instead (needs git and the Rust toolchain with the MSVC target).

.EXAMPLE
  ./tools/fetch-native.ps1                 # x64 download
  ./tools/fetch-native.ps1 -Arch arm64
  ./tools/fetch-native.ps1 -Build
#>
param(
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64',
    [switch] $Build,
    [string] $Repo = 'neatbrowser/neat-adblock'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$native = Join-Path $root 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$target = Join-Path $native 'neat_adblock.dll'

if ($Build) {
    $triple = if ($Arch -eq 'arm64') { 'aarch64-pc-windows-msvc' } else { 'x86_64-pc-windows-msvc' }
    $work = Join-Path ([IO.Path]::GetTempPath()) ('neat-adblock-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    git clone --depth 1 "https://github.com/$Repo.git" $work
    Push-Location $work
    try {
        rustup target add $triple
        cargo build --release --locked --target $triple
        Copy-Item "target/$triple/release/neat_adblock.dll" $target -Force
    }
    finally {
        Pop-Location
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}
else {
    $name = if ($Arch -eq 'arm64') { 'neat_adblock-arm64.dll' } else { 'neat_adblock.dll' }
    $base = "https://github.com/$Repo/releases/latest/download"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) $name
    Invoke-WebRequest "$base/$name" -OutFile $tmp
    # GitHub serves the .sha256 asset as application/octet-stream, so .Content is byte[] and must be decoded.
    $raw = (Invoke-WebRequest "$base/$name.sha256").Content
    if ($raw -is [byte[]]) { $raw = [Text.Encoding]::ASCII.GetString($raw) }
    $expected = (([string]$raw).Trim() -split '\s+')[0].ToLower()
    $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
    if ($expected -ne $actual) {
        Remove-Item $tmp -Force
        throw "SHA-256 mismatch for $name (expected $expected, got $actual). Not using the file."
    }
    Move-Item $tmp $target -Force
}

Write-Host "OK: $target ($((Get-Item $target).Length) bytes)"
