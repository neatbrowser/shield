<#
.SYNOPSIS
  Downloads the latest neat-filters release into filters\ so the first start works without internet.
  The app copies and updates its own filters later; this is only the starting point.
#>
param(
    [string] $BaseUrl = 'https://github.com/neatbrowser/neat-filters/releases/latest/download/'
)

$ErrorActionPreference = 'Stop'
if (-not $BaseUrl.EndsWith('/')) { $BaseUrl += '/' }
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'filters'
New-Item -ItemType Directory -Force $dest | Out-Null

$manifestPath = Join-Path $dest 'manifest.json'
Invoke-WebRequest ($BaseUrl + 'manifest.json') -OutFile $manifestPath
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1) { throw "Unsupported manifest schema $($manifest.schema)." }

foreach ($f in $manifest.files) {
    if ($f.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "Unsafe file name in manifest: $($f.name)" }
    $path = Join-Path $dest $f.name
    Invoke-WebRequest ($BaseUrl + $f.name) -OutFile $path
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $f.sha256.ToLower()) { Remove-Item $path -Force; throw "SHA-256 mismatch for $($f.name)." }
    if ((Get-Item $path).Length -ne $f.size) { Remove-Item $path -Force; throw "Size mismatch for $($f.name)." }
    Write-Host "ok  $($f.name)"
}

Write-Host "Filters $($manifest.version) saved to $dest"
