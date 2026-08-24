# Release Procedure

## 0. Preconditions

* CI green: `dotnet build -c Debug`, `dotnet test` (76/76), installer script dry-run.
* Manual VM matrix pass completed (docs/TESTING.md) — **required for production**.
* Version bumped in `Directory.Build.props` (`VersionPrefix`) and `scripts/build-installer.ps1`
  (`$version`). MSI `ProductCode` is regenerated automatically; `UpgradeCode` must never change.

## 1. Build artifacts

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Configuration Production
```

Produces:
* `artifacts\publish\` — self-contained + ReadyToRun payload, no PDBs
* `artifacts\NeyraOptimizer-<version>-x64.msi`

## 2. Code signing (manual — organization certificate only)

Sign, in this order:

```bat
signtool sign /fd SHA256 /tr <RFC3161-TSA-URL> /td SHA256 ^
    artifacts\publish\NeyraOptimizer.exe

signtool sign /fd SHA256 /tr <RFC3161-TSA-URL> /td SHA256 ^
    artifacts\NeyraOptimizer-<version>-x64.msi
```

Verify with `signtool verify /pa /all`. Never commit certificates or passwords; use your org's
signing service. Unsigned external releases are not permitted.

## 3. Release smoke test

On a clean VM (Win10 1809-class and Win11):
1. Install MSI → Start Menu shortcut present → app launches → onboarding appears.
2. Analyze My PC completes; Optimization preview shows counts; One-Click applies with one UAC.
3. Restore Center rolls the snapshot back; integrity badge shows ✓ SHA-256.
4. Uninstall keeps snapshots; `--emergency` window still lists them.

## 4. Publish checklist

- [ ] Tag git: `v<version>`
- [ ] Attach MSI + publish zip to GitHub Release
- [ ] Record SHA-256 of both files
- [ ] Note tested builds in release notes; restate "x64 only, ARM64 untested"
