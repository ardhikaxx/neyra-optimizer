# Neyra Optimizer

**Performance Tuning Tool for Windows 10 & 11**

Neyra Optimizer is a native Windows desktop utility (C# / .NET 10 / WPF) that helps users of
low-end and mid-range PCs reduce unnecessary resource usage — background activity, startup
bloat, scheduled-task churn, visual overhead — **safely, reversibly and transparently**.

It is *not* a registry cleaner, not a "service killer", not an antivirus, not a driver updater,
and not an overclocking tool. It makes no guaranteed performance claims: every reported number
comes from actual before/after measurements on the machine.

---

## Feature overview (all functional — no mock pages)

| Area | What it really does |
|---|---|
| Dashboard | Live RAM/CPU/GPU/disk/process metrics + deterministic Performance Score (transparent components) |
| Analyze | Full local scan: OS/CPU/RAM/GPU/storage/battery/power plan/Windows Security/elevation + device class with reasons |
| Optimization Center | Rule-engine recommendations (Safe / Recommended / Optional / Advanced), One-Click Safe Optimization |
| Startup Manager | Run keys (HKCU/HKLM/WOW64) + startup folders; disable/enable without deleting; protected entries locked |
| Services Manager | SCM enumeration via service key names; classification; manual/auto/disabled changes with dependency awareness |
| Scheduled Tasks | Task Scheduler COM enumeration by full path; disable/enable only non-protected tasks |
| Debloat | AppX/MSIX + Win32 uninstall list with whitelist protection; explicit per-app user selection |
| Background Apps | User-process view (system/security never killable) + official background-execution toggles |
| Visual Effects | Presets (Best Appearance / Balanced / Best Performance) via documented registry values |
| Power & Performance | Power plans + power-mode overlay (battery-aware); reversible |
| Modes | Safe Windows / Balanced / Low-End / Office / Gaming / Battery Saver plans through the same safety pipeline |
| Privacy | Advertising ID, tailored experiences, Bing-in-search toggles (never touches security components) |
| Cleanup | Dry-run scan then explicit deletion of temp/cache categories only |
| Restore Center | Optimization Snapshots with previous values + SHA-256 integrity manifests; one-click rollback |
| History | Every batch with applied/failed/skipped counts and per-item details |
| Logs | Structured JSONL logs with severity filter + search |
| Settings | Theme, language, monitoring, restore-point default, advanced mode, uninstall assistant |
| Help & Safety | Rollback explainer, protected-component list, support bundle export |

Safety architecture highlights:

* **Operation pipeline**: Validate → Snapshot (+ optional System Restore point) → Preview → Confirm → Elevate once (UAC batch) → Apply → Verify → Log → Commit.
* **Restore point gate**: if creating a restore point fails, risky changes are aborted and the user is told why.
* **Protected components** (Defender, RPC, storage/audio drivers, Windows Update security deps…) are rejected *before* any Windows API call.
* **Crash recovery**: interrupted batches are detected on next launch and offered rollback — never auto-replayed.
* **Emergency Restore**: `NeyraOptimizer.exe --emergency` opens a minimal window reading snapshots directly from disk.
* **Single instance** + operation lock so two batches can never mutate the system concurrently.

## Supported platforms

| OS | Architecture | Status |
|---|---|---|
| Windows 10 1809+ (build 17763+) | x64 | ✅ Target platform |
| Windows 11 (all supported builds) | x64 | ✅ Target platform |
| ARM64 | — | ❌ Not tested; do not claim |

Unsupported builds put the app into **read-only diagnostics mode automatically**.

Runtime: .NET 10 Desktop Runtime (installer is framework-dependent in `Release`;
`Production` configuration publishes self-contained + ReadyToRun).

## Quick start (developers)

Requirements: Windows 10/11 x64, .NET SDK 10.0.x, WiX 5 CLI (`dotnet tool install --global wix --version 5.0.2`) for the installer.

```powershell
git clone <repo-url> NeyraOptimizer
cd NeyraOptimizer

# 1. Restore packages
dotnet restore NeyraOptimizer.sln

# 2. Build Debug
dotnet build NeyraOptimizer.sln -c Debug

# 3. Run tests (unit + fake integration; no system mutation on dev machines)
dotnet test tests\NeyraOptimizer.Tests\NeyraOptimizer.Tests.csproj

# 4. Run the app (Debug)
src\NeyraOptimizer.App\bin\Debug\net10.0-windows10.0.19041.0\NeyraOptimizer.exe

# 5. Publish Release (framework-dependent)
dotnet publish src\NeyraOptimizer.App\NeyraOptimizer.App.csproj -c Release -r win-x64 `
  --self-contained false -o artifacts\publish

# 6. Build installer (Release or Production=self-contained+ReadyToRun)
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Configuration Release
```

Visual Studio: open `NeyraOptimizer.sln`, set `NeyraOptimizer.App` as startup project, F5.
The solution uses central package management (`Directory.Packages.props`) and shared build
settings (`Directory.Build.props`).

### Installer output & install/uninstall

```
artifacts\NeyraOptimizer-1.0.0-x64.msi        # final MSI (also under installer\bin\...)
```

Install: double-click the MSI (or `msiexec /i artifacts\NeyraOptimizer-1.0.0-x64.msi`).
Uninstall: Apps & Features → Neyra Optimizer → Uninstall. The uninstaller removes program
files only; snapshots/history/settings are preserved intentionally (see “Uninstall assistant”
in Settings to roll back all changes first).

## Documentation index

* [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) – layers, ports/adapters, data flow
* [docs/BUILD.md](docs/BUILD.md) – configurations (Debug/Release/Production), trimming policy
* [docs/TESTING.md](docs/TESTING.md) – read vs mutation tests, fake layer, test matrix
* [docs/SECURITY.md](docs/SECURITY.md) – threat model, elevation design, integrity checks
* [docs/RELEASE.md](docs/RELEASE.md) – versioning, signing checklist, release procedure
* [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) – honest limitations

## Repository layout

```
NeyraOptimizer.sln
Directory.Build.props          # shared build settings, Production config
Directory.Packages.props       # central NuGet versions
src/
  NeyraOptimizer.Domain/         # models, enums, ports, classifier/score/recommendation engines
  NeyraOptimizer.Infrastructure/ # JSON persistence, structured logging, crash-recovery tracker
  NeyraOptimizer.Security/       # protected components, elevation contracts/gateway, integrity
  NeyraOptimizer.Windows/        # WMI/SCM/TaskScheduler/registry/AppX/perfcounter implementations
  NeyraOptimizer.Diagnostics/    # system analyzer, compatibility checker, baseline measurement,
                                 #   health report generator, support bundle
  NeyraOptimizer.Optimization/   # safety engine, operation pipeline, rules catalog, mode builder,
                                 #   restore engine
  NeyraOptimizer.Application/    # use-case orchestration (analysis, optimization coordinator,
                                 #   modes, single-item actions, cleanup, recovery, session)
  NeyraOptimizer.App/            # WPF shell: themes, localization (en/id), MVVM pages, dialogs,
                                 #   onboarding, emergency window, elevated child entry point
tests/NeyraOptimizer.Tests/     # unit + fake-integration tests (76)
installer/                      # WiX v5 project (Product.wxs + generated payload fragment)
scripts/                        # build-installer.ps1, publish-payload.psm1, make-icon.ps1
docs/
```

## License / attribution

Copyright © Neyra Software. All rights reserved.
