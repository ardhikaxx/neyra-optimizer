<#
.SYNOPSIS
  Publishes Neyra Optimizer and builds the final MSI installer.

.DESCRIPTION
  Production pipeline:
    1. dotnet publish (Release framework-dependent | Production self-contained + ReadyToRun)
    2. Generates a deterministic WiX payload fragment from the publish output
    3. WiX v5 build -> artifacts\NeyraOptimizer-<version>-x64.msi
    4. Prints manual code-signing steps (signing is intentionally not automated)

.USAGE
  powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 [-Configuration Release|Production]
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Production')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifacts 'publish'
$version = '1.0.0'

Write-Host "== 1/4 Publishing app ($Configuration, win-x64) ==" -ForegroundColor Cyan
$publishArgs = @(
    'publish', (Join-Path $repoRoot 'src\NeyraOptimizer.App\NeyraOptimizer.App.csproj'),
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $publishDir,
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    '--nologo', '-v', 'q'
)
$publishArgs += "-p:VersionPrefix=$version"
if ($Configuration -eq 'Production') {
    $publishArgs += @('--self-contained', 'true', '-p:PublishReadyToRun=true')
}
else {
    $publishArgs += @('--self-contained', 'false')
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Defense in depth: never ship debug symbols or dev configs.
Get-ChildItem $publishDir -Include '*.pdb' -Recurse | Remove-Item -Force

Write-Host "== 2/4 Generating payload fragment ==" -ForegroundColor Cyan
Import-Module (Join-Path $PSScriptRoot 'publish-payload.psm1') -Force
$fragmentPath = Join-Path $repoRoot 'installer\obj\PublishPayload.g.wxs'
New-PublishPayloadFragment -PublishDir $publishDir -OutputPath $fragmentPath -ExeName 'NeyraOptimizer.exe'

Write-Host "== 3/4 Building MSI ==" -ForegroundColor Cyan
$wixproj = Join-Path $repoRoot 'installer\NeyraOptimizer.Installer.wixproj'
& dotnet build $wixproj -c $Configuration "-p:ProductVersion=$version" "-p:SolutionDir=$repoRoot\" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE" }

Write-Host "== 4/4 Code signing (manual step before release) ==" -ForegroundColor Yellow
Write-Host @"
Sign both binaries with your organization certificate:

  signtool sign /fd SHA256 /tr <RFC3161-TSA> /td SHA256 "$publishDir\NeyraOptimizer.exe"
  signtool sign /fd SHA256 /tr <RFC3161-TSA> /td SHA256 "<msi>"
See docs/RELEASE.md for the full release checklist.
"@

# Stage the final MSI into artifacts\
$builtMsi = Get-ChildItem (Join-Path $repoRoot 'installer\bin') -Filter '*.msi' -Recurse | Select-Object -First 1
if ($null -eq $builtMsi) { throw "MSI not found after build." }
$finalMsi = Join-Path $artifacts "NeyraOptimizer-$version-x64.msi"
Copy-Item $builtMsi.FullName $finalMsi -Force

Write-Host "Done." -ForegroundColor Green
Get-Item $finalMsi | Select-Object FullName, Length
