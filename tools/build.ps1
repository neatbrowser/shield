<#
.SYNOPSIS
  Builds NeatShield (app + command-line tool) into dist\<arch>\ on Windows.
#>
param(
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64',
    [string] $Configuration = 'Release',
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
dotnet run --project tests/NeatShield.Core.Tests -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

dotnet publish src/NeatShield.App -c $Configuration -r $rid -p:Platform=$platform -o $out
dotnet publish src/NeatShield.Cli -c $Configuration -r $rid --self-contained false -o "$out/cli"
Write-Host "Built: $out\NeatShield.exe"
