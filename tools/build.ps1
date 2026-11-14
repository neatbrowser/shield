<#
.SYNOPSIS
  Builds NeatShield (app + command-line tool) into dist\<arch>\ on Windows.
#>
param(
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64',
    [string] $Configuration = 'Release',
    # Version stamped into the app, e.g. 0.3.0 or 0.3.0-rc.1 (the release workflow passes the tag without its "v").
    [string] $Version = '',
    [switch] $SkipFetch
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipFetch) {
    & "$PSScriptRoot/fetch-native.ps1" -Arch $Arch
    & "$PSScriptRoot/fetch-filters.ps1"
}

$rid = "win-$Arch"
$platform = if ($Arch -eq "arm64") { "ARM64" } else { "x64" }
$out = Join-Path $root "dist/$Arch"
# Typed array + plain assignment on purpose: "$x = if (...) { @('a') }" would turn the one-element array into a bare
# string, and splatting a string passes every character as a separate argument (MSB1001 "Unknown switch").
[string[]] $versionArg = @()
if ($Version) { $versionArg = @("-p:Version=$Version") }
dotnet run --project tests/NeatShield.Core.Tests -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# Tests of the script that is injected into pages (needs Node.js; skipped when it is not installed).
if (Get-Command npm -ErrorAction SilentlyContinue) {
    Push-Location (Join-Path $root 'tests/js')
    try {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
        npm test
        if ($LASTEXITCODE -ne 0) { throw 'The page-script tests failed.' }
    }
    finally { Pop-Location }
}
else {
    Write-Warning 'Node.js was not found: skipping the page-script tests (tests/js).'
}

dotnet publish src/NeatShield.App -c $Configuration -r $rid -p:Platform=$platform @versionArg -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publishing the app failed.' }
dotnet publish src/NeatShield.Cli -c $Configuration -r $rid --self-contained false @versionArg -o "$out/cli"
if ($LASTEXITCODE -ne 0) { throw 'Publishing the command-line tool failed.' }
Write-Host "Built: $out\NeatShield.exe"
