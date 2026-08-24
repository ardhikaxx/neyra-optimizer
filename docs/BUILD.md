# Build & Configurations

## Requirements

* Windows 10 (1809+) or Windows 11, x64
* .NET SDK **10.0.x** (`dotnet --list-sdks`)
* WiX 5 CLI for the installer: `dotnet tool install --global wix --version 5.0.2`
* Visual Studio 2026/2022 (17.12+) optional — the solution opens and builds with F5

## Commands

```powershell
dotnet restore NeyraOptimizer.sln
dotnet build NeyraOptimizer.sln -c Debug
dotnet test tests\NeyraOptimizer.Tests\NeyraOptimizer.Tests.csproj

# Framework-dependent publish (end users need .NET Desktop Runtime 10 x64)
dotnet publish src\NeyraOptimizer.App\NeyraOptimizer.App.csproj -c Release -r win-x64 `
    --self-contained false -o artifacts\publish -p:DebugType=none

# Full installer pipeline (Release or Production)
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Configuration Production
```

Installer output: `artifacts\NeyraOptimizer-<version>-x64.msi`.

## Configuration matrix

| | Debug | Release | Production |
|---|---|---|---|
| Optimize | off | on | on |
| PDBs in payload | yes | **no** (`DebugType=none`) | no |
| Self-contained | n/a | no (framework-dependent) | **yes** + `PublishReadyToRun` |
| Use case | development | broad distribution, small MSI (~5.8 MB) | offline machines without runtime |

Trimming is intentionally **disabled** in all configurations: WPF + WinRT projection
(`Microsoft.Windows.SDK.NET`) rely on reflection patterns that are not trim-safe, and the
size win does not justify broken features.

Shared settings live in `Directory.Build.props` (version prefix 1.0.0, deterministic build,
Release/Production hardening). Package versions are centralized in `Directory.Packages.props`.

## Installer notes (WiX v5)

* `installer/Product.wxs` — per-machine package, stable UpgradeCode, major-upgrade strategy,
  Start-Menu shortcut, ARPPRODUCTICON, `WixUI_InstallDir`.
* The publish payload is harvested by `scripts/build-installer.ps1` into a generated fragment
  (`installer/obj/PublishPayload.g.wxs`) with deterministic component GUIDs and correct
  subdirectories (localized satellites under `id\`).
* No custom actions / no scripts run during install or uninstall.
* Uninstall preserves `%ProgramData%\NeyraOptimizer` snapshots and `%AppData%` settings by design.

## Troubleshooting builds

| Symptom | Fix |
|---|---|
| `wix : command not found` in script step 3 | `dotnet tool install --global wix --version 5.0.2`, reopen shell |
| MSI missing files after re-publish | re-run the whole script (publish → fragment → wixproj build) |
| XAML designer errors in VS | ensure startup project is `NeyraOptimizer.App`; first full CLI build populates obj |
| Tests fail on non-Windows CI | tests target `net10.0-windows10.0.19041.0`; run on Windows agents |
