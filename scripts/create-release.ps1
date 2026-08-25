<#
.SYNOPSIS
  Membuat GitHub Release v1.0.0 dan mengunggah installer MSI + checksum.

.PREREQ
  gh sudah terautentikasi: jalankan sekali  ->  gh auth login
  (pilih GitHub.com > HTTPS > Login with a web browser)

.USAGE
  powershell -ExecutionPolicy Bypass -File scripts\create-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$Repo = 'ardhikaxx/neyra-optimizer'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'

# 0. Auth check
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "gh belum login. Jalankan: gh auth login  (lalu ulangi script ini)." }

$tag = "v$Version"
$title = "Neyra Optimizer $tag"
$notes = Join-Path $artifacts 'RELEASE-NOTES.md'
if (-not (Test-Path $notes)) { throw "Release notes tidak ditemukan: $notes" }

# 1. Pastikan tag ada di remote
git fetch --tags --quiet
git rev-parse "refs/tags/$tag" *> $null
if ($LASTEXITCODE -ne 0) { throw "Tag $tag tidak ditemukan. Buat dulu: git tag -a $tag -m '...' && git push origin $tag" }

# 2. Buat release (idempoten: pakai yang sudah ada bila duplikat)
Write-Host "== Membuat release $tag =="
gh release create $tag --repo $Repo --title $title --notes-file $notes --target main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Release mungkin sudah ada — melanjutkan ke upload aset..." -ForegroundColor Yellow
}

# 3. Upload aset
$assets = @(
    (Join-Path $artifacts "NeyraOptimizer-$Version-win10-11-x64-offline.msi"),
    (Join-Path $artifacts "NeyraOptimizer-$Version-win10-11-x64-light.msi"),
    (Join-Path $artifacts 'SHA256SUMS.txt')
)
foreach ($a in $assets) {
    if (-not (Test-Path $a)) { Write-Warning "Lewati (tidak ada): $a"; continue }
    $name = Split-Path $a -Leaf
    Write-Host "== Upload $name =="
    gh release upload $tag $a --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "Upload gagal untuk $name" }
}

$rel = gh release view $tag --repo $Repo --json url -q .url
Write-Host ""
Write-Host "Selesai! Release:" -ForegroundColor Green
Write-Host $rel
